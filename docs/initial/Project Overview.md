# Project Overview — Fortuna API

## What This Is

Fortuna is a personal finance system that tracks a person's whole financial life — bank accounts,
credit cards, investments, expenses and earnings — in one place, and turns that history into
tabular views, charts and forward-looking projections. **This repository is the back end only:**
an HTTP API that owns the domain, the data and the integrations. The Flutter application that runs
on Windows, Linux, the browser and mobile is a separate repository and a separate consumer of this
API, sharing one code base across every target so the experience is the same everywhere.

## The Problem

A person's financial life is scattered across places that do not talk to each other: one bank's
app, another bank's statement PDF, a broker's portal, a credit card invoice, and a spreadsheet
somebody maintains by hand to make sense of the rest. Nothing reconciles, so the two questions that
actually matter — *where did the money go?* and *where will I be in six months?* — cannot be
answered without an afternoon of manual work.

The people who feel it are individuals and households who want a single, trustworthy ledger of
their own money, and who are not willing to hand that data to a service they do not control.
Fortuna is self-hostable for exactly that reason.

## Who It's For

| Consumer | What they need from the API |
| --- | --- |
| **Account owner** | The everyday user. Records and reviews their own financial data, imports statements, connects banks, reads charts and projections. Sees only their own data. |
| **Instance administrator** | Runs a shared instance. Manages users, integrations and operational health — and has no access to anybody's financial records. |
| **Fortuna client applications** | The Flutter desktop, web and mobile app. The primary caller of every endpoint. |
| **Heimdall API** | The identity provider. Owns users, credentials and the tokens Fortuna trusts. |
| **Pluggy** | The open-banking aggregator Fortuna pulls account and transaction data from. |

An instance runs in one of two shapes, and the API supports both without a code change:

- **Single self-hosted owner** — one person, their own machine or VPS, one user account.
- **Shared instance** — several registered users on one deployment, each strictly isolated from the
  others.

## What It Does

- **Tracks accounts** — bank accounts, credit cards and investments, each with its own balance,
  currency and history.
- **Records money movement** — expenses, earnings and transfers between accounts, entered by hand
  or brought in automatically.
- **Ingests from several sources** — manual entry, the Pluggy open-banking API, Excel spreadsheets,
  and a defined set of PDF statement layouts. The ingestion design is pluggable: a new source is
  added without disturbing the existing ones.
- **Handles multiple currencies** — every amount carries its currency, and conversions are explicit
  and auditable rather than implied.
- **Serves the data as a spreadsheet** — filtered, sorted, paginated tabular queries the client
  renders as a grid.
- **Serves the data as charts** — aggregations by period, category, account and counterparty, each
  drillable: a client can ask for the breakdown *behind* a chart element and get the next level
  down, and eventually the individual transactions.
- **Projects the future** — forward projections built from recurring commitments, credit card
  installments and observed history.
- **Exports** — CSV, Excel and PDF renderings of any queried data set.
- **Searches, filters, updates and deletes** — across every kind of record it holds.

## What It Doesn't Do

These are deliberate exclusions. They are not "later" — they are outside what Fortuna is.

- **It does not move money.** No payments, no transfers executed at a bank, no trading. Fortuna
  reads and records; it never writes to a financial institution.
- **It does not give financial advice.** A projection is arithmetic over the user's own data, not a
  recommendation.
- **It does not do tax filing or accounting compliance.** No tax forms, no double-entry ledger for
  a business, no fiscal reporting.
- **It does not store bank credentials.** Open-banking access lives with Pluggy and is referenced
  by token; Fortuna never holds a bank username, password or MFA secret.
- **It does not manage users, passwords or sessions.** That belongs to the Heimdall API. The one
  exception is the desktop offline account described below, which is Fortuna's own.
- **It does not render the user interface.** No screens, no charts drawn server-side — the API
  serves the numbers, the Flutter client draws them.

## Two Ways In

Authentication has two distinct paths, and the split matters to nearly every use case:

1. **Connected mode** — the normal path. Heimdall authenticates the user and issues the token
   Fortuna trusts. All user management, sign-up, password recovery and multi-factor authentication
   are Heimdall's.
2. **Desktop offline mode** — resolved entirely inside Fortuna. A desktop installation may run a
   local account, held in memory or backed by the operating system's credential store, with **no**
   password reset path: recovery codes issued at creation are the only way back in. This exists so
   the desktop app works with no network and no Heimdall reachable.

## How Success Is Measured

- **Every feature works, in a secure deployment.** The full feature set above is delivered, and the
  instance is safe to expose: authenticated, isolated per user, no credential ever stored, no user's
  data reachable by another.
- **Money is exact.** No rounding drift, no floating-point error, ever. A balance computed twice
  gives the same answer, and it matches the sum of its transactions to the cent.
- **It feels instant.** The targets, chosen for interactive use, are: a single record read or a
  page of a list in **under 200 ms** at the 95th percentile; a write in **under 500 ms**; a chart
  aggregation or drill-down over a year of data in **under 1 second**. Those thresholds come from
  how interaction actually reads to a person — around 100 ms feels immediate, and past a second
  attention breaks — so they are the boundary between "responsive" and "waiting". Bulk work
  (imports, Pluggy synchronization, PDF exports) is exempt: it runs asynchronously and reports
  progress instead.
- **Imports need no cleanup.** A statement or spreadsheet brought in lands in the right accounts,
  with duplicates detected against what is already there, without the user correcting it row by row.
- **It runs the same everywhere.** One `docker compose` invocation brings the instance up on Docker
  Desktop for Windows, on Docker in WSL Ubuntu, and on a Linux VPS — differing only in configuration.
