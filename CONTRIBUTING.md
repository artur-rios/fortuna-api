# Contributing

One use case = one branch = one issue = one pull request. The full process — branch naming, the
issue status lifecycle, the approval gates, the testing gate, and the Definition of Done — is in the
[Development Workflow Document](docs/requirements/Development%20Workflow%20Document.md), with its
step-by-step operational form in [`docs/initial/Workflow.md`](docs/initial/Workflow.md). Work
branches are cut from and merged into `develop`; see the [branching model](#branching-model) below.

## Prerequisites

The **.NET 10 SDK** and either a reachable **PostgreSQL** instance for a shared deployment or local
**SQLite** storage for desktop offline mode. Building the in-process desktop core also requires the
stable **Rust** toolchain. Docker is required for the container workflow and for the functional
tests — see the
[Technology Stack Document](docs/requirements/Technology%20Stack%20Document.md) and the
[Operations & Infrastructure Document](docs/requirements/Operations%20%26%20Infrastructure%20Document.md).
The helper scripts under `scripts/` need Python 3.

## Building

```bash
dotnet tool restore  # dotnet-ef, ReportGenerator and the Swashbuckle CLI, pinned in .config/dotnet-tools.json
dotnet restore src/ArturRios.Fortuna.sln
dotnet build src/ArturRios.Fortuna.sln -m:1
```

The native core is built and tested with Cargo; its ABI, ownership and build contract are in
[`native/README.md`](native/README.md).

## Testing

The following commands run the complete suite described in the
[Testing Specification Document](docs/requirements/Testing%20Specification%20Document.md):

```bash
dotnet test src/ArturRios.Fortuna.sln -m:1
cargo test --manifest-path native/fortuna-core/Cargo.toml --locked
```

The suite covers **unit** tests over handlers, validators and domain behavior, and **functional**
tests over every endpoint end to end against a real PostgreSQL instance provisioned by
Testcontainers, plus file-backed SQLite migration and exact-decimal persistence tests. Run one
category at a time:

```bash
dotnet test src/ArturRios.Fortuna.sln -m:1 --filter "Category=Unit"
```

CI runs the unit and functional categories as separate steps and fails first if any test carries neither, since
neither step would run it.

Merged line coverage is gated at 90%, enforced in CI and reproducibly on a developer machine. That
is a floor, not a target — the standard is to test everything that can be tested. Every use case
ships with its tests before its pull request is opened.

```bash
dotnet tool restore  # ReportGenerator is pinned in .config/dotnet-tools.json
python3 scripts/coverage.py
```

## Checks CI runs

Besides the tests, these run on every pull request into `develop` and `main`, and can be run
locally:

```bash
python3 -m unittest discover -s scripts -p "test_*.py"   # the helper scripts' own tests
python3 scripts/check_blank_lines.py                     # a blank line above every return, break and yield
python3 scripts/vulnerabilities.py                       # fail on any vulnerable NuGet package, transitive included
python3 scripts/openapi.py --check                       # docs/openapi/fortuna.json matches the code
```

When the native core or the OpenAPI document changes, CI also runs `cargo fmt --check`,
`cargo clippy -- -D warnings`, the tests on the declared minimum Rust version, and checks that the
generated C header is committed. The `audit` check scans the native core's dependencies against the
RustSec advisory database on every pull request.

## OpenAPI document

`docs/openapi/fortuna.json` is generated from the code and committed. After changing the API
contract, regenerate it and commit the result:

```bash
python3 scripts/openapi.py
```

The native core generates its operation table and C header from this document, so a contract change
is an input to the native build too.

## Migrations

`scripts/migrations.py` is an interactive menu that lists, creates and applies the EF Core
migrations for `ArturRios.Fortuna.Data`, loading the connection string from one of the environment
files under the Web API's `Environments` folder. It needs the pinned EF tool — run
`dotnet tool restore` once after cloning.

```bash
python3 scripts/migrations.py
```

## Branching model

```
feature/<name> ─┐
fix/<name> ─────┴─▶ develop ──▶ release/x.y.z ──▶ main  (tag vx.y.z)
```

| Branch | Cut from | Merges into | How |
|---|---|---|---|
| `feature/<name>`, `fix/<name>` | `develop` | `develop` | Pull request, squash or merge. The branch is deleted on merge. |
| `release/x.y.z` | `develop` | `main` | Pull request. **Never merged by hand** — see below. |
| `develop`, `main` | — | — | Protected: no direct pushes, no force pushes, no deletion. |

Names are lowercase: letters, digits, `.`, `_` and `-`. A `release/` branch is a snapshot of
`develop` and carries no commits of its own: a fix for a release lands on `develop` through a
`fix/` branch and a new release branch is cut.

The **Branch Policy** workflow checks all of this on every pull request and is a required check
on `develop` and `main`.

## Commits and the changelog

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/) with a lowercase
subject, e.g. `feat: record processing consent` or `fix: keep statement due date after closing date`.

Record every change an API client or an operator would notice under `## [Unreleased]` in
[CHANGELOG.md](./CHANGELOG.md), in the same pull request that makes it.

## Versioning

The API follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). A release is numbered
`<major>.<minor>.<patch>`, and each part is measured against what API clients and operators depend
on:

- **Major** — a change an existing client or deployment has to adapt to: an endpoint, field,
  status code or message removed, renamed or changed in meaning; a configuration variable removed
  or renamed, or a new one made mandatory; a migration that needs manual steps on an existing
  database; or a breaking change to the native core's C ABI, whose header clients vendor.
- **Minor** — a backward-compatible addition: a new endpoint, an optional field or query
  parameter, a new optional configuration variable, a new native operation, or a migration that
  applies on its own.
- **Patch** — a fix that changes no contract: a bug fix, a dependency upgrade, or a change to
  logging or performance.

The version is not stored in the source. It is the name of the release branch: `release/x.y.z`
releases `vx.y.z`, Jenkins takes the number from the branch name, tags the deployed image
`x.y.z-<short sha>` (the version the yggdrasil console shows) and creates the tag `vx.y.z` when
the release merges. The Branch Policy check rejects a release branch whose version already has a
tag. Two other numbers are independent of the release: the API contract version the liveness check
reports (`v1`, the OpenAPI `info.version`), and the native crate's `version` in
`native/fortuna-core/Cargo.toml`.

## Releasing

1. Because a release branch carries no commits of its own, finalize the changelog on `develop`
   first: in a `feature/` branch, rename `## [Unreleased]` in [CHANGELOG.md](./CHANGELOG.md) to
   `## [x.y.z] - <yyyy-mm-dd>` above a fresh, empty `## [Unreleased]`, update the links at the
   bottom, and merge it into `develop`.
2. `git switch develop && git pull && git switch -c release/1.4.0 && git push -u origin release/1.4.0`
   — Jenkins deploys the branch to **homologation**.
3. Open a pull request `release/1.4.0 → main`.
4. When every GitHub check on the pull request passes, Jenkins deploys to **production**. On
   success it sets the `deploy/production` status, merges the pull request with a merge commit,
   creates the tag and GitHub release `v1.4.0`, and deletes the release branch.
5. If the production deploy fails, Jenkins rolls back to the previous image and the pull request
   stays open. Fix on `develop`, then cut a new release.

Follow a release in the **yggdrasil console** (`https://yggdrasil.<domain>`, or the Android app).
The system card shows this application's version, commit, deploy time and health in each
environment.

The repository owner can bypass these rules. That is for emergencies, not for routine work.

## Where this is deployed from

Deployment is managed by [yggdrasil](https://github.com/artur-rios/yggdrasil). This repository is
the application `fortuna-api` in its `catalog.yaml`, which is what gives it:
- its Jenkins deploy job
- its GitHub rulesets and required checks (the catalog's `checks`)
- its Prometheus scraping
- its place in the console

If a required check is renamed or added here, update the catalog entry, then run
`python github/rulesets.py fortuna-api` in yggdrasil.
