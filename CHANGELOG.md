# Changelog

All notable changes to the Fortuna API are recorded in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [3.1.0] - 2026-10-20

### Added

- `FORTUNA_HEIMDALL_BASE_URL` may be plain HTTP to a private host, so Fortuna reaches Heimdall over the internal
  network; inside yggdrasil that is `http://heimdall-api.<environment>:8080/`. HTTPS URLs work as before.
  Startup refuses a plain-HTTP URL that names a public IP address, and any URL that carries a user name or
  password.
- A private-address guard on that plain-HTTP transport: each connection is opened only to a private address
  (10/8, 172.16/12, 192.168/16, loopback, `fc00::/7`; an IPv4-mapped IPv6 address counts as its IPv4 address),
  checked when the connection is made rather than at startup, so a name that resolves to a public address, then
  or later, is refused before any credential or caller key is sent. Link-local addresses are not private.
- In plain-HTTP mode no redirect is followed, so a redirect never carries a request to another host, and no
  proxy from the environment is used.
- A start-up line names the transport: `Heimdall credential exchange: <url> over TLS` or
  `over plain HTTP to private addresses only`, and whether the caller key is configured, never the key.
- When the guard refuses a connection, the warning that reports Heimdall unavailable says so, instead of naming
  only the exception type.
- Upgrading: deploy 3.1.0 before switching `FORTUNA_HEIMDALL_BASE_URL` to `http`; 3.0.0 refuses it at startup.

### Fixed

- The tests no longer try to reach a PostgreSQL on localhost. Suites that start no test database configure an
  unreachable placeholder instead, so a run never logs in to a server that happens to listen on the default port.

## [3.0.0] - 2026-10-18

### Added

- **Breaking (configuration):** Heimdall's ES256 tokens are verified with the public keys Heimdall publishes. The
  new, required `FORTUNA_HEIMDALL_JWKS_URL` names its key set; inside yggdrasil that is
  `http://heimdall-api.<environment>:8080/.well-known/jwks.json`. The set is fetched when a token names a key
  Fortuna does not know, at most once a minute, and a failed fetch keeps the keys already known.
- **Breaking (configuration):** `FORTUNA_LOCAL_TOKEN_SECRET` (required) and `FORTUNA_LOCAL_TOKEN_ISSUER` (default
  `fortuna-local`). The secret must be at least 32 characters and differ from `FORTUNA_AUTH_TOKEN_SECRET` and
  `FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS`, and the issuer must differ from `FORTUNA_AUTH_TOKEN_ISSUER`. Local-account
  tokens are signed with Fortuna's own key under Fortuna's own issuer, so Heimdall never accepts them and no other
  service can mint them.

### Changed

- `FORTUNA_AUTH_TOKEN_SECRET` is optional. While it is set (and optionally `_PREVIOUS`), Heimdall's HS256 tokens
  and the local tokens Fortuna issued under Heimdall's issuer are still accepted, and each secret must still be at
  least 32 characters. Remove both once Heimdall no longer issues HS256 tokens and a token lifetime has passed.
  `_PREVIOUS` without the secret stops startup.
- A token is a local-account token when it names the local issuer. The `fortuna_local` claim counts only on an
  HS256 token under Heimdall's issuer, verified with the legacy shared secret while it is configured; it is removed
  from every other token, so an ES256 token from Heimdall is never local by claim.

### Security

- Each key verifies its own algorithm only (an EC key ES256, a secret HS256), and the token's issuer chooses the
  keys, so a token's `alg` header never selects the kind of key. Fortuna previously accepted any HMAC algorithm.
  `ArturRios.Jwt` 1.4.0 is referenced directly, replacing the 1.2.0 that `ArturRios.Util.WebApi` brought in.
- The key-source and forwarding settings under `Authentication:Schemes:Bearer` (for example the
  `Authentication__Schemes__Bearer__Authority`, `__MetadataAddress` or `__ForwardDefault` environment variables) are
  now ignored. Before, `Authority` or `MetadataAddress` made Fortuna fetch that address's key set and accept tokens
  signed with its keys; exploiting it needed write access to the environment. Now only Heimdall's published key set,
  the legacy shared secret and Fortuna's local secret verify a token.

**Operators:** set `FORTUNA_HEIMDALL_JWKS_URL` and `FORTUNA_LOCAL_TOKEN_SECRET` before deploying; startup refuses
to run without them. Leave `FORTUNA_AUTH_TOKEN_SECRET` and `FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS` as they are when
deploying: Heimdall issues HS256 tokens until its own move to ES256. While the secret is set, local-account users
stay signed in with their current token until it expires; their next sign-in issues a token under the local
issuer. Remove both only once Heimdall no longer issues HS256 tokens and a token lifetime has passed. From then on,
Heimdall's HS256 tokens and the local-account tokens issued under Heimdall's issuer are answered `401`, and their
holders sign in again.

## [2.2.0] - 2026-10-18

### Changed

- A Heimdall `429` is answered `429` with Heimdall's message and, when Heimdall gives it in seconds, its
  `Retry-After` header, instead of always "Too many attempts. Wait a minute and try again." with no `Retry-After`.
  This covers Heimdall's per-person mail budgets on `resend-verification` and the email method of `2fa/enable`.
  "Too many attempts. Wait a minute and try again." remains the message when Heimdall's answer carries none (#213).
- `FORTUNA_HEIMDALL_CALLER_KEY` may not contain a comma, since Heimdall splits `HEIMDALL_TRUSTED_CALLER_KEYS` on
  commas and could never match such a key. Startup rejects it, naming the variable and not its value (#213).
- Microsoft packages upgraded to 10.0.13, which brings patched `System.Security.Cryptography.Pkcs` and
  `System.Security.Cryptography.Xml` (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7, GHSA-r955-8q5j-3h83,
  GHSA-7hqp-7c5m-4vq6, GHSA-c399-9853-6jmj).

### Fixed

- A Heimdall `409 Conflict`, such as for two-factor authentication that is already active, is answered `409` with
  Heimdall's message instead of `503` "The identity service is temporarily unavailable.". When Heimdall's answer
  carries no message, the message is "The identity request conflicts with the account's current state.".
  `register` and `verify-email` document it. `POST /api/auth/2fa/challenge/resend` still answers it like a
  success (#213).

### Security

- The native core's `zip` dependency is upgraded to 9.0.5, which patches RUSTSEC-2026-0367, RUSTSEC-2026-0368 and
  RUSTSEC-2026-0369 (GHSA-vv5m-jxxh-4627, GHSA-4v9h-p7m9-rh26, GHSA-4684-h2q9-2vcp) and clears the `audit` check.
  Fortuna only writes archives, so the affected extraction functions were never called. The native core now needs
  Rust 1.88.

## [2.1.0] - 2026-10-13

### Added

- UC-81: `POST /api/auth/register` forwards self-registration to Heimdall with the configured scope, answering new and taken addresses alike.
- `POST /api/auth/verify-email` accepts the optional `password` chosen at sign-up and forwards it to Heimdall, which requires it to confirm a registration. A missing password answers `422` and a wrong one `401`, each with its own message.
- Optional `FORTUNA_HEIMDALL_CALLER_KEY`: one of Heimdall's `HEIMDALL_TRUSTED_CALLER_KEYS` (at least 32 visible ASCII characters; startup rejects anything else). When set, requests to Heimdall carry the key and the end user's address, so Heimdall rate-limits anonymous auth per user instead of once for the whole instance. Unset, nothing changes.

### Changed

- Heimdall's `429` is answered `429` "Too many attempts. Wait a minute and try again." instead of `503` "The identity service is temporarily unavailable.", and `register` and `verify-email` document it.
- The anonymous-auth rate limiter groups IPv6 clients by their `/64`, so a client cannot dodge the limit by rotating addresses within its prefix.

## [2.0.0] - 2026-10-12

### Added

- Every API request is logged with its client IP address, which is also tagged on the request's trace as
  `client.address`. Behind a reverse proxy, list the proxy in `FORTUNA_FORWARDED_KNOWN_NETWORKS` or
  `FORTUNA_FORWARDED_KNOWN_PROXIES` so the address is the caller's. Scrapes of the private metrics port are not
  logged.

### Changed

- **Breaking (native C ABI):** the 26 routes the native core exports but does not implement now answer the new
  `FORTUNA_STATUS_NOT_IMPLEMENTED` (`501`) with the reason, and `fortuna_capabilities` lists them under a new
  `notImplemented` field instead of `operations`. They are file imports and import retry, data-set exports, reports,
  projections, transfers, installment plans, statements, recurring materialization, budget consumption, goal
  progress and reconciliation. They used to report success without doing the work: imports and exports were marked
  completed without parsing or rendering anything, reports and projections returned zeros, and the rest stored the
  request as one unvalidated record. No exported function changed; clients must re-vendor the regenerated header.
- Job correlation ids taken from a request are now its 32-character W3C trace id.
- The OpenAPI document no longer marks `ObjectDataOutput.data` as `nullable`.
- Dependencies upgraded to their latest stable releases, among them the `ArturRios.*` packages (Util.WebApi 5.1.0,
  Mediator 2.0.0, Util 2.1.0, Data.Relational.Core 5.0.0), Microsoft 10.0.12, OpenTelemetry 1.19 and
  Swashbuckle 10.3.0, the Docker base images, and the native core's `zip`, `base64` and `uuid` crates.
- The deployment targets four environments: `local` on Docker Desktop, and `development`, `homologation` and
  `production` on one Linux VPS, deployed by yggdrasil (development and homologation on demand). Docker in WSL is no
  longer a target. `docker/homologation.env.example` is new, and the templates now carry each environment's public
  host names (`PUBLIC_HOST`, `UI_HOST`), its own database (`fortuna_local`, `fortuna_development`,
  `fortuna_homologation`, `fortuna`) on that machine's PostgreSQL at `host.docker.internal`, its own Heimdall
  (`https://heimdall-api-dev.example.com/`, `-hml`, `https://heimdall-api.example.com/`) and, on the VPS, the Docker
  networks as trusted proxies. Locally the API is published on port `8083`, beside heimdall-api's `8080`.

### Fixed

- The license names Fortuna; its title still read "ArturRios.Heimdall".
- Regenerating local account recovery codes with an empty current secret is refused as an invalid secret instead
  of failing with a server error.
- Pluggy synchronization imports transactions again. Pluggy's ISO 8601 date-times were rejected, so every
  transaction was refused as invalid and auto-created cards got closing and due day 1.
- Excel and Pluggy imports put a charge dated in a settled billing cycle on the next open statement, flagged as
  late-arriving, instead of failing the whole import.
- A failed Excel or Pluggy import commits none of its rows. Rows applied before the failure used to be saved under
  the failed job, and a failed database save left the import job and its background job running forever.
- A PDF import that fails after it was picked up marks its background job failed too, so it can be retried.
- Re-importing a row whose amount has more than 4 decimal places, or whose external id is longer than 200
  characters, is detected as a duplicate instead of being imported again.
- An Excel row whose category name is longer than 200 characters, or whose amount rounds to zero at four decimal
  places, is rejected on its own instead of failing the import.
- Retrying a failed Pluggy synchronization is refused with `409 Conflict` when the connection is revoked, needs
  reauthentication, or already has a synchronization pending or running. The retry used to be accepted and either
  stay pending forever or fail with a server error.
- Each installment of a plan lands in the billing cycle after the previous one, including purchases made on the
  29th to the 31st. Before, two installments could share a statement. The plan's total now always equals the sum
  of its installments.
- Editing the description, category or tags of a statement payment's card leg no longer adds the payment to a
  statement and lowers that statement's total.
- Budget periods anchored on the 29th to the 31st are now contiguous. Before, days such as 28 to 30 March were in
  no period, so their expenses were never counted.
- Net position and goal progress count an investment movement dated on its valuation day the same way the
  investment endpoints do.
- Recording an investment movement returns the position computed from the latest valuation.
- A cross-currency statement payment that converts to less than the card currency's minor unit is refused with
  `400 Bad Request` instead of failing with a server error.
- Audit entries for Excel and PDF imports and for data exports record the queued job.
- Queries stop when the client disconnects.
- Native core: recovery codes ignore case and surrounding whitespace, names are trimmed, and a secret's length is
  counted in characters with the 1024-character maximum enforced, as in the HTTP API. Creating a second local
  account returns the HTTP API's message, and a storage failure returns `500` instead of `409`.
- Native core: a database migrated from schema version 1 no longer copies its accounts again on every
  initialization. Hard-deleted accounts came back, and erasing the account always failed.
- Native core: concurrent updates of the same record, such as assigning two tags at once, no longer overwrite each
  other. A soft-deleted record cannot be updated (`404`), and `isDeleted`, `createdAt` and `updatedAt` are set by
  the core, not by the request body.
- Native core: import-job routes no longer return export jobs, and the export route no longer returns import jobs.
- Native core: imports and exports that earlier versions reported as completed without processing them are now
  reported as failed, with the reason, so a file that was never imported no longer looks imported.
- Native core: reassigning a category's transactions, merging counterparties and suggesting a counterparty's
  category apply the HTTP API's rules, messages and statuses. Before, reassigning and merging only stamped the source
  record and reported success, and the suggestion was always an empty list. A transaction or recurring rule that
  names a counterparty is now linked to it, matched by trimmed, case-insensitive name and created when missing, as
  over HTTP; a name longer than 200 characters is refused. Transactions and rules recorded offline before this
  version are linked the first time the core starts.
- The generated C header no longer contains `/*` inside a comment, which made C compilers warn about a nested
  comment.
- Native core: an account balance applies `asOf`, as the HTTP API does, and rejects an `asOf` outside 1900 to 2100.
- Native core: a manual exchange rate recorded for a pair and date whose earlier rate was deleted takes effect.

### Security

- Native core: local account recovery and recovery-code regeneration are unavailable (`404`) when local
  authentication is disabled, as in the HTTP API. Before, a recovery code still issued a session.
- Native core: recovering a local account takes the same time for an unknown name as for a known one.
- Personal data archives replace backslashes in attachment file names, so an archive extracted on Windows cannot
  write outside its folder.

## [1.0.0] - 2026-09-19

### Added

- Authentication with Heimdall tokens, profile provisioning on first access, and Heimdall sign-in, credential and
  two-factor management proxied through the Fortuna API.
- A desktop local account for offline mode, with sign-in, recovery codes and their regeneration.
- Supported currencies, exchange rates synchronized from the Banco Central do Brasil's PTAX service or recorded
  manually, and figures converted to a display currency.
- Financial accounts with derived balances, credit cards with billing cycles, statements and settlement, and
  investments with movements, valuations and positions.
- Transactions, transfers, installment purchases, recurring transactions and reconciliation.
- Categories, tags, counterparties, budgets and goals.
- Ingestion from Pluggy connections, Excel workbooks and Nubank credit card invoice PDFs, with import job
  monitoring, retries and review of imported records.
- Attachments, tabular queries, chart aggregations with drill-down, net position, cash-flow projections, committed
  obligations, and CSV, Excel and PDF exports.
- Two-stage deletion and restoration of records, the audit trail, processing consent, a complete personal data
  export and account erasure.
- PostgreSQL for shared deployments and SQLite for desktop offline mode, plus a native desktop core exposing the
  offline operation surface through a C ABI.
- A public liveness check that reports the API contract version, an administrator-only detailed health check, and
  Prometheus metrics on a private port.

[Unreleased]: https://github.com/artur-rios/fortuna-api/compare/v3.1.0...HEAD
[3.1.0]: https://github.com/artur-rios/fortuna-api/compare/v3.0.0...v3.1.0
[3.0.0]: https://github.com/artur-rios/fortuna-api/compare/v2.2.0...v3.0.0
[2.2.0]: https://github.com/artur-rios/fortuna-api/compare/v2.1.0...v2.2.0
[2.1.0]: https://github.com/artur-rios/fortuna-api/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/artur-rios/fortuna-api/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/artur-rios/fortuna-api/releases/tag/v1.0.0
