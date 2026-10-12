# Fortuna native core

The native core is the in-process backend for Windows and Linux desktop installations. It is a
Rust `cdylib` with its own SQLite persistence layer, selected after the .NET NativeAOT feasibility
probe showed that EF Core 10 could not produce this library reliably. The ASP.NET Core API remains
the server and connected-mode backend; it is not linked into this library.

## Build and test

```bash
cargo test --manifest-path native/fortuna-core/Cargo.toml
cargo build --manifest-path native/fortuna-core/Cargo.toml --release
```

The build produces `libfortuna_core.so` on Linux and `fortuna_core.dll` on Windows. It also runs
`cbindgen`, which generates the committed [C header](fortuna-core/include/fortuna_core.h). To verify
that the published header has not drifted:

```bash
cargo build --manifest-path native/fortuna-core/Cargo.toml
git diff --exit-code -- native/fortuna-core/include/fortuna_core.h
```

CI runs the tests and publishes a native library plus the header for both supported platforms.

## ABI contract

Every function except `fortuna_string_free` returns an HTTP-compatible numeric status and writes a
UTF-8 JSON response to `char **response_json`. Responses use the HTTP API's
`data`, `messages`, `errors`, `timestamp`, `success` envelope in that order. The caller owns no
returned allocation: it must pass every non-null response to `fortuna_string_free` exactly once,
including failure responses.

The caller must pass valid NUL-terminated UTF-8 for request strings and a valid response out pointer.
A null response out pointer returns `400` because no error body can safely be written. No Rust panic
is allowed to unwind across the boundary. Calls are safe from concurrent background threads; each
operation opens its own SQLite connection, while lifecycle changes and the hashed session registry
are synchronized.

| Function | Request JSON | Success |
| --- | --- | --- |
| `fortuna_initialize` | `{"databasePath":"/path/fortuna.db","localAuthEnabled":true,"tokenLifetimeSeconds":3600}` | `200` |
| `fortuna_shutdown` | `{}` | `200` |
| `fortuna_version` | `{}` | `200` |
| `fortuna_health` | `{}` | `200` after initialization |
| `fortuna_capabilities` | `{}` | `200`; initialization is not required |
| `fortuna_api_local_accounts_authenticate` | HTTP authentication body: `{"name":"...","secret":"..."}` | `200` |
| `fortuna_api_accounts_get_by_id` | `{"token":"...","id":"uuid","includeDeleted":false}` | `200` |

The header additionally contains 115 concrete route functions generated from the checked-in OpenAPI
contract. Their deterministic names end in the lowercase HTTP method; for example,
`fortuna_api_transactions_by_id_put` mirrors `PUT /api/transactions/{id}`. An authenticated call
wraps transport metadata while leaving the HTTP body unchanged:

```json
{
  "token": "opaque-local-token",
  "route": { "id": "00000000-0000-0000-0000-000000000000" },
  "query": { "includeDeleted": false },
  "body": { "name": "Updated name" }
}
```

`fortuna_capabilities` is the machine-readable registry. Its `operations` list gives each implemented
function's symbol, method, path, area and `longRunning` flag, and `unavailable` explains why
connected identity, administrator-only user erasure, Pluggy, remote rate synchronization and
HTTP-host health routes are not exported at all.

### Exported but not implemented offline

Every eligible route has an export, so the ABI stays stable, but 26 of them are not implemented by the
native core. Each answers `FORTUNA_STATUS_NOT_IMPLEMENTED` (`501`) with a failure envelope whose
error names the route and the reason, before and after initialization alike, and does no work: no job
is queued, no record is stored. `fortuna_capabilities` lists them under `notImplemented` (same fields
as `operations`, plus `reason`) and not under `operations`, so a client can show them as not
available offline without calling them. The generated header lists them too.

| Routes | Why |
| --- | --- |
| `POST /api/imports/excel`, `POST /api/imports/pdf`, `POST /api/import-jobs/{id}/retry` | No workbook or PDF statement parser exists natively, so an import would not be processed |
| `POST /api/exports` | No data-set export renderer exists natively; the personal data archive is available |
| `/api/reports/*` (4), `/api/projections/*` (2) | Reports and projections are not computed natively |
| `/api/transfers` and `/api/transfers/{id}` (4) | Paired transaction legs, conversion and lifecycle cascade are not implemented |
| `/api/installment-plans` and `/api/installment-plans/{id}` (4) | Splitting a purchase into installments assigned to billing cycles is not implemented |
| `GET /api/statements/{id}`, `POST /api/statements/{id}/close`, `POST /api/statements/{id}/settle`, `GET /api/credit-cards/{id}/statements` | Charges are not assigned to billing cycles natively |
| `POST /api/recurring-transactions/materialize` | Occurrences are not materialized natively |
| `GET /api/budgets/{id}/consumption`, `GET /api/goals/{id}/progress` | Consumption and progress are not computed natively |
| `POST /api/transactions/{id}/reconcile` | Needs imported records, which the native core does not create |

Earlier versions answered these with a success while doing nothing: imports and exports returned
`202` and marked their job completed without parsing or rendering anything, and the rest stored the
request as one generic record. Schema migration 5 marks every such import and export job failed with
the reason, so a client shows that the file was never imported. The generic records are kept and
are part of the personal data archive.

Import-job reads (`GET /api/import-jobs`, `GET /api/import-jobs/{id}`, its `records`) and
`GET /api/exports/{id}` remain available, so those jobs can still be seen.

### Classification rules ported from the HTTP API

Category reassignment (`POST /api/categories/{id}/reassign`), counterparty merging
(`POST /api/counterparties/{id}/merge`) and category suggestion
(`GET /api/counterparties/{id}/suggested-category`) apply the HTTP API's rules, messages and statuses.
They rely on the counterparty a transaction names: creating or updating a transaction or a
recurring rule with a `counterparty` name links it, by `counterpartyId`, to the owner's live
counterparty with the same trimmed, case-insensitive name, creating one when none exists. A blank
name clears the link; a name over 200 characters is refused. Schema migration 6 links, once, the transactions and
rules earlier versions stored with the name only, keeping their timestamps.

Personal-data portability is implemented inside the native boundary rather than delegated to the
hosted API. `POST /api/me/data-export` snapshots owner-keyed native records into an expiring ZIP,
stores the blob beside its native job, and returns immediately. The job route returns its ZIP as
base64 in the C ABI envelope. The archive contains a manifest, JSON data and schemas, real attachment
bytes when present, invariant decimal strings and redacted imported payloads; credential and recovery
tables are never read by the builder.

Local tokens are opaque, are stored only as SHA-256 hashes, expire at the configured lifetime, and
are invalidated at shutdown. Credential-bearing request copies are zeroized and never logged.

## Persistence boundary

The native core owns tables prefixed `native_` and applies idempotent SQLite migrations during
initialization. It does not use EF migrations or the .NET domain/application assemblies. Monetary
amounts retain their decimal JSON lexemes in SQLite `TEXT` documents and are parsed with arbitrary
precision, never through binary floating point. The schema contains local profiles, credential and
recovery-code hashes, owner-keyed offline records, append-only audit entries and monitorable
operation jobs, and expiring personal-archive blobs. Every record query includes the authenticated owner identifier even though a desktop
installation normally has only one local user. Audit rows carry a separate random subject reference;
confirmed owner erasure deletes its mapping and all identity/record/job rows atomically while leaving
the now-unlinkable audit facts intact.
