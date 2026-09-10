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

mod operation;
mod persistence;
mod personal_archive;

use operation::OperationSpec;
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
/// An operation requiring initialized services was called before initialization.
pub const FORTUNA_STATUS_NOT_INITIALIZED: c_int = 503;

const INVALID_JSON: &str = "The request body is not valid JSON.";
const INVALID_CREDENTIALS: &str = "The local account name or secret is invalid.";
const AUTHENTICATED: &str = "Local account authenticated successfully.";
const LOCAL_AUTH_DISABLED: &str = "Local authentication is not available in this deployment.";
const NOT_INITIALIZED: &str = "The native core is not initialized.";
const INTERNAL_ERROR: &str = "The native core could not complete the operation.";

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

        let credentials = self
            .store
            .find_credentials(&request.name)
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
        let expires_at = Utc::now() + Duration::seconds(self.token_lifetime_seconds);
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

        Ok(AuthenticationOutput {
            token,
            expires_at: wire_timestamp(expires_at),
        })
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

    fn issue_session(&self, user_id: Uuid) -> AuthenticationOutput {
        let expires_at = Utc::now() + Duration::seconds(self.token_lifetime_seconds);
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
    InvalidCredentials,
    Internal,
}

fn default_true() -> bool {
    true
}

fn default_token_lifetime() -> i64 {
    3600
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
    if config.database_path.as_os_str().is_empty() || config.token_lifetime_seconds <= 0 {
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

/// Describe every route available to offline callers and every deliberately absent route.
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
/// "tokenLifetimeSeconds":3600}`. The database is created and migrated on demand.
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
                    "DatabasePath and a positive TokenLifetimeSeconds are required.",
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
/// SQLite TEXT and emitted as arbitrary-precision JSON numbers without a float conversion.
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
                .contains("\"openingBalance\":123456789012345.6789")
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
                .contains("\"openingBalance\":123456789012345.6789")
        );
        let account_json: Value = serde_json::from_str(&account.body).unwrap();
        assert_eq!(account_id.to_string(), account_json["data"]["id"]);
        assert_eq!(
            "123456789012345.6789",
            account_json["data"]["openingBalance"].to_string()
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
        assert_eq!(113, operations.len());
        assert!(operations.iter().any(|operation| {
            operation["method"] == "POST" && operation["path"] == "/api/imports/excel"
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
        assert_eq!(113, NATIVE_OPERATION_FUNCTIONS.len());
        for function in NATIVE_OPERATION_FUNCTIONS {
            let response = call(*function, "{}");
            assert_eq!(
                FORTUNA_STATUS_NOT_INITIALIZED, response.status,
                "{}",
                response.body
            );
            assert_eq!(
                Value::Bool(false),
                serde_json::from_str::<Value>(&response.body).unwrap()["success"]
            );
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
        assert!(rate.body.contains("\"rate\":1.0001"));

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
        assert_eq!(FORTUNA_STATUS_ACCEPTED, import.status, "{}", import.body);
        assert_eq!(
            0,
            serde_json::from_str::<Value>(&import.body).unwrap()["data"]["progress"]
        );

        let report = call(
            fortuna_api_reports_table_post,
            &serde_json::json!({"token":token,"body":{"dataSet":1}}).to_string(),
        );
        assert_eq!(FORTUNA_STATUS_OK, report.status);
        assert_eq!(
            1,
            serde_json::from_str::<Value>(&report.body).unwrap()["data"]["rows"]
                .as_array()
                .unwrap()
                .len()
        );
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
            fortuna_api_reports_aggregate_get,
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
}
