#![deny(unsafe_op_in_unsafe_fn)]

use std::collections::HashMap;
use std::ffi::{CStr, CString, c_char, c_int};
use std::path::PathBuf;
use std::ptr;
use std::str::FromStr;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex, OnceLock};

use argon2::Argon2;
use argon2::password_hash::{PasswordHasher, PasswordVerifier, phc::PasswordHash};
use chrono::{DateTime, Duration, Timelike, Utc};
use serde::{Deserialize, Serialize};
use serde_json::{Number, Value};
use sha2::{Digest, Sha256};
use uuid::Uuid;
use zeroize::{Zeroize, Zeroizing};

mod persistence;

use persistence::NativeStore;

/// Successful call. Operation functions use HTTP-compatible numeric statuses.
pub const FORTUNA_STATUS_OK: c_int = 200;
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
const ACCOUNT_RETRIEVED: &str = "Financial account retrieved successfully.";
const ACCOUNT_NOT_FOUND: &str = "Financial account not found.";
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

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct GetAccountRequest {
    token: String,
    id: Uuid,
    #[serde(default)]
    include_deleted: bool,
}

impl Drop for GetAccountRequest {
    fn drop(&mut self) {
        self.token.zeroize();
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
struct FinancialAccountOutput {
    id: Uuid,
    name: String,
    institution: Option<String>,
    account_type: i16,
    currency_code: String,
    opening_balance: Number,
    is_deleted: bool,
    created_at: String,
    updated_at: String,
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
    fn success(data: T, message: impl Into<String>) -> Self {
        Self {
            data: Some(data),
            messages: vec![message.into()],
            errors: Vec::new(),
            timestamp: wire_timestamp(Utc::now()),
            success: true,
        }
    }

    fn failure(error: impl Into<String>) -> Self {
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

    fn get_account(
        &self,
        request: &mut GetAccountRequest,
    ) -> Result<FinancialAccountOutput, AccountError> {
        let token_hash: [u8; 32] = Sha256::digest(request.token.as_bytes()).into();
        request.token.zeroize();
        let user_id = {
            let mut sessions = self
                .sessions
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            sessions.retain(|_, session| session.expires_at > Utc::now());
            sessions.get(&token_hash).map(|session| session.user_id)
        }
        .ok_or(AccountError::Unauthorized)?;

        let account = self
            .store
            .find_financial_account(request.id, user_id, request.include_deleted)
            .map_err(|_| AccountError::Internal)?
            .ok_or(AccountError::NotFound)?;

        Ok(FinancialAccountOutput {
            id: Uuid::parse_str(&account.public_id).map_err(|_| AccountError::Internal)?,
            name: account.name,
            institution: account.institution,
            account_type: account.account_type,
            currency_code: account.currency_code,
            opening_balance: Number::from_str(&account.opening_balance)
                .map_err(|_| AccountError::Internal)?,
            is_deleted: account.is_deleted,
            created_at: account.created_at,
            updated_at: account.updated_at,
        })
    }
}

#[derive(Debug)]
enum AuthError {
    Disabled,
    InvalidCredentials,
    Internal,
}

#[derive(Debug)]
enum AccountError {
    Unauthorized,
    NotFound,
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
    guarded_call(response_json, || {
        let Some(core) = current_core() else {
            return (
                FORTUNA_STATUS_NOT_INITIALIZED,
                serialize(&DataOutput::<Value>::failure(NOT_INITIALIZED)),
            );
        };
        let Ok(mut request) = read_json::<GetAccountRequest>(request_json) else {
            return (
                FORTUNA_STATUS_BAD_REQUEST,
                serialize(&DataOutput::<Value>::failure(INVALID_JSON)),
            );
        };
        match core.get_account(&mut request) {
            Ok(output) => (
                FORTUNA_STATUS_OK,
                serialize(&DataOutput::success(output, ACCOUNT_RETRIEVED)),
            ),
            Err(AccountError::Unauthorized) => (
                FORTUNA_STATUS_UNAUTHORIZED,
                serialize(&DataOutput::<Value>::failure(
                    "The local authentication token is invalid or expired.",
                )),
            ),
            Err(AccountError::NotFound) => (
                FORTUNA_STATUS_NOT_FOUND,
                serialize(&DataOutput::<Value>::failure(ACCOUNT_NOT_FOUND)),
            ),
            Err(AccountError::Internal) => (
                FORTUNA_STATUS_INTERNAL_ERROR,
                serialize(&DataOutput::<Value>::failure(INTERNAL_ERROR)),
            ),
        }
    })
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
    use std::path::Path;

    use rusqlite::{Connection, params};

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
                "INSERT INTO native_financial_account(
                 public_id, user_id, name, institution, account_type, currency_code,
                 opening_balance, is_deleted, created_at, updated_at)
             VALUES (?1, ?2, 'Exact account', 'Native bank', 1, 'BRL', ?3, 0, ?4, ?4)",
                params![
                    account_id.to_string(),
                    user_id.to_string(),
                    opening_balance,
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
        assert_eq!(ACCOUNT_RETRIEVED, account_json["messages"][0]);
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
}
