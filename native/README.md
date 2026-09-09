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
| `fortuna_api_local_accounts_authenticate` | HTTP authentication body: `{"name":"...","secret":"..."}` | `200` |
| `fortuna_api_accounts_get_by_id` | `{"token":"...","id":"uuid","includeDeleted":false}` | `200` |

The account read wraps the HTTP route value, query value, and authorization metadata in its one ABI
request object; its response is the same `DataOutput<FinancialAccountOutput?>` body returned by
`GET /api/accounts/{id}`. Local tokens are opaque, are stored only as SHA-256 hashes, expire at the
configured lifetime, and are invalidated at shutdown. Credential-bearing request copies are
zeroized and never logged.

## Persistence boundary

The native core owns tables prefixed `native_` and applies idempotent SQLite migrations during
initialization. It does not use EF migrations or the .NET domain/application assemblies. Monetary
amounts are stored as SQLite `TEXT` and parsed into arbitrary-precision JSON numbers, never through
binary floating point. The initial schema exists to prove local authentication and one owned account
read; issue #157 expands it with the rest of the offline operation surface.
