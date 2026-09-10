# Technology Stack Document — Fortuna API

## 1. Purpose

This document is the **single source of truth for the technologies used to build the Fortuna API** —
the runtime platform, language, libraries, data storage, cross-cutting concerns, and testing tools,
together with the version each is pinned to and the role it plays.

Every other document in this folder **references this document** for technical choices instead of
restating them, so that:

- The domain documents ([Vision](Vision%20Document.md),
  [System Requirements](System%20Requirements%20Document.md),
  [Use Case Specification](Use%20Case%20Specification%20Document.md)) stay focused on *what* the
  system does.
- The [Operations & Infrastructure Document](Operations%20%26%20Infrastructure%20Document.md) stays
  focused on the platform's structure and operations.
- The [Testing Specification Document](Testing%20Specification%20Document.md) stays focused on
  *how* to test.
- Technology versions and roles are maintained in exactly **one** place.

> **Rule:** when a technology choice changes, it changes here first. Other documents link to this
> one rather than duplicating the detail.

### 1.1 Versioning policy

Only the runtime platform is pinned to a number here. **Every package version is recorded as
`latest stable at implementation time`** — the version resolved when the unit of work that first
needs the package is implemented, and then pinned in `Directory.Packages.props`.

Two constraints bound that policy:

1. **The `ArturRios.*` family moves together.** Those packages carry their own Entity Framework
   Core dependency, and mixing versions produces a compile-time split (`CS1705`) rather than a
   clean failure. Take the family as a coherent set, and keep it aligned with the set the
   [Heimdall API](https://github.com/artur-rios/heimdall-api) currently resolves — the two services
   share these libraries and must interoperate.
2. **Central package management with transitive pinning is on.** One
   `Directory.Packages.props` decides every version for the whole repository, and pinned transitive
   dependencies mean no project silently resolves an EF Core one patch ahead of another.

Once a package is first pinned, it stays at that version until a deliberate upgrade. "Latest stable
at implementation time" is the rule for *choosing* the number, not a licence to float.

---

## 2. Platform & Language

| Concern | Choice | Notes |
| --- | --- | --- |
| Runtime / framework | **.NET 10** (`net10.0`) | Every project targets `net10.0`. The Web API uses the `Microsoft.NET.Sdk.Web` SDK; libraries use `Microsoft.NET.Sdk`. |
| Language | **C# 14** | The default language version for `net10.0`, used implicitly. No explicit `<LangVersion>` is set, which keeps the language tracking the target framework's default. |
| Native desktop core | **Rust 2024 edition**, minimum Rust `1.85` | Builds a C-compatible dynamic library for Windows and Linux. It is a separate implementation used only for in-process offline desktop mode; the HTTP host remains .NET. |
| Language features | `Nullable` **enabled**, `ImplicitUsings` **enabled** | Applied uniformly to every production and test project. |
| Package management | **Central**, via `Directory.Packages.props`, with `CentralPackageTransitivePinningEnabled` | A `PackageReference` names a package; that one file decides its version. |

---

## 3. Libraries

### 3.1 First-party (`ArturRios.*`)

The same library family the Heimdall API is built on. Consumed as NuGet `PackageReference`s, taken
as a coherent set (§1.1).

| Package | Version | Used by | Role |
| --- | --- | --- | --- |
| **ArturRios.Util** | latest stable at implementation time | Command, Query, Domain, Shared, Data | Core cross-cutting utilities: the `DataOutput<T>` result type (namespace `ArturRios.Output`) every handler returns, hashing helpers (used for local-account recovery codes), HTTP helpers, and cryptographically strong random text (`ArturRios.Util.Random.CustomRandom`, which mints the recovery codes). |
| **ArturRios.Util.WebApi** | latest stable at implementation time | WebApi | Web API foundation: the `WebApiStartup` base class, environment and configuration loading, the security stack (role attributes, requirements, authentication middleware), **JWT validation** (namespace `ArturRios.Jwt`), exception middleware, Swagger-with-JWT wiring, and the `ResponseResolver` that maps a `DataOutput<T>` to an HTTP response. |
| **ArturRios.Mediator** | latest stable at implementation time | Command, Query, WebApi | Lightweight CQRS mediator: `CommandMediator` / `QueryMediator` and the handler contracts (`ICommandHandlerAsync`, `IQueryHandlerAsync`, `IPaginatedQueryHandlerAsync`) that dispatch each command or query to its single handler. |
| **ArturRios.Data.Relational.Core** | latest stable at implementation time | Command, Query, Domain | Provider-agnostic relational data layer: entity base types, the repository abstractions handlers depend on (`IAsyncRepository<T>`, `IAsyncReadOnlyRepository<T>`), the EF Core `DbContext` base with its diagnostics options, and `AddDataConfigFromEnvironment<TDbContext>(prefix)`. |
| **ArturRios.Data.PostgreSql** | latest stable at implementation time | Data | PostgreSQL binding for the relational core — `AddPostgreSqlProvider()`, wiring EF Core to Npgsql. |
| **ArturRios.Util.Test** | latest stable at implementation time | all test projects | The testing toolkit (§7). |

### 3.2 Third-party

| Package | Version | Used by | Role |
| --- | --- | --- | --- |
| **Microsoft.EntityFrameworkCore** (+ `.Relational`, `.Abstractions`, `.Analyzers`, `.Design`) | latest stable at implementation time | Data | The ORM and its design-time tooling. The three packages nothing references directly are declared anyway, because they are exactly the transitive dependencies that would otherwise lag a patch behind and split the build. |
| **EFCore.NamingConventions** | latest stable at implementation time | Data | Maps entities to `snake_case`, singular table and column names. |
| **FluentValidation** | latest stable at implementation time | Command, Query | `IValidator<T>` implementations for command and query inputs, registered in DI and invoked inside the handlers. |
| **Serilog** (+ `Serilog.AspNetCore`, `Serilog.Sinks.Map`) | latest stable at implementation time | WebApi | Structured logging through `Host.UseSerilog()`, JSON-formatted, with a configurable log directory. |
| **Swashbuckle.AspNetCore** | latest stable at implementation time | WebApi | The OpenAPI document and Swagger UI, with JWT auth support. |
| **UglyToad.PdfPig** | latest stable at implementation time | Data (PDF import adapter) | Text and word-position extraction from PDF statements. Chosen over the alternatives because it exposes each word's bounding box, which is what makes column and section detection possible in a statement layout — and because it is MIT-licensed, which the AGPL PDF libraries are not. |
| **QuestPDF** | latest stable at implementation time | Data (PDF export adapter) | Renders exported reports to PDF. Its Community licence covers this project's use. |
| **ClosedXML** | latest stable at implementation time | Data (Excel import/export adapters) | Reads and writes `.xlsx`. MIT-licensed, unlike EPPlus's non-commercial terms. |
| **CsvHelper** | latest stable at implementation time | Data (CSV export adapter) | CSV writing, with correct quoting and culture-aware number formatting. |
| **AWSSDK.S3** | latest stable at implementation time | Data (object storage adapter) | S3-protocol client, pointed at an S3-compatible endpoint (MEGA S4) rather than AWS. |

### 3.3 Native core (Cargo)

Cargo resolves and locks native dependencies in `native/fortuna-core/Cargo.lock`.

| Crate | Version selected | Role |
| --- | --- | --- |
| **rusqlite** | `0.40.2` | SQLite persistence with SQLite compiled into the library for identical Windows/Linux packaging. |
| **argon2** | `0.6.0` | Argon2id local-secret hashing and constant-work credential verification. |
| **serde / serde_json** | `1.0.229` / `1.0.151` | HTTP-compatible camel-case JSON envelopes; schema-driven normalization publishes exact decimals as strings and accepts legacy numeric input without floating-point conversion. |
| **chrono / uuid / sha2 / zeroize** | versions pinned by `Cargo.lock` | UTC wire timestamps, public identifiers, hashed session tokens and clearing credential-bearing memory. |
| **cbindgen** | `0.29.4` | Generates the committed public C header directly from exported Rust functions. |

---

## 4. Data Storage

### 4.1 Relational database

| Concern | Choice |
| --- | --- |
| Relational database | **PostgreSQL** for shared/server deployments; **SQLite** for single-user desktop offline mode. |
| Provider integration | EF Core over Npgsql or `Microsoft.EntityFrameworkCore.Sqlite`, selected only in infrastructure. Application and domain code share one model and do not choose a provider. |
| Connection configuration | `FORTUNA_DATA_DATABASETYPE` is `PostgreSql` or `SQLite`. `FORTUNA_DATA_CONNECTIONSTRING` is a PostgreSQL connection string or, for SQLite, either a `Data Source=...` connection string or a database file path. |
| Migrations | Provider-specific migration assemblies: PostgreSQL migrations remain with the data project; SQLite migrations are in `ArturRios.Fortuna.Data.Sqlite.Migrations`. |
| Schema | PostgreSQL uses `fortuna`, with the connection's `Search Path` pinned to it. SQLite uses its single database namespace. |

Server functional tests run against a real PostgreSQL instance provisioned by Testcontainers.
SQLite persistence tests apply the real file-backed migrations and verify exact storage and
aggregation behavior. Switching between the two is configuration-only.

The embedded native core uses its **own** SQLite persistence implementation and tables prefixed
`native_`; it does not load EF Core or the .NET application assemblies. This is an explicit desktop
architecture boundary adopted after EF Core's NativeAOT path failed the #156 feasibility slice.
It stores monetary values as SQLite `TEXT` and produces the same JSON data shapes, invariant decimal
strings and status meaning as the HTTP transport. The .NET SQLite provider remains available for a
local HTTP deployment.

### 4.2 Monetary storage

This is the most consequential storage decision in the project, so it is stated as a rule rather
than a preference:

| Concern | Choice |
| --- | --- |
| Database column | PostgreSQL **`numeric(19, 4)`**; SQLite **`TEXT`** through the provider's lossless decimal mapping. SQLite monetary values must never use `REAL`. |
| CLR type | **`decimal`**. |
| JSON wire contract | Every CLR or native decimal is published as an invariant string matching `^-?(?:0\|[1-9][0-9]*)(?:\.[0-9]+)?$`; OpenAPI declares `type: string` and `format: decimal`. Numeric JSON input remains accepted temporarily for compatibility, but output is always a string. |
| Forbidden | `float`, `double`, `real`, `double precision`, and `System.Single`/`System.Double` anywhere a monetary value can reach — entity, DTO, query projection, export cell, or intermediate calculation. |
| Exchange rates | PostgreSQL **`numeric(19, 8)`**; SQLite **`TEXT`**, preserving the same CLR `decimal` value. A rate needs more fractional precision than an amount. |

### 4.3 Binary storage

Attachments do not go in the database. Storage is an abstraction (`IAttachmentStore`) with two
implementations at launch and room for more:

| Implementation | Used by | Backing |
| --- | --- | --- |
| **Filesystem store** | Desktop and single self-hosted installations | A configured directory, mounted as a Docker volume where containerized. |
| **S3-compatible object store** | Shared instances | **MEGA S4** over the S3 protocol, via `AWSSDK.S3`. Any S3-compatible endpoint works — the implementation is written against the protocol, not the vendor. |

Which one is active is a configuration choice, not a code path selected at runtime per request. A
third backing (another provider, or a database-backed store for a trivial deployment) is added by
implementing the same abstraction.

---

## 5. Data Access

| Concern | Choice | Version |
| --- | --- | --- |
| ORM | **Entity Framework Core**, code-first | latest stable at implementation time |
| Migrations | `dotnet ef` via `Microsoft.EntityFrameworkCore.Design`; the `Data` library is its own startup project | latest stable at implementation time |
| Naming convention | `EFCore.NamingConventions` — `snake_case`, singular | — |
| Context | `AppDbContext`, on the `ArturRios.Data.Relational.Core` context base, configured through entity maps in `ArturRios.Fortuna.Data.EntityMaps` | — |
| Diagnostics | `DbContextDiagnosticsOptions` — sensitive-data logging and detailed errors **only outside Production** | — |

Access is **repository-based**: application handlers depend on `IAsyncReadOnlyRepository<T>` /
`IAsyncRepository<T>` rather than on `DbContext`, which is what lets every handler be unit-tested
against `AsyncFakeRepository<T>` with no database at all.

**The one exception is the read side of reporting.** A chart aggregation over a year of transactions,
grouped by category and period, is not something a repository abstraction expresses without loading
far more than it needs. Those queries are written as dedicated read-model queries against the
context, kept in query handlers, and covered by functional tests against a real database rather than
by unit tests against a fake. Everything that writes goes through a repository, without exception.

---

## 6. Cross-Cutting Technologies

| Concern | Technology | Version | How it is used |
| --- | --- | --- | --- |
| Input validation | **FluentValidation** | latest stable at implementation time | One `IValidator<T>` per command or query input, invoked inside the handler before any work. |
| Logging | **Serilog** | latest stable at implementation time | Structured JSON logging via `Host.UseSerilog()`. Monetary amounts, account identifiers and attachment contents are never logged. |
| Authentication | **JWT validation** via `ArturRios.Util.WebApi` (namespace `ArturRios.Jwt`) | latest stable at implementation time | Tokens are **issued by Heimdall and validated locally by Fortuna** against the shared issuer, audience and signing configuration. Credential exchanges use a typed framework `HttpClient`; token validation never calls Heimdall. |
| Authorization | Role attributes and middleware from `ArturRios.Util.WebApi`, plus per-record ownership checks | latest stable at implementation time | The role gate is the library's; the ownership gate is Fortuna's own and applies to every domain endpoint. |
| Local (offline) authentication | Fortuna's own implementation over `ArturRios.Util` hashing and `CustomRandom` | latest stable at implementation time | Desktop-only. Recovery codes are hashed, never stored or returned in the clear after the response that mints them. |
| Native local authentication | RustCrypto Argon2id plus SHA-256-hashed opaque sessions | versions pinned by `Cargo.lock` | In-process desktop only. Secrets and token-bearing request copies are zeroized and never logged; shutdown invalidates every session. |
| Result / error model | `DataOutput<T>` (namespace `ArturRios.Output`) | latest stable at implementation time | Handlers return success, errors, messages and data rather than throwing; `ResponseResolver` maps that to an HTTP response. |
| Background execution | `BackgroundService` + a bounded `System.Threading.Channels` queue, with `ImportJob` as the durable record | — (framework) | Imports, synchronizations and exports are accepted, persisted as a job, queued, and executed off the request thread. A restart re-queues jobs left `Pending` or `Running`. |
| API documentation | **Swagger / OpenAPI** via `Swashbuckle.AspNetCore` | latest stable at implementation time | Enabled with JWT auth support. |
| Configuration | `.env.<environment>` files plus environment variables, all prefixed `FORTUNA_` | — | Loaded by the `ArturRios.Util.WebApi` configuration loader; `.env*` files are copied next to the built assembly and never baked into the image. |

### 6.1 External services

| Service | Role | Integration |
| --- | --- | --- |
| **Heimdall API** | Identity, users, credentials, recovery, multi-factor | Fortuna registers as a Heimdall **scope**. A typed `HttpClient` proxies explicit credential, Google and two-factor exchanges while attaching that scope; successful tokens are validated locally on every later request. Credentials are neither logged nor retained. |
| **Pluggy** | Open-banking aggregation — accounts, cards, transactions | A typed HTTP client over Pluggy's REST API, behind Fortuna's own ingestion-source abstraction. Fortuna stores the item reference and access token, never a bank credential. |
| **Banco Central do Brasil — PTAX** | Official exchange rates | The free, key-less Olinda OData service. It publishes both *cotação* (currency ↔ BRL) and *paridade* (currency ↔ USD), so non-BRL cross rates are derivable from this one source. Rates are fetched on a schedule and cached; a user may always override with a manually entered rate. |
| **MEGA S4** | S3-compatible object storage for attachments | `AWSSDK.S3` against a configured endpoint (§4.3). |

Every integration is **read-only with respect to the external system**. Nothing Fortuna does writes
back to a financial institution.

---

## 7. Testing Technologies

These are the technologies mandated for tests. **How** they are applied — naming, structure,
coverage, the per-use-case workflow — is defined in the
[Testing Specification Document](Testing%20Specification%20Document.md); this section is the
canonical list of the tools.

| Concern | Technology | Version | How it is used |
| --- | --- | --- | --- |
| Test framework | **xUnit** (`xunit`, `xunit.runner.visualstudio`) | latest stable at implementation time | The framework for every test project. |
| Test runner / SDK | `Microsoft.NET.Test.Sdk` | latest stable at implementation time | Host and runner integration for `dotnet test` and IDEs. |
| Coverage | `coverlet.collector`, reported by `dotnet-reportgenerator-globaltool` | latest stable at implementation time | Collects coverage per project; the report is merged and gated in CI. |
| Test helpers & doubles | **ArturRios.Util.Test** | latest stable at implementation time | The category attributes (`[UnitFact]` / `[UnitTheory]`, `[FunctionalFact]` / `[FunctionalTheory]`, which stamp a `Category` trait), the `WebApiTest<TEntryPoint>` functional base class, `FakeRepository<T>`, `AsyncFakeRepository<T>`, and `CustomAssert`. |
| Mocking | **Moq** | latest stable at implementation time | The single mocking library, for non-repository collaborators. Do not introduce a second one. |
| Test data generation | **Bogus** | latest stable at implementation time | `Faker<T>` for entities, commands and DTOs, instead of large inline literals. |
| Functional database | **Testcontainers.PostgreSql** | latest stable at implementation time | A real, throwaway PostgreSQL container per functional run. |
| Dependency vulnerability scanning | `dotnet list package --vulnerable`, parsed by `scripts/vulnerabilities.py`; RustSec `cargo audit` for Cargo | — | Runs on every CI build, because a dependency does not have to change to become vulnerable. |
| Native tests | Rust built-in test harness through `cargo test --locked` | Rust stable | Calls the exported C functions directly on Windows and Linux, covering lifecycle, statuses, JSON parity, threading, memory ownership and exact decimal round trips. |

External services are never reached from a test. Pluggy, PTAX and the object store are exercised
through their abstractions with in-repository fakes and recorded fixtures — see the
[Testing Specification Document](Testing%20Specification%20Document.md).

---

## 8. Version Summary

| Category | Package / Tool | Version |
| --- | --- | --- |
| Platform | .NET | `10` (`net10.0`) |
| Language | C# | `14` (framework default) |
| First-party | ArturRios.Util | latest stable at implementation time |
| First-party | ArturRios.Util.WebApi | latest stable at implementation time |
| First-party | ArturRios.Mediator | latest stable at implementation time |
| First-party | ArturRios.Data.Relational.Core | latest stable at implementation time |
| First-party | ArturRios.Data.PostgreSql | latest stable at implementation time |
| First-party | ArturRios.Util.Test | latest stable at implementation time |
| Data | Microsoft.EntityFrameworkCore (+ Relational, Abstractions, Analyzers, Design) | latest stable at implementation time |
| Data | EFCore.NamingConventions | latest stable at implementation time |
| Validation | FluentValidation | latest stable at implementation time |
| Logging | Serilog (+ AspNetCore, Sinks.Map) | latest stable at implementation time |
| Documentation | Swashbuckle.AspNetCore | latest stable at implementation time |
| Import / export | UglyToad.PdfPig | latest stable at implementation time |
| Import / export | QuestPDF | latest stable at implementation time |
| Import / export | ClosedXML | latest stable at implementation time |
| Import / export | CsvHelper | latest stable at implementation time |
| Storage | AWSSDK.S3 | latest stable at implementation time |
| Testing | xunit | latest stable at implementation time |
| Testing | xunit.runner.visualstudio | latest stable at implementation time |
| Testing | Microsoft.NET.Test.Sdk | latest stable at implementation time |
| Testing | coverlet.collector | latest stable at implementation time |
| Testing | ArturRios.Util.Test | latest stable at implementation time |
| Testing | Moq | latest stable at implementation time |
| Testing | Bogus | latest stable at implementation time |
| Testing | Testcontainers.PostgreSql | latest stable at implementation time |
