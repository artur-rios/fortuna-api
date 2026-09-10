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

The header additionally contains 111 concrete route functions generated from the checked-in OpenAPI
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

`fortuna_capabilities` is the machine-readable registry. It includes each function's method, path,
area and `longRunning` flag and explains why connected identity, administrator-only user erasure,
Pluggy, remote rate synchronization and HTTP-host health routes are absent. Imports and exports return `202` with a persisted job and
progress; job reads do not block the caller.

Local tokens are opaque, are stored only as SHA-256 hashes, expire at the configured lifetime, and
are invalidated at shutdown. Credential-bearing request copies are zeroized and never logged.

## Persistence boundary

The native core owns tables prefixed `native_` and applies idempotent SQLite migrations during
initialization. It does not use EF migrations or the .NET domain/application assemblies. Monetary
amounts retain their decimal JSON lexemes in SQLite `TEXT` documents and are parsed with arbitrary
precision, never through binary floating point. The schema contains local profiles, credential and
recovery-code hashes, owner-keyed offline records, append-only audit entries and monitorable
operation jobs. Every record query includes the authenticated owner identifier even though a desktop
installation normally has only one local user. Audit rows carry a separate random subject reference;
confirmed owner erasure deletes its mapping and all identity/record/job rows atomically while leaving
the now-unlinkable audit facts intact.
