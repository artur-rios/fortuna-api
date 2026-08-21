# Testing Specification Document — Fortuna API

## 1. Purpose

This document defines **how a use case is tested once it has been implemented**. It is a standard to
be followed by any human or agent that builds tests for this project, so that every use case
(UC-01 … UC-75 in the
[Use Case Specification Document](Use%20Case%20Specification%20Document.md) and the
[Operations & Infrastructure Document](Operations%20%26%20Infrastructure%20Document.md)) receives
the same shape of testing, with the same tools, naming and structure.

The rule is simple:

> **After a use case is developed, tests are built for it in the same change — before the use case
> is considered done.** A use case without its tests is an incomplete use case.

The tools and versions used are defined in the
[Technology Stack Document](Technology%20Stack%20Document.md); when the tests run in the delivery
flow is defined in the [Development Workflow Document](Development%20Workflow%20Document.md).

---

## 2. Testing philosophy

1. **Behavior-driven.** Tests describe *behavior*, not implementation. Every test is written in the
   **Given / When / Then** style and is named accordingly (§5). A test that would have to change
   because a private method was renamed is testing the wrong thing.
2. **Test at the right layer.** Business logic — command and query handlers, validators, and any
   domain type that makes a decision — is covered by **unit tests**. The Web API is covered by
   **functional (end-to-end) tests**. Read-model queries that go to the database directly are
   covered functionally, because a fake repository cannot tell you whether the SQL is right.
3. **Isolation in unit tests.** A unit test exercises *one* class. Every collaborator is replaced by
   a test double. Only the behavior of the method under test is asserted.
4. **Realism in functional tests.** A functional test exercises the API exactly as a client would —
   over HTTP, against a **real PostgreSQL** provisioned on the fly by Testcontainers. Both the
   **response** and the **resulting database state** are asserted. An in-memory provider is never
   used: it does not enforce the constraints, the decimal precision, or the transactional semantics
   this domain depends on.
5. **Money and provenance get more than the average.** Monetary arithmetic, currency conversion,
   installment splitting, statement cycle assignment, duplicate detection and the import parsers are
   where a defect is silent, cumulative and expensive. They are tested exhaustively, with explicit
   boundary and rounding cases, regardless of what coverage already reports.
6. **Same pattern every time.** The workflow in §9 is applied identically to every use case, so the
   suite stays predictable as the system grows.

### 2.1 The coverage floor, and why it is only a floor

Merged **line coverage must stay at or above 90%**. It is enforced by `scripts/coverage.py`, in
continuous integration and on a developer machine alike, and the build fails below it (NFR-25).
Branch coverage is reported but not gated; raising that to a gate is a decision to take deliberately,
by writing the missing tests first rather than by setting a threshold to whatever currently passes.

**The floor is not the target.** The standard for this project is to test everything that can be
tested, not to reach a number and stop. Coverage catches one specific failure — a subsystem arriving
with no tests at all — and it is blind to the ones that matter more: a handler covered by a single
happy-path test, an alternative flow nobody wrote, a rounding boundary nobody probed. §3 and §6.3 are
what actually decide whether a use case is tested. A change can hold 96% line coverage while testing
none of the behavior that would break a balance.

---

## 3. What to test for each use case

When a use case is implemented, walk this list and produce every applicable test:

| Artifact produced by the use case | Test kind | Test project |
| --- | --- | --- |
| A **command** handler (`*CommandHandler`) | Unit | `ArturRios.Fortuna.Command.Tests` |
| A **query** handler (`*QueryHandler`) | Unit | `ArturRios.Fortuna.Query.Tests` |
| An input **validator** (`*Validator`) | Unit, alongside the handler that uses it | `*.Command.Tests` / `*.Query.Tests` |
| A **domain** type that implements behavior — money arithmetic, a billing cycle calculation, an installment split, a state transition, an invariant guard | Unit | `ArturRios.Fortuna.Domain.Tests` |
| A **service** shared between command and query sides | Unit | `ArturRios.Fortuna.Shared.Tests` |
| An **ingestion source**, a **statement parser**, an **export renderer**, an **exchange rate client**, an **attachment store** | Unit against recorded fixtures, plus functional through the endpoint that drives it | `ArturRios.Fortuna.Integration.Tests` |
| An **entity map**, a **migration**, or a **read-model query** | Functional, against the real database | `ArturRios.Fortuna.Data.Tests` / `*.WebApi.Tests` |
| A **controller** endpoint exposing the use case | Functional | `ArturRios.Fortuna.WebApi.Tests` |

Notes on what deliberately gets **no** tests, and why:

- **Anemic entities** — plain data holders with properties and navigation collections only — carry no
  behavior and get no unit tests of their own. Their behavior is observed through the handlers and
  the functional suite. An entity earns unit tests the moment it gains a method that decides
  something: a guard clause, a state transition, a calculation.
- **Generated migrations** are not unit-tested. They are exercised by the functional suite, which
  applies them to a real container and asserts the resulting schema.
- **Mapping-only code** with no branch — a DTO projection that copies fields — is covered
  incidentally by the tests of whatever produces it, not by a test of its own.

Every use case that reaches the API **must** have functional coverage, even when its handler is
already unit-tested. The two layers verify different things: the unit test says the handler decided
correctly, the functional test says the request reached it, was authorized, persisted, and came back
as the contract promises.

---

## 4. Test project layout

Tests live under a top-level `tests/` directory that **mirrors** the `src/` layer folders. Each
production project has exactly one corresponding test project, named by appending **`.Tests`**. Each
production class has one corresponding test class, named by appending **`Tests`**.

```
src/
  Domain/
    ArturRios.Fortuna.Domain/            →  Money.cs, BillingCycle.cs, InstallmentSplitter.cs
  Application/
    ArturRios.Fortuna.Command/           →  RecordTransactionCommandHandler.cs
    ArturRios.Fortuna.Query/             →  SearchTransactionsQueryHandler.cs
    ArturRios.Fortuna.Shared/            →  CurrencyConverter.cs
  Infrastructure/
    ArturRios.Fortuna.Data/              →  AppDbContext.cs, EntityMaps/, Migrations/
    ArturRios.Fortuna.Integration/       →  Pluggy/, Ptax/, Statements/Nubank/, Storage/, Export/
  Presentation/
    ArturRios.Fortuna.WebApi/            →  TransactionController.cs

tests/
  Domain/
    ArturRios.Fortuna.Domain.Tests/      →  MoneyTests.cs, BillingCycleTests.cs
  Application/
    ArturRios.Fortuna.Command.Tests/     →  RecordTransactionCommandHandlerTests.cs
    ArturRios.Fortuna.Query.Tests/       →  SearchTransactionsQueryHandlerTests.cs
    ArturRios.Fortuna.Shared.Tests/      →  CurrencyConverterTests.cs
  Infrastructure/
    ArturRios.Fortuna.Data.Tests/        →  schema and migration assertions
    ArturRios.Fortuna.Integration.Tests/ →  NubankStatementParserTests.cs, PtaxRateClientTests.cs
  Presentation/
    ArturRios.Fortuna.WebApi.Tests/      →  TransactionControllerRecordTests.cs (functional)
  Fixtures/                              →  recorded source documents and payloads (§7.2)
```

Rules:

- **One test project per production project**, named `<ProjectName>.Tests`.
- **One test class per production class under test**, named `<ClassName>Tests`, in a namespace
  mirroring the production namespace. A class with many behaviors may be split into several test
  classes suffixed by the behavior — `TransactionControllerRecordTests`,
  `TransactionControllerSearchTests` — rather than growing one file past readability.
- The test project **references the production project it tests** through a `<ProjectReference>`.
- Test projects set `<IsPackable>false</IsPackable>` and are added to the solution under the matching
  `Tests` solution folder.

---

## 5. Naming and structure

Every test is named with the **Given / When / Then** pattern, in Pascal case, with the three parts
separated by underscores:

```
GivenSomeCondition_WhenSomeAction_ThenSomeOutcome
```

Real examples from this domain:

```
GivenAmountIsZero_WhenRecordingTransaction_ThenValidationFails
GivenChargeDateFallsAfterClosingDay_WhenAssigningToCycle_ThenItGoesToTheNextStatement
GivenStatementIsSettled_WhenLateChargeArrives_ThenItAttachesToTheNextOpenStatement
GivenTotalDoesNotDivideEvenly_WhenSplittingIntoInstallments_ThenTheRemainderLandsOnTheFirst
GivenTransactionBelongsToAnotherUser_WhenRequestingIt_ThenNotFoundIsReturned
GivenInvoiceLinesDoNotReconcile_WhenImportingStatement_ThenNothingIsImported
```

Every test body follows the same three-part shape, with the parts marked:

```csharp
[UnitFact]
public void GivenTotalDoesNotDivideEvenly_WhenSplittingIntoInstallments_ThenTheRemainderLandsOnTheFirst()
{
    // Given
    var total = new Money(100.00m, Currency.Brl);
    var splitter = new InstallmentSplitter();

    // When
    var installments = splitter.Split(total, count: 3);

    // Then
    Assert.Equal(3, installments.Count);
    Assert.Equal(33.34m, installments[0].Amount);
    Assert.Equal(33.33m, installments[1].Amount);
    Assert.Equal(33.33m, installments[2].Amount);
    Assert.Equal(total.Amount, installments.Sum(i => i.Amount));   // the assertion that matters
}
```

The last assertion is the point of the test. When a split, a conversion or a balance is under test,
**assert the invariant, not only the individual values** — that the parts sum to the whole, that the
balance equals the sum of its transactions, that a round trip returns the original.

---

## 6. Unit testing standard

### 6.1 Scope of a unit test

One unit test exercises **one class**, through **one public method**, with every collaborator
replaced. It must not open a database connection, read a file from disk outside the fixtures folder,
reach the network, or read the system clock or a random source directly — time and randomness are
injected so a test can pin them.

### 6.2 Test doubles

| Collaborator | Double |
| --- | --- |
| `IAsyncRepository<T>` / `IAsyncReadOnlyRepository<T>` | `AsyncFakeRepository<T>` from `ArturRios.Util.Test` — a real in-memory collection, not a mock. Assert against its contents. |
| A validator, a mediator, a domain service, a clock | **Moq**. One mocking library; do not introduce a second. |
| An ingestion source, a rate client, an attachment store | A hand-written fake in the test project, plus recorded fixtures (§7.2). Never a live call. |
| Entities, commands and DTOs used as input | **Bogus** `Faker<T>`, seeded deterministically. Not large inline literals, and not a shared mutable fixture. |

Bogus is for data whose *values* do not matter to the assertion. When a value **is** the point — an
amount of `100.00`, a date on a closing day, a rate of `5.37` — write it literally, so the test reads
as the statement it is making.

### 6.3 Coverage per unit

For each production unit, walk this checklist and write a test for every line that applies:

- [ ] The **happy path**, asserting both the result and the state change.
- [ ] **Each validation failure** the unit can produce, asserted by its message or code, one test per
      rule — not one test asserting "it failed".
- [ ] **Each not-found** path, including the cross-user case, which must be indistinguishable from a
      genuinely absent record.
- [ ] **Each authorization denial.**
- [ ] **Each alternative flow `AF-xx`** in the use case that this unit is responsible for.
- [ ] **Every boundary**: zero, one, the maximum, the day before and after a closing day, the last
      day of a short month, a period spanning a year boundary, an empty collection, a single-element
      collection.
- [ ] **Every rounding case** where money is divided, converted or totalled — including one that does
      not divide evenly, and one where naive rounding would break the sum.
- [ ] **The invariant**, asserted directly, wherever the unit is responsible for one.

---

## 7. Functional testing standard

### 7.1 Scope

One functional test exercises the API **end to end**, through HTTP, from the request a client would
send to the response it would receive — and then asserts the database. It is built on
`WebApiTest<TEntryPoint>` from `ArturRios.Util.Test`, which starts the host and provides an HTTP
gateway and authentication helpers.

Both halves are mandatory:

- **The response** — status code, body shape, and the values a client depends on.
- **The persisted state** — that the row exists with the values expected, that a soft delete set the
  flag rather than removing the row, that a cascade reached what it should and nothing it should not,
  that an audit entry was written.

A functional test that asserts only the status code has verified almost nothing.

### 7.2 External dependencies

| Dependency | In tests |
| --- | --- |
| **PostgreSQL** | Real, in a throwaway container per run, provisioned by Testcontainers. Migrations are applied and the resulting schema is asserted. Never an in-memory provider. |
| **Heimdall** | Never called. Tests mint tokens locally with the same signing configuration the API validates against, so the whole authentication path is exercised without a second service. |
| **Pluggy** | Never called. A fake source implementation serves **recorded payloads** captured once from the real API and committed under `tests/Fixtures/`. |
| **BCB PTAX** | Never called. A fake rate client serves recorded responses, including a weekend with no publication and an unreachable-source case. |
| **Statement PDFs** | Real files, committed under `tests/Fixtures/Statements/`, with every personal detail replaced by synthetic values and every amount rewritten to a set that still reconciles. The structure is what is under test, never the data. |
| **Attachment storage** | The filesystem implementation against a temporary directory; the S3-compatible implementation against a fake S3 endpoint. Never a real bucket. |

No test reaches the network. A suite that depends on an external service being up is a suite that
goes red for reasons that have nothing to do with the change under review.

### 7.3 Coverage per endpoint

For each endpoint a use case exposes:

- [ ] The **main flow**, asserting response and persisted state.
- [ ] **Every `AF-xx`** of the use case that surfaces at this endpoint.
- [ ] **Unauthenticated** — no token: `401`.
- [ ] **Cross-user** — a valid token for a different user, naming a record that exists: `404`, with a
      body indistinguishable from the genuinely-absent case.
- [ ] **Invalid input** — at least one case per validated field, asserting the field is named.
- [ ] For a **write**: that exactly one audit entry was written, on success and on refusal alike.
- [ ] For a **soft delete**: that the row survives, the flag is set, the cascade reached its
      dependents, and the record no longer appears in balances, aggregates or exports.
- [ ] For a **money-returning** endpoint: that the value is exact, carries its currency, and — where
      converted — reports the rate and rate date.

---

## 8. Testing money, imports and time

Three areas need a standard of their own, because the ordinary checklist does not catch what goes
wrong in them.

**Money.** Every calculation gets a test whose assertion is the invariant, not the value: the
installments sum to the total, the balance equals opening plus transactions, converting and
converting back lands within one minor unit, a total computed twice is identical. At least one test
per calculation uses a figure that does not divide evenly. No test asserts a monetary value against a
floating-point literal — a `double` in a test is the same defect as a `double` in production.

**Imports.** Each supported layout gets a committed fixture and, at minimum: a clean import; a
re-import of the same file producing only duplicates; a file whose lines do not reconcile, importing
nothing; a period spanning a year boundary; a foreign-currency line with its original amount and
rate; an installment marker; a negative amount in each of the two minus characters; and a
malformed row that is rejected while its neighbours import. New layouts follow the same list.

**Time.** The clock is injected. Any behavior that depends on today — a due occurrence, a closing
date, a future-dated transaction, a goal's remaining days — is tested by pinning the clock, never by
computing an offset from the real current date. A test that passes only on some days of the month is
a defect in the test.

---

## 9. Per-use-case workflow

Apply this every time, in this order:

1. **Read the use case** — its main flow and every `AF-xx` — and the `FR-<AREA>-xx` requirements it
   cites.
2. **List the tests before writing them**: one per flow, per validation rule, per boundary. The list
   is the test plan the design gate approved.
3. **Write the unit tests** for each handler, validator and domain behavior the use case added.
4. **Write the functional tests** for each endpoint it exposes, per §7.3.
5. **Run both suites**, read the output, and fix what fails — in the implementation or in the test,
   whichever is actually wrong.
6. **Re-run until green.**
7. **Check the coverage report**, and treat a gap as a missing test rather than as a number to
   negotiate.
8. Only then does the use case leave the Testing stage.

---

## 10. Running the suites

```bash
dotnet test
```

| Suite | Command |
| --- | --- |
| Unit only | `dotnet test --filter "Category=Unit"` |
| Functional only | `dotnet test --filter "Category=Functional"` |
| With coverage | `dotnet test --collect:"XPlat Code Coverage"` |
| Coverage report and threshold | `python scripts/coverage.py` |

The `Category` trait comes from the `[UnitFact]` / `[UnitTheory]` and `[FunctionalFact]` /
`[FunctionalTheory]` attributes in `ArturRios.Util.Test`, so the filter needs no per-project
configuration. Every test carries one of those attributes; a bare `[Fact]` belongs to no category and
is missed by both filters, which is why it is not used.

Running the unit suite first is the habit worth keeping: it costs seconds, while the functional suite
pulls and starts a database container.
