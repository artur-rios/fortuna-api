# Fortuna API

Fortuna is a personal finance system that tracks a person's whole financial life — bank accounts,
credit cards, investments, expenses and earnings — in one place, and turns that history into tabular
views, drillable charts and forward projections. **This repository is the back end only:** an HTTP
API that owns the domain, the data and the integrations. The Flutter application that runs on
Windows, Linux, the browser and mobile is a separate repository and a separate consumer of this API.

> **Status:** foundation complete; use-case implementation is in progress.

## What it does

- Tracks **bank accounts, credit cards and investments**, each with its own currency and history.
- Records **expenses, earnings and transfers**, plus installment purchases and recurring commitments.
- Ingests from several sources — **manual entry, the Pluggy open-banking API, Excel spreadsheets and
  supported PDF statement layouts** — behind one pluggable source contract.
- Handles **multiple currencies** with exact decimal arithmetic and dated, attributable exchange
  rates from the Banco Central do Brasil's PTAX service.
- Serves the data as a **spreadsheet-shaped table** and as **chart aggregations that drill down** to
  the individual transactions behind any figure.
- **Projects the future** from recurring commitments, unbilled installments and statements due.
- **Exports** to CSV, Excel and PDF, and files **attachments** against transactions.
- Runs **offline on a desktop**, or **multi-user on a shared instance**, from the same code.

## What it doesn't do

- **Move money.** No payments, no transfers executed at a bank, no trading. Fortuna reads and
  records; it never writes to a financial institution.
- **Give financial advice.** A projection is arithmetic over the user's own data.
- **Tax filing or accounting compliance.**
- **Store bank credentials.** Open-banking access lives with Pluggy and is referenced by token.
- **Manage users.** Identity belongs to the [Heimdall API](https://github.com/artur-rios/heimdall-api);
  the desktop offline account is the one exception, and Fortuna owns it because there is no network
  to reach Heimdall over.
- **Render a user interface.** The API serves the numbers; the Flutter client draws them.

## Specifications

The project is specified before it is built. Start with the `initial/` documents for context, then
the `requirements/` documents for the normative detail.

| Document | What's in it |
|---|---|
| [Brainstorm](docs/initial/Brainstorm.md) | The original free-form notes this project grew from. |
| [Project Overview](docs/initial/Project%20Overview.md) | What the project is, who it's for, and how success is measured. |
| [Technology Stack](docs/initial/Technology%20Stack.md) | The informal stack decisions. |
| [Workflow](docs/initial/Workflow.md) | How one use case is delivered, step by step. |
| [Business Rules](docs/initial/Business%20Rules.md) | Domain entities, relationships, and the `BR-01` … `BR-41` rules. |
| [Vision Document](docs/requirements/Vision%20Document.md) | Stakeholders, positioning, and the `F-01` … `F-20` features. |
| [System Requirements Document](docs/requirements/System%20Requirements%20Document.md) | The `FR-<AREA>-xx` and `NFR-xx` requirements, data model, endpoint surface, authorization matrix and traceability. |
| [Use Case Specification Document](docs/requirements/Use%20Case%20Specification%20Document.md) | The `UC-01` … `UC-74` use cases, their flows, and their `AF-xx` alternatives. |
| [Development Workflow Document](docs/requirements/Development%20Workflow%20Document.md) | The normative branch pattern, issue lifecycle, approval gates, and Definition of Done. |
| [Testing Specification Document](docs/requirements/Testing%20Specification%20Document.md) | How tests are written, named, and run. |
| [Technology Stack Document](docs/requirements/Technology%20Stack%20Document.md) | The single source of truth for every technology and version. |
| [Operations & Infrastructure Document](docs/requirements/Operations%20%26%20Infrastructure%20Document.md) | Layout, configuration, logging, health, `UC-75`, and the `IR-01` … `IR-20` platform requirements. |

## Installation

Prerequisites: the **.NET 10 SDK**, **Docker**, and a reachable **PostgreSQL** instance — see the
[Technology Stack Document](docs/requirements/Technology%20Stack%20Document.md) and the
[Operations & Infrastructure Document](docs/requirements/Operations%20%26%20Infrastructure%20Document.md).

```bash
git clone https://github.com/artur-rios/fortuna-api.git
```

```bash
dotnet restore src/ArturRios.Fortuna.sln
```

Configuration is resolved entirely from environment variables prefixed `FORTUNA_`. Copy the example
file for your environment from `docker/` and fill it in; no secret is ever committed.

```bash
cp docker/local.env.example docker/local.env
```

## Running

One compose file serves all three target environments — Docker Desktop on Windows, Docker in WSL
Ubuntu, and a Linux VPS — differing only in the environment file supplied:

```bash
docker compose --env-file docker/local.env up -d --build
```

The API's liveness is observable at the public `GET /healthcheck` endpoint.

## Testing

The following command runs the complete suite described in the
[Testing Specification Document](docs/requirements/Testing%20Specification%20Document.md):

```bash
dotnet test src/ArturRios.Fortuna.sln -m:1
```

The suite covers **unit** tests over handlers, validators and domain behavior, and **functional**
tests over every endpoint end to end against a real PostgreSQL instance provisioned by
Testcontainers. Run one category at a time:

```bash
dotnet test src/ArturRios.Fortuna.sln -m:1 --filter "Category=Unit"
```

Merged line coverage is gated at 90%, enforced in CI and reproducibly on a developer machine. That
is a floor, not a target — the standard is to test everything that can be tested. Every use case
ships with its tests before its pull request is opened.

```bash
dotnet tool install --global dotnet-reportgenerator-globaltool
python3 scripts/coverage.py
```

## Roadmap

Seven milestones, in dependency order. Every milestone after `M-01` depends on it.
Closed counts are as of creation — the [milestones page](https://github.com/artur-rios/fortuna-api/milestones)
and the [project board](https://github.com/users/artur-rios/projects/12) are the live view. The board's
`Status` field carries the lifecycle the
[Development Workflow Document](docs/requirements/Development%20Workflow%20Document.md) defines:
**Todo → In Progress → Testing → Done**.

| Milestone | Delivers | Depends on | Issues | Status |
|---|---|---|---|---|
| [M-01 — Foundation](https://github.com/artur-rios/fortuna-api/milestone/1) | The project scaffold, data layer, job runner and CI every use case is built on | — | 1 | 1 / 1 closed |
| [M-02 — Access and cross-cutting mechanisms](https://github.com/artur-rios/fortuna-api/milestone/2) | Token validation, profile provisioning, the desktop local account, currencies and exchange rates, the two-stage deletion lifecycle and the audit trail | M-01 | 12 | 3 / 12 closed |
| [M-03 — Holdings](https://github.com/artur-rios/fortuna-api/milestone/3) | Financial accounts, credit cards with billing cycles and statements, and investments | M-02 | 19 | 0 / 19 closed |
| [M-04 — Money movement](https://github.com/artur-rios/fortuna-api/milestone/4) | Transactions, transfers, installment plans, recurring commitments and reconciliation | M-03 | 11 | 0 / 11 closed |
| [M-05 — Organization and planning](https://github.com/artur-rios/fortuna-api/milestone/5) | Categories, tags, counterparties, budgets and goals | M-04 | 11 | 0 / 11 closed |
| [M-06 — Ingestion](https://github.com/artur-rios/fortuna-api/milestone/6) | The source contract, Pluggy connections and synchronization, Excel and Nubank PDF imports, and job monitoring | M-04 | 10 | 0 / 10 closed |
| [M-07 — Insight and output](https://github.com/artur-rios/fortuna-api/milestone/7) | Attachments, tables, chart aggregations with drill-down, net position, projections, export and the health check | M-05, M-06 | 12 | 0 / 12 closed |

## Backlog

76 issues: one per use case, plus one foundation issue. One use case = one branch = one issue = one
pull request.

### M-01 — Foundation

| Issue | Work | Spec |
|---|---|---|
| [#1](https://github.com/artur-rios/fortuna-api/issues/1) | ✅ Project scaffold and initial infrastructure | [Operations & Infrastructure](docs/requirements/Operations%20%26%20Infrastructure%20Document.md) |

### M-02 — Access and cross-cutting mechanisms

| Issue | Work | Spec |
|---|---|---|
| [#2](https://github.com/artur-rios/fortuna-api/issues/2) | ✅ UC-01: Authenticate a Request with a Heimdall Token | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#3](https://github.com/artur-rios/fortuna-api/issues/3) | ✅ UC-02: Provision a User Profile on First Access | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#4](https://github.com/artur-rios/fortuna-api/issues/4) | ✅ UC-03: Create a Desktop Local Account | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#5](https://github.com/artur-rios/fortuna-api/issues/5) | UC-04: Authenticate with a Local Account | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#6](https://github.com/artur-rios/fortuna-api/issues/6) | UC-05: Recover a Local Account with a Recovery Code | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#7](https://github.com/artur-rios/fortuna-api/issues/7) | UC-06: Regenerate Local Account Recovery Codes | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#8](https://github.com/artur-rios/fortuna-api/issues/8) | UC-07: List Supported Currencies | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#9](https://github.com/artur-rios/fortuna-api/issues/9) | UC-08: Synchronize Exchange Rates | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#10](https://github.com/artur-rios/fortuna-api/issues/10) | UC-09: Record a Manual Exchange Rate | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#11](https://github.com/artur-rios/fortuna-api/issues/11) | UC-10: View Figures in a Display Currency | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#53](https://github.com/artur-rios/fortuna-api/issues/53) | UC-52: Delete and Restore a Record | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#54](https://github.com/artur-rios/fortuna-api/issues/54) | UC-53: Read the Audit Trail | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |

### M-03 — Holdings

| Issue | Work | Spec |
|---|---|---|
| [#12](https://github.com/artur-rios/fortuna-api/issues/12) | UC-11: Create a Financial Account | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#13](https://github.com/artur-rios/fortuna-api/issues/13) | UC-12: View Financial Accounts | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#14](https://github.com/artur-rios/fortuna-api/issues/14) | UC-13: Update a Financial Account | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#15](https://github.com/artur-rios/fortuna-api/issues/15) | UC-14: View an Account Balance | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#16](https://github.com/artur-rios/fortuna-api/issues/16) | UC-15: Delete a Financial Account | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#17](https://github.com/artur-rios/fortuna-api/issues/17) | UC-16: Create a Credit Card | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#18](https://github.com/artur-rios/fortuna-api/issues/18) | UC-17: View Credit Cards and Limits | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#19](https://github.com/artur-rios/fortuna-api/issues/19) | UC-18: Update a Credit Card | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#20](https://github.com/artur-rios/fortuna-api/issues/20) | UC-19: Delete a Credit Card | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#21](https://github.com/artur-rios/fortuna-api/issues/21) | UC-20: Assign a Charge to a Billing Cycle | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#22](https://github.com/artur-rios/fortuna-api/issues/22) | UC-21: Close a Statement | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#23](https://github.com/artur-rios/fortuna-api/issues/23) | UC-22: View a Statement | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#24](https://github.com/artur-rios/fortuna-api/issues/24) | UC-23: Settle a Statement | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#25](https://github.com/artur-rios/fortuna-api/issues/25) | UC-24: Create an Investment | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#26](https://github.com/artur-rios/fortuna-api/issues/26) | UC-25: Record an Investment Movement | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#27](https://github.com/artur-rios/fortuna-api/issues/27) | UC-26: Record an Investment Valuation | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#28](https://github.com/artur-rios/fortuna-api/issues/28) | UC-27: View Investments and Positions | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#29](https://github.com/artur-rios/fortuna-api/issues/29) | UC-28: Update an Investment | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#30](https://github.com/artur-rios/fortuna-api/issues/30) | UC-29: Delete an Investment | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |

### M-04 — Money movement

| Issue | Work | Spec |
|---|---|---|
| [#31](https://github.com/artur-rios/fortuna-api/issues/31) | UC-30: Record a Transaction | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#32](https://github.com/artur-rios/fortuna-api/issues/32) | UC-31: Search Transactions | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#33](https://github.com/artur-rios/fortuna-api/issues/33) | UC-32: Update a Transaction | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#34](https://github.com/artur-rios/fortuna-api/issues/34) | UC-33: Delete a Transaction | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#35](https://github.com/artur-rios/fortuna-api/issues/35) | UC-34: Record a Transfer | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#36](https://github.com/artur-rios/fortuna-api/issues/36) | UC-35: Delete a Transfer | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#37](https://github.com/artur-rios/fortuna-api/issues/37) | UC-36: Record an Installment Purchase | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#38](https://github.com/artur-rios/fortuna-api/issues/38) | UC-37: Define a Recurring Transaction | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#39](https://github.com/artur-rios/fortuna-api/issues/39) | UC-38: Materialize Recurring Occurrences | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#40](https://github.com/artur-rios/fortuna-api/issues/40) | UC-39: Update a Recurring Transaction | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#41](https://github.com/artur-rios/fortuna-api/issues/41) | UC-40: Reconcile a Transaction | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |

### M-05 — Organization and planning

| Issue | Work | Spec |
|---|---|---|
| [#42](https://github.com/artur-rios/fortuna-api/issues/42) | UC-41: Create a Category | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#43](https://github.com/artur-rios/fortuna-api/issues/43) | UC-42: View the Category Tree | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#44](https://github.com/artur-rios/fortuna-api/issues/44) | UC-43: Update a Category | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#45](https://github.com/artur-rios/fortuna-api/issues/45) | UC-44: Reassign Transactions Between Categories | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#46](https://github.com/artur-rios/fortuna-api/issues/46) | UC-45: Delete a Category | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#47](https://github.com/artur-rios/fortuna-api/issues/47) | UC-46: Manage Tags | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#48](https://github.com/artur-rios/fortuna-api/issues/48) | UC-47: Manage Counterparties | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#49](https://github.com/artur-rios/fortuna-api/issues/49) | UC-48: Define a Budget | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#50](https://github.com/artur-rios/fortuna-api/issues/50) | UC-49: Track Budget Consumption | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#51](https://github.com/artur-rios/fortuna-api/issues/51) | UC-50: Define a Goal | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#52](https://github.com/artur-rios/fortuna-api/issues/52) | UC-51: Track Goal Progress | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |

### M-06 — Ingestion

| Issue | Work | Spec |
|---|---|---|
| [#55](https://github.com/artur-rios/fortuna-api/issues/55) | UC-54: Discover Available Data Sources | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#56](https://github.com/artur-rios/fortuna-api/issues/56) | UC-55: Connect an Institution through Pluggy | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#57](https://github.com/artur-rios/fortuna-api/issues/57) | UC-56: Synchronize from a Connection | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#58](https://github.com/artur-rios/fortuna-api/issues/58) | UC-57: Reauthenticate a Connection | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#59](https://github.com/artur-rios/fortuna-api/issues/59) | UC-58: Revoke a Connection | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#60](https://github.com/artur-rios/fortuna-api/issues/60) | UC-59: Import Transactions from an Excel Workbook | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#61](https://github.com/artur-rios/fortuna-api/issues/61) | UC-60: Import a Nubank Credit Card Invoice PDF | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#62](https://github.com/artur-rios/fortuna-api/issues/62) | UC-61: Monitor an Import Job | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#63](https://github.com/artur-rios/fortuna-api/issues/63) | UC-62: Retry a Failed Import Job | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#64](https://github.com/artur-rios/fortuna-api/issues/64) | UC-63: Review Imported Records | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |

### M-07 — Insight and output

| Issue | Work | Spec |
|---|---|---|
| [#65](https://github.com/artur-rios/fortuna-api/issues/65) | UC-64: Attach a Document to a Transaction | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#66](https://github.com/artur-rios/fortuna-api/issues/66) | UC-65: Download an Attachment | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#67](https://github.com/artur-rios/fortuna-api/issues/67) | UC-66: Delete an Attachment | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#68](https://github.com/artur-rios/fortuna-api/issues/68) | UC-67: Query Records as a Table | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#69](https://github.com/artur-rios/fortuna-api/issues/69) | UC-68: Aggregate Transactions for a Chart | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#70](https://github.com/artur-rios/fortuna-api/issues/70) | UC-69: Drill Into an Aggregation | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#71](https://github.com/artur-rios/fortuna-api/issues/71) | UC-70: View the Net Position | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#72](https://github.com/artur-rios/fortuna-api/issues/72) | UC-71: Project Cash Flow | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#73](https://github.com/artur-rios/fortuna-api/issues/73) | UC-72: View Committed Obligations | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#74](https://github.com/artur-rios/fortuna-api/issues/74) | UC-73: Export a Data Set | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#75](https://github.com/artur-rios/fortuna-api/issues/75) | UC-74: Retrieve a Completed Export | [Use Case Specification](docs/requirements/Use%20Case%20Specification%20Document.md) |
| [#76](https://github.com/artur-rios/fortuna-api/issues/76) | UC-75: Check API Health | [Operations & Infrastructure](docs/requirements/Operations%20%26%20Infrastructure%20Document.md) |

## Contributing

One use case = one branch = one issue = one pull request. The full process — branch naming, the
issue status lifecycle, the approval gates, the testing gate, and the Definition of Done — is in the
[Development Workflow Document](docs/requirements/Development%20Workflow%20Document.md), with its
step-by-step operational form in [`docs/initial/Workflow.md`](docs/initial/Workflow.md).
