# ARTR.LockSight

SPDX-License-Identifier: Apache-2.0

Local CLI doctor for NuGet `packages.lock.json` drift and `NU1004` / `RestoreLockedMode` failures.

This is **not** Dependabot and **not** “just run restore.” It explains why locked restore fails and scans your tree for pin-drift, multi-TFM mismatches, and ProjectReference-related lock issues — on your machine or in CI.

## Requirements

- .NET SDK 10 (`net10.0`)

## Install

### From a local pack (dogfood / this repo)

```bash
dotnet pack src/ARTR.LockSight/ARTR.LockSight.csproj -c Release -o ./artifacts
dotnet tool install ARTR.LockSight --add-source ./artifacts --version 0.1.0
# or, for a repo-local manifest:
dotnet new tool-manifest --force
dotnet tool install ARTR.LockSight --add-source ./artifacts --version 0.1.0
```

Tool command name: **`artr-locksight`**

### After publish to NuGet.org

```bash
dotnet tool install -g ARTR.LockSight
```

## Usage

### `drift` — find lockfile drift

Scans a solution, project, or directory for lockfile problems and prints a table (project / TFM / package / reason / detail).

```bash
artr-locksight drift
artr-locksight drift ./src
artr-locksight drift path/to/App.csproj
```

Reasons covered in the MVP:

| Reason | Meaning |
|--------|---------|
| `PinDrift` | PackageReference / CPM pin does not match the lockfile |
| `MultiTfmMismatch` | TFM missing from the lockfile, or resolved versions disagree across TFMs |
| `ProjectReferenceLockIssue` | A referenced project’s lockfile is missing / path broken |
| `MissingLockfile` | Lockfiles are enabled but `packages.lock.json` is absent |

### `explain` / `why` — NU1004 guide

```bash
artr-locksight explain
artr-locksight why
artr-locksight explain nu1004
```

### `--fix` / `fix` — regenerate lockfiles

Wraps `dotnet restore --force-evaluate` with clear output. Review and commit updated lockfiles afterward.

```bash
artr-locksight fix
artr-locksight --fix ./My.sln
artr-locksight drift ./src --fix
```

### `--ci` — pipelines

Non-zero exit when drift is found; prints GitHub Actions–style `::error` lines.

```bash
artr-locksight drift --ci
artr-locksight drift ./src --ci
# exit code 1 if findings exist
```

## Dogfood note

This repository itself is a good smoke target after install:

```bash
dotnet build
dotnet test
dotnet pack src/ARTR.LockSight/ARTR.LockSight.csproj -c Release -o ./artifacts
dotnet tool install ARTR.LockSight --add-source ./artifacts --version 0.1.0 --tool-path ./tools
./tools/artr-locksight explain
./tools/artr-locksight drift .
```

`drift` against this repo will usually report “no lockfile drift” until you enable `RestorePackagesWithLockFile` / add `packages.lock.json`. To exercise detection without network restore fixtures, use the unit tests under `tests/ARTR.LockSight.Tests` (temp projects + synthetic lockfiles).

## Manual verification (restore / fix)

`fix` needs network access to NuGet feeds. On a machine with feeds available:

```bash
artr-locksight fix path/to/solution-or-project
```

Heavy integration tests that call restore are intentionally skipped in CI-less cloud agents; prefer the unit tests for parsing/reporting.

## Phase 2 (not in this MVP)

- Polished GitHub Action wrapper
- Extra `diff` / `scan` polish

## License

Apache-2.0 — see [LICENSE](LICENSE).
