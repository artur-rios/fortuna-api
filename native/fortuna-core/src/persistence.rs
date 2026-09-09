use std::path::{Path, PathBuf};

use rusqlite::{Connection, OptionalExtension, params};
use uuid::Uuid;

#[derive(Debug)]
pub(crate) struct NativeStore {
    database_path: PathBuf,
}

#[derive(Debug)]
pub(crate) struct CredentialRecord {
    pub(crate) user_id: String,
    pub(crate) secret_hash: String,
}

#[derive(Debug)]
pub(crate) struct FinancialAccountRecord {
    pub(crate) public_id: String,
    pub(crate) name: String,
    pub(crate) institution: Option<String>,
    pub(crate) account_type: i16,
    pub(crate) currency_code: String,
    pub(crate) opening_balance: String,
    pub(crate) is_deleted: bool,
    pub(crate) created_at: String,
    pub(crate) updated_at: String,
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

    pub(crate) fn find_financial_account(
        &self,
        public_id: Uuid,
        user_id: Uuid,
        include_deleted: bool,
    ) -> Result<Option<FinancialAccountRecord>, StoreError> {
        self.open()
            .and_then(|connection| {
                connection
                    .query_row(
                        "SELECT public_id, name, institution, account_type, currency_code, opening_balance,
                                is_deleted, created_at, updated_at
                         FROM native_financial_account
                         WHERE public_id = ?1 AND user_id = ?2 AND (?3 = 1 OR is_deleted = 0)",
                        params![public_id.to_string(), user_id.to_string(), include_deleted],
                        |row| {
                            Ok(FinancialAccountRecord {
                                public_id: row.get(0)?,
                                name: row.get(1)?,
                                institution: row.get(2)?,
                                account_type: row.get(3)?,
                                currency_code: row.get(4)?,
                                opening_balance: row.get(5)?,
                                is_deleted: row.get(6)?,
                                created_at: row.get(7)?,
                                updated_at: row.get(8)?,
                            })
                        },
                    )
                    .optional()
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
         INSERT OR IGNORE INTO native_schema_migration(version) VALUES (1);",
    )?;
    Ok(())
}
