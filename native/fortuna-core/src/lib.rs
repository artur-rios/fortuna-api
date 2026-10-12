#![deny(unsafe_op_in_unsafe_fn)]

use std::collections::HashMap;
use std::ffi::{CStr, CString, c_char, c_int};
use std::path::PathBuf;
use std::ptr;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex, OnceLock};

use argon2::Argon2;
use argon2::password_hash::{PasswordHasher, PasswordVerifier, phc::PasswordHash};
use chrono::{DateTime, Duration, Timelike, Utc};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use sha2::{Digest, Sha256};
use uuid::Uuid;
use zeroize::{Zeroize, Zeroizing};

mod classification;
mod operation;
mod persistence;
mod personal_archive;

use operation::{NotImplementedOperation, OperationSpec};
use persistence::NativeStore;

#[cfg(test)]
type NativeOperationFunction = extern "C" fn(*const c_char, *mut *mut c_char) -> c_int;

/// Successful call. Operation functions use HTTP-compatible numeric statuses.
pub const FORTUNA_STATUS_OK: c_int = 200;
/// A resource was created successfully.
pub const FORTUNA_STATUS_CREATED: c_int = 201;
/// Long-running work was accepted and can be monitored through its job route.
pub const FORTUNA_STATUS_ACCEPTED: c_int = 202;
/// The JSON request or initialization configuration is invalid.
pub const FORTUNA_STATUS_BAD_REQUEST: c_int = 400;
/// Authentication is absent, invalid, or expired.
pub const FORTUNA_STATUS_UNAUTHORIZED: c_int = 401;
/// The requested resource or disabled local-auth surface is unavailable.
pub const FORTUNA_STATUS_NOT_FOUND: c_int = 404;
/// Initialization was requested while the core was already initialized.
pub const FORTUNA_STATUS_CONFLICT: c_int = 409;
/// Persistence or another unexpected native-core operation failed.
pub const FORTUNA_STATUS_INTERNAL_ERROR: c_int = 500;
/// The route is exported for ABI stability but the native core does not implement it; the
/// failure envelope states why. `fortuna_capabilities` lists every such route under
/// `notImplemented`, so callers can present it as unavailable offline without calling it.
pub const FORTUNA_STATUS_NOT_IMPLEMENTED: c_int = 501;
/// An operation requiring initialized services was called before initialization.
pub const FORTUNA_STATUS_NOT_INITIALIZED: c_int = 503;

const INVALID_JSON: &str = "The request body is not valid JSON.";
const INVALID_CREDENTIALS: &str = "The local account name or secret is invalid.";
const INVALID_AUTHENTICATION_REQUEST: &str =
    "The local account name must be 1 to 200 characters and the secret 1 to 1024 characters.";
const AUTHENTICATED: &str = "Local account authenticated successfully.";
const LOCAL_AUTH_DISABLED: &str = "Local authentication is not available in this deployment.";
const NOT_INITIALIZED: &str = "The native core is not initialized.";
const INTERNAL_ERROR: &str = "The native core could not complete the operation.";
/// Upper bound for `tokenLifetimeSeconds`: 30 days. Anything longer is refused at
/// initialization so the expiry arithmetic can never overflow.
const MAXIMUM_TOKEN_LIFETIME_SECONDS: i64 = 30 * 24 * 60 * 60;

static CORE: OnceLock<Mutex<Option<Arc<Core>>>> = OnceLock::new();
static OUTSTANDING_STRINGS: AtomicUsize = AtomicUsize::new(0);

#[derive(Debug)]
struct Core {
    store: NativeStore,
    local_auth_enabled: bool,
    token_lifetime_seconds: i64,
    sessions: Mutex<HashMap<[u8; 32], Session>>,
}

#[derive(Clone, Debug)]
struct Session {
    user_id: Uuid,
    expires_at: DateTime<Utc>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct InitializeRequest {
    database_path: PathBuf,
    #[serde(default = "default_true")]
    local_auth_enabled: bool,
    #[serde(default = "default_token_lifetime")]
    token_lifetime_seconds: i64,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct AuthenticateRequest {
    name: String,
    secret: String,
}

impl Drop for AuthenticateRequest {
    fn drop(&mut self) {
        self.secret.zeroize();
    }
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct AuthenticationOutput {
    token: String,
    expires_at: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct VersionOutput<'a> {
    version: &'a str,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct HealthOutput<'a> {
    status: &'a str,
    storage: &'a str,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct LifecycleOutput<'a> {
    status: &'a str,
}

#[derive(Debug, Serialize)]
struct DataOutput<T: Serialize> {
    data: Option<T>,
    messages: Vec<String>,
    errors: Vec<String>,
    timestamp: String,
    success: bool,
}

impl<T: Serialize> DataOutput<T> {
    pub(crate) fn success(data: T, message: impl Into<String>) -> Self {
        Self {
            data: Some(data),
            messages: vec![message.into()],
            errors: Vec::new(),
            timestamp: wire_timestamp(Utc::now()),
            success: true,
        }
    }

    pub(crate) fn failure(error: impl Into<String>) -> Self {
        Self {
            data: None,
            messages: Vec::new(),
            errors: vec![error.into()],
            timestamp: wire_timestamp(Utc::now()),
            success: false,
        }
    }
}

impl Core {
    fn authenticate(
        &self,
        request: &mut AuthenticateRequest,
    ) -> Result<AuthenticationOutput, AuthError> {
        if !self.local_auth_enabled {
            request.secret.zeroize();
            return Err(AuthError::Disabled);
        }
        // Mirrors the HTTP host: the name is trimmed and both inputs are bounded (in characters)
        // before anything reaches the store or Argon2.
        let name = request.name.trim();
        if name.is_empty()
            || name.chars().count() > 200
            || request.secret.is_empty()
            || request.secret.chars().count() > 1024
        {
            request.secret.zeroize();
            return Err(AuthError::InvalidRequest);
        }

        let credentials = self
            .store
            .find_credentials(name)
            .map_err(|_| AuthError::Internal)?;

        let dummy_hash = dummy_password_hash();
        let encoded_hash = credentials
            .as_ref()
            .map(|credentials| credentials.secret_hash.as_str())
            .unwrap_or(dummy_hash);
        let parsed_hash = PasswordHash::new(encoded_hash).map_err(|_| AuthError::Internal)?;
        let verified = Argon2::default()
            .verify_password(request.secret.as_bytes(), &parsed_hash)
            .is_ok();
        request.secret.zeroize();

        let Some(credentials) = credentials.filter(|_| verified) else {
            return Err(AuthError::InvalidCredentials);
        };
        let user_id = Uuid::parse_str(&credentials.user_id).map_err(|_| AuthError::Internal)?;
        let expires_at = self.session_expiry().ok_or(AuthError::Internal)?;

        Ok(self.issue_session(user_id, expires_at))
    }

    /// When a session issued now expires, or `None` when the configured lifetime cannot be
    /// represented. Callers compute this before any irreversible step (such as consuming a
    /// recovery code) so a failure here never leaves the account half-updated.
    fn session_expiry(&self) -> Option<DateTime<Utc>> {
        session_expiry(Utc::now(), self.token_lifetime_seconds)
    }

    fn authorize(&self, token: &mut String) -> Result<Uuid, AuthError> {
        let token_hash: [u8; 32] = Sha256::digest(token.as_bytes()).into();
        token.zeroize();
        let mut sessions = self
            .sessions
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        sessions.retain(|_, session| session.expires_at > Utc::now());
        sessions
            .get(&token_hash)
            .map(|session| session.user_id)
            .ok_or(AuthError::InvalidCredentials)
    }

    fn issue_session(&self, user_id: Uuid, expires_at: DateTime<Utc>) -> AuthenticationOutput {
        let token = format!("{}{}", Uuid::new_v4().simple(), Uuid::new_v4().simple());
        let token_hash = Sha256::digest(token.as_bytes()).into();
        let mut sessions = self
            .sessions
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        sessions.retain(|_, session| session.expires_at > Utc::now());
        sessions.insert(
            token_hash,
            Session {
                user_id,
                expires_at,
            },
        );
        AuthenticationOutput {
            token,
            expires_at: wire_timestamp(expires_at),
        }
    }
}

#[derive(Debug)]
enum AuthError {
    Disabled,
    InvalidRequest,
    InvalidCredentials,
    Internal,
}

fn default_true() -> bool {
    true
}

fn default_token_lifetime() -> i64 {
    3600
}

fn session_expiry(now: DateTime<Utc>, lifetime_seconds: i64) -> Option<DateTime<Utc>> {
    Duration::try_seconds(lifetime_seconds).and_then(|lifetime| now.checked_add_signed(lifetime))
}

fn is_valid_token_lifetime(lifetime_seconds: i64) -> bool {
    (1..=MAXIMUM_TOKEN_LIFETIME_SECONDS).contains(&lifetime_seconds)
}

fn core_slot() -> &'static Mutex<Option<Arc<Core>>> {
    CORE.get_or_init(|| Mutex::new(None))
}

fn current_core() -> Option<Arc<Core>> {
    core_slot()
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
        .clone()
}

fn initialize_core(config: InitializeRequest) -> Result<(), c_int> {
    if config.database_path.as_os_str().is_empty()
        || !is_valid_token_lifetime(config.token_lifetime_seconds)
    {
        return Err(FORTUNA_STATUS_BAD_REQUEST);
    }

    let mut slot = core_slot()
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    if slot.is_some() {
        return Err(FORTUNA_STATUS_CONFLICT);
    }

    // Keep the first unknown-name authentication on the same one-Argon2-check path
    // as a wrong secret for an existing account.
    let _ = dummy_password_hash();
    let store =
        NativeStore::initialize(config.database_path).map_err(|_| FORTUNA_STATUS_INTERNAL_ERROR)?;
    *slot = Some(Arc::new(Core {
        store,
        local_auth_enabled: config.local_auth_enabled,
        token_lifetime_seconds: config.token_lifetime_seconds,
        sessions: Mutex::new(HashMap::new()),
    }));
    Ok(())
}

fn dummy_password_hash() -> &'static str {
    static DUMMY: OnceLock<String> = OnceLock::new();
    DUMMY.get_or_init(|| hash_secret("fortuna-native-dummy-credential").expect("dummy hash"))
}

fn hash_secret(secret: &str) -> Result<String, argon2::password_hash::Error> {
    Argon2::default()
        .hash_password(secret.as_bytes())
        .map(|hash| hash.to_string())
}

fn wire_timestamp(timestamp: DateTime<Utc>) -> String {
    format!(
        "{}.{:07}Z",
        timestamp.format("%Y-%m-%dT%H:%M:%S"),
        timestamp.nanosecond() / 100
    )
}

fn serialize<T: Serialize>(value: &T) -> String {
    serde_json::to_string(value).unwrap_or_else(|_| {
        "{\"data\":null,\"messages\":[],\"errors\":[\"The native core could not serialize its response.\"],\"timestamp\":\"1970-01-01T00:00:00.0000000Z\",\"success\":false}".to_owned()
    })
}

fn read_json<T: for<'de> Deserialize<'de>>(request_json: *const c_char) -> Result<T, ()> {
    if request_json.is_null() {
        return Err(());
    }
    // SAFETY: The C contract requires a valid, NUL-terminated UTF-8 string for every request.
    let raw = unsafe { CStr::from_ptr(request_json) }
        .to_str()
        .map_err(|_| ())?;
    let owned = Zeroizing::new(raw.to_owned());
    serde_json::from_str(&owned).map_err(|_| ())
}

fn write_response(response_json: *mut *mut c_char, body: String) -> c_int {
    let Ok(response) = CString::new(body) else {
        return FORTUNA_STATUS_INTERNAL_ERROR;
    };
    // SAFETY: guarded_call rejects NULL and initializes this caller-owned out pointer.
    unsafe { *response_json = response.into_raw() };
    OUTSTANDING_STRINGS.fetch_add(1, Ordering::SeqCst);
    FORTUNA_STATUS_OK
}

fn guarded_call(response_json: *mut *mut c_char, body: impl FnOnce() -> (c_int, String)) -> c_int {
    if response_json.is_null() {
        return FORTUNA_STATUS_BAD_REQUEST;
    }
    // SAFETY: The pointer was checked for NULL and belongs to the caller.
    unsafe { *response_json = ptr::null_mut() };

    let (status, response) = std::panic::catch_unwind(std::panic::AssertUnwindSafe(body))
        .unwrap_or_else(|_| {
            (
                FORTUNA_STATUS_INTERNAL_ERROR,
                serialize(&DataOutput::<Value>::failure(INTERNAL_ERROR)),
            )
        });
    if write_response(response_json, response) == FORTUNA_STATUS_OK {
        status
    } else {
        FORTUNA_STATUS_INTERNAL_ERROR
    }
}

fn operation_call(
    operation: OperationSpec,
    request_json: *const c_char,
    response_json: *mut *mut c_char,
) -> c_int {
    guarded_call(response_json, || {
        operation::execute(operation, request_json)
    })
}

/// Answer an export the native core does not implement. Neither initialization nor the request
/// matter: the route never does work, so it never reports success.
fn not_implemented_call(
    operation: NotImplementedOperation,
    response_json: *mut *mut c_char,
) -> c_int {
    guarded_call(response_json, || operation::not_implemented(operation))
}

/// Describe every route available to offline callers, every exported route that is not
/// implemented offline, and every deliberately absent route.
/// Initialization is not required. The response is library-owned and must be released with
/// `fortuna_string_free`.
#[unsafe(no_mangle)]
pub extern "C" fn fortuna_capabilities(
    request_json: *const c_char,
    response_json: *mut *mut c_char,
) -> c_int {
    guarded_call(response_json, || operation::capabilities(request_json))
}

include!(concat!(env!("OUT_DIR"), "/generated_operations.rs"));

/// Initialize the native core from a UTF-8 JSON object.
///
/// Request: `{"databasePath":"/absolute/path/fortuna.db","localAuthEnabled":true,
/// "tokenLifetimeSeconds":3600}`. `tokenLifetimeSeconds` must be between 1 and 2592000
/// (30 days). The database is created and migrated on demand.
/// Concurrent calls are supported; a second initialization returns 409.
/// The response is always library-owned and must be released with `fortuna_string_free`.
#[unsafe(no_mangle)]
pub extern "C" fn fortuna_initialize(
    request_json: *const c_char,
    response_json: *mut *mut c_char,
) -> c_int {
    guarded_call(response_json, || {
        let Ok(config) = read_json::<InitializeRequest>(request_json) else {
            return (
                FORTUNA_STATUS_BAD_REQUEST,
                serialize(&DataOutput::<Value>::failure(INVALID_JSON)),
            );
        };
        match initialize_core(config) {
            Ok(()) => (
                FORTUNA_STATUS_OK,
                serialize(&DataOutput::success(
                    LifecycleOutput {
                        status: "initialized",
                    },
                    "Native core initialized successfully.",
                )),
            ),
            Err(FORTUNA_STATUS_BAD_REQUEST) => (
                FORTUNA_STATUS_BAD_REQUEST,
                serialize(&DataOutput::<Value>::failure(
                    "DatabasePath and a TokenLifetimeSeconds between 1 and 2592000 are required.",
                )),
            ),
            Err(FORTUNA_STATUS_CONFLICT) => (
                FORTUNA_STATUS_CONFLICT,
                serialize(&DataOutput::<Value>::failure(
                    "The native core is already initialized.",
                )),
            ),
            Err(_) => (
                FORTUNA_STATUS_INTERNAL_ERROR,
                serialize(&DataOutput::<Value>::failure(INTERNAL_ERROR)),
            ),
        }
    })
}

/// Shut down the current native core and invalidate all in-memory sessions.
/// The response is library-owned and must be released with `fortuna_string_free`.
#[unsafe(no_mangle)]
pub extern "C" fn fortuna_shutdown(
    request_json: *const c_char,
    response_json: *mut *mut c_char,
) -> c_int {
    guarded_call(response_json, || {
        if read_json::<Value>(request_json).is_err() {
            return (
                FORTUNA_STATUS_BAD_REQUEST,
                serialize(&DataOutput::<Value>::failure(INVALID_JSON)),
            );
        }
        let mut slot = core_slot()
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        if slot.take().is_none() {
            return (
                FORTUNA_STATUS_NOT_INITIALIZED,
                serialize(&DataOutput::<Value>::failure(NOT_INITIALIZED)),
            );
        }
        (
            FORTUNA_STATUS_OK,
            serialize(&DataOutput::success(
                LifecycleOutput { status: "stopped" },
                "Native core shut down successfully.",
            )),
        )
    })
}

/// Return the native core package version. Initialization is not required.
/// The response is library-owned and must be released with `fortuna_string_free`.
#[unsafe(no_mangle)]
pub extern "C" fn fortuna_version(
    request_json: *const c_char,
    response_json: *mut *mut c_char,
) -> c_int {
    guarded_call(response_json, || {
        if read_json::<Value>(request_json).is_err() {
            return (
                FORTUNA_STATUS_BAD_REQUEST,
                serialize(&DataOutput::<Value>::failure(INVALID_JSON)),
            );
        }
        (
            FORTUNA_STATUS_OK,
            serialize(&DataOutput::success(
                VersionOutput {
                    version: env!("CARGO_PKG_VERSION"),
                },
                "Native core version retrieved successfully.",
            )),
        )
    })
}

/// Return 200 when SQLite can be opened, or 503 before initialization.
/// The response is library-owned and must be released with `fortuna_string_free`.
#[unsafe(no_mangle)]
pub extern "C" fn fortuna_health(
    request_json: *const c_char,
    response_json: *mut *mut c_char,
) -> c_int {
    guarded_call(response_json, || {
        if read_json::<Value>(request_json).is_err() {
            return (
                FORTUNA_STATUS_BAD_REQUEST,
                serialize(&DataOutput::<Value>::failure(INVALID_JSON)),
            );
        }
        let Some(core) = current_core() else {
            return (
                FORTUNA_STATUS_NOT_INITIALIZED,
                serialize(&DataOutput::<Value>::failure(NOT_INITIALIZED)),
            );
        };
        match core.store.health() {
            Ok(()) => (
                FORTUNA_STATUS_OK,
                serialize(&DataOutput::success(
                    HealthOutput {
                        status: "healthy",
                        storage: "sqlite",
                    },
                    "Native core is healthy.",
                )),
            ),
            Err(_) => (
                FORTUNA_STATUS_INTERNAL_ERROR,
                serialize(&DataOutput::<Value>::failure(INTERNAL_ERROR)),
            ),
        }
    })
}

/// Mirror `POST /api/local-accounts/authenticate` for the native local account.
///
/// Request body: `{"name":"Local User","secret":"..."}`. The response uses the
/// same `DataOutput<AuthenticateLocalAccountCommandOutput?>` JSON shape as HTTP.
/// The request is copied only into zeroizing memory and is never logged.
/// The response is library-owned and must be released with `fortuna_string_free`.
#[unsafe(no_mangle)]
pub extern "C" fn fortuna_api_local_accounts_authenticate(
    request_json: *const c_char,
    response_json: *mut *mut c_char,
) -> c_int {
    guarded_call(response_json, || {
        let Some(core) = current_core() else {
            return (
                FORTUNA_STATUS_NOT_INITIALIZED,
                serialize(&DataOutput::<Value>::failure(NOT_INITIALIZED)),
            );
        };
        let Ok(mut request) = read_json::<AuthenticateRequest>(request_json) else {
            return (
                FORTUNA_STATUS_BAD_REQUEST,
                serialize(&DataOutput::<Value>::failure(INVALID_JSON)),
            );
        };
        match core.authenticate(&mut request) {
            Ok(output) => (
                FORTUNA_STATUS_OK,
                serialize(&DataOutput::success(output, AUTHENTICATED)),
            ),
            Err(AuthError::Disabled) => (
                FORTUNA_STATUS_NOT_FOUND,
                serialize(&DataOutput::<Value>::failure(LOCAL_AUTH_DISABLED)),
            ),
            Err(AuthError::InvalidRequest) => (
                FORTUNA_STATUS_BAD_REQUEST,
                serialize(&DataOutput::<Value>::failure(
                    INVALID_AUTHENTICATION_REQUEST,
                )),
            ),
            Err(AuthError::InvalidCredentials) => (
                FORTUNA_STATUS_UNAUTHORIZED,
                serialize(&DataOutput::<Value>::failure(INVALID_CREDENTIALS)),
            ),
            Err(AuthError::Internal) => (
                FORTUNA_STATUS_INTERNAL_ERROR,
                serialize(&DataOutput::<Value>::failure(INTERNAL_ERROR)),
            ),
        }
    })
}

/// Mirror `GET /api/accounts/{id}` for the authenticated native local account.
///
/// Transport metadata and route values are carried in one JSON object:
/// `{"token":"...","id":"uuid","includeDeleted":false}`. The response is the
/// HTTP `DataOutput<FinancialAccountOutput?>` shape. Decimal fields are read from
/// SQLite TEXT and emitted as invariant decimal strings without a float conversion.
/// The response is library-owned and must be released with `fortuna_string_free`.
#[unsafe(no_mangle)]
pub extern "C" fn fortuna_api_accounts_get_by_id(
    request_json: *const c_char,
    response_json: *mut *mut c_char,
) -> c_int {
    operation_call(
        OperationSpec {
            symbol: "fortuna_api_accounts_get_by_id",
            method: "GET",
            path: "/api/accounts/{id}",
            area: "Accounts",
            long_running: false,
            response_schema: "FinancialAccountOutputDataOutput",
        },
        request_json,
        response_json,
    )
}

/// Release a response returned by any Fortuna native-core function.
///
/// Passing NULL is a no-op. A non-NULL pointer must be released exactly once by
/// the same loaded library instance; accessing it after this call is invalid.
///
/// # Safety
///
/// A non-NULL pointer must have been returned through a response out parameter by
/// this exact loaded library instance and must not have been freed previously.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fortuna_string_free(response_json: *mut c_char) {
    if response_json.is_null() {
        return;
    }
    // SAFETY: The contract requires a pointer returned by CString::into_raw in write_response.
    drop(unsafe { CString::from_raw(response_json) });
    OUTSTANDING_STRINGS.fetch_sub(1, Ordering::SeqCst);
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::CString;
    use std::io::{Cursor, Read};
    use std::path::Path;

    use base64::Engine;
    use base64::engine::general_purpose::STANDARD;
    use rusqlite::{Connection, params};
    use zip::ZipArchive;

    static TEST_LOCK: Mutex<()> = Mutex::new(());

    struct Response {
        status: c_int,
        body: String,
    }

    fn call(
        function: extern "C" fn(*const c_char, *mut *mut c_char) -> c_int,
        request: &str,
    ) -> Response {
        let request = CString::new(request).unwrap();
        let mut output = ptr::null_mut();
        let status = function(request.as_ptr(), &mut output);
        assert!(!output.is_null());
        // SAFETY: The ABI returned a live NUL-terminated response pointer.
        let body = unsafe { CStr::from_ptr(output) }
            .to_str()
            .unwrap()
            .to_owned();
        // SAFETY: `output` came from this loaded library and is released once here.
        unsafe { fortuna_string_free(output) };
        Response { status, body }
    }

    fn temp_database() -> PathBuf {
        std::env::temp_dir().join(format!("fortuna-native-{}.db", Uuid::new_v4()))
    }

    fn initialize(path: &Path, enabled: bool) -> Response {
        call(
            fortuna_initialize,
            &serde_json::json!({
                "databasePath": path,
                "localAuthEnabled": enabled,
                "tokenLifetimeSeconds": 3600
            })
            .to_string(),
        )
    }

    fn seed(path: &Path, secret: &str, opening_balance: &str) -> (Uuid, Uuid) {
        let user_id = Uuid::new_v4();
        let account_id = Uuid::new_v4();
        let now = "2026-09-09T12:34:56.1234567Z";
        let connection = Connection::open(path).unwrap();
        connection
            .execute(
                "INSERT INTO native_user_profile(public_id, display_name, created_at, updated_at)
             VALUES (?1, 'Local User', ?2, ?2)",
                params![user_id.to_string(), now],
            )
            .unwrap();
        connection.execute(
            "INSERT INTO native_local_account(public_id, user_id, name, secret_hash, created_at, updated_at)
             VALUES (?1, ?2, 'Local User', ?3, ?4, ?4)",
            params![Uuid::new_v4().to_string(), user_id.to_string(), hash_secret(secret).unwrap(), now],
        ).unwrap();
        connection
            .execute(
                "INSERT INTO native_offline_record(
                 public_id, user_id, resource, body_json, is_deleted, created_at, updated_at)
             VALUES (?1, ?2, 'accounts', ?3, 0, ?4, ?4)",
                params![
                    account_id.to_string(),
                    user_id.to_string(),
                    format!(
                        r#"{{"id":"{account_id}","name":"Exact account","institution":"Native bank","accountType":1,"currencyCode":"BRL","openingBalance":{opening_balance},"isDeleted":false,"createdAt":"{now}","updatedAt":"{now}"}}"#
                    ),
                    now
                ],
            )
            .unwrap();
        (user_id, account_id)
    }

    fn token_from(response: &Response) -> String {
        serde_json::from_str::<Value>(&response.body).unwrap()["data"]["token"]
            .as_str()
            .unwrap()
            .to_owned()
    }

    fn create_local_user(path: &Path) -> String {
        assert_eq!(FORTUNA_STATUS_OK, initialize(path, true).status);
        let created = call(
            fortuna_api_local_accounts_post,
            r#"{"displayName":"Local User","secret":"correct-horse-battery-staple","storageMode":1}"#,
        );
        assert_eq!(FORTUNA_STATUS_CREATED, created.status, "{}", created.body);
        let created_json: Value = serde_json::from_str(&created.body).unwrap();
        assert_eq!(
            10,
            created_json["data"]["recoveryCodes"]
                .as_array()
                .unwrap()
                .len()
        );
        let authenticated = call(
            fortuna_api_local_accounts_authenticate_post,
            r#"{"name":"Local User","secret":"correct-horse-battery-staple"}"#,
        );
        assert_eq!(
            FORTUNA_STATUS_OK, authenticated.status,
            "{}",
            authenticated.body
        );
        token_from(&authenticated)
    }

    fn stop() {
        if current_core().is_some() {
            let response = call(fortuna_shutdown, "{}");
            assert_eq!(FORTUNA_STATUS_OK, response.status);
        }
    }

    fn test_guard() -> std::sync::MutexGuard<'static, ()> {
        TEST_LOCK
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    #[test]
    fn given_native_lifecycle_when_called_then_initialization_version_health_and_shutdown_are_safe()
    {
        let _guard = test_guard();
        stop();
        let path = temp_database();

        assert_eq!(FORTUNA_STATUS_OK, call(fortuna_version, "{}").status);
        assert_eq!(
            FORTUNA_STATUS_NOT_INITIALIZED,
            call(fortuna_health, "{}").status
        );
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        assert_eq!(FORTUNA_STATUS_CONFLICT, initialize(&path, true).status);
        assert_eq!(FORTUNA_STATUS_OK, call(fortuna_health, "{}").status);
        assert_eq!(FORTUNA_STATUS_OK, call(fortuna_shutdown, "{}").status);
        assert_eq!(
            FORTUNA_STATUS_NOT_INITIALIZED,
            call(fortuna_shutdown, "{}").status
        );
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_version_one_native_database_when_initialized_then_accounts_migrate_to_the_shared_dispatcher()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let user_id = Uuid::new_v4();
        let account_id = Uuid::new_v4();
        let local_account_id = Uuid::new_v4();
        let now = "2026-09-09T12:34:56.1234567Z";
        let connection = Connection::open(&path).unwrap();
        connection
            .execute_batch(
                "PRAGMA foreign_keys = ON;
                 CREATE TABLE native_user_profile (
                     public_id TEXT PRIMARY KEY NOT NULL, display_name TEXT NOT NULL,
                     created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                 CREATE TABLE native_local_account (
                     public_id TEXT PRIMARY KEY NOT NULL, user_id TEXT NOT NULL UNIQUE,
                     name TEXT NOT NULL UNIQUE, secret_hash TEXT NOT NULL,
                     created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                 CREATE TABLE native_financial_account (
                     public_id TEXT PRIMARY KEY NOT NULL, user_id TEXT NOT NULL, name TEXT NOT NULL,
                     institution TEXT NULL, account_type INTEGER NOT NULL, currency_code TEXT NOT NULL,
                     opening_balance TEXT NOT NULL, is_deleted INTEGER NOT NULL,
                     created_at TEXT NOT NULL, updated_at TEXT NOT NULL);",
            )
            .unwrap();
        connection
            .execute(
                "INSERT INTO native_user_profile VALUES (?1, 'Local User', ?2, ?2)",
                params![user_id.to_string(), now],
            )
            .unwrap();
        connection
            .execute(
                "INSERT INTO native_local_account VALUES (?1, ?2, 'Local User', ?3, ?4, ?4)",
                params![
                    local_account_id.to_string(),
                    user_id.to_string(),
                    hash_secret("migration-secret").unwrap(),
                    now
                ],
            )
            .unwrap();
        connection
            .execute(
                "INSERT INTO native_financial_account VALUES (?1, ?2, 'Migrated', NULL, 1, 'BRL',
                 '123456789012345.6789', 0, ?3, ?3)",
                params![account_id.to_string(), user_id.to_string(), now],
            )
            .unwrap();
        drop(connection);

        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let authenticated = call(
            fortuna_api_local_accounts_authenticate,
            r#"{"name":"Local User","secret":"migration-secret"}"#,
        );
        let account = call(
            fortuna_api_accounts_get_by_id,
            &serde_json::json!({"token":token_from(&authenticated),"id":account_id}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, account.status, "{}", account.body);
        assert!(
            account
                .body
                .contains("\"openingBalance\":\"123456789012345.6789\"")
        );
        stop();
    }

    #[test]
    fn given_local_credentials_when_authenticating_and_reading_then_http_contract_and_decimal_are_preserved()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let (_, account_id) = seed(
            &path,
            "correct-horse-battery-staple",
            "123456789012345.6789",
        );

        let authentication = call(
            fortuna_api_local_accounts_authenticate,
            r#"{"name":"Local User","secret":"correct-horse-battery-staple"}"#,
        );
        assert_eq!(FORTUNA_STATUS_OK, authentication.status);
        let authentication_json: Value = serde_json::from_str(&authentication.body).unwrap();
        assert!(authentication.body.starts_with("{\"data\":"));
        let messages_index = authentication.body.find("\"messages\":").unwrap();
        let errors_index = authentication.body.find("\"errors\":").unwrap();
        let timestamp_index = authentication.body.find("\"timestamp\":").unwrap();
        let success_index = authentication.body.find("\"success\":").unwrap();
        assert!(messages_index < errors_index);
        assert!(errors_index < timestamp_index);
        assert!(timestamp_index < success_index);
        assert!(authentication.body.ends_with("Z\",\"success\":true}"));
        assert_eq!(Value::Bool(true), authentication_json["success"]);
        assert_eq!(AUTHENTICATED, authentication_json["messages"][0]);

        let account = call(
            fortuna_api_accounts_get_by_id,
            &serde_json::json!({
                "token": token_from(&authentication),
                "id": account_id,
                "includeDeleted": false
            })
            .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, account.status);
        assert!(
            account
                .body
                .contains("\"openingBalance\":\"123456789012345.6789\"")
        );
        let account_json: Value = serde_json::from_str(&account.body).unwrap();
        assert_eq!(account_id.to_string(), account_json["data"]["id"]);
        assert_eq!(
            "123456789012345.6789",
            account_json["data"]["openingBalance"].as_str().unwrap()
        );
        assert_eq!(
            "Accounts retrieved successfully.",
            account_json["messages"][0]
        );
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_version_two_native_audit_when_initialized_then_subject_is_rekeyed_opaquely() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let user_id = Uuid::new_v4();
        let entry_id = Uuid::new_v4();
        let now = "2026-09-09T12:34:56.1234567Z";
        let connection = Connection::open(&path).unwrap();
        connection
            .execute_batch(
                "PRAGMA foreign_keys = ON;
                 CREATE TABLE native_schema_migration (version INTEGER PRIMARY KEY NOT NULL);
                 INSERT INTO native_schema_migration(version) VALUES (1), (2);
                 CREATE TABLE native_user_profile (
                     public_id TEXT PRIMARY KEY NOT NULL, display_name TEXT NOT NULL,
                     display_currency TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                 CREATE TABLE native_audit_entry (
                     public_id TEXT PRIMARY KEY NOT NULL, user_id TEXT NOT NULL,
                     entity_type TEXT NOT NULL, entity_id TEXT NOT NULL,
                     operation TEXT NOT NULL, outcome TEXT NOT NULL, occurred_at TEXT NOT NULL);",
            )
            .unwrap();
        connection
            .execute(
                "INSERT INTO native_user_profile VALUES (?1, 'Legacy User', 'BRL', ?2, ?2)",
                params![user_id.to_string(), now],
            )
            .unwrap();
        connection
            .execute(
                "INSERT INTO native_audit_entry VALUES (?1, ?2, 'Account', ?3,
                 'CreateAccountCommand', 'Succeeded', ?4)",
                params![
                    entry_id.to_string(),
                    user_id.to_string(),
                    Uuid::new_v4().to_string(),
                    now
                ],
            )
            .unwrap();
        drop(connection);

        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let connection = Connection::open(&path).unwrap();
        let migrated: (String, String, i64) = connection
            .query_row(
                "SELECT subject.subject_reference, entry.subject_reference,
                        (SELECT count(*) FROM native_schema_migration WHERE version = 3)
                 FROM native_audit_subject AS subject
                 JOIN native_audit_entry AS entry
                   ON entry.subject_reference = subject.subject_reference
                 WHERE subject.user_id = ?1 AND entry.public_id = ?2",
                params![user_id.to_string(), entry_id.to_string()],
                |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?)),
            )
            .unwrap();
        assert_eq!(migrated.0, migrated.1);
        assert_ne!(user_id.to_string(), migrated.0);
        assert_eq!(1, migrated.2);
        drop(connection);
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_boundary_failures_when_called_then_every_status_has_json_and_no_allocation_leaks() {
        let _guard = test_guard();
        stop();
        let path = temp_database();

        let before_init = call(fortuna_api_local_accounts_authenticate, "{}");
        assert_eq!(FORTUNA_STATUS_NOT_INITIALIZED, before_init.status);
        assert_eq!(
            FORTUNA_STATUS_BAD_REQUEST,
            call(fortuna_initialize, "not-json").status
        );
        let outstanding_before_null_call = OUTSTANDING_STRINGS.load(Ordering::SeqCst);
        assert_eq!(
            FORTUNA_STATUS_BAD_REQUEST,
            fortuna_version(ptr::null(), ptr::null_mut())
        );
        assert_eq!(
            outstanding_before_null_call,
            OUTSTANDING_STRINGS.load(Ordering::SeqCst)
        );
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        assert_eq!(
            FORTUNA_STATUS_BAD_REQUEST,
            call(fortuna_api_local_accounts_authenticate, "not-json").status
        );
        assert_eq!(
            FORTUNA_STATUS_UNAUTHORIZED,
            call(
                fortuna_api_local_accounts_authenticate,
                r#"{"name":"unknown","secret":"wrong-secret"}"#,
            )
            .status
        );

        let (_, account_id) = seed(&path, "right-secret", "1.0000");
        assert_eq!(
            FORTUNA_STATUS_UNAUTHORIZED,
            call(
                fortuna_api_accounts_get_by_id,
                &serde_json::json!({"token":"invalid","id":account_id}).to_string(),
            )
            .status
        );
        let authenticated = call(
            fortuna_api_local_accounts_authenticate,
            r#"{"name":"Local User","secret":"right-secret"}"#,
        );
        assert_eq!(
            FORTUNA_STATUS_NOT_FOUND,
            call(
                fortuna_api_accounts_get_by_id,
                &serde_json::json!({"token":token_from(&authenticated),"id":Uuid::new_v4()})
                    .to_string(),
            )
            .status
        );
        stop();

        let invalid_database =
            std::env::temp_dir().join(format!("fortuna-native-file-{}", Uuid::new_v4()));
        std::fs::write(&invalid_database, b"not sqlite").unwrap();
        assert_eq!(
            FORTUNA_STATUS_INTERNAL_ERROR,
            initialize(&invalid_database, true).status
        );
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_disabled_local_auth_when_authenticating_then_route_is_hidden() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, false).status);
        let response = call(
            fortuna_api_local_accounts_authenticate,
            r#"{"name":"Local User","secret":"never-retained"}"#,
        );
        assert_eq!(FORTUNA_STATUS_NOT_FOUND, response.status);
        assert!(response.body.contains(LOCAL_AUTH_DISABLED));
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_calls_from_multiple_threads_when_reading_version_then_allocations_remain_balanced() {
        let _guard = test_guard();
        stop();
        let calls = (0..16)
            .map(|_| std::thread::spawn(|| call(fortuna_version, "{}")))
            .collect::<Vec<_>>();
        for response in calls {
            assert_eq!(FORTUNA_STATUS_OK, response.join().unwrap().status);
        }
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_authenticated_session_when_accounts_are_read_concurrently_then_each_call_succeeds() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let (_, account_id) = seed(&path, "concurrent-secret", "42.1000");
        let authentication = call(
            fortuna_api_local_accounts_authenticate,
            r#"{"name":"Local User","secret":"concurrent-secret"}"#,
        );
        let token = token_from(&authentication);

        let calls = (0..8)
            .map(|_| {
                let token = token.clone();
                std::thread::spawn(move || {
                    call(
                        fortuna_api_accounts_get_by_id,
                        &serde_json::json!({"token":token,"id":account_id}).to_string(),
                    )
                })
            })
            .collect::<Vec<_>>();
        for response in calls {
            assert_eq!(FORTUNA_STATUS_OK, response.join().unwrap().status);
        }
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_checked_in_http_contract_when_discovering_capabilities_then_every_offline_route_is_listed()
     {
        let _guard = test_guard();
        stop();
        let response = call(fortuna_capabilities, "{}");
        assert_eq!(FORTUNA_STATUS_OK, response.status);
        let body: Value = serde_json::from_str(&response.body).unwrap();
        let operations = body["data"]["operations"].as_array().unwrap();
        let not_implemented = body["data"]["notImplemented"].as_array().unwrap();
        // Every eligible route is exported; the ones the core does not implement are reported
        // as such instead of as available operations.
        assert_eq!(115, operations.len() + not_implemented.len());
        assert_eq!(89, operations.len());
        assert!(!operations.iter().any(|operation| {
            operation["method"] == "POST" && operation["path"] == "/api/imports/excel"
        }));
        assert!(not_implemented.iter().any(|operation| {
            operation["method"] == "POST"
                && operation["path"] == "/api/imports/excel"
                && operation["symbol"] == "fortuna_api_imports_excel_post"
                && operation["longRunning"] == true
        }));
        assert!(operations.iter().any(|operation| {
            operation["method"] == "GET" && operation["path"] == "/api/recurring-transactions"
        }));
        assert!(operations.iter().any(|operation| {
            operation["method"] == "GET"
                && operation["path"] == "/api/transactions/{id}/attachments"
        }));
        assert!(!operations.iter().any(|operation| {
            operation["path"].as_str().is_some_and(|path| {
                path.starts_with("/api/auth")
                    || path.starts_with("/api/connections")
                    || path.starts_with("/api/me/consents")
            })
        }));
        assert!(operations.iter().any(|operation| {
            operation["method"] == "POST" && operation["path"] == "/api/me/erasure"
        }));
        assert!(operations.iter().any(|operation| {
            operation["method"] == "POST" && operation["path"] == "/api/me/data-export"
        }));
        assert!(!operations.iter().any(|operation| {
            operation["method"] == "DELETE" && operation["path"] == "/api/users/{id}"
        }));
        let unavailable = body["data"]["unavailable"].as_array().unwrap();
        assert_eq!(7, unavailable.len());
        assert!(unavailable.iter().any(|entry| {
            entry["routes"] == "/api/me/consents/**"
                && entry["reason"]
                    .as_str()
                    .is_some_and(|reason| reason.contains("hosted integrations"))
        }));
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_every_generated_route_when_called_before_initialization_then_each_symbol_is_safe() {
        let _guard = test_guard();
        stop();
        assert_eq!(
            115,
            NATIVE_OPERATION_FUNCTIONS.len() + NOT_IMPLEMENTED_OPERATION_FUNCTIONS.len()
        );
        for (functions, status) in [
            (NATIVE_OPERATION_FUNCTIONS, FORTUNA_STATUS_NOT_INITIALIZED),
            (
                NOT_IMPLEMENTED_OPERATION_FUNCTIONS,
                FORTUNA_STATUS_NOT_IMPLEMENTED,
            ),
        ] {
            for function in functions {
                let response = call(*function, "{}");
                assert_eq!(status, response.status, "{}", response.body);
                assert_eq!(
                    Value::Bool(false),
                    serde_json::from_str::<Value>(&response.body).unwrap()["success"]
                );
            }
        }
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_confirmed_local_owner_when_erased_then_identity_data_and_mapping_are_gone_but_audit_remains()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);
        let account = call(
            fortuna_api_accounts_post,
            &serde_json::json!({
                "token": token,
                "body": {
                    "name": "Erased account",
                    "accountType": 1,
                    "currencyCode": "BRL",
                    "openingBalance": 1
                }
            })
            .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_CREATED, account.status, "{}", account.body);
        let user_id: String = Connection::open(&path)
            .unwrap()
            .query_row("SELECT user_id FROM native_local_account", [], |row| {
                row.get(0)
            })
            .unwrap();

        let invalid = call(
            fortuna_api_me_erasure_post,
            &serde_json::json!({"token": token, "body": {"confirmation": "erase"}}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_BAD_REQUEST, invalid.status);

        let erased = call(
            fortuna_api_me_erasure_post,
            &serde_json::json!({"token": token, "body": {"confirmation": "ERASE"}}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, erased.status, "{}", erased.body);
        let body: Value = serde_json::from_str(&erased.body).unwrap();
        assert_eq!(Value::Bool(true), body["data"]["irreversible"]);
        assert_eq!(1, body["data"]["erased"]["profiles"]);
        assert_eq!(1, body["data"]["erased"]["credentials"]);
        assert_eq!(10, body["data"]["erased"]["recoveryCodes"]);
        assert_eq!(1, body["data"]["erased"]["financialRecords"]);

        let connection = Connection::open(&path).unwrap();
        for table in [
            "native_user_profile",
            "native_local_account",
            "native_local_recovery_code",
            "native_offline_record",
            "native_audit_subject",
        ] {
            let count: i64 = connection
                .query_row(&format!("SELECT count(*) FROM {table}"), [], |row| {
                    row.get(0)
                })
                .unwrap();
            assert_eq!(0, count, "{table}");
        }
        let retained: (i64, String, i64) = connection
            .query_row(
                "SELECT count(*), min(subject_reference),
                        count(DISTINCT subject_reference)
                 FROM native_audit_entry",
                [],
                |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?)),
            )
            .unwrap();
        assert_eq!(2, retained.0);
        assert_eq!(1, retained.2);
        assert_ne!(user_id, retained.1);
        drop(connection);

        assert_eq!(
            FORTUNA_STATUS_NOT_FOUND,
            call(
                fortuna_api_me_erasure_post,
                &serde_json::json!({"token": token, "body": {"confirmation": "ERASE"}}).to_string(),
            )
            .status
        );
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_local_recovery_code_when_recovering_and_regenerating_then_one_time_credentials_match_http_behavior()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let created = call(
            fortuna_api_local_accounts_post,
            r#"{"displayName":"Local User","secret":"initial-secret","storageMode":1}"#,
        );
        let created_json: Value = serde_json::from_str(&created.body).unwrap();
        let code = created_json["data"]["recoveryCodes"][0]
            .as_str()
            .unwrap()
            .to_owned();

        let recovered = call(
            fortuna_api_local_accounts_recover_post,
            &serde_json::json!({
                "name":"Local User","recoveryCode":code,"newSecret":"recovered-secret"
            })
            .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, recovered.status, "{}", recovered.body);
        assert_eq!(
            9,
            serde_json::from_str::<Value>(&recovered.body).unwrap()["data"]["remainingRecoveryCodes"]
        );
        assert_eq!(
            FORTUNA_STATUS_UNAUTHORIZED,
            call(
                fortuna_api_local_accounts_authenticate_post,
                r#"{"name":"Local User","secret":"initial-secret"}"#,
            )
            .status
        );
        let authenticated = call(
            fortuna_api_local_accounts_authenticate_post,
            r#"{"name":"Local User","secret":"recovered-secret"}"#,
        );
        assert_eq!(FORTUNA_STATUS_OK, authenticated.status);
        let regenerated = call(
            fortuna_api_local_accounts_recovery_codes_regenerate_post,
            &serde_json::json!({
                "token":token_from(&authenticated),"body":{"secret":"recovered-secret"}
            })
            .to_string(),
        );
        assert_eq!(
            FORTUNA_STATUS_OK, regenerated.status,
            "{}",
            regenerated.body
        );
        assert_eq!(
            10,
            serde_json::from_str::<Value>(&regenerated.body).unwrap()["data"]["recoveryCodes"]
                .as_array()
                .unwrap()
                .len()
        );
        assert_eq!(
            FORTUNA_STATUS_UNAUTHORIZED,
            call(
                fortuna_api_local_accounts_recover_post,
                &serde_json::json!({
                    "name":"Local User","recoveryCode":code,"newSecret":"another-secret"
                })
                .to_string(),
            )
            .status
        );
        stop();
    }

    #[test]
    fn given_local_account_when_using_identity_reference_and_exact_money_then_http_wire_contract_is_preserved()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);

        let profile = call(
            fortuna_api_me_get,
            &serde_json::json!({"token": token}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, profile.status);
        let profile_json: Value = serde_json::from_str(&profile.body).unwrap();
        assert_eq!("Local User", profile_json["data"]["displayName"]);

        let currencies = call(
            fortuna_api_currencies_get,
            &serde_json::json!({"token": token}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, currencies.status);
        let currencies_json: Value = serde_json::from_str(&currencies.body).unwrap();
        assert!(
            currencies_json["data"]["currencies"]
                .as_array()
                .unwrap()
                .len()
                >= 170
        );
        assert_eq!("AED", currencies_json["data"]["currencies"][0]["code"]);

        let rate = call(
            fortuna_api_exchange_rates_post,
            &format!(
                r#"{{"token":"{token}","body":{{"baseCurrencyCode":"USD","quoteCurrencyCode":"BRL","rate":1.0001,"rateDate":"2026-09-08"}}}}"#
            ),
        );
        assert_eq!(FORTUNA_STATUS_CREATED, rate.status, "{}", rate.body);
        assert!(rate.body.contains("\"rate\":\"1.0001\""));

        let converted = call(
            fortuna_api_exchange_rates_convert_post,
            &format!(
                r#"{{"token":"{token}","body":{{"amounts":[{{"amount":123456789012345.6789,"currencyCode":"USD"}}],"displayCurrencyCode":"BRL","figureDate":"2026-09-09"}}}}"#
            ),
        );
        assert_eq!(FORTUNA_STATUS_OK, converted.status, "{}", converted.body);
        assert!(converted.body.contains("123469134691246.91"));
        stop();
    }

    #[test]
    fn given_owned_records_when_using_each_offline_area_then_crud_lifecycle_jobs_and_reports_are_available()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);

        let account = call(
            fortuna_api_accounts_post,
            &format!(
                r#"{{"token":"{token}","body":{{"name":"Wallet","institution":null,"accountType":1,"currencyCode":"BRL","openingBalance":123456789012345.6789}}}}"#
            ),
        );
        assert_eq!(FORTUNA_STATUS_CREATED, account.status, "{}", account.body);
        assert!(account.body.contains("123456789012345.6789"));
        let account_id = serde_json::from_str::<Value>(&account.body).unwrap()["data"]["id"]
            .as_str()
            .unwrap()
            .to_owned();

        let transaction = call(
            fortuna_api_transactions_post,
            &serde_json::json!({
                "token": token,
                "body": {"financialAccountId":account_id,"direction":2,"amount":19.90,
                    "description":"Offline purchase","occurredAt":"2026-09-09T12:00:00Z"}
            })
            .to_string(),
        );
        assert_eq!(
            FORTUNA_STATUS_CREATED, transaction.status,
            "{}",
            transaction.body
        );
        let transaction_id =
            serde_json::from_str::<Value>(&transaction.body).unwrap()["data"]["id"]
                .as_str()
                .unwrap()
                .to_owned();

        let category = call(
            fortuna_api_categories_post,
            &serde_json::json!({"token":token,"body":{"name":"Food","color":"#112233"}})
                .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_CREATED, category.status);

        let attachment = call(
            fortuna_api_transactions_by_id_attachments_post,
            &serde_json::json!({
                "token":token,"route":{"id":transaction_id},
                "body":{"fileName":"receipt.txt","contentType":"text/plain","content":"cmVjZWlwdA=="}
            })
            .to_string(),
        );
        assert_eq!(
            FORTUNA_STATUS_CREATED, attachment.status,
            "{}",
            attachment.body
        );

        let deleted = call(
            fortuna_api_accounts_by_id_delete,
            &serde_json::json!({"token":token,"route":{"id":account_id}}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, deleted.status);
        let restored = call(
            fortuna_api_accounts_by_id_restore_post,
            &serde_json::json!({"token":token,"route":{"id":account_id}}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, restored.status);

        let audit = call(
            fortuna_api_audit_entries_get,
            &serde_json::json!({"token":token}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, audit.status);
        assert!(
            serde_json::from_str::<Value>(&audit.body).unwrap()["data"]
                .as_array()
                .unwrap()
                .len()
                >= 5
        );

        let import = call(
            fortuna_api_imports_excel_post,
            &serde_json::json!({"token":token,"body":{"fileName":"records.xlsx","content":"AA=="}})
                .to_string(),
        );
        // Imports and reports are not implemented offline and say so rather than pretending.
        assert_eq!(
            FORTUNA_STATUS_NOT_IMPLEMENTED, import.status,
            "{}",
            import.body
        );
        let report = call(
            fortuna_api_reports_table_post,
            &serde_json::json!({"token":token,"body":{"dataSet":1}}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_NOT_IMPLEMENTED, report.status);
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_native_owner_data_when_personal_archive_completes_then_zip_is_complete_exact_and_secret_free()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);

        let account = call(
            fortuna_api_accounts_post,
            &format!(
                r#"{{"token":"{token}","body":{{"name":"Exact account","institution":"Native bank","accountType":1,"currencyCode":"BRL","openingBalance":123456789012345.6789}}}}"#
            ),
        );
        assert_eq!(FORTUNA_STATUS_CREATED, account.status, "{}", account.body);
        let account_id = serde_json::from_str::<Value>(&account.body).unwrap()["data"]["id"]
            .as_str()
            .unwrap()
            .to_owned();
        let transaction = call(
            fortuna_api_transactions_post,
            &serde_json::json!({
                "token": token,
                "body": {"financialAccountId":account_id,"direction":2,"amount":0.0001,
                    "description":"Portable","occurredAt":"2026-09-10T01:00:00Z"}
            })
            .to_string(),
        );
        let transaction_id =
            serde_json::from_str::<Value>(&transaction.body).unwrap()["data"]["id"]
                .as_str()
                .unwrap()
                .to_owned();
        let attachment = call(
            fortuna_api_transactions_by_id_attachments_post,
            &serde_json::json!({
                "token":token,"route":{"id":transaction_id},
                "body":{"fileName":"receipt.txt","contentType":"text/plain",
                    "content":STANDARD.encode("native attachment")}
            })
            .to_string(),
        );
        assert_eq!(
            FORTUNA_STATUS_CREATED, attachment.status,
            "{}",
            attachment.body
        );

        let connection = Connection::open(&path).unwrap();
        let user_id: String = connection
            .query_row(
                "SELECT public_id FROM native_user_profile LIMIT 1",
                [],
                |row| row.get(0),
            )
            .unwrap();
        let imported_id = Uuid::new_v4();
        connection
            .execute(
                "INSERT INTO native_offline_record(
                 public_id, user_id, resource, body_json, is_deleted, created_at, updated_at)
             VALUES (?1, ?2, 'imported-records', ?3, 0, ?4, ?4)",
                params![
                    imported_id.to_string(),
                    user_id,
                    serde_json::json!({
                        "id": imported_id,
                        "rawPayload": {"access_token":"NATIVE-ACCESS-TOKEN",
                            "nested":{"password":"NATIVE-PASSWORD"}}
                    })
                    .to_string(),
                    "2026-09-10T01:00:00Z"
                ],
            )
            .unwrap();
        drop(connection);

        let queued = call(
            fortuna_api_me_data_export_post,
            &serde_json::json!({"token":token}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_ACCEPTED, queued.status, "{}", queued.body);
        let job_id = serde_json::from_str::<Value>(&queued.body).unwrap()["data"]["jobId"]
            .as_str()
            .unwrap()
            .to_owned();

        let completed = (0..200)
            .find_map(|_| {
                let response = call(
                    fortuna_api_me_data_export_by_job_id_get,
                    &serde_json::json!({"token":token,"route":{"jobId":job_id}}).to_string(),
                );
                let body = serde_json::from_str::<Value>(&response.body).unwrap();
                if body["data"]["status"] == 3 {
                    Some(body)
                } else {
                    std::thread::sleep(std::time::Duration::from_millis(10));
                    None
                }
            })
            .expect("personal archive did not complete");
        let content = STANDARD
            .decode(completed["data"]["contentBase64"].as_str().unwrap())
            .unwrap();
        let mut zip = ZipArchive::new(Cursor::new(content)).unwrap();
        assert!(zip.by_name("manifest.json").is_ok());
        assert!(zip.by_name("schemas/profile.schema.json").is_ok());
        let mut accounts = String::new();
        zip.by_name("data/financial-accounts.json")
            .unwrap()
            .read_to_string(&mut accounts)
            .unwrap();
        assert!(accounts.contains(r#""openingBalance": "123456789012345.6789""#));
        let mut imported = String::new();
        zip.by_name("data/imported-records.json")
            .unwrap()
            .read_to_string(&mut imported)
            .unwrap();
        assert!(!imported.contains("NATIVE-ACCESS-TOKEN"));
        assert!(!imported.contains("NATIVE-PASSWORD"));
        assert!(imported.contains("[redacted]"));
        let attachment_path = (0..zip.len())
            .find_map(|index| {
                let name = zip.by_index(index).ok()?.name().to_owned();
                name.starts_with("attachments/").then_some(name)
            })
            .expect("attachment file missing");
        let mut attachment_bytes = Vec::new();
        zip.by_name(&attachment_path)
            .unwrap()
            .read_to_end(&mut attachment_bytes)
            .unwrap();
        assert_eq!(b"native attachment", attachment_bytes.as_slice());

        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_foreign_record_when_requested_through_native_route_then_it_is_indistinguishable_from_missing()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);
        let foreign_user = Uuid::new_v4();
        let foreign_account = Uuid::new_v4();
        let now = "2026-09-09T12:34:56.1234567Z";
        let connection = Connection::open(&path).unwrap();
        connection
            .execute(
                "INSERT INTO native_user_profile(public_id, display_name, display_currency, created_at, updated_at)
                 VALUES (?1, 'Foreign User', 'BRL', ?2, ?2)",
                params![foreign_user.to_string(), now],
            )
            .unwrap();
        connection
            .execute(
                "INSERT INTO native_offline_record(user_id, resource, public_id, body_json, is_deleted, created_at, updated_at)
                 VALUES (?1, 'accounts', ?2, ?3, 0, ?4, ?4)",
                params![
                    foreign_user.to_string(),
                    foreign_account.to_string(),
                    serde_json::json!({"id":foreign_account,"name":"Hidden","isDeleted":false}).to_string(),
                    now
                ],
            )
            .unwrap();

        let foreign = call(
            fortuna_api_accounts_by_id_get,
            &serde_json::json!({"token":token,"route":{"id":foreign_account}}).to_string(),
        );
        let missing = call(
            fortuna_api_accounts_by_id_get,
            &serde_json::json!({"token":token,"route":{"id":Uuid::new_v4()}}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_NOT_FOUND, foreign.status);
        assert_eq!(FORTUNA_STATUS_NOT_FOUND, missing.status);
        let foreign_json: Value = serde_json::from_str(&foreign.body).unwrap();
        let missing_json: Value = serde_json::from_str(&missing.body).unwrap();
        assert_eq!(foreign_json["errors"], missing_json["errors"]);
        assert_eq!(foreign_json["messages"], missing_json["messages"]);
        assert_eq!(foreign_json["success"], missing_json["success"]);
        stop();
    }

    #[test]
    fn given_each_offline_area_when_authentication_is_invalid_then_the_http_failure_contract_is_shared()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let calls: [extern "C" fn(*const c_char, *mut *mut c_char) -> c_int; 8] = [
            fortuna_api_me_get,
            fortuna_api_currencies_get,
            fortuna_api_accounts_get,
            fortuna_api_transactions_get,
            fortuna_api_categories_get,
            fortuna_api_audit_entries_get,
            fortuna_api_import_jobs_get,
            fortuna_api_budgets_get,
        ];
        for function in calls {
            let response = call(function, r#"{"token":"invalid"}"#);
            assert_eq!(
                FORTUNA_STATUS_UNAUTHORIZED, response.status,
                "{}",
                response.body
            );
            let body: Value = serde_json::from_str(&response.body).unwrap();
            assert_eq!(Value::Bool(false), body["success"]);
            assert_eq!(
                "The local authentication token is invalid or expired.",
                body["errors"][0]
            );
        }
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_token_lifetime_outside_bounds_when_initializing_then_request_is_rejected() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        for lifetime in [0, -1, MAXIMUM_TOKEN_LIFETIME_SECONDS + 1, i64::MAX] {
            let response = call(
                fortuna_initialize,
                &serde_json::json!({
                    "databasePath": path,
                    "tokenLifetimeSeconds": lifetime
                })
                .to_string(),
            );
            assert_eq!(
                FORTUNA_STATUS_BAD_REQUEST, response.status,
                "{lifetime}: {}",
                response.body
            );
            assert!(current_core().is_none());
        }

        let accepted = call(
            fortuna_initialize,
            &serde_json::json!({
                "databasePath": path,
                "tokenLifetimeSeconds": MAXIMUM_TOKEN_LIFETIME_SECONDS
            })
            .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, accepted.status, "{}", accepted.body);
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_unrepresentable_lifetime_when_computing_session_expiry_then_none_is_returned() {
        let now = Utc::now();

        assert_eq!(None, session_expiry(now, i64::MAX));
        assert_eq!(None, session_expiry(DateTime::<Utc>::MAX_UTC, 1));
        assert_eq!(Some(now + Duration::seconds(60)), session_expiry(now, 60));
    }

    #[test]
    fn given_session_cannot_be_issued_when_recovering_then_recovery_code_is_not_consumed() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let created = call(
            fortuna_api_local_accounts_post,
            r#"{"displayName":"Local User","secret":"initial-secret","storageMode":1}"#,
        );
        let code = serde_json::from_str::<Value>(&created.body).unwrap()["data"]["recoveryCodes"]
            [0]
        .as_str()
        .unwrap()
        .to_owned();
        let recover = serde_json::json!({
            "name":"Local User","recoveryCode":code,"newSecret":"recovered-secret"
        })
        .to_string();

        // Swap in a core whose lifetime cannot be added to "now"; initialization refuses such a
        // value, so this simulates any failure between validating and issuing the session.
        let original = current_core().unwrap();
        let broken = Arc::new(Core {
            store: original.store.clone(),
            local_auth_enabled: true,
            token_lifetime_seconds: i64::MAX,
            sessions: Mutex::new(HashMap::new()),
        });
        *core_slot().lock().unwrap() = Some(broken);
        let failed = call(fortuna_api_local_accounts_recover_post, &recover);
        assert_eq!(
            FORTUNA_STATUS_INTERNAL_ERROR, failed.status,
            "{}",
            failed.body
        );

        *core_slot().lock().unwrap() = Some(original);
        assert_eq!(
            FORTUNA_STATUS_UNAUTHORIZED,
            call(
                fortuna_api_local_accounts_authenticate_post,
                r#"{"name":"Local User","secret":"recovered-secret"}"#,
            )
            .status
        );
        let recovered = call(fortuna_api_local_accounts_recover_post, &recover);
        assert_eq!(FORTUNA_STATUS_OK, recovered.status, "{}", recovered.body);
        assert_eq!(
            9,
            serde_json::from_str::<Value>(&recovered.body).unwrap()["data"]["remainingRecoveryCodes"]
        );
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_paginated_route_when_page_is_requested_then_only_that_page_is_returned() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);
        for name in ["First", "Second", "Third"] {
            let account = call(
                fortuna_api_accounts_post,
                &serde_json::json!({
                    "token": token,
                    "body": {"name":name,"institution":null,"accountType":1,"currencyCode":"BRL","openingBalance":0}
                })
                .to_string(),
            );
            assert_eq!(FORTUNA_STATUS_CREATED, account.status, "{}", account.body);
        }
        let audit = |query: Value| {
            let response = call(
                fortuna_api_audit_entries_get,
                &serde_json::json!({"token": token, "query": query}).to_string(),
            );
            let body: Value = serde_json::from_str(&response.body).unwrap();
            (response.status, body)
        };

        let (status, everything) = audit(serde_json::json!({}));
        assert_eq!(FORTUNA_STATUS_OK, status);
        let total = everything["totalItems"].as_u64().unwrap();
        assert!(total >= 3);
        assert_eq!(100, everything["pageSize"]);
        assert_eq!(1, everything["pageNumber"]);

        let (status, second) = audit(serde_json::json!({"PageNumber": 2, "PageSize": 2}));
        assert_eq!(FORTUNA_STATUS_OK, status);
        assert_eq!(
            everything["data"].as_array().unwrap()[2..4.min(total as usize)],
            second["data"].as_array().unwrap()[..]
        );
        assert_eq!(total, second["totalItems"].as_u64().unwrap());
        assert_eq!(total.div_ceil(2), second["totalPages"].as_u64().unwrap());

        let (status, from_strings) = audit(serde_json::json!({"PageNumber": "2", "PageSize": "2"}));
        assert_eq!(FORTUNA_STATUS_OK, status);
        assert_eq!(second["data"], from_strings["data"]);

        let (status, past_end) = audit(serde_json::json!({"PageNumber": 1000, "PageSize": 2}));
        assert_eq!(FORTUNA_STATUS_OK, status);
        assert_eq!(0, past_end["data"].as_array().unwrap().len());

        let (status, clamped) = audit(serde_json::json!({"PageSize": 1000}));
        assert_eq!(FORTUNA_STATUS_OK, status);
        assert_eq!(100, clamped["pageSize"]);

        for (query, error) in [
            (
                serde_json::json!({"PageNumber": 0}),
                "PageNumber must be at least 1.",
            ),
            (
                serde_json::json!({"PageNumber": -1}),
                "PageNumber must be at least 1.",
            ),
            (
                serde_json::json!({"PageNumber": "x"}),
                "PageNumber must be at least 1.",
            ),
            (
                serde_json::json!({"PageSize": 0}),
                "PageSize must be at least 1.",
            ),
            (
                serde_json::json!({"PageSize": 1.5}),
                "PageSize must be at least 1.",
            ),
        ] {
            let (status, body) = audit(query.clone());
            assert_eq!(FORTUNA_STATUS_BAD_REQUEST, status, "{query}");
            assert_eq!(error, body["errors"][0], "{query}");
        }
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_transaction_list_when_page_is_requested_then_items_are_paged() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);
        let account = call(
            fortuna_api_accounts_post,
            &serde_json::json!({
                "token": token,
                "body": {"name":"Wallet","institution":null,"accountType":1,"currencyCode":"BRL","openingBalance":0}
            })
            .to_string(),
        );
        let account_id = serde_json::from_str::<Value>(&account.body).unwrap()["data"]["id"]
            .as_str()
            .unwrap()
            .to_owned();
        for amount in [1, 2, 3] {
            let transaction = call(
                fortuna_api_transactions_post,
                &serde_json::json!({
                    "token": token,
                    "body": {"financialAccountId":account_id,"direction":2,"amount":amount,
                        "description":"Offline purchase","occurredAt":"2026-09-09T12:00:00Z"}
                })
                .to_string(),
            );
            assert_eq!(
                FORTUNA_STATUS_CREATED, transaction.status,
                "{}",
                transaction.body
            );
        }

        let page = call(
            fortuna_api_transactions_get,
            &serde_json::json!({"token": token, "query": {"PageNumber": 2, "PageSize": 2}})
                .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, page.status, "{}", page.body);
        let body: Value = serde_json::from_str(&page.body).unwrap();
        assert_eq!(1, body["data"]["items"].as_array().unwrap().len());
        assert_eq!(2, body["data"]["pageNumber"]);
        assert_eq!(2, body["data"]["pageSize"]);
        assert_eq!(3, body["data"]["totalItems"]);
        assert_eq!(2, body["data"]["totalPages"]);

        let invalid = call(
            fortuna_api_transactions_get,
            &serde_json::json!({"token": token, "query": {"PageNumber": 0}}).to_string(),
        );
        assert_eq!(
            FORTUNA_STATUS_BAD_REQUEST, invalid.status,
            "{}",
            invalid.body
        );
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    fn body_of(response: &Response) -> Value {
        serde_json::from_str(&response.body).unwrap()
    }

    fn create_account(token: &str, opening_balance: &str) -> String {
        let account = call(
            fortuna_api_accounts_post,
            &format!(
                r#"{{"token":"{token}","body":{{"name":"Wallet","accountType":1,"currencyCode":"BRL","openingBalance":{opening_balance}}}}}"#
            ),
        );
        assert_eq!(FORTUNA_STATUS_CREATED, account.status, "{}", account.body);
        body_of(&account)["data"]["id"].as_str().unwrap().to_owned()
    }

    #[test]
    fn given_disabled_local_auth_when_recovering_or_regenerating_then_routes_are_hidden() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let created = call(
            fortuna_api_local_accounts_post,
            r#"{"displayName":"Local User","secret":"initial-secret","storageMode":1}"#,
        );
        let code = body_of(&created)["data"]["recoveryCodes"][0]
            .as_str()
            .unwrap()
            .to_owned();
        stop();

        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, false).status);
        let recovered = call(
            fortuna_api_local_accounts_recover_post,
            &serde_json::json!({
                "body": {"name":"Local User","recoveryCode":code,"newSecret":"recovered-secret"}
            })
            .to_string(),
        );
        assert_eq!(
            FORTUNA_STATUS_NOT_FOUND, recovered.status,
            "{}",
            recovered.body
        );
        assert_eq!(LOCAL_AUTH_DISABLED, body_of(&recovered)["errors"][0]);
        let regenerated = call(
            fortuna_api_local_accounts_recovery_codes_regenerate_post,
            r#"{"token":"anything","body":{"secret":"initial-secret"}}"#,
        );
        assert_eq!(FORTUNA_STATUS_NOT_FOUND, regenerated.status);
        stop();

        // The code was not consumed while local authentication was disabled.
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let recovered = call(
            fortuna_api_local_accounts_recover_post,
            &serde_json::json!({
                "body": {"name":"Local User","recoveryCode":code,"newSecret":"recovered-secret"}
            })
            .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, recovered.status, "{}", recovered.body);
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_recovery_code_in_lower_case_with_padding_when_recovering_then_it_matches_as_in_http() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let created = call(
            fortuna_api_local_accounts_post,
            r#"{"displayName":"Local User","secret":"initial-secret","storageMode":1}"#,
        );
        let code = body_of(&created)["data"]["recoveryCodes"][0]
            .as_str()
            .unwrap()
            .to_owned();

        let recovered = call(
            fortuna_api_local_accounts_recover_post,
            &serde_json::json!({
                "body": {
                    "name": "  Local User ",
                    "recoveryCode": format!(" {} ", code.to_lowercase()),
                    "newSecret": "recovered-secret"
                }
            })
            .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, recovered.status, "{}", recovered.body);
        let authenticated = call(
            fortuna_api_local_accounts_authenticate_post,
            r#"{"name":" Local User ","secret":"recovered-secret"}"#,
        );
        assert_eq!(
            FORTUNA_STATUS_OK, authenticated.status,
            "{}",
            authenticated.body
        );
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_out_of_bounds_local_account_input_when_creating_then_400_and_not_conflict() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let long_name = "n".repeat(201);
        for body in [
            serde_json::json!({"displayName": long_name, "secret": "long-enough-secret"}),
            // Eight bytes but four characters: too short, as the HTTP host counts characters.
            serde_json::json!({"displayName": "Local User", "secret": "çççç"}),
            serde_json::json!({"displayName": "Local User", "secret": "s".repeat(1025)}),
        ] {
            let created = call(
                fortuna_api_local_accounts_post,
                &serde_json::json!({ "body": body }).to_string(),
            );
            assert_eq!(
                FORTUNA_STATUS_BAD_REQUEST, created.status,
                "{}",
                created.body
            );
        }
        let authenticated = call(
            fortuna_api_local_accounts_authenticate_post,
            &serde_json::json!({"name": "Local User", "secret": "s".repeat(1025)}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_BAD_REQUEST, authenticated.status);

        let created = call(
            fortuna_api_local_accounts_post,
            r#"{"displayName":"Local User","secret":"initial-secret"}"#,
        );
        assert_eq!(FORTUNA_STATUS_CREATED, created.status, "{}", created.body);
        let duplicate = call(
            fortuna_api_local_accounts_post,
            r#"{"displayName":"Other User","secret":"initial-secret"}"#,
        );
        assert_eq!(FORTUNA_STATUS_CONFLICT, duplicate.status);
        assert_eq!(
            "A local account already exists on this installation.",
            body_of(&duplicate)["errors"][0]
        );
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_version_one_database_with_profile_reference_when_erased_and_reinitialized_then_both_succeed()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let user_id = Uuid::new_v4();
        let account_id = Uuid::new_v4();
        let now = "2026-09-09T12:34:56.1234567Z";
        let connection = Connection::open(&path).unwrap();
        connection
            .execute_batch(
                "PRAGMA foreign_keys = ON;
                 CREATE TABLE native_user_profile (
                     public_id TEXT PRIMARY KEY NOT NULL, display_name TEXT NOT NULL,
                     created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                 CREATE TABLE native_local_account (
                     public_id TEXT PRIMARY KEY NOT NULL,
                     user_id TEXT NOT NULL UNIQUE REFERENCES native_user_profile(public_id) ON DELETE CASCADE,
                     name TEXT NOT NULL UNIQUE, secret_hash TEXT NOT NULL,
                     created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                 CREATE TABLE native_financial_account (
                     public_id TEXT PRIMARY KEY NOT NULL,
                     user_id TEXT NOT NULL REFERENCES native_user_profile(public_id) ON DELETE RESTRICT,
                     name TEXT NOT NULL, institution TEXT NULL, account_type INTEGER NOT NULL,
                     currency_code TEXT NOT NULL, opening_balance TEXT NOT NULL,
                     is_deleted INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);",
            )
            .unwrap();
        connection
            .execute(
                "INSERT INTO native_user_profile VALUES (?1, 'Local User', ?2, ?2)",
                params![user_id.to_string(), now],
            )
            .unwrap();
        connection
            .execute(
                "INSERT INTO native_local_account VALUES (?1, ?2, 'Local User', ?3, ?4, ?4)",
                params![
                    Uuid::new_v4().to_string(),
                    user_id.to_string(),
                    hash_secret("migration-secret").unwrap(),
                    now
                ],
            )
            .unwrap();
        connection
            .execute(
                "INSERT INTO native_financial_account VALUES (?1, ?2, 'Migrated', NULL, 1, 'BRL',
                 '10.00', 0, ?3, ?3)",
                params![account_id.to_string(), user_id.to_string(), now],
            )
            .unwrap();
        drop(connection);

        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let token = token_from(&call(
            fortuna_api_local_accounts_authenticate_post,
            r#"{"name":"Local User","secret":"migration-secret"}"#,
        ));
        let route = serde_json::json!({"token": token, "route": {"id": account_id}}).to_string();
        assert_eq!(
            FORTUNA_STATUS_OK,
            call(fortuna_api_accounts_by_id_delete, &route).status
        );
        assert_eq!(
            FORTUNA_STATUS_OK,
            call(fortuna_api_accounts_by_id_hard_delete, &route).status
        );
        stop();

        // A hard-deleted migrated account is not copied back by a later initialization.
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let token = token_from(&call(
            fortuna_api_local_accounts_authenticate_post,
            r#"{"name":"Local User","secret":"migration-secret"}"#,
        ));
        let route = serde_json::json!({"token": token, "route": {"id": account_id}}).to_string();
        assert_eq!(
            FORTUNA_STATUS_NOT_FOUND,
            call(fortuna_api_accounts_by_id_get, &route).status
        );

        let erased = call(
            fortuna_api_me_erasure_post,
            &serde_json::json!({"token": token, "body": {"confirmation": "ERASE"}}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, erased.status, "{}", erased.body);
        stop();
        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_jobs_of_each_kind_when_read_through_job_routes_then_kinds_stay_apart_and_retry_is_not_implemented()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);
        let connection = Connection::open(&path).unwrap();
        let user_id: String = connection
            .query_row("SELECT public_id FROM native_user_profile", [], |row| {
                row.get(0)
            })
            .unwrap();
        let (import_id, export_id) = (Uuid::new_v4(), Uuid::new_v4());
        for (id, kind) in [
            (import_id, "/api/imports/excel"),
            (export_id, "/api/exports"),
        ] {
            connection
                .execute(
                    "INSERT INTO native_operation_job(
                         public_id, user_id, kind, status, progress, request_json, created_at,
                         updated_at, failure_reason)
                     VALUES (?1, ?2, ?3, 4, 0, '{}', '2026-09-01T00:00:00.0000000Z',
                         '2026-09-01T00:00:00.0000000Z', 'Not processed.')",
                    params![id.to_string(), user_id, kind],
                )
                .unwrap();
        }

        let export_route =
            serde_json::json!({"token": token, "route": {"id": export_id}}).to_string();
        let import_route =
            serde_json::json!({"token": token, "route": {"id": import_id}}).to_string();
        // Import-job routes see only import jobs and the export route only export jobs.
        for (function, route) in [
            (
                fortuna_api_import_jobs_by_id_get as NativeOperationFunction,
                &export_route,
            ),
            (fortuna_api_import_jobs_by_id_records_get, &export_route),
            (fortuna_api_exports_by_id_get, &import_route),
        ] {
            assert_eq!(FORTUNA_STATUS_NOT_FOUND, call(function, route).status);
        }
        let records = call(fortuna_api_import_jobs_by_id_records_get, &import_route);
        assert_eq!(FORTUNA_STATUS_OK, records.status, "{}", records.body);
        assert_eq!(0, body_of(&records)["totalItems"]);
        let listed = call(
            fortuna_api_import_jobs_get,
            &serde_json::json!({"token": token}).to_string(),
        );
        assert_eq!(1, body_of(&listed)["totalItems"], "{}", listed.body);
        assert_eq!(
            "Not processed.",
            body_of(&listed)["data"][0]["failureReason"]
        );

        // Nothing would process a requeued import, so retrying is refused, not faked.
        let retried = call(fortuna_api_import_jobs_by_id_retry_post, &import_route);
        assert_eq!(
            FORTUNA_STATUS_NOT_IMPLEMENTED, retried.status,
            "{}",
            retried.body
        );
        let status: i64 = connection
            .query_row(
                "SELECT status FROM native_operation_job WHERE public_id = ?1",
                params![import_id.to_string()],
                |row| row.get(0),
            )
            .unwrap();
        assert_eq!(4, status);
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_soft_deleted_record_when_updating_then_it_is_not_found_and_lifecycle_fields_stay_server_owned()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);
        let account_id = create_account(&token, "1");

        let updated = call(
            fortuna_api_accounts_by_id_put,
            &serde_json::json!({
                "token": token, "route": {"id": account_id},
                "body": {"name": "Renamed", "isDeleted": true, "createdAt": "2000-01-01T00:00:00Z"}
            })
            .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, updated.status, "{}", updated.body);
        let read = call(
            fortuna_api_accounts_by_id_get,
            &serde_json::json!({"token": token, "route": {"id": account_id}}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, read.status, "{}", read.body);
        let data = body_of(&read)["data"].clone();
        assert_eq!("Renamed", data["name"]);
        assert_eq!(Value::Bool(false), data["isDeleted"]);
        assert_ne!("2000-01-01T00:00:00Z", data["createdAt"]);

        let route = serde_json::json!({"token": token, "route": {"id": account_id}}).to_string();
        assert_eq!(
            FORTUNA_STATUS_OK,
            call(fortuna_api_accounts_by_id_delete, &route).status
        );
        let updated = call(
            fortuna_api_accounts_by_id_put,
            &serde_json::json!({"token": token, "route": {"id": account_id}, "body": {"name": "Ghost"}})
                .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_NOT_FOUND, updated.status, "{}", updated.body);
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_concurrent_tag_assignments_when_applied_then_no_update_is_lost() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);
        let account_id = create_account(&token, "1");
        let transaction = call(
            fortuna_api_transactions_post,
            &serde_json::json!({
                "token": token,
                "body": {"financialAccountId": account_id, "direction": 1, "amount": "1.00",
                    "occurredOn": "2026-09-09"}
            })
            .to_string(),
        );
        let transaction_id = body_of(&transaction)["data"]["id"]
            .as_str()
            .unwrap()
            .to_owned();
        let tags: Vec<Uuid> = (0..8).map(|_| Uuid::new_v4()).collect();
        std::thread::scope(|scope| {
            for tag in &tags {
                let request = serde_json::json!({
                    "token": token, "route": {"id": transaction_id, "tagId": tag}
                })
                .to_string();
                scope.spawn(move || {
                    let response =
                        call(fortuna_api_transactions_by_id_tags_by_tag_id_post, &request);
                    assert_eq!(FORTUNA_STATUS_OK, response.status, "{}", response.body);
                });
            }
        });
        let read = call(
            fortuna_api_transactions_by_id_get,
            &serde_json::json!({"token": token, "route": {"id": transaction_id}}).to_string(),
        );
        let stored: Value = Connection::open(&path)
            .unwrap()
            .query_row(
                "SELECT body_json FROM native_offline_record WHERE public_id = ?1",
                params![transaction_id],
                |row| row.get::<_, String>(0),
            )
            .map(|json| serde_json::from_str(&json).unwrap())
            .unwrap();
        assert_eq!(FORTUNA_STATUS_OK, read.status);
        assert_eq!(tags.len(), stored["tagIds"].as_array().unwrap().len());
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_dated_movements_when_reading_balance_as_of_a_day_then_only_movements_up_to_it_count() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);
        let account_id = create_account(&token, "100.00");
        for (direction, amount, occurred_on) in [
            (2, "50.00", "2026-01-10"),
            (1, "20.00", "2026-02-10"),
            (2, "1000.00", "2099-01-01"),
        ] {
            let transaction = call(
                fortuna_api_transactions_post,
                &serde_json::json!({
                    "token": token,
                    "body": {"financialAccountId": account_id.to_uppercase(), "direction": direction,
                        "amount": amount, "occurredOn": occurred_on}
                })
                .to_string(),
            );
            assert_eq!(FORTUNA_STATUS_CREATED, transaction.status);
        }
        let balance = |as_of: &str| {
            call(
                fortuna_api_accounts_by_id_balance_get,
                &serde_json::json!({
                    "token": token, "route": {"id": account_id}, "query": {"asOf": as_of}
                })
                .to_string(),
            )
        };
        let today = Utc::now().date_naive();
        let as_of = today.to_string();
        let current = balance(&as_of);
        assert_eq!(FORTUNA_STATUS_OK, current.status, "{}", current.body);
        assert_eq!(as_of, body_of(&current)["data"]["asOf"]);
        assert_eq!("130", body_of(&current)["data"]["balance"]);
        let future = balance("2099-12-31");
        assert_eq!("1130", body_of(&future)["data"]["balance"]);
        // Before the account was opened the balance is the opening balance only.
        let before_opening = balance("2026-01-31");
        assert_eq!("100", body_of(&before_opening)["data"]["balance"]);
        for invalid in ["1899-12-31", "2026-13-01", "tomorrow"] {
            assert_eq!(
                FORTUNA_STATUS_BAD_REQUEST,
                balance(invalid).status,
                "{invalid}"
            );
        }
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    /// Every export the native core does not implement. Listed by hand, so the test states the
    /// availability contract instead of re-deriving it from the generator under test.
    fn unimplemented_exports() -> Vec<(&'static str, NativeOperationFunction)> {
        vec![
            (
                "fortuna_api_imports_excel_post",
                fortuna_api_imports_excel_post,
            ),
            ("fortuna_api_imports_pdf_post", fortuna_api_imports_pdf_post),
            (
                "fortuna_api_import_jobs_by_id_retry_post",
                fortuna_api_import_jobs_by_id_retry_post,
            ),
            ("fortuna_api_exports_post", fortuna_api_exports_post),
            (
                "fortuna_api_reports_aggregate_get",
                fortuna_api_reports_aggregate_get,
            ),
            (
                "fortuna_api_reports_drill_down_get",
                fortuna_api_reports_drill_down_get,
            ),
            (
                "fortuna_api_reports_net_position_get",
                fortuna_api_reports_net_position_get,
            ),
            (
                "fortuna_api_reports_table_post",
                fortuna_api_reports_table_post,
            ),
            (
                "fortuna_api_projections_cash_flow_get",
                fortuna_api_projections_cash_flow_get,
            ),
            (
                "fortuna_api_projections_commitments_get",
                fortuna_api_projections_commitments_get,
            ),
            ("fortuna_api_transfers_post", fortuna_api_transfers_post),
            (
                "fortuna_api_transfers_by_id_get",
                fortuna_api_transfers_by_id_get,
            ),
            (
                "fortuna_api_transfers_by_id_delete",
                fortuna_api_transfers_by_id_delete,
            ),
            (
                "fortuna_api_transfers_by_id_restore_post",
                fortuna_api_transfers_by_id_restore_post,
            ),
            (
                "fortuna_api_installment_plans_post",
                fortuna_api_installment_plans_post,
            ),
            (
                "fortuna_api_installment_plans_by_id_get",
                fortuna_api_installment_plans_by_id_get,
            ),
            (
                "fortuna_api_installment_plans_by_id_delete",
                fortuna_api_installment_plans_by_id_delete,
            ),
            (
                "fortuna_api_installment_plans_by_id_restore_post",
                fortuna_api_installment_plans_by_id_restore_post,
            ),
            (
                "fortuna_api_recurring_transactions_materialize_post",
                fortuna_api_recurring_transactions_materialize_post,
            ),
            (
                "fortuna_api_statements_by_id_get",
                fortuna_api_statements_by_id_get,
            ),
            (
                "fortuna_api_statements_by_id_close_post",
                fortuna_api_statements_by_id_close_post,
            ),
            (
                "fortuna_api_statements_by_id_settle_post",
                fortuna_api_statements_by_id_settle_post,
            ),
            (
                "fortuna_api_credit_cards_by_id_statements_get",
                fortuna_api_credit_cards_by_id_statements_get,
            ),
            (
                "fortuna_api_budgets_by_id_consumption_get",
                fortuna_api_budgets_by_id_consumption_get,
            ),
            (
                "fortuna_api_goals_by_id_progress_get",
                fortuna_api_goals_by_id_progress_get,
            ),
            (
                "fortuna_api_transactions_by_id_reconcile_post",
                fortuna_api_transactions_by_id_reconcile_post,
            ),
        ]
    }

    fn create_owned(function: NativeOperationFunction, token: &str, body: Value) -> String {
        let created = call(
            function,
            &serde_json::json!({"token": token, "body": body}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_CREATED, created.status, "{}", created.body);
        body_of(&created)["data"]["id"].as_str().unwrap().to_owned()
    }

    #[test]
    fn given_unimplemented_operation_when_called_then_501_is_returned_no_work_is_recorded_and_capabilities_omit_it()
     {
        let _guard = test_guard();
        stop();
        let unimplemented = unimplemented_exports();

        let capabilities = body_of(&call(fortuna_capabilities, "{}"));
        let operations = capabilities["data"]["operations"].as_array().unwrap();
        let not_implemented = capabilities["data"]["notImplemented"].as_array().unwrap();
        assert_eq!(115 - unimplemented.len(), operations.len());
        assert_eq!(unimplemented.len(), not_implemented.len());
        for (symbol, _) in &unimplemented {
            assert!(
                !operations
                    .iter()
                    .any(|operation| operation["symbol"] == *symbol),
                "{symbol} is reported as available"
            );
            let entry = not_implemented
                .iter()
                .find(|operation| operation["symbol"] == *symbol)
                .unwrap_or_else(|| panic!("{symbol} is not reported as not implemented"));
            assert!(entry["method"].is_string() && entry["path"].is_string());
            assert!(
                entry["reason"]
                    .as_str()
                    .is_some_and(|reason| !reason.is_empty())
            );
        }

        // The answer does not depend on initialization or on the request.
        for (symbol, function) in &unimplemented {
            let response = call(*function, "{}");
            assert_eq!(
                FORTUNA_STATUS_NOT_IMPLEMENTED, response.status,
                "{symbol}: {}",
                response.body
            );
            assert_eq!(Value::Bool(false), body_of(&response)["success"]);
        }

        let path = temp_database();
        let token = create_local_user(&path);
        let account_id = create_account(&token, "10");
        let card_id = create_owned(
            fortuna_api_credit_cards_post,
            &token,
            serde_json::json!({"name": "Card", "currencyCode": "BRL", "creditLimit": "1000",
                "closingDay": 5, "dueDay": 15}),
        );
        let transaction_id = create_owned(
            fortuna_api_transactions_post,
            &token,
            serde_json::json!({"financialAccountId": account_id, "direction": 1,
                "amount": "10", "occurredOn": "2026-03-01"}),
        );
        for (symbol, function) in &unimplemented {
            let id = if symbol.contains("credit_cards") {
                card_id.clone()
            } else if symbol.contains("transactions_by_id") {
                transaction_id.clone()
            } else {
                Uuid::new_v4().to_string()
            };
            let response = call(
                *function,
                &serde_json::json!({
                    "token": token,
                    "route": {"id": id},
                    "query": {},
                    "body": {"originFinancialAccountId": account_id, "amount": "abc",
                        "fileName": "records.xlsx", "content": "AA==", "dataSet": 1}
                })
                .to_string(),
            );
            assert_eq!(
                FORTUNA_STATUS_NOT_IMPLEMENTED, response.status,
                "{symbol}: {}",
                response.body
            );
            let body = body_of(&response);
            assert_eq!(Value::Bool(false), body["success"]);
            assert_eq!(Value::Null, body["data"]);
            assert!(
                body["errors"][0]
                    .as_str()
                    .is_some_and(|error| error.contains("not available offline")),
                "{symbol}: {}",
                response.body
            );
        }

        // Nothing was queued, stored or marked as done on the caller's behalf.
        let connection = Connection::open(&path).unwrap();
        let jobs: i64 = connection
            .query_row("SELECT count(*) FROM native_operation_job", [], |row| {
                row.get(0)
            })
            .unwrap();
        assert_eq!(0, jobs);
        let records: i64 = connection
            .query_row(
                "SELECT count(*) FROM native_offline_record
                 WHERE resource NOT IN ('accounts', 'credit-cards', 'transactions')",
                [],
                |row| row.get(0),
            )
            .unwrap();
        assert_eq!(0, records);
        let transaction = call(
            fortuna_api_transactions_by_id_get,
            &serde_json::json!({"token": token, "route": {"id": transaction_id}}).to_string(),
        );
        assert_eq!(
            Value::Bool(false),
            body_of(&transaction)["data"]["isReconciled"]
        );
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_jobs_reported_completed_without_processing_when_initialized_then_they_read_as_failed_with_the_reason()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        create_local_user(&path);
        stop();
        let connection = Connection::open(&path).unwrap();
        let user_id: String = connection
            .query_row("SELECT public_id FROM native_user_profile", [], |row| {
                row.get(0)
            })
            .unwrap();
        let (import_id, pending_import_id, export_id, archive_id) = (
            Uuid::new_v4(),
            Uuid::new_v4(),
            Uuid::new_v4(),
            Uuid::new_v4(),
        );
        // What earlier versions stored: imports and exports reported done (or still queued)
        // although nothing was parsed or rendered.
        for (id, kind, status, progress) in [
            (import_id, "/api/imports/excel", 3, 100),
            (pending_import_id, "/api/imports/pdf", 1, 0),
            (export_id, "/api/exports", 3, 100),
            (archive_id, "/api/me/data-export", 3, 100),
        ] {
            connection
                .execute(
                    "INSERT INTO native_operation_job(
                         public_id, user_id, kind, status, progress, request_json, created_at, updated_at)
                     VALUES (?1, ?2, ?3, ?4, ?5, '{}', '2026-09-01T00:00:00.0000000Z',
                         '2026-09-01T00:00:00.0000000Z')",
                    params![id.to_string(), user_id, kind, status, progress],
                )
                .unwrap();
        }
        connection
            .execute("DELETE FROM native_schema_migration WHERE version >= 5", [])
            .unwrap();
        drop(connection);

        assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
        let token = token_from(&call(
            fortuna_api_local_accounts_authenticate_post,
            r#"{"name":"Local User","secret":"correct-horse-battery-staple"}"#,
        ));
        for id in [import_id, pending_import_id] {
            let job = call(
                fortuna_api_import_jobs_by_id_get,
                &serde_json::json!({"token": token, "route": {"id": id}}).to_string(),
            );
            assert_eq!(FORTUNA_STATUS_OK, job.status, "{}", job.body);
            let data = &body_of(&job)["data"];
            assert_eq!(4, data["status"], "{}", job.body);
            assert_eq!(0, data["progress"]);
            assert!(
                data["failureReason"]
                    .as_str()
                    .is_some_and(|reason| reason.contains("not processed")),
                "{}",
                job.body
            );
        }
        let export = call(
            fortuna_api_exports_by_id_get,
            &serde_json::json!({"token": token, "route": {"id": export_id}}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, export.status, "{}", export.body);
        assert_eq!(4, body_of(&export)["data"]["status"]);
        assert!(body_of(&export)["data"]["failureReason"].is_string());
        let archive_status: i64 = Connection::open(&path)
            .unwrap()
            .query_row(
                "SELECT status FROM native_operation_job WHERE public_id = ?1",
                params![archive_id.to_string()],
                |row| row.get(0),
            )
            .unwrap();
        assert_eq!(3, archive_status);
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    fn reassign(token: &str, source: &str, body: Value) -> Response {
        call(
            fortuna_api_categories_by_id_reassign_post,
            &serde_json::json!({"token": token, "route": {"id": source}, "body": body}).to_string(),
        )
    }

    fn transaction_field(token: &str, id: &str, field: &str) -> Value {
        let transaction = call(
            fortuna_api_transactions_by_id_get,
            &serde_json::json!({"token": token, "route": {"id": id},
                "query": {"includeDeleted": true}})
            .to_string(),
        );
        assert_eq!(
            FORTUNA_STATUS_OK, transaction.status,
            "{}",
            transaction.body
        );
        body_of(&transaction)["data"][field].clone()
    }

    #[test]
    fn given_categories_with_transactions_when_reassigning_then_live_transactions_move_as_in_http()
    {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);
        let account_id = create_account(&token, "0");
        let category = |name: &str, parent: Option<&str>| {
            create_owned(
                fortuna_api_categories_post,
                &token,
                serde_json::json!({"name": name, "parentId": parent}),
            )
        };
        let food = category("Food", None);
        let groceries = category("Groceries", Some(&food));
        let leisure = category("Leisure", None);
        let transaction = |category_id: &str| {
            create_owned(
                fortuna_api_transactions_post,
                &token,
                serde_json::json!({"financialAccountId": account_id, "direction": 1,
                    "amount": "1", "occurredOn": "2026-03-01", "categoryId": category_id}),
            )
        };
        let in_food = transaction(&food);
        let in_groceries = transaction(&groceries);
        let deleted_in_food = transaction(&food.to_uppercase());
        let in_leisure = transaction(&leisure);
        assert_eq!(
            FORTUNA_STATUS_OK,
            call(
                fortuna_api_transactions_by_id_delete,
                &serde_json::json!({"token": token, "route": {"id": deleted_in_food}}).to_string(),
            )
            .status
        );

        let moved = reassign(
            &token,
            &food,
            serde_json::json!({"targetCategoryId": leisure}),
        );
        assert_eq!(FORTUNA_STATUS_OK, moved.status, "{}", moved.body);
        let body = body_of(&moved);
        assert_eq!(
            "Category transactions reassigned successfully.",
            body["messages"][0]
        );
        assert_eq!(
            serde_json::json!({"id": food, "targetCategoryId": leisure,
                "includeDescendants": false, "reassignedCount": 1}),
            body["data"]
        );
        assert_eq!(leisure, transaction_field(&token, &in_food, "categoryId"));
        assert_eq!(
            groceries,
            transaction_field(&token, &in_groceries, "categoryId")
        );
        // A deleted transaction is not live and keeps its category (FR-CT-06).
        assert_eq!(
            food.to_uppercase(),
            transaction_field(&token, &deleted_in_food, "categoryId")
        );
        assert_eq!(
            leisure,
            transaction_field(&token, &in_leisure, "categoryId")
        );

        let with_descendants = reassign(
            &token,
            &food,
            serde_json::json!({"targetCategoryId": leisure, "includeDescendants": true}),
        );
        assert_eq!(FORTUNA_STATUS_OK, with_descendants.status);
        assert_eq!(1, body_of(&with_descendants)["data"]["reassignedCount"]);
        assert_eq!(
            true,
            body_of(&with_descendants)["data"]["includeDescendants"]
        );
        assert_eq!(
            leisure,
            transaction_field(&token, &in_groceries, "categoryId")
        );

        let refusals = [
            (
                food.clone(),
                serde_json::json!({"targetCategoryId": food}),
                FORTUNA_STATUS_BAD_REQUEST,
                "Source and target categories must be different.",
            ),
            (
                food.clone(),
                serde_json::json!({}),
                FORTUNA_STATUS_BAD_REQUEST,
                "TargetCategoryId cannot be empty.",
            ),
            (
                food.clone(),
                serde_json::json!({"targetCategoryId": Uuid::nil()}),
                FORTUNA_STATUS_BAD_REQUEST,
                "TargetCategoryId cannot be empty.",
            ),
            (
                food.clone(),
                serde_json::json!({"targetCategoryId": Uuid::new_v4()}),
                FORTUNA_STATUS_NOT_FOUND,
                "Category not found.",
            ),
            (
                Uuid::new_v4().to_string(),
                serde_json::json!({"targetCategoryId": leisure}),
                FORTUNA_STATUS_NOT_FOUND,
                "Category not found.",
            ),
        ];
        for (source, body, status, error) in refusals {
            let refused = reassign(&token, &source, body.clone());
            assert_eq!(status, refused.status, "{body}: {}", refused.body);
            assert_eq!(error, body_of(&refused)["errors"][0], "{body}");
        }
        assert_eq!(
            FORTUNA_STATUS_OK,
            call(
                fortuna_api_categories_by_id_delete,
                &serde_json::json!({"token": token, "route": {"id": groceries}}).to_string(),
            )
            .status
        );
        let from_deleted = reassign(
            &token,
            &groceries,
            serde_json::json!({"targetCategoryId": leisure}),
        );
        assert_eq!(FORTUNA_STATUS_NOT_FOUND, from_deleted.status);
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    /// The HTTP host's `GivenDescendants_WhenIncluded_ThenEveryLiveSubtreeTransactionMoves`,
    /// over the native transport: a target inside the source's subtree is not itself a source.
    #[test]
    fn given_target_inside_source_subtree_when_reassigning_descendants_then_counts_match_http() {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);
        let account_id = create_account(&token, "0");
        let category = |name: &str, parent: Option<&str>| {
            create_owned(
                fortuna_api_categories_post,
                &token,
                serde_json::json!({"name": name, "parentId": parent}),
            )
        };
        let source = category("Tree Source", None);
        let child = category("Tree Child", Some(&source));
        let grandchild = category("Tree Grandchild", Some(&child));
        let target = category("Tree Target", Some(&child));
        let transactions = [&source, &child, &grandchild, &target].map(|category_id| {
            create_owned(
                fortuna_api_transactions_post,
                &token,
                serde_json::json!({"financialAccountId": account_id, "direction": 1,
                    "amount": "1", "occurredOn": "2026-09-04", "categoryId": category_id}),
            )
        });

        let response = reassign(
            &token,
            &source,
            serde_json::json!({"targetCategoryId": target, "includeDescendants": true}),
        );
        assert_eq!(FORTUNA_STATUS_OK, response.status, "{}", response.body);
        assert_eq!(3, body_of(&response)["data"]["reassignedCount"]);
        assert_eq!(true, body_of(&response)["data"]["includeDescendants"]);
        for transaction in &transactions {
            assert_eq!(target, transaction_field(&token, transaction, "categoryId"));
        }
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_counterparty_names_on_transactions_when_merging_and_suggesting_then_http_rules_apply()
    {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        let token = create_local_user(&path);
        let account_id = create_account(&token, "0");
        let food = create_owned(
            fortuna_api_categories_post,
            &token,
            serde_json::json!({"name": "Food"}),
        );
        let leisure = create_owned(
            fortuna_api_categories_post,
            &token,
            serde_json::json!({"name": "Leisure"}),
        );
        let transaction = |counterparty: &str, category_id: &str, occurred_on: &str| {
            create_owned(
                fortuna_api_transactions_post,
                &token,
                serde_json::json!({"financialAccountId": account_id, "direction": 1,
                    "amount": "1", "occurredOn": occurred_on, "categoryId": category_id,
                    "counterparty": counterparty}),
            )
        };
        let first_market = transaction("Mercado Central", &food, "2026-03-01");
        let latest_market = transaction("  mercado central ", &leisure, "2026-03-05");
        let bakery = transaction("Padaria", &food, "2026-03-02");

        // FR-CT-10: a name is matched to an existing counterparty before one is created.
        let listed = call(
            fortuna_api_counterparties_get,
            &serde_json::json!({"token": token}).to_string(),
        );
        let counterparties = body_of(&listed)["data"]["counterparties"].clone();
        assert_eq!(
            2,
            counterparties.as_array().unwrap().len(),
            "{}",
            listed.body
        );
        let market = transaction_field(&token, &first_market, "counterpartyId");
        let market = market.as_str().unwrap().to_owned();
        assert_eq!(
            market,
            transaction_field(&token, &latest_market, "counterpartyId")
        );
        let padaria = transaction_field(&token, &bakery, "counterpartyId");
        let padaria = padaria.as_str().unwrap().to_owned();
        assert_ne!(market, padaria);

        let suggest = |id: &str| {
            call(
                fortuna_api_counterparties_by_id_suggested_category_get,
                &serde_json::json!({"token": token, "route": {"id": id}}).to_string(),
            )
        };
        let suggestion = suggest(&market);
        assert_eq!(FORTUNA_STATUS_OK, suggestion.status, "{}", suggestion.body);
        assert_eq!(
            "Category suggestion retrieved successfully.",
            body_of(&suggestion)["messages"][0]
        );
        assert_eq!(
            serde_json::json!({"counterpartyId": market, "hasSuggestion": true,
                "categoryId": leisure, "categoryName": "Leisure"}),
            body_of(&suggestion)["data"]
        );
        // A deleted category is never suggested; the next most recent use is.
        assert_eq!(
            FORTUNA_STATUS_OK,
            call(
                fortuna_api_categories_by_id_delete,
                &serde_json::json!({"token": token, "route": {"id": leisure}}).to_string(),
            )
            .status
        );
        assert_eq!(food, body_of(&suggest(&market))["data"]["categoryId"]);

        let unused = create_owned(
            fortuna_api_counterparties_post,
            &token,
            serde_json::json!({"name": "Unused"}),
        );
        let none = suggest(&unused);
        assert_eq!(FORTUNA_STATUS_OK, none.status, "{}", none.body);
        assert_eq!(
            "No prior category was found for this counterparty.",
            body_of(&none)["messages"][0]
        );
        assert_eq!(
            serde_json::json!({"counterpartyId": unused, "hasSuggestion": false,
                "categoryId": null, "categoryName": null}),
            body_of(&none)["data"]
        );
        let missing = suggest(&Uuid::new_v4().to_string());
        assert_eq!(FORTUNA_STATUS_NOT_FOUND, missing.status);
        assert_eq!("Counterparty not found.", body_of(&missing)["errors"][0]);

        let rule = create_owned(
            fortuna_api_recurring_transactions_post,
            &token,
            serde_json::json!({"financialAccountId": account_id, "direction": 1, "amount": "5",
                "categoryId": food, "counterparty": "PADARIA", "frequency": 3,
                "startsOn": "2026-03-01"}),
        );

        let merge = |source: &str, body: Value| {
            call(
                fortuna_api_counterparties_by_id_merge_post,
                &serde_json::json!({"token": token, "route": {"id": source}, "body": body})
                    .to_string(),
            )
        };
        let merged = merge(&padaria, serde_json::json!({"targetId": market}));
        assert_eq!(FORTUNA_STATUS_OK, merged.status, "{}", merged.body);
        assert_eq!(
            "Counterparties merged successfully.",
            body_of(&merged)["messages"][0]
        );
        assert_eq!(
            serde_json::json!({"id": padaria, "sourceId": padaria, "targetId": market,
                "reassignedTransactionCount": 1}),
            body_of(&merged)["data"]
        );
        assert_eq!(market, transaction_field(&token, &bakery, "counterpartyId"));
        let rule = call(
            fortuna_api_recurring_transactions_by_id_get,
            &serde_json::json!({"token": token, "route": {"id": rule}}).to_string(),
        );
        assert_eq!(
            market,
            body_of(&rule)["data"]["counterpartyId"],
            "{}",
            rule.body
        );
        let listed = call(
            fortuna_api_counterparties_get,
            &serde_json::json!({"token": token}).to_string(),
        );
        let names = body_of(&listed)["data"]["counterparties"]
            .as_array()
            .unwrap()
            .iter()
            .map(|counterparty| counterparty["name"].as_str().unwrap().to_owned())
            .collect::<Vec<_>>();
        assert_eq!(vec!["Mercado Central", "Unused"], names);

        for (source, body, status, error) in [
            (
                market.clone(),
                serde_json::json!({"targetId": market}),
                FORTUNA_STATUS_BAD_REQUEST,
                "A counterparty cannot be merged into itself.",
            ),
            (
                market.clone(),
                serde_json::json!({}),
                FORTUNA_STATUS_BAD_REQUEST,
                "TargetId cannot be empty.",
            ),
            (
                market.clone(),
                serde_json::json!({"targetId": padaria}),
                FORTUNA_STATUS_NOT_FOUND,
                "Counterparty not found.",
            ),
        ] {
            let refused = merge(&source, body.clone());
            assert_eq!(status, refused.status, "{body}: {}", refused.body);
            assert_eq!(error, body_of(&refused)["errors"][0], "{body}");
        }

        // An update relinks or clears the counterparty, as the HTTP update command does.
        let updated = call(
            fortuna_api_transactions_by_id_put,
            &serde_json::json!({"token": token, "route": {"id": first_market},
                "body": {"counterparty": "Feira"}})
            .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, updated.status, "{}", updated.body);
        let feira = transaction_field(&token, &first_market, "counterpartyId");
        assert!(feira.is_string() && feira != market);
        let cleared = call(
            fortuna_api_transactions_by_id_put,
            &serde_json::json!({"token": token, "route": {"id": first_market},
                "body": {"counterparty": " "}})
            .to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, cleared.status, "{}", cleared.body);
        assert_eq!(
            Value::Null,
            transaction_field(&token, &first_market, "counterpartyId")
        );

        let too_long = call(
            fortuna_api_transactions_post,
            &serde_json::json!({"token": token, "body": {"financialAccountId": account_id,
                "direction": 1, "amount": "1", "occurredOn": "2026-03-01",
                "categoryId": food, "counterparty": "x".repeat(201)}})
            .to_string(),
        );
        assert_eq!(
            FORTUNA_STATUS_BAD_REQUEST, too_long.status,
            "{}",
            too_long.body
        );
        assert_eq!(
            "Counterparty cannot exceed 200 characters.",
            body_of(&too_long)["errors"][0]
        );
        stop();
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }

    #[test]
    fn given_transactions_named_counterparties_before_linking_when_initialized_then_they_are_linked_once()
     {
        let _guard = test_guard();
        stop();
        let path = temp_database();
        create_local_user(&path);
        stop();
        let connection = Connection::open(&path).unwrap();
        let user_id: String = connection
            .query_row("SELECT public_id FROM native_user_profile", [], |row| {
                row.get(0)
            })
            .unwrap();
        let stored_at = "2026-09-01T00:00:00.0000000Z";
        // What earlier versions stored: the name only, with no counterparty record or link.
        for (resource, counterparty) in [
            ("transactions", Value::from("Mercado")),
            ("transactions", Value::from(" MERCADO ")),
            ("transactions", Value::from("Padaria")),
            ("transactions", Value::Null),
            ("recurring-transactions", Value::from("padaria")),
        ] {
            let id = Uuid::new_v4().to_string();
            connection
                .execute(
                    "INSERT INTO native_offline_record(
                         public_id, user_id, resource, body_json, is_deleted, created_at, updated_at)
                     VALUES (?1, ?2, ?3, ?4, 0, ?5, ?5)",
                    params![
                        id,
                        user_id,
                        resource,
                        serde_json::json!({"id": id, "counterparty": counterparty,
                            "isDeleted": false, "createdAt": stored_at, "updatedAt": stored_at})
                        .to_string(),
                        stored_at
                    ],
                )
                .unwrap();
        }
        connection
            .execute("DELETE FROM native_schema_migration WHERE version >= 6", [])
            .unwrap();

        let documents = |resource: &str| {
            let mut statement = connection
                .prepare(
                    "SELECT body_json FROM native_offline_record WHERE resource = ?1 ORDER BY rowid",
                )
                .unwrap();
            statement
                .query_map(params![resource], |row| row.get::<_, String>(0))
                .unwrap()
                .map(|json| serde_json::from_str::<Value>(&json.unwrap()).unwrap())
                .collect::<Vec<_>>()
        };
        for _ in 0..2 {
            assert_eq!(FORTUNA_STATUS_OK, initialize(&path, true).status);
            stop();
            let counterparties = documents("counterparties");
            let names = counterparties
                .iter()
                .map(|counterparty| counterparty["name"].as_str().unwrap())
                .collect::<Vec<_>>();
            assert_eq!(vec!["Mercado", "Padaria"], names);
            let transactions = documents("transactions");
            assert_eq!(counterparties[0]["id"], transactions[0]["counterpartyId"]);
            assert_eq!(counterparties[0]["id"], transactions[1]["counterpartyId"]);
            assert_eq!(counterparties[1]["id"], transactions[2]["counterpartyId"]);
            assert_eq!(Value::Null, transactions[3]["counterpartyId"]);
            assert!(
                transactions
                    .iter()
                    .all(|transaction| transaction["updatedAt"] == stored_at)
            );
            assert_eq!(
                counterparties[1]["id"],
                documents("recurring-transactions")[0]["counterpartyId"]
            );
        }
        assert_eq!(0, OUTSTANDING_STRINGS.load(Ordering::SeqCst));
    }
}
