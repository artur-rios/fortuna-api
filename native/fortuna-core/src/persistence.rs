use std::path::{Path, PathBuf};

use rusqlite::{Connection, OptionalExtension, params};
use serde_json::{Map, Value};
use uuid::Uuid;

#[derive(Clone, Debug)]
pub(crate) struct NativeStore {
    database_path: PathBuf,
}

#[derive(Debug)]
pub(crate) struct CredentialRecord {
    pub(crate) user_id: String,
    pub(crate) secret_hash: String,
}

#[derive(Debug)]
pub(crate) struct LocalAccountRecord {
    pub(crate) account_id: String,
    pub(crate) user_id: String,
    pub(crate) secret_hash: String,
}

impl NativeStore {
    pub(crate) fn initialize(database_path: PathBuf) -> Result<Self, StoreError> {
        prepare_parent(&database_path).map_err(|_| StoreError)?;
        migrate(&database_path).map_err(|_| StoreError)?;
        Ok(Self { database_path })
    }

    pub(crate) fn health(&self) -> Result<(), StoreError> {
        self.open()
            .and_then(|connection| connection.query_row("SELECT 1", [], |_| Ok(())))
            .map_err(|_| StoreError)
    }

    pub(crate) fn find_credentials(
        &self,
        name: &str,
    ) -> Result<Option<CredentialRecord>, StoreError> {
        self.open()
            .and_then(|connection| {
                connection
                    .query_row(
                        "SELECT user_id, secret_hash FROM native_local_account WHERE name = ?1",
                        params![name],
                        |row| {
                            Ok(CredentialRecord {
                                user_id: row.get(0)?,
                                secret_hash: row.get(1)?,
                            })
                        },
                    )
                    .optional()
            })
            .map_err(|_| StoreError)
    }

    pub(crate) fn create_local_account(
        &self,
        display_name: &str,
        secret_hash: &str,
        recovery_hashes: &[String],
        timestamp: &str,
    ) -> Result<(Uuid, Uuid), StoreError> {
        let mut connection = self.open().map_err(|_| StoreError)?;
        let transaction = connection.transaction().map_err(|_| StoreError)?;
        let exists: bool = transaction
            .query_row(
                "SELECT EXISTS(SELECT 1 FROM native_local_account)",
                [],
                |row| row.get(0),
            )
            .map_err(|_| StoreError)?;
        if exists {
            return Err(StoreError);
        }
        let user_id = Uuid::new_v4();
        let account_id = Uuid::new_v4();
        transaction
            .execute(
                "INSERT INTO native_user_profile(public_id, display_name, display_currency, created_at, updated_at)
                 VALUES (?1, ?2, 'BRL', ?3, ?3)",
                params![user_id.to_string(), display_name, timestamp],
            )
            .map_err(|_| StoreError)?;
        transaction
            .execute(
                "INSERT INTO native_local_account(public_id, user_id, name, secret_hash, created_at, updated_at)
                 VALUES (?1, ?2, ?3, ?4, ?5, ?5)",
                params![account_id.to_string(), user_id.to_string(), display_name, secret_hash, timestamp],
            )
            .map_err(|_| StoreError)?;
        for hash in recovery_hashes {
            transaction
                .execute(
                    "INSERT INTO native_local_recovery_code(public_id, local_account_id, code_hash, created_at)
                     VALUES (?1, ?2, ?3, ?4)",
                    params![Uuid::new_v4().to_string(), account_id.to_string(), hash, timestamp],
                )
                .map_err(|_| StoreError)?;
        }
        transaction.commit().map_err(|_| StoreError)?;
        Ok((account_id, user_id))
    }

    pub(crate) fn find_local_account(
        &self,
        name: &str,
    ) -> Result<Option<LocalAccountRecord>, StoreError> {
        self.open()
            .and_then(|connection| {
                connection
                    .query_row(
                        "SELECT public_id, user_id, secret_hash FROM native_local_account WHERE name = ?1",
                        params![name],
                        |row| {
                            Ok(LocalAccountRecord {
                                account_id: row.get(0)?,
                                user_id: row.get(1)?,
                                secret_hash: row.get(2)?,
                            })
                        },
                    )
                    .optional()
            })
            .map_err(|_| StoreError)
    }

    pub(crate) fn find_local_account_by_user(
        &self,
        user_id: Uuid,
    ) -> Result<Option<LocalAccountRecord>, StoreError> {
        self.open()
            .and_then(|connection| {
                connection
                    .query_row(
                        "SELECT public_id, user_id, secret_hash
                         FROM native_local_account WHERE user_id = ?1",
                        params![user_id.to_string()],
                        |row| {
                            Ok(LocalAccountRecord {
                                account_id: row.get(0)?,
                                user_id: row.get(1)?,
                                secret_hash: row.get(2)?,
                            })
                        },
                    )
                    .optional()
            })
            .map_err(|_| StoreError)
    }

    pub(crate) fn consume_recovery_code(
        &self,
        account_id: &str,
        code_hash: &str,
        new_secret_hash: &str,
        timestamp: &str,
    ) -> Result<Option<usize>, StoreError> {
        let mut connection = self.open().map_err(|_| StoreError)?;
        let transaction = connection.transaction().map_err(|_| StoreError)?;
        let changed = transaction
            .execute(
                "UPDATE native_local_recovery_code SET used_at = ?1
                 WHERE local_account_id = ?2 AND code_hash = ?3 AND used_at IS NULL",
                params![timestamp, account_id, code_hash],
            )
            .map_err(|_| StoreError)?;
        if changed == 0 {
            return Ok(None);
        }
        transaction
            .execute(
                "UPDATE native_local_account SET secret_hash = ?1, updated_at = ?2 WHERE public_id = ?3",
                params![new_secret_hash, timestamp, account_id],
            )
            .map_err(|_| StoreError)?;
        let remaining: i64 = transaction
            .query_row(
                "SELECT count(*) FROM native_local_recovery_code
                 WHERE local_account_id = ?1 AND used_at IS NULL",
                params![account_id],
                |row| row.get(0),
            )
            .map_err(|_| StoreError)?;
        transaction.commit().map_err(|_| StoreError)?;
        Ok(Some(remaining as usize))
    }

    pub(crate) fn replace_recovery_codes(
        &self,
        account_id: &str,
        recovery_hashes: &[String],
        timestamp: &str,
    ) -> Result<(), StoreError> {
        let mut connection = self.open().map_err(|_| StoreError)?;
        let transaction = connection.transaction().map_err(|_| StoreError)?;
        transaction
            .execute(
                "DELETE FROM native_local_recovery_code WHERE local_account_id = ?1",
                params![account_id],
            )
            .map_err(|_| StoreError)?;
        for hash in recovery_hashes {
            transaction
                .execute(
                    "INSERT INTO native_local_recovery_code(public_id, local_account_id, code_hash, created_at)
                     VALUES (?1, ?2, ?3, ?4)",
                    params![Uuid::new_v4().to_string(), account_id, hash, timestamp],
                )
                .map_err(|_| StoreError)?;
        }
        transaction.commit().map_err(|_| StoreError)
    }

    pub(crate) fn get_profile(&self, user_id: Uuid) -> Result<Option<Value>, StoreError> {
        self.open()
            .and_then(|connection| {
                connection
                    .query_row(
                        "SELECT public_id, display_name, display_currency, created_at, updated_at
                         FROM native_user_profile WHERE public_id = ?1",
                        params![user_id.to_string()],
                        |row| {
                            Ok(serde_json::json!({
                                "id": row.get::<_, String>(0)?,
                                "displayName": row.get::<_, String>(1)?,
                                "displayCurrency": row.get::<_, Option<String>>(2)?,
                                "displayCurrencyRequiresConfirmation": false,
                                "createdAt": row.get::<_, String>(3)?,
                                "updatedAt": row.get::<_, String>(4)?,
                            }))
                        },
                    )
                    .optional()
            })
            .map_err(|_| StoreError)
    }

    pub(crate) fn create_record(
        &self,
        user_id: Uuid,
        resource: &str,
        mut body: Value,
        timestamp: &str,
    ) -> Result<Value, StoreError> {
        let object = body.as_object_mut().ok_or(StoreError)?;
        let id = object
            .get("id")
            .and_then(Value::as_str)
            .and_then(|value| Uuid::parse_str(value).ok())
            .unwrap_or_else(Uuid::new_v4);
        object.insert("id".to_owned(), Value::String(id.to_string()));
        object
            .entry("createdAt")
            .or_insert_with(|| Value::String(timestamp.to_owned()));
        object.insert("updatedAt".to_owned(), Value::String(timestamp.to_owned()));
        object.entry("isDeleted").or_insert(Value::Bool(false));
        let body_json = serde_json::to_string(&body).map_err(|_| StoreError)?;
        let mut connection = self.open().map_err(|_| StoreError)?;
        let transaction = connection.transaction().map_err(|_| StoreError)?;
        transaction
            .execute(
                "INSERT INTO native_offline_record(user_id, resource, public_id, body_json, is_deleted, created_at, updated_at)
                 VALUES (?1, ?2, ?3, ?4, 0, ?5, ?5)",
                params![user_id.to_string(), resource, id.to_string(), body_json, timestamp],
            )
            .map_err(|_| StoreError)?;
        append_audit(
            &transaction,
            user_id,
            resource,
            id,
            "Create",
            "Succeeded",
            timestamp,
        )
        .map_err(|_| StoreError)?;
        transaction.commit().map_err(|_| StoreError)?;
        Ok(body)
    }

    pub(crate) fn get_record(
        &self,
        user_id: Uuid,
        resource: &str,
        id: Uuid,
        include_deleted: bool,
    ) -> Result<Option<Value>, StoreError> {
        self.open()
            .and_then(|connection| {
                connection
                    .query_row(
                        "SELECT body_json FROM native_offline_record
                         WHERE user_id = ?1 AND resource = ?2 AND public_id = ?3
                           AND (?4 = 1 OR is_deleted = 0)",
                        params![
                            user_id.to_string(),
                            resource,
                            id.to_string(),
                            include_deleted
                        ],
                        |row| row.get::<_, String>(0),
                    )
                    .optional()
            })
            .map_err(|_| StoreError)?
            .map(|json| serde_json::from_str(&json).map_err(|_| StoreError))
            .transpose()
    }

    pub(crate) fn list_records(
        &self,
        user_id: Uuid,
        resource: &str,
        include_deleted: bool,
    ) -> Result<Vec<Value>, StoreError> {
        let connection = self.open().map_err(|_| StoreError)?;
        let mut statement = connection
            .prepare(
                "SELECT body_json FROM native_offline_record
                 WHERE user_id = ?1 AND resource = ?2 AND (?3 = 1 OR is_deleted = 0)
                 ORDER BY created_at, public_id",
            )
            .map_err(|_| StoreError)?;
        let rows = statement
            .query_map(
                params![user_id.to_string(), resource, include_deleted],
                |row| row.get::<_, String>(0),
            )
            .map_err(|_| StoreError)?;
        rows.map(|row| {
            let json = row.map_err(|_| StoreError)?;
            serde_json::from_str(&json).map_err(|_| StoreError)
        })
        .collect()
    }

    pub(crate) fn update_record(
        &self,
        user_id: Uuid,
        resource: &str,
        id: Uuid,
        mut body: Value,
        timestamp: &str,
    ) -> Result<Option<Value>, StoreError> {
        let Some(existing) = self.get_record(user_id, resource, id, true)? else {
            return Ok(None);
        };
        let mut merged = existing.as_object().cloned().unwrap_or_default();
        let updates = body.as_object_mut().ok_or(StoreError)?;
        for (key, value) in std::mem::take(updates) {
            if key != "id" && key != "createdAt" {
                merged.insert(key, value);
            }
        }
        merged.insert("id".to_owned(), Value::String(id.to_string()));
        merged.insert("updatedAt".to_owned(), Value::String(timestamp.to_owned()));
        let result = Value::Object(merged);
        let json = serde_json::to_string(&result).map_err(|_| StoreError)?;
        let mut connection = self.open().map_err(|_| StoreError)?;
        let transaction = connection.transaction().map_err(|_| StoreError)?;
        transaction
            .execute(
                "UPDATE native_offline_record SET body_json = ?1, updated_at = ?2
                 WHERE user_id = ?3 AND resource = ?4 AND public_id = ?5",
                params![
                    json,
                    timestamp,
                    user_id.to_string(),
                    resource,
                    id.to_string()
                ],
            )
            .map_err(|_| StoreError)?;
        append_audit(
            &transaction,
            user_id,
            resource,
            id,
            "Update",
            "Succeeded",
            timestamp,
        )
        .map_err(|_| StoreError)?;
        transaction.commit().map_err(|_| StoreError)?;
        Ok(Some(result))
    }

    pub(crate) fn set_deleted(
        &self,
        user_id: Uuid,
        resource: &str,
        id: Uuid,
        deleted: bool,
        hard: bool,
        timestamp: &str,
    ) -> Result<bool, StoreError> {
        let mut connection = self.open().map_err(|_| StoreError)?;
        let transaction = connection.transaction().map_err(|_| StoreError)?;
        let changed = if hard {
            transaction.execute(
                "DELETE FROM native_offline_record WHERE user_id = ?1 AND resource = ?2 AND public_id = ?3",
                params![user_id.to_string(), resource, id.to_string()],
            )
        } else {
            let current: Option<String> = transaction
                .query_row(
                    "SELECT body_json FROM native_offline_record WHERE user_id = ?1 AND resource = ?2 AND public_id = ?3",
                    params![user_id.to_string(), resource, id.to_string()],
                    |row| row.get(0),
                )
                .optional()
                .map_err(|_| StoreError)?;
            let Some(json) = current else { return Ok(false) };
            let mut value: Value = serde_json::from_str(&json).map_err(|_| StoreError)?;
            let object = value.as_object_mut().ok_or(StoreError)?;
            object.insert("isDeleted".to_owned(), Value::Bool(deleted));
            object.insert("updatedAt".to_owned(), Value::String(timestamp.to_owned()));
            transaction.execute(
                "UPDATE native_offline_record SET body_json = ?1, is_deleted = ?2, updated_at = ?3
                 WHERE user_id = ?4 AND resource = ?5 AND public_id = ?6",
                params![serde_json::to_string(&value).map_err(|_| StoreError)?, deleted, timestamp,
                    user_id.to_string(), resource, id.to_string()],
            )
        }
        .map_err(|_| StoreError)?;
        if changed > 0 {
            let operation = if hard {
                "HardDelete"
            } else if deleted {
                "Delete"
            } else {
                "Restore"
            };
            append_audit(
                &transaction,
                user_id,
                resource,
                id,
                operation,
                "Succeeded",
                timestamp,
            )
            .map_err(|_| StoreError)?;
        }
        transaction.commit().map_err(|_| StoreError)?;
        Ok(changed > 0)
    }

    pub(crate) fn audit_entries(&self, user_id: Uuid) -> Result<Vec<Value>, StoreError> {
        let connection = self.open().map_err(|_| StoreError)?;
        let mut statement = connection
            .prepare(
                "SELECT public_id, entity_type, entity_id, operation, outcome, occurred_at
                 FROM native_audit_entry WHERE user_id = ?1 ORDER BY occurred_at DESC, public_id DESC",
            )
            .map_err(|_| StoreError)?;
        let rows = statement
            .query_map(params![user_id.to_string()], |row| {
                Ok(serde_json::json!({
                    "id": row.get::<_, String>(0)?,
                    "entityType": row.get::<_, String>(1)?,
                    "entityId": row.get::<_, String>(2)?,
                    "operation": row.get::<_, String>(3)?,
                    "outcome": row.get::<_, String>(4)?,
                    "occurredAt": row.get::<_, String>(5)?,
                }))
            })
            .map_err(|_| StoreError)?;
        rows.map(|row| row.map_err(|_| StoreError)).collect()
    }

    pub(crate) fn create_job(
        &self,
        user_id: Uuid,
        kind: &str,
        request: &Value,
        timestamp: &str,
    ) -> Result<Value, StoreError> {
        let id = Uuid::new_v4();
        self.open()
            .and_then(|connection| {
                connection.execute(
                    "INSERT INTO native_operation_job(public_id, user_id, kind, status, progress, request_json, created_at, updated_at)
                     VALUES (?1, ?2, ?3, 1, 0, ?4, ?5, ?5)",
                    params![id.to_string(), user_id.to_string(), kind,
                        serde_json::to_string(request).map_err(|_| rusqlite::Error::InvalidQuery)?, timestamp],
                )?;
                Ok(())
            })
            .map_err(|_| StoreError)?;
        Ok(job_json(id, kind, 1, 0, request, timestamp, timestamp))
    }

    pub(crate) fn get_job(&self, user_id: Uuid, id: Uuid) -> Result<Option<Value>, StoreError> {
        self.open()
            .and_then(|connection| {
                connection
                    .query_row(
                        "SELECT kind, status, progress, request_json, created_at, updated_at FROM native_operation_job
                         WHERE user_id = ?1 AND public_id = ?2",
                        params![user_id.to_string(), id.to_string()],
                        |row| {
                            let kind: String = row.get(0)?;
                            let status: i64 = row.get(1)?;
                            let progress: i64 = row.get(2)?;
                            let request_json: String = row.get(3)?;
                            let request = serde_json::from_str(&request_json).unwrap_or(Value::Null);
                            let created_at: String = row.get(4)?;
                            let updated_at: String = row.get(5)?;
                            Ok(job_json(id, &kind, status, progress, &request, &created_at, &updated_at))
                        },
                    )
                    .optional()
            })
            .map_err(|_| StoreError)
    }

    pub(crate) fn list_jobs(&self, user_id: Uuid) -> Result<Vec<Value>, StoreError> {
        let connection = self.open().map_err(|_| StoreError)?;
        let mut statement = connection
            .prepare(
                "SELECT public_id, kind, status, progress, request_json, created_at, updated_at
                 FROM native_operation_job WHERE user_id = ?1 ORDER BY created_at DESC, public_id DESC",
            )
            .map_err(|_| StoreError)?;
        let rows = statement
            .query_map(params![user_id.to_string()], |row| {
                let id = Uuid::parse_str(&row.get::<_, String>(0)?)
                    .map_err(|_| rusqlite::Error::InvalidQuery)?;
                let request_json: String = row.get(4)?;
                let request = serde_json::from_str(&request_json).unwrap_or(Value::Null);
                Ok(job_json(
                    id,
                    &row.get::<_, String>(1)?,
                    row.get(2)?,
                    row.get(3)?,
                    &request,
                    &row.get::<_, String>(5)?,
                    &row.get::<_, String>(6)?,
                ))
            })
            .map_err(|_| StoreError)?;
        rows.map(|row| row.map_err(|_| StoreError)).collect()
    }

    pub(crate) fn requeue_job(
        &self,
        user_id: Uuid,
        id: Uuid,
        timestamp: &str,
    ) -> Result<bool, StoreError> {
        self.open()
            .and_then(|connection| {
                connection.execute(
                    "UPDATE native_operation_job SET status = 1, progress = 0, updated_at = ?1
                     WHERE user_id = ?2 AND public_id = ?3",
                    params![timestamp, user_id.to_string(), id.to_string()],
                )
            })
            .map(|changed| changed > 0)
            .map_err(|_| StoreError)
    }

    pub(crate) fn complete_job(
        &self,
        user_id: Uuid,
        id: Uuid,
        timestamp: &str,
    ) -> Result<(), StoreError> {
        self.open()
            .and_then(|connection| {
                connection.execute(
                    "UPDATE native_operation_job SET status = 3, progress = 100, updated_at = ?1
                     WHERE user_id = ?2 AND public_id = ?3",
                    params![timestamp, user_id.to_string(), id.to_string()],
                )?;
                Ok(())
            })
            .map_err(|_| StoreError)
    }

    fn open(&self) -> rusqlite::Result<Connection> {
        let connection = Connection::open(&self.database_path)?;
        connection.busy_timeout(std::time::Duration::from_secs(5))?;
        connection.pragma_update(None, "foreign_keys", "ON")?;
        Ok(connection)
    }
}

#[derive(Debug)]
pub(crate) struct StoreError;

fn prepare_parent(database_path: &Path) -> std::io::Result<()> {
    if let Some(parent) = database_path
        .parent()
        .filter(|parent| !parent.as_os_str().is_empty())
    {
        std::fs::create_dir_all(parent)?;
    }
    Ok(())
}

fn migrate(database_path: &Path) -> rusqlite::Result<()> {
    let connection = Connection::open(database_path)?;
    connection.busy_timeout(std::time::Duration::from_secs(5))?;
    connection.execute_batch(
        "PRAGMA foreign_keys = ON;
         PRAGMA journal_mode = WAL;
         CREATE TABLE IF NOT EXISTS native_schema_migration (
             version INTEGER PRIMARY KEY NOT NULL,
             applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
         );
         CREATE TABLE IF NOT EXISTS native_user_profile (
             public_id TEXT PRIMARY KEY NOT NULL,
             display_name TEXT NOT NULL CHECK(length(display_name) BETWEEN 1 AND 200),
             display_currency TEXT NULL CHECK(display_currency IS NULL OR length(display_currency) = 3),
             created_at TEXT NOT NULL,
             updated_at TEXT NOT NULL
         );
         CREATE TABLE IF NOT EXISTS native_local_account (
             public_id TEXT PRIMARY KEY NOT NULL,
             user_id TEXT NOT NULL UNIQUE REFERENCES native_user_profile(public_id) ON DELETE CASCADE,
             name TEXT NOT NULL UNIQUE CHECK(length(name) BETWEEN 1 AND 200),
             secret_hash TEXT NOT NULL,
             created_at TEXT NOT NULL,
             updated_at TEXT NOT NULL
         );
         CREATE TABLE IF NOT EXISTS native_financial_account (
             public_id TEXT PRIMARY KEY NOT NULL,
             user_id TEXT NOT NULL REFERENCES native_user_profile(public_id) ON DELETE RESTRICT,
             name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 200),
             institution TEXT NULL CHECK(institution IS NULL OR length(institution) <= 200),
             account_type INTEGER NOT NULL CHECK(account_type BETWEEN 1 AND 4),
             currency_code TEXT NOT NULL CHECK(length(currency_code) = 3),
             opening_balance TEXT NOT NULL,
             is_deleted INTEGER NOT NULL DEFAULT 0 CHECK(is_deleted IN (0, 1)),
             created_at TEXT NOT NULL,
             updated_at TEXT NOT NULL
         );
         CREATE INDEX IF NOT EXISTS ix_native_financial_account_owner
             ON native_financial_account(user_id, is_deleted);
         CREATE TABLE IF NOT EXISTS native_local_recovery_code (
             public_id TEXT PRIMARY KEY NOT NULL,
             local_account_id TEXT NOT NULL REFERENCES native_local_account(public_id) ON DELETE CASCADE,
             code_hash TEXT NOT NULL,
             created_at TEXT NOT NULL,
             used_at TEXT NULL,
             UNIQUE(local_account_id, code_hash)
         );
         CREATE TABLE IF NOT EXISTS native_offline_record (
             public_id TEXT NOT NULL,
             user_id TEXT NOT NULL REFERENCES native_user_profile(public_id) ON DELETE RESTRICT,
             resource TEXT NOT NULL,
             body_json TEXT NOT NULL,
             is_deleted INTEGER NOT NULL DEFAULT 0 CHECK(is_deleted IN (0, 1)),
             created_at TEXT NOT NULL,
             updated_at TEXT NOT NULL,
             PRIMARY KEY(user_id, resource, public_id)
         );
         CREATE INDEX IF NOT EXISTS ix_native_offline_record_owner_resource
             ON native_offline_record(user_id, resource, is_deleted, created_at);
         INSERT OR IGNORE INTO native_offline_record(
             public_id, user_id, resource, body_json, is_deleted, created_at, updated_at)
         SELECT public_id, user_id, 'accounts', json_object(
             'id', public_id,
             'name', name,
             'institution', institution,
             'accountType', account_type,
             'currencyCode', currency_code,
             'openingBalance', json(opening_balance),
             'isDeleted', json(CASE is_deleted WHEN 1 THEN 'true' ELSE 'false' END),
             'createdAt', created_at,
             'updatedAt', updated_at),
             is_deleted, created_at, updated_at
         FROM native_financial_account;
         CREATE TABLE IF NOT EXISTS native_audit_entry (
             public_id TEXT PRIMARY KEY NOT NULL,
             user_id TEXT NOT NULL REFERENCES native_user_profile(public_id) ON DELETE RESTRICT,
             entity_type TEXT NOT NULL,
             entity_id TEXT NOT NULL,
             operation TEXT NOT NULL,
             outcome TEXT NOT NULL,
             occurred_at TEXT NOT NULL
         );
         CREATE INDEX IF NOT EXISTS ix_native_audit_entry_owner
             ON native_audit_entry(user_id, occurred_at);
         CREATE TABLE IF NOT EXISTS native_operation_job (
             public_id TEXT PRIMARY KEY NOT NULL,
             user_id TEXT NOT NULL REFERENCES native_user_profile(public_id) ON DELETE RESTRICT,
             kind TEXT NOT NULL,
             status INTEGER NOT NULL CHECK(status BETWEEN 1 AND 4),
             progress INTEGER NOT NULL CHECK(progress BETWEEN 0 AND 100),
             request_json TEXT NOT NULL,
             created_at TEXT NOT NULL,
             updated_at TEXT NOT NULL
         );
         INSERT OR IGNORE INTO native_schema_migration(version) VALUES (1);",
    )?;
    let has_display_currency = {
        let mut statement = connection.prepare("PRAGMA table_info(native_user_profile)")?;
        statement
            .query_map([], |row| row.get::<_, String>(1))?
            .collect::<rusqlite::Result<Vec<_>>>()?
            .iter()
            .any(|column| column == "display_currency")
    };
    if !has_display_currency {
        connection.execute(
            "ALTER TABLE native_user_profile ADD COLUMN display_currency TEXT NULL",
            [],
        )?;
    }
    connection.execute(
        "INSERT OR IGNORE INTO native_schema_migration(version) VALUES (2)",
        [],
    )?;
    Ok(())
}

fn append_audit(
    transaction: &rusqlite::Transaction<'_>,
    user_id: Uuid,
    entity_type: &str,
    entity_id: Uuid,
    operation: &str,
    outcome: &str,
    timestamp: &str,
) -> rusqlite::Result<()> {
    transaction.execute(
        "INSERT INTO native_audit_entry(public_id, user_id, entity_type, entity_id, operation, outcome, occurred_at)
         VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)",
        params![Uuid::new_v4().to_string(), user_id.to_string(), entity_type, entity_id.to_string(),
            operation, outcome, timestamp],
    )?;
    Ok(())
}

fn job_json(
    id: Uuid,
    kind: &str,
    status: i64,
    progress: i64,
    request: &Value,
    created_at: &str,
    updated_at: &str,
) -> Value {
    let mut output = Map::new();
    output.insert("id".to_owned(), Value::String(id.to_string()));
    output.insert("kind".to_owned(), Value::String(kind.to_owned()));
    output.insert("status".to_owned(), Value::Number(status.into()));
    output.insert("progress".to_owned(), Value::Number(progress.into()));
    output.insert("request".to_owned(), request.clone());
    output.insert("createdAt".to_owned(), Value::String(created_at.to_owned()));
    output.insert("updatedAt".to_owned(), Value::String(updated_at.to_owned()));
    Value::Object(output)
}
