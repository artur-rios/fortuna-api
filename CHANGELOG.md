# Changelog

All notable changes to the Fortuna API are recorded in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/artur-rios/fortuna-api/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/artur-rios/fortuna-api/releases/tag/v1.0.0
