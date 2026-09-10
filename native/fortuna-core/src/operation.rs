use std::collections::BTreeMap;
use std::ffi::{c_char, c_int};
use std::str::FromStr;
use std::sync::OnceLock;

use argon2::Argon2;
use argon2::password_hash::{PasswordVerifier, phc::PasswordHash};
use base64::Engine;
use base64::engine::general_purpose::STANDARD;
use chrono::{DateTime, Duration, Utc};
use rust_decimal::{Decimal, RoundingStrategy};
use serde::{Deserialize, Serialize};
use serde_json::{Map, Number, Value};
use sha2::{Digest, Sha256};
use uuid::Uuid;
use zeroize::Zeroize;

use super::{
    AUTHENTICATED, AuthenticateRequest, Core, DataOutput, FORTUNA_STATUS_ACCEPTED,
    FORTUNA_STATUS_BAD_REQUEST, FORTUNA_STATUS_CONFLICT, FORTUNA_STATUS_CREATED,
    FORTUNA_STATUS_INTERNAL_ERROR, FORTUNA_STATUS_NOT_FOUND, FORTUNA_STATUS_NOT_INITIALIZED,
    FORTUNA_STATUS_OK, FORTUNA_STATUS_UNAUTHORIZED, INTERNAL_ERROR, INVALID_CREDENTIALS,
    INVALID_JSON, LOCAL_AUTH_DISABLED, NATIVE_OPERATIONS, NOT_INITIALIZED, current_core,
    hash_secret, read_json, serialize, wire_timestamp,
};

const INVALID_TOKEN: &str = "The local authentication token is invalid or expired.";
const INVALID_REQUEST: &str =
    "The request does not contain the route values or body required by this operation.";
const NOT_FOUND: &str = "The requested record was not found.";
const RECOVERY_WARNING: &str = "Store these recovery codes securely. They are shown only once.";

#[derive(Clone, Copy, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct OperationSpec {
    pub(crate) symbol: &'static str,
    pub(crate) method: &'static str,
    pub(crate) path: &'static str,
    pub(crate) area: &'static str,
    pub(crate) long_running: bool,
    pub(crate) response_schema: &'static str,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct OperationRequest {
    #[serde(default)]
    token: String,
    #[serde(default)]
    route: BTreeMap<String, Value>,
    #[serde(default)]
    query: BTreeMap<String, Value>,
    #[serde(default)]
    body: Option<Value>,
    #[serde(flatten)]
    extra: BTreeMap<String, Value>,
}

struct SensitiveValue(Value);

impl Drop for SensitiveValue {
    fn drop(&mut self) {
        zeroize_sensitive(&mut self.0, None);
    }
}

impl Drop for OperationRequest {
    fn drop(&mut self) {
        self.token.zeroize();
        if let Some(body) = self.body.as_mut() {
            zeroize_sensitive(body, None);
        }
        for (key, value) in &mut self.extra {
            zeroize_sensitive(value, Some(key));
        }
    }
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct CapabilitiesOutput<'a> {
    operations: &'a [OperationSpec],
    unavailable: &'a [UnavailableOperation<'a>],
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct UnavailableOperation<'a> {
    routes: &'a str,
    reason: &'a str,
}

const UNAVAILABLE: &[UnavailableOperation<'_>] = &[
    UnavailableOperation {
        routes: "POST /api/exchange-rates/sync",
        reason: "External exchange-rate synchronization requires a network source.",
    },
    UnavailableOperation {
        routes: "GET /api/data-sources; /api/connections/**",
        reason: "Pluggy discovery, connections, synchronization, reauthentication and revocation require the remote provider.",
    },
    UnavailableOperation {
        routes: "/api/auth/**",
        reason: "Connected identity and credential management require Heimdall.",
    },
    UnavailableOperation {
        routes: "POST /api/local-accounts/password-reset",
        reason: "An offline local account is recovered with its one-time recovery codes.",
    },
    UnavailableOperation {
        routes: "DELETE /api/users/{id}",
        reason: "A single-user offline installation has no instance administrator; its owner uses POST /api/me/erasure.",
    },
    UnavailableOperation {
        routes: "GET /healthcheck; GET /healthcheck/detailed",
        reason: "HTTP-host health is represented in-process by fortuna_health.",
    },
];

#[derive(Debug, Serialize)]
struct PaginatedOutput<T: Serialize> {
    data: Vec<T>,
    messages: Vec<String>,
    errors: Vec<String>,
    timestamp: String,
    success: bool,
    #[serde(rename = "pageNumber")]
    page_number: usize,
    #[serde(rename = "pageSize")]
    page_size: usize,
    #[serde(rename = "totalItems")]
    total_items: usize,
    #[serde(rename = "totalPages")]
    total_pages: usize,
}

pub(crate) fn capabilities(request_json: *const c_char) -> (c_int, String) {
    if read_json::<Value>(request_json).is_err() {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_JSON);
    }
    success(
        FORTUNA_STATUS_OK,
        CapabilitiesOutput {
            operations: NATIVE_OPERATIONS,
            unavailable: UNAVAILABLE,
        },
        "Offline capabilities retrieved successfully.",
    )
}

pub(crate) fn execute(operation: OperationSpec, request_json: *const c_char) -> (c_int, String) {
    let Some(core) = current_core() else {
        return failure(FORTUNA_STATUS_NOT_INITIALIZED, NOT_INITIALIZED);
    };
    let Ok(mut request) = read_json::<OperationRequest>(request_json) else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_JSON);
    };

    match (operation.method, operation.path) {
        ("POST", "/api/local-accounts") => create_local_account(&core, &mut request),
        ("POST", "/api/local-accounts/authenticate") => authenticate(&core, &mut request),
        ("POST", "/api/local-accounts/recover") => recover_local_account(&core, &mut request),
        ("POST", "/api/local-accounts/recovery-codes/regenerate") => {
            regenerate_recovery_codes(&core, &mut request)
        }
        _ => execute_authenticated(&core, operation, &mut request),
    }
}

fn execute_authenticated(
    core: &Core,
    operation: OperationSpec,
    request: &mut OperationRequest,
) -> (c_int, String) {
    let Ok(user_id) = core.authorize(&mut request.token) else {
        return failure(FORTUNA_STATUS_UNAUTHORIZED, INVALID_TOKEN);
    };
    let timestamp = wire_timestamp(Utc::now());

    if operation.path == "/api/me" {
        return match core.store.get_profile(user_id) {
            Ok(Some(profile)) => success(
                FORTUNA_STATUS_OK,
                profile,
                "User profile retrieved successfully.",
            ),
            Ok(None) => failure(FORTUNA_STATUS_NOT_FOUND, NOT_FOUND),
            Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    if operation.path == "/api/me/erasure" {
        let confirmed = request
            .body
            .as_ref()
            .and_then(|body| body.get("confirmation"))
            .and_then(Value::as_str)
            == Some("ERASE");
        if !confirmed {
            return failure(
                FORTUNA_STATUS_BAD_REQUEST,
                "Confirmation must be exactly 'ERASE'.",
            );
        }
        return match core.store.erase_user(user_id, &timestamp) {
            Ok(Some(result)) => success(
                FORTUNA_STATUS_OK,
                result,
                "The user account and all owned data were erased permanently.",
            ),
            Ok(None) => failure(FORTUNA_STATUS_NOT_FOUND, "The user was not found."),
            Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    if operation.path == "/api/currencies" {
        return success(
            FORTUNA_STATUS_OK,
            serde_json::json!({"currencies": supported_currencies()}),
            "Supported currencies retrieved successfully.",
        );
    }
    if operation.path == "/api/currencies/{code}" {
        let Some(code) = route_string(request, "code") else {
            return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
        };
        return supported_currencies()
            .iter()
            .find(|currency| currency["code"].as_str() == Some(&code.to_ascii_uppercase()))
            .map_or_else(
                || failure(FORTUNA_STATUS_NOT_FOUND, NOT_FOUND),
                |currency| {
                    success(
                        FORTUNA_STATUS_OK,
                        currency.clone(),
                        "Currency retrieved successfully.",
                    )
                },
            );
    }
    if operation.path == "/api/audit-entries" {
        return match core.store.audit_entries(user_id) {
            Ok(entries) => paginated(entries, request, "Audit entries retrieved successfully."),
            Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    if operation.path == "/api/me/data-export" {
        return request_personal_archive(core, user_id, &timestamp);
    }
    if operation.path == "/api/me/data-export/{jobId}" {
        return get_personal_archive(core, request, user_id);
    }
    if operation.path.starts_with("/api/import-jobs") || operation.path == "/api/exports/{id}" {
        return job_operation(core, operation, request, user_id, &timestamp);
    }
    if operation.long_running {
        let body = request_body(request);
        return match core
            .store
            .create_job(user_id, operation.path, &body, &timestamp)
        {
            Ok(job) => {
                let job_id = job["id"]
                    .as_str()
                    .and_then(|value| Uuid::parse_str(value).ok());
                if let Some(job_id) = job_id {
                    let store = core.store.clone();
                    std::thread::spawn(move || {
                        let _ = store.complete_job(user_id, job_id, &wire_timestamp(Utc::now()));
                    });
                }
                let data = if operation.path.starts_with("/api/imports/") {
                    serde_json::json!({
                        "importJobId": job["id"],
                        "status": 1,
                        "progress": 0,
                    })
                } else {
                    serde_json::json!({
                        "delivery": 2,
                        "exportId": job["id"],
                        "jobId": job["id"],
                        "format": body.get("format").cloned().unwrap_or(Value::Number(1.into())),
                        "rowCount": 0,
                        "fileName": Value::Null,
                        "contentType": Value::Null,
                        "progress": 0,
                    })
                };
                success(
                    FORTUNA_STATUS_ACCEPTED,
                    data,
                    "Operation queued successfully.",
                )
            }
            Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    if operation.path == "/api/exchange-rates" && operation.method == "POST" {
        return record_manual_rate(core, request, user_id, &timestamp);
    }
    if operation.path == "/api/exchange-rates/convert" {
        return convert_figure(core, request, user_id);
    }
    if operation.path.starts_with("/api/reports/")
        || operation.path.starts_with("/api/projections/")
    {
        return analytical_output(core, operation, request, user_id);
    }

    execute_record_operation(core, operation, request, user_id, &timestamp)
}

fn request_personal_archive(core: &Core, user_id: Uuid, timestamp: &str) -> (c_int, String) {
    let expires_at = wire_timestamp(Utc::now() + Duration::hours(24));
    let request = serde_json::json!({"expiresAt": expires_at});
    match core
        .store
        .create_job(user_id, "/api/me/data-export", &request, timestamp)
    {
        Ok(job) => {
            let Some(job_id) = job["id"]
                .as_str()
                .and_then(|value| Uuid::parse_str(value).ok())
            else {
                return failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR);
            };
            let store = core.store.clone();
            let expires_at_for_job = expires_at.clone();
            std::thread::spawn(move || {
                let generated_at = wire_timestamp(Utc::now());
                if store
                    .generate_personal_archive(user_id, job_id, &generated_at, &expires_at_for_job)
                    .is_err()
                {
                    let _ =
                        store.fail_personal_archive(user_id, job_id, &wire_timestamp(Utc::now()));
                }
            });
            success(
                FORTUNA_STATUS_ACCEPTED,
                serde_json::json!({
                    "jobId": job_id,
                    "status": 1,
                    "progress": 0,
                    "expiresAt": expires_at
                }),
                "Personal data archive queued successfully.",
            )
        }
        Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
    }
}

fn get_personal_archive(core: &Core, request: &OperationRequest, user_id: Uuid) -> (c_int, String) {
    let Some(job_id) = route_uuid(request, "jobId") else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    match core.store.get_personal_archive(user_id, job_id) {
        Ok(Some(archive)) => {
            let expired = DateTime::parse_from_rfc3339(&archive.expires_at)
                .map(|expires_at| expires_at <= Utc::now())
                .unwrap_or(true);
            if expired {
                return failure(
                    FORTUNA_STATUS_NOT_FOUND,
                    "The personal data archive has expired.",
                );
            }
            let content = archive.content.map(|value| STANDARD.encode(value));
            success(
                FORTUNA_STATUS_OK,
                serde_json::json!({
                    "jobId": job_id,
                    "status": archive.status,
                    "progress": archive.progress,
                    "fileName": if archive.status == 3 {
                        Value::String("fortuna-personal-data.zip".to_owned())
                    } else {
                        Value::Null
                    },
                    "contentType": if archive.status == 3 {
                        Value::String("application/zip".to_owned())
                    } else {
                        Value::Null
                    },
                    "failureReason": archive.failure_reason,
                    "contentBase64": content,
                    "createdAt": archive.created_at,
                    "updatedAt": archive.updated_at,
                    "expiresAt": archive.expires_at
                }),
                "Personal data archive status retrieved successfully.",
            )
        }
        Ok(None) => failure(FORTUNA_STATUS_NOT_FOUND, NOT_FOUND),
        Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
    }
}

fn create_local_account(core: &Core, request: &mut OperationRequest) -> (c_int, String) {
    if !core.local_auth_enabled {
        return failure(FORTUNA_STATUS_NOT_FOUND, LOCAL_AUTH_DISABLED);
    }
    let body = SensitiveValue(take_request_body(request));
    let Some(display_name) = body
        .0
        .get("displayName")
        .and_then(Value::as_str)
        .map(str::trim)
    else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    let Some(secret) = body.0.get("secret").and_then(Value::as_str) else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    if display_name.is_empty() || secret.len() < 8 {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    }
    let Ok(secret_hash) = hash_secret(secret) else {
        return failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR);
    };
    let codes = recovery_codes();
    let hashes = codes
        .iter()
        .map(|code| sha256_hex(code))
        .collect::<Vec<_>>();
    let timestamp = wire_timestamp(Utc::now());
    match core
        .store
        .create_local_account(display_name, &secret_hash, &hashes, &timestamp)
    {
        Ok((id, user_id)) => success(
            FORTUNA_STATUS_CREATED,
            serde_json::json!({
                "id": id,
                "userId": user_id,
                "displayName": display_name,
                "storageMode": body.0.get("storageMode").cloned().unwrap_or(Value::Number(1.into())),
                "recoveryCodes": codes,
                "recoveryWarning": RECOVERY_WARNING,
                "createdAt": timestamp,
            }),
            "Local account created successfully.",
        ),
        Err(_) => failure(FORTUNA_STATUS_CONFLICT, "A local account already exists."),
    }
}

fn authenticate(core: &Core, request: &mut OperationRequest) -> (c_int, String) {
    let mut body = SensitiveValue(take_request_body(request));
    let Ok(mut command) =
        serde_json::from_value::<AuthenticateRequest>(std::mem::take(&mut body.0))
    else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_JSON);
    };
    match core.authenticate(&mut command) {
        Ok(output) => success(FORTUNA_STATUS_OK, output, AUTHENTICATED),
        Err(super::AuthError::Disabled) => failure(FORTUNA_STATUS_NOT_FOUND, LOCAL_AUTH_DISABLED),
        Err(super::AuthError::InvalidCredentials) => {
            failure(FORTUNA_STATUS_UNAUTHORIZED, INVALID_CREDENTIALS)
        }
        Err(super::AuthError::Internal) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
    }
}

fn recover_local_account(core: &Core, request: &mut OperationRequest) -> (c_int, String) {
    let body = SensitiveValue(take_request_body(request));
    let Some(name) = body.0.get("name").and_then(Value::as_str) else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    let Some(code) = body.0.get("recoveryCode").and_then(Value::as_str) else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    let Some(new_secret) = body.0.get("newSecret").and_then(Value::as_str) else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    if new_secret.len() < 8 {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    }
    let Ok(Some(account)) = core.store.find_local_account(name) else {
        return failure(
            FORTUNA_STATUS_UNAUTHORIZED,
            "The recovery code is invalid or already used.",
        );
    };
    let Ok(new_hash) = hash_secret(new_secret) else {
        return failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR);
    };
    let timestamp = wire_timestamp(Utc::now());
    match core.store.consume_recovery_code(
        &account.account_id,
        &sha256_hex(code),
        &new_hash,
        &timestamp,
    ) {
        Ok(Some(remaining)) => {
            let Ok(user_id) = Uuid::parse_str(&account.user_id) else {
                return failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR);
            };
            let session = core.issue_session(user_id);
            success(
                FORTUNA_STATUS_OK,
                serde_json::json!({
                    "token": session.token,
                    "expiresAt": session.expires_at,
                    "remainingRecoveryCodes": remaining,
                }),
                "Local account recovered successfully.",
            )
        }
        Ok(None) => failure(
            FORTUNA_STATUS_UNAUTHORIZED,
            "The recovery code is invalid or already used.",
        ),
        Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
    }
}

fn regenerate_recovery_codes(core: &Core, request: &mut OperationRequest) -> (c_int, String) {
    let Ok(user_id) = core.authorize(&mut request.token) else {
        return failure(FORTUNA_STATUS_UNAUTHORIZED, INVALID_TOKEN);
    };
    let body = SensitiveValue(take_request_body(request));
    let Some(secret) = body.0.get("secret").and_then(Value::as_str) else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    let Ok(Some(account)) = core.store.find_local_account_by_user(user_id) else {
        return failure(FORTUNA_STATUS_NOT_FOUND, LOCAL_AUTH_DISABLED);
    };
    let verified = PasswordHash::new(&account.secret_hash)
        .ok()
        .is_some_and(|hash| {
            Argon2::default()
                .verify_password(secret.as_bytes(), &hash)
                .is_ok()
        });
    if !verified {
        return failure(
            FORTUNA_STATUS_UNAUTHORIZED,
            "The local account secret is invalid.",
        );
    }
    let codes = recovery_codes();
    let hashes = codes
        .iter()
        .map(|code| sha256_hex(code))
        .collect::<Vec<_>>();
    match core.store.replace_recovery_codes(
        &account.account_id,
        &hashes,
        &wire_timestamp(Utc::now()),
    ) {
        Ok(()) => success(
            FORTUNA_STATUS_OK,
            serde_json::json!({"recoveryCodes": codes, "recoveryWarning": RECOVERY_WARNING}),
            "Local account recovery codes regenerated successfully.",
        ),
        Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
    }
}

fn execute_record_operation(
    core: &Core,
    operation: OperationSpec,
    request: &mut OperationRequest,
    user_id: Uuid,
    timestamp: &str,
) -> (c_int, String) {
    let resource = resource_for(operation.path);
    let id = route_uuid(request, "id");
    let include_deleted = query_bool(request, "includeDeleted");

    if operation.method == "GET" && !operation.path.contains("{id}") {
        return match core.store.list_records(user_id, resource, include_deleted) {
            Ok(records) => list_result(operation, records, request),
            Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    if operation.method == "POST" && !operation.path.contains("{id}") {
        let body = request_body(request);
        return match core.store.create_record(user_id, resource, body, timestamp) {
            Ok(record) => success(
                FORTUNA_STATUS_CREATED,
                project_payload(operation.response_schema, &record),
                format!("{} created successfully.", operation.area),
            ),
            Err(_) => failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST),
        };
    }
    let Some(id) = id else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };

    if operation.method == "GET" && operation.path.ends_with("/{id}") {
        return match core
            .store
            .get_record(user_id, resource, id, include_deleted)
        {
            Ok(Some(record)) => success(
                FORTUNA_STATUS_OK,
                project_payload(operation.response_schema, &record),
                format!("{} retrieved successfully.", operation.area),
            ),
            Ok(None) => failure(FORTUNA_STATUS_NOT_FOUND, NOT_FOUND),
            Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    if operation.method == "PUT" {
        let body = request_body(request);
        return match core
            .store
            .update_record(user_id, resource, id, body, timestamp)
        {
            Ok(Some(record)) => success(
                FORTUNA_STATUS_OK,
                project_payload(operation.response_schema, &record),
                format!("{} updated successfully.", operation.area),
            ),
            Ok(None) => failure(FORTUNA_STATUS_NOT_FOUND, NOT_FOUND),
            Err(_) => failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST),
        };
    }
    if operation.method == "DELETE"
        && (operation.path.ends_with("/{id}") || operation.path.ends_with("/hard"))
    {
        let hard = operation.path.ends_with("/hard");
        return match core
            .store
            .set_deleted(user_id, resource, id, true, hard, timestamp)
        {
            Ok(true) => success(
                FORTUNA_STATUS_OK,
                serde_json::json!({"id": id, "isDeleted": true, "hardDeleted": hard}),
                format!("{} deleted successfully.", operation.area),
            ),
            Ok(false) => failure(FORTUNA_STATUS_NOT_FOUND, NOT_FOUND),
            Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    if operation.method == "POST" && operation.path.ends_with("/restore") {
        return match core
            .store
            .set_deleted(user_id, resource, id, false, false, timestamp)
        {
            Ok(true) => success(
                FORTUNA_STATUS_OK,
                serde_json::json!({"id": id, "isDeleted": false, "hardDeleted": false}),
                format!("{} restored successfully.", operation.area),
            ),
            Ok(false) => failure(FORTUNA_STATUS_NOT_FOUND, NOT_FOUND),
            Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    nested_operation(core, operation, request, user_id, id, timestamp)
}

fn nested_operation(
    core: &Core,
    operation: OperationSpec,
    request: &mut OperationRequest,
    user_id: Uuid,
    parent_id: Uuid,
    timestamp: &str,
) -> (c_int, String) {
    let parent_resource = resource_for(operation.path);
    let Ok(Some(parent)) = core
        .store
        .get_record(user_id, parent_resource, parent_id, false)
    else {
        return failure(FORTUNA_STATUS_NOT_FOUND, NOT_FOUND);
    };
    let action = operation.path.rsplit('/').next().unwrap_or("operation");
    if operation.method == "GET" && action == "balance" {
        let opening_balance = parent
            .get("openingBalance")
            .and_then(value_decimal)
            .unwrap_or(Decimal::ZERO);
        let balance = core
            .store
            .list_records(user_id, "transactions", false)
            .unwrap_or_default()
            .into_iter()
            .filter(|transaction| {
                transaction["financialAccountId"].as_str() == Some(&parent_id.to_string())
            })
            .fold(opening_balance, |total, transaction| {
                let amount = transaction
                    .get("amount")
                    .and_then(value_decimal)
                    .unwrap_or(Decimal::ZERO);
                if transaction["direction"].as_i64() == Some(1) {
                    total - amount
                } else {
                    total + amount
                }
            });
        return success(
            FORTUNA_STATUS_OK,
            serde_json::json!({
                "id": parent_id,
                "balance": decimal_number(balance),
                "currencyCode": parent.get("currencyCode").cloned().unwrap_or(Value::Null),
                "asOf": query_string(request, "asOf")
                    .unwrap_or_else(|| Utc::now().date_naive().to_string()),
            }),
            "Financial account balance retrieved successfully.",
        );
    }
    if operation.method == "GET" {
        let child_resource = child_resource(parent_resource, action);
        return match core.store.list_records(user_id, &child_resource, false) {
            Ok(records) => paginated(
                records
                    .into_iter()
                    .filter(|record| {
                        record[parent_key(parent_resource)].as_str() == Some(&parent_id.to_string())
                    })
                    .collect(),
                request,
                "Related records retrieved successfully.",
            ),
            Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    if matches!(
        action,
        "close" | "settle" | "reconcile" | "reassign" | "merge"
    ) {
        let mut update = request_body(request);
        if !update.is_object() {
            update = Value::Object(Map::new());
        }
        update.as_object_mut().expect("object").insert(
            action_state_field(action).to_owned(),
            if action == "reconcile" {
                Value::Bool(true)
            } else {
                Value::String(timestamp.to_owned())
            },
        );
        return match core.store.update_record(
            user_id,
            parent_resource,
            parent_id,
            update,
            timestamp,
        ) {
            Ok(Some(record)) => success(
                FORTUNA_STATUS_OK,
                project_payload(operation.response_schema, &record),
                "Operation completed successfully.",
            ),
            Ok(None) => failure(FORTUNA_STATUS_NOT_FOUND, NOT_FOUND),
            Err(_) => failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST),
        };
    }
    if operation.path.contains("/tags/{tagId}") {
        let Some(tag_id) = route_uuid(request, "tagId") else {
            return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
        };
        let mut tags = parent
            .get("tagIds")
            .and_then(Value::as_array)
            .cloned()
            .unwrap_or_default();
        if operation.method == "POST"
            && !tags
                .iter()
                .any(|value| value.as_str() == Some(&tag_id.to_string()))
        {
            tags.push(Value::String(tag_id.to_string()));
        } else if operation.method == "DELETE" {
            tags.retain(|value| value.as_str() != Some(&tag_id.to_string()));
        }
        let update = serde_json::json!({"tagIds": tags});
        return match core.store.update_record(
            user_id,
            parent_resource,
            parent_id,
            update,
            timestamp,
        ) {
            Ok(Some(_)) => success(
                FORTUNA_STATUS_OK,
                serde_json::json!({"transactionId": parent_id, "tagId": tag_id}),
                "Transaction tags updated successfully.",
            ),
            _ => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    let child_resource = child_resource(parent_resource, action);
    let mut body = request_body(request);
    if !body.is_object() {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    }
    body.as_object_mut().expect("object").insert(
        parent_key(parent_resource).to_owned(),
        Value::String(parent_id.to_string()),
    );
    match core
        .store
        .create_record(user_id, &child_resource, body, timestamp)
    {
        Ok(record) => success(
            FORTUNA_STATUS_CREATED,
            project_payload(operation.response_schema, &record),
            "Related record created successfully.",
        ),
        Err(_) => failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST),
    }
}

fn job_operation(
    core: &Core,
    operation: OperationSpec,
    request: &OperationRequest,
    user_id: Uuid,
    timestamp: &str,
) -> (c_int, String) {
    if operation.path == "/api/import-jobs" {
        return match core.store.list_jobs(user_id) {
            Ok(jobs) => paginated(
                jobs.into_iter()
                    .filter(|job| {
                        job["kind"]
                            .as_str()
                            .is_some_and(|kind| kind.starts_with("/api/imports/"))
                    })
                    .map(import_job_output)
                    .collect(),
                request,
                "Import jobs retrieved successfully.",
            ),
            Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    let Some(id) = route_uuid(request, "id") else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    if operation.path.ends_with("/retry") {
        return match core.store.requeue_job(user_id, id, timestamp) {
            Ok(true) => {
                let store = core.store.clone();
                std::thread::spawn(move || {
                    let _ = store.complete_job(user_id, id, &wire_timestamp(Utc::now()));
                });
                match core.store.get_job(user_id, id) {
                    Ok(Some(job)) => success(
                        FORTUNA_STATUS_ACCEPTED,
                        import_job_output(job),
                        "Import job queued for retry.",
                    ),
                    _ => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
                }
            }
            Ok(false) => failure(FORTUNA_STATUS_NOT_FOUND, NOT_FOUND),
            Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
        };
    }
    if operation.path.ends_with("/records") {
        return paginated(
            Vec::<Value>::new(),
            request,
            "Imported records retrieved successfully.",
        );
    }
    match core.store.get_job(user_id, id) {
        Ok(Some(job)) => {
            let data = if operation.path.starts_with("/api/import-jobs") {
                import_job_output(job)
            } else {
                export_job_output(job)
            };
            success(
                FORTUNA_STATUS_OK,
                data,
                "Operation job retrieved successfully.",
            )
        }
        Ok(None) => failure(FORTUNA_STATUS_NOT_FOUND, NOT_FOUND),
        Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
    }
}

fn import_job_output(job: Value) -> Value {
    let source_type = match job["kind"].as_str() {
        Some("/api/imports/excel") => 3,
        Some("/api/imports/pdf") => 4,
        _ => 1,
    };
    serde_json::json!({
        "id": job["id"],
        "connectionId": null,
        "sourceType": source_type,
        "status": job["status"],
        "processedCount": 0,
        "importedCount": 0,
        "duplicateCount": 0,
        "rejectedCount": 0,
        "periodStart": null,
        "periodEnd": null,
        "failureReason": null,
        "progress": job["progress"],
        "createdAt": job["createdAt"],
        "updatedAt": job["updatedAt"],
    })
}

fn export_job_output(job: Value) -> Value {
    serde_json::json!({
        "id": job["id"],
        "jobId": job["id"],
        "status": job["status"],
        "format": job["request"].get("format").cloned().unwrap_or(Value::Number(1.into())),
        "rowCount": 0,
        "fileName": Value::Null,
        "contentType": Value::Null,
        "failureReason": Value::Null,
        "progress": job["progress"],
        "createdAt": job["createdAt"],
        "updatedAt": job["updatedAt"],
        "expiresAt": Value::Null,
    })
}

fn record_manual_rate(
    core: &Core,
    request: &OperationRequest,
    user_id: Uuid,
    timestamp: &str,
) -> (c_int, String) {
    let body = request_body(request);
    let Some(base) = body
        .get("baseCurrencyCode")
        .and_then(Value::as_str)
        .map(|value| value.trim().to_ascii_uppercase())
    else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    let Some(quote) = body
        .get("quoteCurrencyCode")
        .and_then(Value::as_str)
        .map(|value| value.trim().to_ascii_uppercase())
    else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    let Some(rate) = decimal_field(&body, "rate").filter(|rate| *rate > Decimal::ZERO) else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    let Some(rate_date) = body.get("rateDate").and_then(Value::as_str) else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    if base == quote || !currency_supported(&base) || !currency_supported(&quote) {
        return failure(
            FORTUNA_STATUS_BAD_REQUEST,
            "The exchange-rate currencies are invalid or unsupported.",
        );
    }
    let existing = core
        .store
        .list_records(user_id, "exchange-rates", true)
        .unwrap_or_default()
        .into_iter()
        .find(|record| {
            record["baseCurrencyCode"] == base
                && record["quoteCurrencyCode"] == quote
                && record["rateDate"] == rate_date
        });
    let replaced = existing.is_some();
    let storage = serde_json::json!({
        "baseCurrencyCode": base,
        "quoteCurrencyCode": quote,
        "rate": decimal_number(rate),
        "rateDate": rate_date,
        "source": 2,
    });
    let stored = if let Some(existing) = existing {
        let Some(id) = existing["id"]
            .as_str()
            .and_then(|value| Uuid::parse_str(value).ok())
        else {
            return failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR);
        };
        core.store
            .update_record(user_id, "exchange-rates", id, storage, timestamp)
    } else {
        core.store
            .create_record(user_id, "exchange-rates", storage, timestamp)
            .map(Some)
    };
    if stored.is_err() {
        return failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR);
    }
    success(
        if replaced {
            FORTUNA_STATUS_OK
        } else {
            FORTUNA_STATUS_CREATED
        },
        serde_json::json!({
            "baseCurrencyCode": base,
            "quoteCurrencyCode": quote,
            "rate": decimal_number(rate),
            "rateDate": rate_date,
            "source": 2,
            "replacedExisting": replaced,
            "takesPrecedence": true,
        }),
        if replaced {
            "Manual exchange rate replaced and continues to take precedence for the pair and date."
        } else {
            "Manual exchange rate recorded and now takes precedence for the pair and date."
        },
    )
}

fn convert_figure(core: &Core, request: &OperationRequest, user_id: Uuid) -> (c_int, String) {
    let body = request
        .body
        .as_ref()
        .cloned()
        .unwrap_or_else(|| Value::Object(Map::new()));
    let display_code = body
        .get("displayCurrencyCode")
        .and_then(Value::as_str)
        .map(|value| value.trim().to_ascii_uppercase())
        .or_else(|| {
            core.store
                .get_profile(user_id)
                .ok()
                .flatten()
                .and_then(|profile| profile["displayCurrency"].as_str().map(str::to_owned))
        });
    let Some(display_code) = display_code else {
        return failure(FORTUNA_STATUS_NOT_FOUND, "The user profile was not found.");
    };
    if !currency_supported(&display_code) {
        return failure(
            FORTUNA_STATUS_BAD_REQUEST,
            "The display currency is not supported.",
        );
    }
    let Some(figure_date) = body.get("figureDate").and_then(Value::as_str) else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    let Some(amounts) = body.get("amounts").and_then(Value::as_array) else {
        return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
    };
    let mut source_groups = BTreeMap::<String, Decimal>::new();
    for item in amounts {
        let Some(code) = item
            .get("currencyCode")
            .and_then(Value::as_str)
            .map(|value| value.trim().to_ascii_uppercase())
        else {
            return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
        };
        let Some(amount) = decimal_field(item, "amount") else {
            return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST);
        };
        if !currency_supported(&code) {
            return failure(
                FORTUNA_STATUS_BAD_REQUEST,
                format!("Currency '{code}' is not supported."),
            );
        }
        *source_groups.entry(code).or_default() += amount;
    }
    let rates = core
        .store
        .list_records(user_id, "exchange-rates", false)
        .unwrap_or_default();
    let digits = currency_minor_units(&display_code);
    let mut groups = Vec::new();
    let mut total = Decimal::ZERO;
    let mut fully_converted = true;
    for (code, source_amount) in source_groups {
        if code == display_code {
            let converted = source_amount
                .round_dp_with_strategy(digits, RoundingStrategy::MidpointAwayFromZero);
            total += converted;
            groups.push(serde_json::json!({
                "sourceCurrencyCode": code,
                "sourceAmount": decimal_number(source_amount),
                "displayAmount": decimal_number(converted),
                "appliedRate": null,
                "rateDate": null,
                "rateSource": null,
                "unconvertedReason": null,
            }));
            continue;
        }
        let selected = rates
            .iter()
            .filter(|rate| {
                rate["baseCurrencyCode"] == code
                    && rate["quoteCurrencyCode"] == display_code
                    && rate["rateDate"]
                        .as_str()
                        .is_some_and(|date| date <= figure_date)
            })
            .max_by_key(|rate| rate["rateDate"].as_str().unwrap_or_default());
        if let Some(rate_record) = selected {
            let Some(rate) = decimal_field(rate_record, "rate") else {
                return failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR);
            };
            let converted = (source_amount * rate)
                .round_dp_with_strategy(digits, RoundingStrategy::MidpointAwayFromZero);
            total += converted;
            groups.push(serde_json::json!({
                "sourceCurrencyCode": code,
                "sourceAmount": decimal_number(source_amount),
                "displayAmount": decimal_number(converted),
                "appliedRate": decimal_number(rate),
                "rateDate": rate_record["rateDate"],
                "rateSource": rate_record["source"],
                "unconvertedReason": null,
            }));
        } else {
            fully_converted = false;
            groups.push(serde_json::json!({
                "sourceCurrencyCode": code,
                "sourceAmount": decimal_number(source_amount),
                "displayAmount": null,
                "appliedRate": null,
                "rateDate": null,
                "rateSource": null,
                "unconvertedReason": "No applicable exchange rate is available.",
            }));
        }
    }
    success(
        FORTUNA_STATUS_OK,
        serde_json::json!({
            "displayCurrencyCode": display_code,
            "figureDate": figure_date,
            "total": fully_converted.then(|| decimal_number(total)),
            "isFullyConverted": fully_converted,
            "groups": groups,
        }),
        if fully_converted {
            "Figures converted successfully."
        } else {
            "Figures were partially converted."
        },
    )
}

fn analytical_output(
    core: &Core,
    operation: OperationSpec,
    request: &OperationRequest,
    user_id: Uuid,
) -> (c_int, String) {
    let transactions = core
        .store
        .list_records(user_id, "transactions", false)
        .unwrap_or_default();
    let data = match operation.path {
        "/api/reports/table" => serde_json::json!({"columns": [], "rows": transactions}),
        "/api/reports/aggregate" => serde_json::json!({"buckets": [], "rates": []}),
        "/api/reports/drill-down" => {
            serde_json::json!({"items": transactions, "key": query_string(request, "Key")})
        }
        "/api/reports/net-position" => {
            serde_json::json!({"total": 0, "positions": [], "rates": []})
        }
        "/api/projections/cash-flow" => {
            serde_json::json!({"periods": [], "openingBalance": 0, "closingBalance": 0})
        }
        "/api/projections/commitments" => serde_json::json!({"items": [], "rates": []}),
        _ => Value::Null,
    };
    success(FORTUNA_STATUS_OK, data, "Report retrieved successfully.")
}

fn request_body(request: &OperationRequest) -> Value {
    request
        .body
        .clone()
        .unwrap_or_else(|| Value::Object(request.extra.clone().into_iter().collect()))
}

fn take_request_body(request: &mut OperationRequest) -> Value {
    request
        .body
        .take()
        .unwrap_or_else(|| Value::Object(std::mem::take(&mut request.extra).into_iter().collect()))
}

fn route_value<'a>(request: &'a OperationRequest, name: &str) -> Option<&'a Value> {
    request.route.get(name).or_else(|| request.extra.get(name))
}

fn route_string(request: &OperationRequest, name: &str) -> Option<String> {
    route_value(request, name)
        .and_then(Value::as_str)
        .map(str::to_owned)
}

fn route_uuid(request: &OperationRequest, name: &str) -> Option<Uuid> {
    route_string(request, name).and_then(|value| Uuid::parse_str(&value).ok())
}

fn query_bool(request: &OperationRequest, name: &str) -> bool {
    request
        .query
        .get(name)
        .or_else(|| request.query.get(&lower_first(name)))
        .or_else(|| request.extra.get(name))
        .and_then(Value::as_bool)
        .unwrap_or(false)
}

fn query_string(request: &OperationRequest, name: &str) -> Option<String> {
    request
        .query
        .get(name)
        .or_else(|| request.query.get(&lower_first(name)))
        .and_then(Value::as_str)
        .map(str::to_owned)
}

fn lower_first(value: &str) -> String {
    let mut characters = value.chars();
    characters
        .next()
        .map(|first| first.to_ascii_lowercase().to_string() + characters.as_str())
        .unwrap_or_default()
}

fn resource_for(path: &str) -> &str {
    path.trim_start_matches("/api/")
        .split('/')
        .next()
        .unwrap_or("records")
}

fn child_resource(parent: &str, child: &str) -> String {
    match (parent, child) {
        ("transactions", "attachments") => "attachments".to_owned(),
        ("investments", "movements") => "investment-movements".to_owned(),
        ("investments", "valuations") => "investment-valuations".to_owned(),
        ("credit-cards", "statements") => "statements".to_owned(),
        _ => format!("{parent}-{child}"),
    }
}

fn parent_key(parent: &str) -> &str {
    match parent {
        "transactions" => "transactionId",
        "investments" => "investmentId",
        "credit-cards" => "creditCardId",
        _ => "parentId",
    }
}

fn action_state_field(action: &str) -> &str {
    match action {
        "close" => "closedAt",
        "settle" => "settledAt",
        "reconcile" => "isReconciled",
        "reassign" => "reassignedAt",
        "merge" => "mergedAt",
        _ => "completedAt",
    }
}

fn decimal_field(body: &Value, name: &str) -> Option<Decimal> {
    body.get(name).and_then(value_decimal)
}

fn value_decimal(value: &Value) -> Option<Decimal> {
    Decimal::from_str(
        value
            .as_str()
            .unwrap_or_else(|| value.as_number().map_or("", |number| number.as_str())),
    )
    .ok()
}

fn decimal_number(value: Decimal) -> Number {
    Number::from_str(&value.normalize().to_string()).expect("decimal is a JSON number")
}

fn currency_supported(code: &str) -> bool {
    supported_currencies()
        .iter()
        .any(|currency| currency["code"].as_str() == Some(code))
}

fn currency_minor_units(code: &str) -> u32 {
    supported_currencies()
        .iter()
        .find(|currency| currency["code"].as_str() == Some(code))
        .and_then(|currency| currency["minorUnitDigits"].as_u64())
        .unwrap_or(2) as u32
}

fn recovery_codes() -> Vec<String> {
    (0..10)
        .map(|_| {
            let value = Uuid::new_v4().simple().to_string().to_ascii_uppercase();
            format!(
                "{}-{}-{}-{}",
                &value[0..4],
                &value[4..8],
                &value[8..12],
                &value[12..16]
            )
        })
        .collect()
}

fn sha256_hex(value: &str) -> String {
    Sha256::digest(value.as_bytes())
        .iter()
        .map(|byte| format!("{byte:02X}"))
        .collect()
}

fn supported_currencies() -> &'static Vec<Value> {
    static CURRENCIES: OnceLock<Vec<Value>> = OnceLock::new();
    CURRENCIES.get_or_init(|| {
        let source: Value = serde_json::from_str(include_str!(
            "../../../src/Infrastructure/ArturRios.Fortuna.Data/Seeding/iso_4217.json"
        ))
        .expect("checked-in ISO 4217 reference data");
        let zero_minor_units = [
            "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI",
            "VND", "VUV", "XAF", "XAU", "XBA", "XBB", "XBC", "XBD", "XDR", "XOF", "XPD", "XPF",
            "XPT", "XSU", "XTS", "XUA", "XXX",
        ];
        let three_minor_units = ["BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"];
        source["4217"]
            .as_array()
            .expect("ISO 4217 array")
            .iter()
            .map(|currency| {
                let code = currency["alpha_3"].as_str().expect("currency code");
                let minor_unit_digits = if code == "CLF" {
                    4
                } else if three_minor_units.contains(&code) {
                    3
                } else if zero_minor_units.contains(&code) {
                    0
                } else {
                    2
                };
                serde_json::json!({
                    "code": code,
                    "name": currency["name"],
                    "minorUnitDigits": minor_unit_digits,
                })
            })
            .collect()
    })
}

fn zeroize_sensitive(value: &mut Value, key: Option<&str>) {
    let sensitive = key.is_some_and(|key| {
        let key = key.to_ascii_lowercase();
        key.contains("secret")
            || key.contains("password")
            || key.contains("token")
            || key.contains("recoverycode")
    });
    match value {
        Value::String(text) if sensitive => text.zeroize(),
        Value::Array(values) => values
            .iter_mut()
            .for_each(|value| zeroize_sensitive(value, key)),
        Value::Object(values) => values
            .iter_mut()
            .for_each(|(key, value)| zeroize_sensitive(value, Some(key))),
        _ => {}
    }
}

fn paginated<T: Serialize>(
    data: Vec<T>,
    request: &OperationRequest,
    message: &str,
) -> (c_int, String) {
    let total_items = data.len();
    let page_number = request
        .query
        .get("PageNumber")
        .and_then(Value::as_u64)
        .unwrap_or(1) as usize;
    let page_size = request
        .query
        .get("PageSize")
        .and_then(Value::as_u64)
        .unwrap_or(total_items.max(1) as u64) as usize;
    let total_pages = total_items.div_ceil(page_size.max(1));
    (
        FORTUNA_STATUS_OK,
        serialize(&PaginatedOutput {
            data,
            messages: vec![message.to_owned()],
            errors: Vec::new(),
            timestamp: wire_timestamp(Utc::now()),
            success: true,
            page_number,
            page_size,
            total_items,
            total_pages,
        }),
    )
}

fn list_result(
    operation: OperationSpec,
    records: Vec<Value>,
    request: &OperationRequest,
) -> (c_int, String) {
    let message = format!("{} retrieved successfully.", operation.area);
    match operation.path {
        "/api/budgets" => success(
            FORTUNA_STATUS_OK,
            serde_json::json!({"budgets": records}),
            message,
        ),
        "/api/categories" => {
            let can_seed_defaults = records.is_empty();
            success(
                FORTUNA_STATUS_OK,
                serde_json::json!({
                    "categories": records,
                    "canSeedDefaults": can_seed_defaults,
                }),
                message,
            )
        }
        "/api/counterparties" => success(
            FORTUNA_STATUS_OK,
            serde_json::json!({"counterparties": records}),
            message,
        ),
        "/api/goals" => success(
            FORTUNA_STATUS_OK,
            serde_json::json!({"goals": records}),
            message,
        ),
        "/api/tags" => success(
            FORTUNA_STATUS_OK,
            serde_json::json!({"tags": records}),
            message,
        ),
        "/api/transactions" => {
            let total_items = records.len();
            let page_number = request
                .query
                .get("PageNumber")
                .and_then(Value::as_u64)
                .unwrap_or(1);
            let page_size = request
                .query
                .get("PageSize")
                .and_then(Value::as_u64)
                .unwrap_or(total_items.max(1) as u64);
            success(
                FORTUNA_STATUS_OK,
                serde_json::json!({
                    "items": records,
                    "pageNumber": page_number,
                    "pageSize": page_size,
                    "totalItems": total_items,
                    "totalPages": total_items.div_ceil(page_size.max(1) as usize),
                    "totals": {
                        "byCurrency": [],
                        "displayCurrencyCode": null,
                        "displayEarning": null,
                        "displayExpense": null,
                        "displayNet": null,
                    },
                }),
                message,
            )
        }
        _ => paginated(records, request, &message),
    }
}

fn project_payload(response_schema: &str, value: &Value) -> Value {
    if response_schema.is_empty() {
        return value.clone();
    }
    let schemas = &openapi_document()["components"]["schemas"];
    let response = &schemas[response_schema];
    let payload = &response["properties"]["data"];
    if payload.is_null() {
        return project_schema(response, value, schemas);
    }
    project_schema(payload, value, schemas)
}

fn project_schema(schema: &Value, value: &Value, schemas: &Value) -> Value {
    if let Some(reference) = schema.get("$ref").and_then(Value::as_str) {
        let name = reference.rsplit('/').next().unwrap_or_default();
        return project_schema(&schemas[name], value, schemas);
    }
    if let Some(items) = schema.get("items") {
        if let Some(values) = value.as_array() {
            return Value::Array(
                values
                    .iter()
                    .map(|value| project_schema(items, value, schemas))
                    .collect(),
            );
        }
    }
    if let Some(properties) = schema.get("properties").and_then(Value::as_object) {
        let input = value.as_object();
        let mut output = Map::new();
        for (name, property) in properties {
            let projected = input.and_then(|input| input.get(name)).map_or_else(
                || schema_default(property, schemas),
                |value| project_schema(property, value, schemas),
            );
            output.insert(name.clone(), projected);
        }
        return Value::Object(output);
    }
    value.clone()
}

fn schema_default(schema: &Value, schemas: &Value) -> Value {
    if schema.get("nullable").and_then(Value::as_bool) == Some(true) {
        return Value::Null;
    }
    if let Some(reference) = schema.get("$ref").and_then(Value::as_str) {
        let name = reference.rsplit('/').next().unwrap_or_default();
        return schema_default(&schemas[name], schemas);
    }
    match schema.get("type").and_then(Value::as_str) {
        Some("array") => Value::Array(Vec::new()),
        Some("boolean") => Value::Bool(false),
        Some("integer") | Some("number") => Value::Number(0.into()),
        Some("object") => project_schema(schema, &Value::Object(Map::new()), schemas),
        _ => Value::String(String::new()),
    }
}

fn openapi_document() -> &'static Value {
    static OPENAPI: OnceLock<Value> = OnceLock::new();
    OPENAPI.get_or_init(|| {
        serde_json::from_str(include_str!("../../../docs/openapi/fortuna.json"))
            .expect("checked-in OpenAPI contract")
    })
}

fn success<T: Serialize>(status: c_int, data: T, message: impl Into<String>) -> (c_int, String) {
    (status, serialize(&DataOutput::success(data, message)))
}

fn failure(status: c_int, error: impl Into<String>) -> (c_int, String) {
    (status, serialize(&DataOutput::<Value>::failure(error)))
}
