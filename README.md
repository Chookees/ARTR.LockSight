# ARTR.LockSight

SPDX-License-Identifier: Apache-2.0

Local CLI doctor for NuGet `packages.lock.json` and `NU1004` / `RestoreLockedMode` failures.

This is **not** Dependabot and **not** “just run restore.” It tells you why a locked restore would fail: pin ranges, missing TFMs, project-reference lockfiles, orphan direct packages, and broken lock rows. It runs on your machine or in CI, with no network, until you ask it to regenerate lockfiles.

## Requirements

- .NET SDK 10 (`net10.0`)

## Install

Tool command: **`artr-locksight`**

### Local pack (this repo)

```bash
dotnet pack src/ARTR.LockSight/ARTR.LockSight.csproj -c Release -o ./artifacts
dotnet tool install ARTR.LockSight --add-source ./artifacts --version 1.0.0 --tool-path ./tools
./tools/artr-locksight --version
```

### After the package is on NuGet.org

```bash
dotnet tool install -g ARTR.LockSight
```

A repo-local manifest works the same way: `dotnet new tool-manifest` then `dotnet tool install` with `--add-source`.

## Commands

### `drift` (alias `scan`)

Scans a directory, a project, or a `.sln` / `.slnx`. Prints one row per project, TFM, package, and reason.

```bash
artr-locksight drift
artr-locksight drift ./src
artr-locksight drift ARTR.LockSight.slnx
artr-locksight drift path/to/App.csproj --format json
```

| Reason | Severity | Meaning |
|--------|----------|---------|
| `PinDrift` | error | PackageReference / VersionOverride / CPM pin does not match the lockfile range, or the resolved version falls outside the pin |
| `MultiTfmMismatch` | error | A project TFM has no section in `packages.lock.json` |
| `ProjectReferenceLockIssue` | error | A project reference is missing, or that project expects a lockfile and does not have one |
| `MissingLockfile` | error | Lockfiles or `RestoreLockedMode` are on, and `packages.lock.json` is absent |
| `OrphanLockEntry` | error | The lockfile still has a Direct package the project no longer references |
| `LockfileInconsistent` | error | A package row has no `contentHash`, or a transitive resolved version is outside its requested range |
| `UnreadableInput` | error | A project or lockfile could not be parsed |
| `MultiTfmVersionSkew` | warning | The same direct package resolved to different versions across TFMs |
| `StaleTfm` | warning | The lockfile still has a TFM the project does not target |

A bare version such as `13.0.3` is the NuGet range `[13.0.3, )`. A resolved version inside that range is not drift. The lockfile `requested` range has to match the pin.

The scanner reads the nearest `Directory.Build.props`, `Directory.Build.targets`, and `Directory.Packages.props`. It does not run a full MSBuild evaluation. Conditions are honored only when they mention `TargetFramework` and quote a TFM.

### `explain` / `why`

```bash
artr-locksight explain
artr-locksight why nu1004
```

### `diff`

Compares two lockfiles, two projects, or two directories. Nothing is restored.

```bash
artr-locksight diff ./before/packages.lock.json ./after/packages.lock.json
artr-locksight diff ./left-tree ./right-tree --ci
```

### `fix` / `--fix`

Wraps `dotnet restore --force-evaluate` and prints the command before it runs.

```bash
artr-locksight fix ./My.sln
artr-locksight --fix .
artr-locksight drift . --fix
```

Needs access to your NuGet feeds. Review and commit the updated lockfiles.

### `--ci` and `--strict`

```bash
artr-locksight drift . --ci
artr-locksight drift . --ci --strict
```

| Exit | When |
|------|------|
| 0 | No blocking findings. Warnings alone stay 0. |
| 1 | `--ci` and at least one error (or any warning when `--strict`) |
| 2 | Bad arguments, missing path, or a runtime failure |

`--ci` also prints GitHub Actions `::error` and `::warning` lines. `--format json` keeps JSON on stdout and moves those annotations to stderr.

## GitHub Action

The repository root is a composite action. The job needs the .NET 10 SDK first.

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: 10.0.x
- uses: Chookees/ARTR.LockSight@main
  with:
    path: Your.sln
    strict: "false"
```

From this repo, `uses: ./` runs the same action. `command: explain` prints the NU1004 guide instead of failing on drift.

## Dogfood

This repo enables `RestorePackagesWithLockFile` and `RestoreLockedMode` in `Directory.Build.props` and commits `packages.lock.json`.

```bash
dotnet test
dotnet restore --locked-mode
dotnet run --project src/ARTR.LockSight/ARTR.LockSight.csproj -- drift ARTR.LockSight.slnx --ci
dotnet run --project src/ARTR.LockSight/ARTR.LockSight.csproj -- explain
```

Unit tests under `tests/ARTR.LockSight.Tests` cover parsing and reporting with temp projects, so they do not need a NuGet feed. `fix` is the one command that talks to the network; run it against a solution when you want to regenerate lockfiles.

## License

Apache-2.0 — see [LICENSE](LICENSE).
