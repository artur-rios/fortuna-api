# Business Rules — Fortuna API

The rules the domain must enforce, independently of how the API is built. They are numbered
`BR-xx`; the formal System Requirements Document traces each one to the functional requirements
that realize it, so the numbers are stable — a rule that is withdrawn keeps its number rather than
letting the ones below it shift.

> Entities marked **(proposed)** below are not named in the brainstorm. They are suggestions drawn
> from what the stated features imply — the domain is reviewed before any of it is implemented.

## Domain Entities

| Entity | Represents |
| --- | --- |
| **User** | The owner of a set of financial records. In connected mode the identity is Heimdall's and Fortuna keeps only a local profile keyed by the Heimdall subject; in desktop offline mode Fortuna owns the identity outright. |
| **LocalAccount** *(proposed)* | A desktop offline identity — held in memory or in the operating system's credential store — with no password reset path. |
| **RecoveryCode** *(proposed)* | A single-use code that is the only way back into a `LocalAccount`. Stored hashed. |
| **Currency** | An ISO 4217 currency and its minor-unit precision. Reference data, not user data. |
| **ExchangeRate** *(proposed)* | A rate between two currencies on a given date, used to convert and recorded with whatever it converted. |
| **FinancialAccount** | A bank account or cash holding: name, institution, type, currency, opening balance. |
| **CreditCard** | A credit card: issuer, limit, currency, closing day and due day. Not a `FinancialAccount` — it behaves differently (see `BR-12`). |
| **CreditCardStatement** *(proposed)* | One billing cycle of a card — the invoice: period, closing date, due date, total, settlement state. |
| **Investment** | A held investment: instrument, institution, type, currency. |
| **InvestmentTransaction** *(proposed)* | A contribution, withdrawal, yield or fee against an `Investment`. |
| **InvestmentValuation** *(proposed)* | The recorded value of an `Investment` on a date. Fortuna records valuations; it does not price instruments. |
| **Transaction** | One movement of money — an expense or an earning — against a `FinancialAccount` or `CreditCard`. The core entity of the system. |
| **Transfer** *(proposed)* | A movement between two of the user's own accounts. Neither an expense nor an earning (`BR-15`). |
| **InstallmentPlan** *(proposed)* | A purchase split across N future charges, most often on a credit card. |
| **RecurringTransaction** *(proposed)* | A rule that generates transactions on a schedule — salary, rent, subscriptions. A template, never itself a movement. |
| **Category** | A user-defined classification of transactions, optionally nested under a parent. |
| **Tag** *(proposed)* | A free-form label; many per transaction, orthogonal to the category tree. |
| **Counterparty** *(proposed)* | The merchant, payer or payee on the other side of a transaction. |
| **Attachment** *(proposed)* | A receipt or document filed against a transaction. |
| **Budget** *(proposed)* | A planned ceiling for a category over a period, compared against what was actually spent. |
| **Goal** *(proposed)* | A savings target with an amount and a date, measured against actual balances. |
| **DataSource** | Where records come from: manual entry, Pluggy, Excel import, PDF import. The extension point new sources plug into. |
| **Connection** *(proposed)* | A live link to an external source — a Pluggy item — holding its reference and access token, never a bank credential. |
| **ImportJob** | One execution of an import or synchronization: source, period, counts, outcome, per-row results. |
| **ImportedRecord** | The raw, unmodified row or entry an import read from its source. Immutable (`BR-23`). |
| **Projection** | A forward-looking computation over the user's data. Derived, never stored as fact (`BR-29`). |
| **AuditLog** *(proposed)* | An append-only record of significant actions: imports, connection changes, deletions, exports. |

## Relationships

| Relationship | Cardinality |
| --- | --- |
| User → FinancialAccount / CreditCard / Investment | 1 : N |
| User → Category / Tag / Budget / Goal / Connection | 1 : N |
| User → LocalAccount | 1 : 0..1 |
| LocalAccount → RecoveryCode | 1 : N |
| FinancialAccount → Transaction | 1 : N |
| CreditCard → Transaction | 1 : N |
| CreditCard → CreditCardStatement | 1 : N |
| CreditCardStatement → Transaction | 1 : N (a transaction belongs to at most one statement) |
| CreditCardStatement → Transaction (its settling payment) | 0..1 : 1 |
| Transfer → FinancialAccount | N : 2 (one origin, one destination — never the same account) |
| InstallmentPlan → Transaction | 1 : N (one per installment) |
| RecurringTransaction → Transaction | 1 : N (the occurrences already materialized) |
| Category → Category (parent) | 0..1 : N |
| Category → Transaction | 1 : N |
| Transaction ↔ Tag | N : N |
| Counterparty → Transaction | 1 : N |
| Transaction → Attachment | 1 : N |
| Investment → InvestmentTransaction / InvestmentValuation | 1 : N |
| Budget → Category | 1 : N |
| DataSource → Connection | 1 : N |
| Connection → ImportJob | 1 : N |
| ImportJob → ImportedRecord | 1 : N |
| ImportedRecord → Transaction | 1 : 0..1 |
| Currency → ExchangeRate | 1 : N (as base and as quote) |

## Rules

### Ownership and isolation

| # | Rule | Rationale |
| --- | --- | --- |
| **BR-01** | Every financial record belongs to exactly one user, and only that user may read or modify it. | The whole point of a self-hostable finance system is that nobody else sees the data — including another user of the same shared instance. |
| **BR-02** | An instance administrator manages users, integrations and operational health, and has no access to any user's financial records. | Running the instance is not a reason to read its contents. Administration and ownership are separate authorities. |
| **BR-03** | A record's owner is fixed at creation and never changes. | Re-owning a transaction would rewrite two users' histories at once. |
| **BR-04** | An operation that would read or write a record belonging to another user is refused as if the record did not exist. | Distinguishing "forbidden" from "not found" leaks the existence of another user's data. |

### Money and currency

| # | Rule | Rationale |
| --- | --- | --- |
| **BR-05** | Every monetary amount is an exact decimal value carrying its currency. Binary floating-point representation is never used for money, at any layer, at any point. | `0.1 + 0.2` is the defect that makes a finance system untrustworthy, and it is unrecoverable once it has accumulated across a history. |
| **BR-06** | Amounts in different currencies are never summed, compared or netted without an explicit conversion. | An implied conversion is an invented number. |
| **BR-07** | Every conversion records the rate applied and the date it was taken from, alongside the converted result. | A converted figure that cannot be re-derived cannot be audited. |
| **BR-08** | An account, card or investment has exactly one currency, set at creation and immutable afterwards. | Changing an account's currency retroactively reinterprets every transaction it holds. |
| **BR-09** | A transaction is denominated in the currency of the account it belongs to. Where the original was in another currency, both the original amount and its currency are kept alongside the converted one. | A foreign purchase has two true amounts; discarding either loses information the user will look for. |
| **BR-10** | Rounding is applied only where a result is presented or converted, never to an intermediate value, and always to the currency's own minor-unit precision. | Rounding intermediates is how a total stops matching the sum of its parts. |
| **BR-11** | A balance, total or aggregate is always derived from the underlying records — it is never a stored, separately editable number. | Two sources of truth for a balance means one of them is wrong and nobody knows which. |

### Accounts, cards and investments

| # | Rule | Rationale |
| --- | --- | --- |
| **BR-12** | A credit card is not a bank account. Its charges accumulate into a statement, and it is settled by a payment transaction from a financial account; that settlement is a transfer, not an expense. | Counting both the purchase and the invoice payment as expenses doubles every month's spending. |
| **BR-13** | A transaction falls into the statement whose billing cycle contains its date, determined by the card's closing day. | The cycle, not the calendar month, is what the user is billed on. |
| **BR-14** | Once a statement is settled, its composition is frozen. A record arriving afterwards that falls in its period is attached to the next open statement and flagged as late-arriving. | A settled invoice is a fact that already happened; silently changing its total contradicts what the bank charged. |
| **BR-15** | A transfer between the user's own accounts is neither an earning nor an expense, and is excluded from income and expense totals. Both sides move together or neither does. | Moving money between one's own pockets is not income, and a half-applied transfer breaks both accounts at once. |
| **BR-16** | The origin and destination of a transfer must be different accounts. | — |
| **BR-17** | Fortuna records investment valuations; it never prices an instrument itself. A position's value is what was contributed, withdrawn and recorded. | Pricing an instrument is a claim about the market, and a wrong one is worse than no number at all. |

### Transactions and schedules

| # | Rule | Rationale |
| --- | --- | --- |
| **BR-18** | Every transaction has a date, an amount, a direction (expense or earning), an owning account and a category. | These are what every view, filter and chart is built on; a record missing one of them cannot be reported. |
| **BR-19** | An amount is always recorded as a positive value; the direction carries the sign. | Two ways to express "spent 50" produce two answers to every query. |
| **BR-20** | An installment plan generates installments whose amounts sum **exactly** to the purchase total, with any rounding remainder carried by the first installment. | Split evenly, 100.00 over 3 gives 99.99. The remainder has to land somewhere explicit. |
| **BR-21** | A recurring transaction is a rule, not a movement. Only its materialized occurrences are real transactions; occurrences still in the future exist solely inside projections. | Otherwise next year's rent is already in this month's balance. |
| **BR-22** | Editing a recurring rule affects future occurrences only; occurrences already materialized keep what they recorded. | The past happened as it happened. |
| **BR-23** | A category may have at most one parent, and the hierarchy may not contain a cycle. | — |

### Import, sources and integrations

| # | Rule | Rationale |
| --- | --- | --- |
| **BR-24** | Imported raw records are immutable. They are never edited, and never deleted by an ordinary operation. Corrections are made on the transaction derived from a record, never on the record itself. | The raw record is the evidence an import can be re-checked against; edit it and there is nothing left to reconcile with. |
| **BR-25** | Every transaction records the source it came from, and — when it was not entered by hand — the imported record it derives from. | Provenance is what makes it possible to answer "why is this here?" a year later. |
| **BR-26** | An incoming record that matches an existing transaction on account, date, amount and — where the source provides one — the source's own identifier, is not imported a second time. | Re-importing an overlapping statement period is normal, and it must not double the month. |
| **BR-27** | An import job reports its outcome per row: imported, skipped as duplicate, or rejected with a reason. A row failing never aborts the rows around it. | One malformed line in a 400-row statement must not cost the other 399. |
| **BR-28** | Bank credentials — username, password, token, MFA secret or any other authentication factor for a financial institution — are never stored, logged or transmitted by Fortuna. Open-banking access lives with Pluggy and is referenced by connection token only. | This is absolute. There is no feature worth holding a bank password for. |
| **BR-29** | Revoking a connection stops future synchronization and leaves everything already imported in place. | Cutting off a bank link is not a decision to erase a year of history. |
| **BR-30** | A new ingestion source is added by implementing the source contract; no existing source, and no consumer of imported data, changes because a source was added. | The brainstorm asks for Pluggy first and other sources later; that only holds if adding one is additive. |

### Projections and reporting

| # | Rule | Rationale |
| --- | --- | --- |
| **BR-31** | A projection is derived on demand from current data and is never persisted as though it were history. | A stored projection becomes a stale claim about the future that nothing invalidates. |
| **BR-32** | Projected figures are always distinguishable from recorded ones in any result that mixes them. | A user must never mistake a forecast for a fact. |
| **BR-33** | Aggregations, exports and balances exclude soft-deleted records. | A deleted record that still moves the total is the worst of both states. |
| **BR-34** | Any aggregate a client can drill into resolves, at its finest level, to the individual transactions that produced it. | A chart the user cannot get behind is a number they have to take on faith. |

### Identity

| # | Rule | Rationale |
| --- | --- | --- |
| **BR-35** | In connected mode, Fortuna never stores or handles a password. Identity, credentials and recovery belong to Heimdall; Fortuna consumes the issued token. | One system owns identity, and it is the one built for it. |
| **BR-36** | A desktop local account has no password reset and no e-mail recovery. Its recovery codes, issued at creation, are the only way back in — each usable once, stored hashed, and unrecoverable if lost. | Offline means there is no channel to prove identity through. This is stated plainly to the user at creation, because losing every code means losing the account. |
| **BR-37** | Local-account data belongs to that installation. It is never synchronized to a shared instance implicitly. | — |

### Deletion and audit

| # | Rule | Rationale |
| --- | --- | --- |
| **BR-38** | Deletion is two-stage: a record is soft-deleted first, and only a soft-deleted record may then be hard-deleted. Nothing goes from live to gone in one step. | Financial history is deleted by accident exactly once, and the two-stage rule is what makes that recoverable. |
| **BR-39** | A soft-deleted record is excluded from every balance, aggregation, projection and export, but remains retrievable and restorable. | — |
| **BR-40** | A hard delete is refused while any live record still references the target. | Leaving a transaction pointing at an account that no longer exists corrupts every query that joins them. |
| **BR-41** | Audit log entries are append-only: never edited, never deleted, not even by a hard delete of what they describe. | An audit trail that can be pruned is not one. |

## Validation Constraints

| Entity | Field | Constraint |
| --- | --- | --- |
| User | Heimdall subject | Required in connected mode, unique per instance. |
| LocalAccount | User name | Required, unique per installation. |
| RecoveryCode | Code | Required, stored hashed, single-use, unique per local account. |
| FinancialAccount / CreditCard / Investment | Name | Required, unique per user within its kind. |
| FinancialAccount / CreditCard / Investment | Currency | Required, a known ISO 4217 code, immutable after creation. |
| FinancialAccount | Opening balance | Required; may be zero or negative. |
| CreditCard | Credit limit | Required, greater than zero. |
| CreditCard | Closing day, due day | Required, 1–31; the due day follows the closing day within the cycle. |
| Transaction | Amount | Required, strictly greater than zero. |
| Transaction | Date | Required; a date more than one day in the future is rejected — a future movement is a recurring rule or a projection, not a transaction. |
| Transaction | Direction | Required: expense or earning. |
| Transaction | Account | Required; must belong to the same user. |
| Transaction | Category | Required; must belong to the same user. |
| Transaction | Description | Optional, bounded length. |
| Transfer | Origin, destination | Both required, both the user's own, and different from each other. |
| InstallmentPlan | Installment count | Required, an integer of at least two. |
| RecurringTransaction | Schedule | Required; a recognized frequency with a start date, and an end date that is not before it. |
| Category | Name | Required, unique among its siblings for that user. |
| Category | Parent | Optional; must belong to the same user and must not create a cycle. |
| Tag | Name | Required, unique per user. |
| Budget | Amount, period | Required; amount greater than zero, period well-formed. |
| Goal | Target amount, target date | Required; amount greater than zero, date in the future at creation. |
| ExchangeRate | Base, quote, rate, date | All required; rate greater than zero; base and quote different; unique per pair and date. |
| Connection | External reference | Required, unique per user and source. |
| ImportedRecord | Payload | Required; retained exactly as received. |
| Attachment | File | Required; bounded size, restricted content types. |

## Permissions

| Capability | Account owner | Instance administrator |
| --- | --- | --- |
| Create, read, update and delete their own financial records | Yes | No |
| Read another user's financial records | No | **No** |
| Import, export, synchronize and project their own data | Yes | No |
| Manage their own categories, tags, budgets and goals | Yes | No |
| Manage their own connections to external sources | Yes | No |
| Hard-delete a record they have already soft-deleted | Yes | No |
| Manage users and their access to the instance | No | Yes — through Heimdall |
| Configure the instance and its integrations | No | Yes |
| Read operational health, logs and import job outcomes across the instance | No | Yes — outcomes and counts, never record contents |
| Read the audit log for their own records | Yes | Yes, for instance-level events |

Roles and their membership are Heimdall's; Fortuna reads them from the issued token and enforces
the matrix above. A desktop local account always holds the account-owner role, and there is no
administrator in a local installation.

## Lifecycle

**Creation.** A record is created by its owner, or by an import acting on their behalf. An imported
transaction is created together with the link to the raw record it came from.

**Transaction states.** `Recorded` → `Reconciled` (matched against an imported record from the
institution, or confirmed by the user) → `SoftDeleted` → `HardDeleted`. A projected or scheduled
occurrence is not in this lifecycle at all until it materializes.

**Statement states.** `Open` (accumulating charges) → `Closed` (past the closing date, total fixed)
→ `Settled` (a payment transaction has cleared it). A statement never returns to a previous state;
a late-arriving charge goes to the next open one (`BR-14`).

**Import job states.** `Pending` → `Running` → `Completed` or `Failed`. A completed job keeps its
per-row outcomes; a failed one keeps the reason.

**Connection states.** `Active` → `RequiresReauthentication` → `Active`, or → `Revoked`. Revoked is
terminal for synchronization and leaves imported data untouched (`BR-29`).

**Deletion.** Soft delete, then hard delete, in that order and never skipping the first (`BR-38`).
Restoring is available from soft-deleted only. Deleting an account soft-deletes its transactions
with it; hard-deleting it is refused while any live transaction remains (`BR-40`).

## Prohibitions

- **Never store, log or transmit a credential for a financial institution.** No exception, no
  configuration flag, no debug mode.
- **Never mutate imported data.** The raw record is evidence, not a draft.
- **Never represent money in binary floating point** — not in the database, not in a DTO, not in an
  intermediate calculation, not in an export.
- **Never let one user's data become reachable by another**, including through an export, an error
  message, a log line, or the difference between two response codes.
- **Never move money.** Fortuna has no payment, transfer-at-bank or trading capability, and no
  endpoint may acquire one.
- **Never hard-delete a record that was not soft-deleted first**, and never hard-delete an audit
  entry at all.
- **Never present a projection as recorded history.**
- **Never write back to an external financial institution.** Every integration is read-only.
