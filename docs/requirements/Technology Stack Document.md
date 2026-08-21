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

---

## 4. Data Storage

### 4.1 Relational database

| Concern | Choice |
| --- | --- |
| Relational database | **PostgreSQL** — the sole supported relational engine. |
| Provider integration | `ArturRios.Data.PostgreSql` → `AddPostgreSqlProvider()` (EF Core over Npgsql). |
| Connection configuration | Environment variables `FORTUNA_DATA_CONNECTIONSTRING` and `FORTUNA_DATA_DATABASETYPE` (`PostgreSql`), bound with `AddDataConfigFromEnvironment<AppDbContext>("FORTUNA_DATA")`. |
| Schema | `fortuna`, with the connection's `Search Path` pinned to it — see the [Operations & Infrastructure Document](Operations%20%26%20Infrastructure%20Document.md). |

PostgreSQL is used in **every** environment, automated tests included: functional tests run against
a real instance provisioned by Testcontainers, never an in-memory provider, so behavior matches
production.

### 4.2 Monetary storage

This is the most consequential storage decision in the project, so it is stated as a rule rather
than a preference:

| Concern | Choice |
| --- | --- |
| Database column | PostgreSQL **`numeric(19, 4)`** — exact decimal arithmetic, four fractional digits to hold minor units plus the headroom an unrounded intermediate needs. |
| CLR type | **`decimal`**. |
| Forbidden | `float`, `double`, `real`, `double precision`, and `System.Single`/`System.Double` anywhere a monetary value can reach — entity, DTO, query projection, export cell, or intermediate calculation. |
| Exchange rates | **`numeric(19, 8)`** — a rate needs more fractional precision than an amount, and rounding it early moves every figure derived from it. |

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
| Authentication | **JWT validation** via `ArturRios.Util.WebApi` (namespace `ArturRios.Jwt`) | latest stable at implementation time | Tokens are **issued by Heimdall and validated locally by Fortuna** against the shared issuer, audience and signing configuration. Fortuna makes no call to Heimdall on the request path. |
| Authorization | Role attributes and middleware from `ArturRios.Util.WebApi`, plus per-record ownership checks | latest stable at implementation time | The role gate is the library's; the ownership gate is Fortuna's own and applies to every domain endpoint. |
| Local (offline) authentication | Fortuna's own implementation over `ArturRios.Util` hashing and `CustomRandom` | latest stable at implementation time | Desktop-only. Recovery codes are hashed, never stored or returned in the clear after the response that mints them. |
| Result / error model | `DataOutput<T>` (namespace `ArturRios.Output`) | latest stable at implementation time | Handlers return success, errors, messages and data rather than throwing; `ResponseResolver` maps that to an HTTP response. |
| Background execution | `BackgroundService` + a bounded `System.Threading.Channels` queue, with `ImportJob` as the durable record | — (framework) | Imports, synchronizations and exports are accepted, persisted as a job, queued, and executed off the request thread. A restart re-queues jobs left `Pending` or `Running`. |
| API documentation | **Swagger / OpenAPI** via `Swashbuckle.AspNetCore` | latest stable at implementation time | Enabled with JWT auth support. |
| Configuration | `.env.<environment>` files plus environment variables, all prefixed `FORTUNA_` | — | Loaded by the `ArturRios.Util.WebApi` configuration loader; `.env*` files are copied next to the built assembly and never baked into the image. |

### 6.1 External services

| Service | Role | Integration |
| --- | --- | --- |
| **Heimdall API** | Identity, users, credentials, recovery, multi-factor | Fortuna registers as a Heimdall **scope** and validates Heimdall-issued JWTs locally (§6). It calls Heimdall on no request path. |
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
| Dependency vulnerability scanning | `dotnet list package --vulnerable`, parsed by `scripts/vulnerabilities.py` | — | Runs on every CI build, because a dependency does not have to change to become vulnerable. |

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
