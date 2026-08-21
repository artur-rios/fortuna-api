# Technology Stack — Fortuna API

Fortuna is built the same way as the [Heimdall API](https://github.com/artur-rios/heimdall-api):
the same layering, the same first-party `ArturRios.*` libraries, the same data and testing stack,
the same Docker deployment shape. That is a deliberate choice — the two services are maintained by
one person, and a shared skeleton means a pattern learned in one is a pattern known in the other.

Exact versions are **not** pinned here. They are pinned once, in the formal Technology Stack
Document, which every other document links to instead of restating.

## Platform & Language

- **.NET 10** (`net10.0`) on the back end, C# at the framework's default language version.
- `Nullable` and `ImplicitUsings` enabled across every project, production and test alike.
- Central package management: one `Directory.Packages.props` decides every package version for the
  whole repository, with transitive pinning on so the whole build sees one version of each dependency.

## Application Type

An **HTTP REST API** (ASP.NET Core Web API), documented with Swagger/OpenAPI. It is the only
back-end process; the Flutter client is a separate repository and a pure consumer.

The architecture is **layered CQRS**, mirroring Heimdall's `src/` layout:

| Layer | Holds |
| --- | --- |
| `Domain` | Entities, enums, value objects, and the behavior that enforces the invariants — money arithmetic above all. |
| `Application` | `Command` and `Query` projects (one handler per operation), plus a `Shared` project for services used by both. |
| `Infrastructure` | `Data` — the EF Core context, entity maps, migrations and seeding — and the integration adapters (Pluggy, spreadsheet and PDF parsers, export renderers). |
| `Presentation` | The Web API: controllers, binding, security wiring, configuration. |

Handlers are dispatched through **ArturRios.Mediator** and return a `DataOutput<T>` (from the
`ArturRios.Util` family) rather than throwing; the `ResponseResolver` in `ArturRios.Util.WebApi`
maps that result to the HTTP response.

## Data Storage

- **PostgreSQL** is the main and only relational database, in every environment including the
  automated test suite — functional tests run against a real PostgreSQL provisioned by
  Testcontainers, never an in-memory provider.
- Bound through **ArturRios.Data.PostgreSql** over **ArturRios.Data.Relational.Core**, configured
  from the environment with a `FORTUNA_DATA` prefix.
- Monetary values are stored as PostgreSQL **`numeric`** and handled in C# as **`decimal`**.
  Floating-point types are forbidden anywhere a monetary amount can reach — this is the single most
  important storage decision in the project.

## Data Access

- **Entity Framework Core**, code-first, with migrations.
- **EFCore.NamingConventions** mapping to `snake_case`, singular table and column names.
- **Repository-based access**: application handlers depend on `IAsyncRepository<T>` /
  `IAsyncReadOnlyRepository<T>` from `ArturRios.Data.Relational.Core`, never on `DbContext`
  directly — which is also what makes them unit-testable against `AsyncFakeRepository<T>`.
- Reporting and chart aggregations are the deliberate exception: where a repository query cannot
  express an aggregation efficiently, a read-side query may go to the database directly.

## Authentication

Two paths, and they do not share an implementation:

**Connected mode — Heimdall.** All user management, sign-up, credentials, password recovery and
multi-factor authentication belong to the [Heimdall API](https://github.com/artur-rios/heimdall-api).
Fortuna registers as a **scope** in Heimdall; Heimdall issues the signed JWT, and Fortuna consumes
it through the `ArturRios.Util.WebApi` security stack (namespace `ArturRios.Jwt`) — reading the
subject, the role and the `scopePermissions` claim to authorize each call. Fortuna never sees a
password.

> *Undecided:* whether Fortuna validates a Heimdall token locally against a shared signing
> configuration, or calls Heimdall to validate it. Both are viable against Heimdall's current
> design; the choice is deferred to the formal Technology Stack Document.

**Desktop offline mode — Fortuna's own.** A desktop installation may authenticate against a local
account held in memory or in the operating system's credential store. There is no password reset
and no e-mail round trip: recovery codes minted at account creation are the only recovery path.
This mode is implemented in Fortuna and involves Heimdall not at all.

Authorization is ownership-based: a record belongs to exactly one user, and the API is what enforces
that no other user can reach it.

## Testing

- **xUnit** as the framework, with `Microsoft.NET.Test.Sdk` and `coverlet.collector`.
- **ArturRios.Util.Test** for the category attributes (`[UnitFact]` / `[FunctionalFact]`), the
  `WebApiTest<TEntryPoint>` functional base class, the repository fakes and `CustomAssert`.
- **Moq** for non-repository doubles — one mocking library, no second one.
- **Bogus** (`Faker<T>`) for test data, instead of large inline literals.
- **Testcontainers.PostgreSql** for the functional suite's throwaway database.
- Two categories, run separately: **unit** tests over handlers, validators and domain behavior;
  **functional** tests over each endpoint end-to-end via HTTP, asserting both the response and the
  resulting database state. Tests are named and written **Given / When / Then**.
- Money arithmetic, currency conversion and the import parsers get unusually dense unit coverage:
  they are where a defect is silent and expensive.

## External Dependencies

| Dependency | Role | Notes |
| --- | --- | --- |
| **Heimdall API** | Identity and user management | The token issuer for connected mode. |
| **Pluggy** | Open banking — automatic account, card and transaction data | Present from the first version. Bank credentials stay with Pluggy; Fortuna holds only the item/connection reference and access token. |
| **PostgreSQL** | Persistence | Shared instance per environment, one database per service, as in Heimdall. |
| *An exchange-rate source* | Currency conversion rates for multi-currency reporting | *Undecided* — which provider, and whether rates are fetched or entered manually. |

The ingestion side is built around a **source abstraction**: Pluggy, Excel import, PDF import and
manual entry are implementations of one contract, so a future source (another aggregator, another
statement layout, an OFX file) is added without touching the ones already there. The same applies
to the export side for CSV, Excel and PDF.

Cross-cutting choices follow Heimdall: **FluentValidation** for input validation, **Serilog** for
structured logging, **Swagger/OpenAPI** for the documented surface.

## Deployment

- **Docker Compose**, one `docker-compose.yml` for every environment, with the differences carried
  by per-environment `.env` files rather than by separate compose files.
- It must run unchanged on **Docker Desktop for Windows**, on **Docker in WSL Ubuntu**, and on a
  **Linux VPS** — which is why the compose file declares `host.docker.internal:host-gateway` and
  assembles its connection string from parts.
- PostgreSQL is **not** a service in the compose file: each environment already runs an instance,
  shared between services, each service owning its own database.
- Configuration is resolved entirely from environment variables, prefixed `FORTUNA_`.
- A `/healthcheck` endpoint backs the container health check.
