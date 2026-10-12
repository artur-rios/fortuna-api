//! Category reassignment (UC-44), counterparty merging and category suggestion (UC-47), and the
//! counterparty a transaction or recurring rule names (FR-CT-10), ported from the HTTP host's
//! `EfCategoryStore`, `EfCounterpartyStore` and `ClassificationResolver` with the same rules,
//! messages and statuses.

use std::cmp::Ordering;
use std::collections::{BTreeSet, HashMap};
use std::ffi::c_int;

use chrono::NaiveDate;
use serde_json::{Value, json};
use uuid::Uuid;

use super::operation::{OperationRequest, failure, request_body, success};
use super::persistence::{RecordTransaction, StoreError, record_id};
use super::{
    Core, FORTUNA_STATUS_BAD_REQUEST, FORTUNA_STATUS_INTERNAL_ERROR, FORTUNA_STATUS_NOT_FOUND,
    FORTUNA_STATUS_OK, INTERNAL_ERROR,
};

const INVALID_REQUEST: &str =
    "The request does not contain the route values or body required by this operation.";

const CATEGORY_NOT_FOUND: &str = "Category not found.";
const TARGET_CATEGORY_ID_INVALID: &str = "TargetCategoryId cannot be empty.";
const SOURCE_AND_TARGET_MUST_DIFFER: &str = "Source and target categories must be different.";
const TRANSACTIONS_REASSIGNED: &str = "Category transactions reassigned successfully.";

const COUNTERPARTY_NOT_FOUND: &str = "Counterparty not found.";
const TARGET_ID_INVALID: &str = "TargetId cannot be empty.";
const SAME_COUNTERPARTY: &str = "A counterparty cannot be merged into itself.";
const MERGED: &str = "Counterparties merged successfully.";
const SUGGESTED: &str = "Category suggestion retrieved successfully.";
const NO_SUGGESTION: &str = "No prior category was found for this counterparty.";
/// `TransactionMessages.CounterpartyTooLong` and `RecurringTransactionMessages.CounterpartyTooLong`.
const COUNTERPARTY_TOO_LONG: &str = "Counterparty cannot exceed 200 characters.";
const COUNTERPARTY_MAXIMUM_LENGTH: usize = 200;

const CATEGORIES: &str = "categories";
const COUNTERPARTIES: &str = "counterparties";
const TRANSACTIONS: &str = "transactions";
const RECURRING_TRANSACTIONS: &str = "recurring-transactions";

/// Whether records of `resource` name their counterparty and so are linked to one.
pub(crate) fn names_counterparty(resource: &str) -> bool {
    matches!(resource, TRANSACTIONS | RECURRING_TRANSACTIONS)
}

/// What a create or update body says about the record's counterparty.
pub(crate) enum CounterpartyLink {
    /// The body does not mention a counterparty; an update keeps the current one.
    Unchanged,
    /// The body names no counterparty (absent on create, null or blank).
    None,
    /// The trimmed name to match or create.
    Named(String),
}

/// Reads the `counterparty` name of a transaction or recurring-rule body, validated as the HTTP
/// validators do. `counterpartyId` is server-owned and is dropped from the body.
pub(crate) fn counterparty_link(
    body: &mut Value,
    creating: bool,
) -> Result<CounterpartyLink, (c_int, String)> {
    let Some(object) = body.as_object_mut() else {
        return Err((FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST.to_owned()));
    };
    object.remove("counterpartyId");
    match object.get("counterparty") {
        None if !creating => Ok(CounterpartyLink::Unchanged),
        None | Some(Value::Null) => Ok(CounterpartyLink::None),
        Some(Value::String(name)) => {
            let name = name.trim();
            if name.is_empty() {
                Ok(CounterpartyLink::None)
            } else if name.chars().count() > COUNTERPARTY_MAXIMUM_LENGTH {
                Err((FORTUNA_STATUS_BAD_REQUEST, COUNTERPARTY_TOO_LONG.to_owned()))
            } else {
                Ok(CounterpartyLink::Named(name.to_owned()))
            }
        }
        Some(_) => Err((FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST.to_owned())),
    }
}

/// Sets `counterpartyId` on `body` from `link`, matching the name to a live counterparty of the
/// owner or creating one, inside the caller's transaction (`ClassificationResolver`).
pub(crate) fn apply_counterparty_link(
    records: &RecordTransaction<'_>,
    body: &mut Value,
    link: CounterpartyLink,
) -> Result<(), StoreError> {
    let counterparty_id = match link {
        CounterpartyLink::Unchanged => return Ok(()),
        CounterpartyLink::None => Value::Null,
        CounterpartyLink::Named(name) => {
            let normalized = normalize_name(&name);
            let existing =
                records
                    .records(COUNTERPARTIES, false)?
                    .into_iter()
                    .find(|counterparty| {
                        counterparty["name"]
                            .as_str()
                            .is_some_and(|candidate| normalize_name(candidate) == normalized)
                    });
            let counterparty = match existing {
                Some(counterparty) => counterparty,
                None => records.insert(COUNTERPARTIES, json!({ "name": name }))?,
            };
            Value::String(record_id(&counterparty).ok_or(StoreError)?.to_string())
        }
    };
    body.as_object_mut()
        .ok_or(StoreError)?
        .insert("counterpartyId".to_owned(), counterparty_id);
    Ok(())
}

/// Links every transaction and recurring rule of the owner that names a counterparty but was
/// stored before records were linked (it has no `counterpartyId` at all). A name the HTTP API
/// would have refused, longer than 200 characters, is left unlinked.
pub(crate) fn link_named_counterparties(records: &RecordTransaction<'_>) -> Result<(), StoreError> {
    for resource in [TRANSACTIONS, RECURRING_TRANSACTIONS] {
        for mut record in records.records(resource, true)? {
            if record
                .as_object()
                .is_none_or(|object| object.contains_key("counterpartyId"))
            {
                continue;
            }
            let link = counterparty_link(&mut record, true).unwrap_or(CounterpartyLink::None);
            apply_counterparty_link(records, &mut record, link)?;
            records.rewrite_keeping_timestamp(resource, &record)?;
        }
    }
    Ok(())
}

/// `POST /api/categories/{id}/reassign`.
pub(crate) fn reassign_category_transactions(
    core: &Core,
    request: &OperationRequest,
    user_id: Uuid,
    source_id: Uuid,
    timestamp: &str,
) -> (c_int, String) {
    let body = request_body(request);
    let include_descendants = match body.get("includeDescendants") {
        None | Some(Value::Null) => false,
        Some(Value::Bool(value)) => *value,
        Some(_) => return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST),
    };
    let target_id = match optional_uuid(&body, "targetCategoryId") {
        Ok(Some(id)) if !id.is_nil() => id,
        Ok(_) => return failure(FORTUNA_STATUS_BAD_REQUEST, TARGET_CATEGORY_ID_INVALID),
        Err(()) => return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST),
    };
    if target_id == source_id {
        return failure(FORTUNA_STATUS_BAD_REQUEST, SOURCE_AND_TARGET_MUST_DIFFER);
    }

    let reassigned = core.store.write(user_id, timestamp, |records| {
        let categories = records.records(CATEGORIES, true)?;
        let is_live = |id: Uuid| {
            categories
                .iter()
                .any(|category| record_id(category) == Some(id) && !is_deleted(category))
        };
        if !is_live(source_id) || !is_live(target_id) {
            return Ok(None);
        }

        // The hierarchy walk includes soft-deleted descendants, as the HTTP host's does.
        let mut sources = BTreeSet::from([source_id]);
        if include_descendants {
            let mut pending = vec![source_id];
            while let Some(parent) = pending.pop() {
                for child in categories
                    .iter()
                    .filter(|category| uuid_field(category, "parentId") == Some(parent))
                    .filter_map(record_id)
                {
                    if sources.insert(child) {
                        pending.push(child);
                    }
                }
            }
        }
        sources.remove(&target_id);

        let mut count = 0_u64;
        for mut transaction in records.records(TRANSACTIONS, false)? {
            if uuid_field(&transaction, "categoryId").is_some_and(|id| sources.contains(&id)) {
                set_field(&mut transaction, "categoryId", target_id)?;
                if records.rewrite(TRANSACTIONS, &transaction)? {
                    count += 1;
                }
            }
        }
        records.audit(CATEGORIES, source_id, "Reassign")?;
        Ok(Some(count))
    });

    match reassigned {
        Ok(Some(count)) => success(
            FORTUNA_STATUS_OK,
            json!({
                "id": source_id,
                "targetCategoryId": target_id,
                "includeDescendants": include_descendants,
                "reassignedCount": count,
            }),
            TRANSACTIONS_REASSIGNED,
        ),
        Ok(None) => failure(FORTUNA_STATUS_NOT_FOUND, CATEGORY_NOT_FOUND),
        Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
    }
}

/// `POST /api/counterparties/{id}/merge`.
pub(crate) fn merge_counterparties(
    core: &Core,
    request: &OperationRequest,
    user_id: Uuid,
    source_id: Uuid,
    timestamp: &str,
) -> (c_int, String) {
    let body = request_body(request);
    let target_id = match optional_uuid(&body, "targetId") {
        Ok(Some(id)) if !id.is_nil() => id,
        Ok(_) => return failure(FORTUNA_STATUS_BAD_REQUEST, TARGET_ID_INVALID),
        Err(()) => return failure(FORTUNA_STATUS_BAD_REQUEST, INVALID_REQUEST),
    };
    if target_id == source_id {
        return failure(FORTUNA_STATUS_BAD_REQUEST, SAME_COUNTERPARTY);
    }

    let merged = core.store.write(user_id, timestamp, |records| {
        let live = records.records(COUNTERPARTIES, false)?;
        let find = |id: Uuid| {
            live.iter()
                .find(|counterparty| record_id(counterparty) == Some(id))
        };
        let (Some(source), Some(_)) = (find(source_id), find(target_id)) else {
            return Ok(None);
        };

        // Every transaction and recurring rule of the source moves, deleted ones included, so
        // a later restore cannot point at the retired counterparty.
        let mut count = 0_u64;
        for resource in [TRANSACTIONS, RECURRING_TRANSACTIONS] {
            for mut record in records.records(resource, true)? {
                if uuid_field(&record, "counterpartyId") == Some(source_id) {
                    set_field(&mut record, "counterpartyId", target_id)?;
                    if records.rewrite(resource, &record)? && resource == TRANSACTIONS {
                        count += 1;
                    }
                }
            }
        }
        let mut source = source.clone();
        source
            .as_object_mut()
            .ok_or(StoreError)?
            .insert("isDeleted".to_owned(), Value::Bool(true));
        records.replace(COUNTERPARTIES, &source, "Merge")?;
        Ok(Some(count))
    });

    match merged {
        Ok(Some(count)) => success(
            FORTUNA_STATUS_OK,
            json!({
                "id": source_id,
                "sourceId": source_id,
                "targetId": target_id,
                "reassignedTransactionCount": count,
            }),
            MERGED,
        ),
        Ok(None) => failure(FORTUNA_STATUS_NOT_FOUND, COUNTERPARTY_NOT_FOUND),
        Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
    }
}

/// `GET /api/counterparties/{id}/suggested-category`: the category of the owner's most recent
/// live transaction with the counterparty whose category is live.
pub(crate) fn suggest_category(
    core: &Core,
    user_id: Uuid,
    counterparty_id: Uuid,
) -> (c_int, String) {
    let suggestion = core.store.read(user_id, |records| {
        let counterparty_is_live = records
            .records(COUNTERPARTIES, false)?
            .iter()
            .any(|counterparty| record_id(counterparty) == Some(counterparty_id));
        if !counterparty_is_live {
            return Ok(None);
        }
        let categories = records
            .records(CATEGORIES, false)?
            .into_iter()
            .filter_map(|category| Some((record_id(&category)?, category)))
            .collect::<HashMap<_, _>>();
        let mut candidates = records
            .records(TRANSACTIONS, false)?
            .into_iter()
            .enumerate()
            .filter(|(_, transaction)| {
                uuid_field(transaction, "counterpartyId") == Some(counterparty_id)
            })
            .filter_map(|(position, transaction)| {
                let category = categories.get(&uuid_field(&transaction, "categoryId")?)?;
                Some((position, transaction, category))
            })
            .collect::<Vec<_>>();
        // Most recent first: OccurredOn, then CreatedAt, then the order of storage.
        candidates.sort_by(|(left_position, left, _), (right_position, right, _)| {
            compare_dates(right, left, "occurredOn")
                .then_with(|| compare_text(right, left, "createdAt"))
                .then_with(|| right_position.cmp(left_position))
        });
        Ok(Some(candidates.first().map(|(_, _, category)| {
            (
                record_id(category).map(|id| id.to_string()),
                category.get("name").cloned().unwrap_or(Value::Null),
            )
        })))
    });

    match suggestion {
        Ok(Some(category)) => {
            let has_suggestion = category.is_some();
            let (category_id, category_name) = category.unwrap_or((None, Value::Null));
            success(
                FORTUNA_STATUS_OK,
                json!({
                    "counterpartyId": counterparty_id,
                    "hasSuggestion": has_suggestion,
                    "categoryId": category_id,
                    "categoryName": category_name,
                }),
                if has_suggestion {
                    SUGGESTED
                } else {
                    NO_SUGGESTION
                },
            )
        }
        Ok(None) => failure(FORTUNA_STATUS_NOT_FOUND, COUNTERPARTY_NOT_FOUND),
        Err(_) => failure(FORTUNA_STATUS_INTERNAL_ERROR, INTERNAL_ERROR),
    }
}

/// The HTTP host's `Trim().ToUpperInvariant()`: a character whose upper case is more than one
/// character (such as `ß`) is kept, as invariant simple case mapping does.
fn normalize_name(name: &str) -> String {
    name.trim()
        .chars()
        .map(|character| {
            let mut upper = character.to_uppercase();
            match (upper.next(), upper.next()) {
                (Some(single), None) => single,
                _ => character,
            }
        })
        .collect()
}

/// `Ok(None)` when the field is absent or null, `Err` when it is not a UUID string.
fn optional_uuid(body: &Value, name: &str) -> Result<Option<Uuid>, ()> {
    match body.get(name) {
        None | Some(Value::Null) => Ok(None),
        Some(Value::String(value)) => Uuid::parse_str(value).map(Some).map_err(|_| ()),
        Some(_) => Err(()),
    }
}

fn uuid_field(record: &Value, name: &str) -> Option<Uuid> {
    record
        .get(name)
        .and_then(Value::as_str)
        .and_then(|value| Uuid::parse_str(value).ok())
}

fn set_field(record: &mut Value, name: &str, id: Uuid) -> Result<(), StoreError> {
    record
        .as_object_mut()
        .ok_or(StoreError)?
        .insert(name.to_owned(), Value::String(id.to_string()));
    Ok(())
}

fn is_deleted(record: &Value) -> bool {
    record["isDeleted"].as_bool().unwrap_or(false)
}

fn compare_dates(left: &Value, right: &Value, name: &str) -> Ordering {
    let date = |record: &Value| {
        record
            .get(name)
            .and_then(Value::as_str)
            .and_then(|value| value.get(..10))
            .and_then(|value| NaiveDate::parse_from_str(value, "%Y-%m-%d").ok())
    };
    date(left).cmp(&date(right))
}

fn compare_text(left: &Value, right: &Value, name: &str) -> Ordering {
    left.get(name)
        .and_then(Value::as_str)
        .cmp(&right.get(name).and_then(Value::as_str))
}
