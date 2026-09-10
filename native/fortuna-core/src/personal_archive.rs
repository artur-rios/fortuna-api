use std::collections::BTreeMap;
use std::io::{Cursor, Write};

use base64::Engine;
use base64::engine::general_purpose::STANDARD;
use serde_json::Value;
use zip::CompressionMethod;
use zip::ZipWriter;
use zip::write::SimpleFileOptions;

pub(crate) const STANDARD_PARTS: &[&str] = &[
    "profile",
    "financial-accounts",
    "credit-cards",
    "credit-card-statements",
    "investments",
    "investment-movements",
    "investment-valuations",
    "transactions",
    "transfers",
    "installment-plans",
    "recurring-transactions",
    "categories",
    "tags",
    "counterparties",
    "budgets",
    "goals",
    "connections",
    "connection-resources",
    "import-jobs",
    "imported-records",
    "attachments",
    "audit-entries",
    "export-history",
];

pub(crate) struct PersonalDataSnapshot {
    pub(crate) profile: Value,
    pub(crate) records: BTreeMap<String, Vec<Value>>,
    pub(crate) audit_entries: Vec<Value>,
    pub(crate) import_jobs: Vec<Value>,
    pub(crate) export_history: Vec<Value>,
}

pub(crate) fn build(
    mut snapshot: PersonalDataSnapshot,
    generated_at: &str,
    expires_at: &str,
) -> Result<Vec<u8>, ()> {
    let mut parts = BTreeMap::<String, Vec<Value>>::new();
    for name in STANDARD_PARTS {
        parts.insert((*name).to_owned(), Vec::new());
    }
    parts
        .get_mut("profile")
        .expect("standard part")
        .push(snapshot.profile);
    parts
        .get_mut("audit-entries")
        .expect("standard part")
        .append(&mut snapshot.audit_entries);
    parts
        .get_mut("import-jobs")
        .expect("standard part")
        .append(&mut snapshot.import_jobs);
    parts
        .get_mut("export-history")
        .expect("standard part")
        .append(&mut snapshot.export_history);
    for (resource, mut records) in snapshot.records {
        parts
            .entry(canonical_part(&resource))
            .or_default()
            .append(&mut records);
    }

    let cursor = Cursor::new(Vec::new());
    let mut zip = ZipWriter::new(cursor);
    let options = SimpleFileOptions::default().compression_method(CompressionMethod::Deflated);
    let mut manifest_parts = Vec::new();
    let mut attachments = Vec::new();

    for (name, records) in &mut parts {
        for record in records.iter_mut() {
            sanitize(record, None);
            if name == "attachments" {
                if let Some(file) = attachment_file(record) {
                    zip.start_file(&file.0, options).map_err(|_| ())?;
                    zip.write_all(&file.1).map_err(|_| ())?;
                    attachments.push(file.0);
                }
            }
        }
        let data_path = format!("data/{name}.json");
        let schema_path = format!("schemas/{name}.schema.json");
        write_json(
            &mut zip,
            &data_path,
            &serde_json::json!({"schemaVersion": 1, "records": records}),
            options,
        )?;
        write_json(
            &mut zip,
            &schema_path,
            &serde_json::json!({
                "$schema": "https://json-schema.org/draft/2020-12/schema",
                "title": format!("Fortuna native {name} portability records"),
                "type": "object",
                "required": ["schemaVersion", "records"],
                "properties": {
                    "schemaVersion": {"type": "integer", "const": 1},
                    "records": {"type": "array", "items": {"type": "object"}}
                }
            }),
            options,
        )?;
        manifest_parts.push(serde_json::json!({
            "name": name,
            "path": data_path,
            "schema": schema_path,
            "count": records.len()
        }));
    }
    attachments.sort();
    write_json(
        &mut zip,
        "manifest.json",
        &serde_json::json!({
            "schemaVersion": 1,
            "archiveType": "fortuna-personal-data",
            "generatedAt": generated_at,
            "expiresAt": expires_at,
            "parts": manifest_parts,
            "attachments": attachments
        }),
        options,
    )?;
    zip.finish()
        .map(|cursor| cursor.into_inner())
        .map_err(|_| ())
}

fn write_json(
    zip: &mut ZipWriter<Cursor<Vec<u8>>>,
    path: &str,
    value: &Value,
    options: SimpleFileOptions,
) -> Result<(), ()> {
    zip.start_file(path, options).map_err(|_| ())?;
    serde_json::to_writer_pretty(zip, value).map_err(|_| ())?;
    Ok(())
}

fn canonical_part(resource: &str) -> String {
    match resource {
        "accounts" => "financial-accounts".to_owned(),
        "statements" => "credit-card-statements".to_owned(),
        value => value.to_owned(),
    }
}

fn attachment_file(record: &mut Value) -> Option<(String, Vec<u8>)> {
    let object = record.as_object_mut()?;
    let encoded = object
        .remove("contentBase64")
        .or_else(|| object.remove("content"))?
        .as_str()?
        .to_owned();
    let content = STANDARD.decode(encoded).ok()?;
    let id = safe_component(object.get("id")?.as_str()?);
    let name = safe_component(
        object
            .get("fileName")
            .and_then(Value::as_str)
            .unwrap_or("attachment"),
    );
    let path = format!("attachments/{id}/{name}");
    object.insert("archivePath".to_owned(), Value::String(path.clone()));
    Some((path, content))
}

fn safe_component(value: &str) -> String {
    let value = value
        .chars()
        .map(|character| match character {
            '/' | '\\' | '\0' => '_',
            other => other,
        })
        .collect::<String>();
    if value.is_empty() {
        "attachment".to_owned()
    } else {
        value
    }
}

fn sanitize(value: &mut Value, key: Option<&str>) {
    if key.is_some_and(sensitive_name) {
        *value = Value::String("[redacted]".to_owned());
        return;
    }
    match value {
        Value::Object(object) => {
            for (name, child) in object {
                sanitize(child, Some(name));
            }
        }
        Value::Array(array) => {
            for child in array {
                sanitize(child, key);
            }
        }
        Value::Number(number) if key.is_some_and(money_name) => {
            *value = Value::String(number.to_string());
        }
        _ => {}
    }
}

fn normalized(name: &str) -> String {
    name.chars()
        .filter(|character| character.is_ascii_alphanumeric())
        .flat_map(char::to_lowercase)
        .collect()
}

fn sensitive_name(name: &str) -> bool {
    let name = normalized(name);
    ["password", "secret", "token", "credential", "recoverycode"]
        .iter()
        .any(|marker| name.contains(marker))
}

fn money_name(name: &str) -> bool {
    let name = normalized(name);
    [
        "amount",
        "balance",
        "value",
        "price",
        "rate",
        "limit",
        "income",
        "expense",
        "budget",
        "principal",
        "interest",
        "cost",
        "proceeds",
    ]
    .iter()
    .any(|marker| name.contains(marker))
}
