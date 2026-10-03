# ARTR.LockSight

SPDX-License-Identifier: Apache-2.0

Local CLI doctor for NuGet `packages.lock.json` and `NU1004` / `RestoreLockedMode` failures.

This is **not** Dependabot and **not** “just run restore.” The static scan tells you why a locked restore would fail: pin ranges, missing TFMs, project-reference lockfiles, orphan direct packages, and broken lock rows. `verify` runs `dotnet restore --locked-mode` so NuGet is the pass/fail signal and the static scan explains the project NuGet named.

## Requirements

- .NET SDK 10 (`net10.0`)

Version **1.1.0** is not published to NuGet.org. Until this release-hygiene work merges and a `v1.1.0` tag is cut on that merge, the tagged GitHub Release is still [v1.0.0](https://github.com/Chookees/ARTR.LockSight/releases/tag/v1.0.0) (nupkg attached there).

## Install

Tool command: **`artr-locksight`**

### Local pack (this repo)

```bash
dotnet pack src/ARTR.LockSight/ARTR.LockSight.csproj -c Release -o ./artifacts
dotnet tool install ARTR.LockSight --add-source ./artifacts --version 1.1.0 --tool-path ./tools
./tools/artr-locksight --version
```

### From the v1.0.0 release asset

```bash
dotnet tool install ARTR.LockSight --add-source /path/to/downloaded --version 1.0.0 --tool-path ./tools
```

A repo-local manifest works the same way: `dotnet new tool-manifest` then `dotnet tool install` with `--add-source`.

## Commands

### `drift` (alias `scan`)

Scans a directory, a project, a `.sln` / `.slnx`, or a `.slnf` solution filter. A filter lists only its projects; paths are relative to the solution directory. Prints one row per project, TFM, package, and reason.

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
| `LockfilePathUnresolved` | warning | `NuGetLockFilePath` contains a `$(...)` this tool cannot expand. The scan falls back to `packages.lock.json` beside the project |
| `LockedRestoreFailed` | error | `verify` / `--evaluate` saw a failed locked restore (usually NU1004) |

A bare version such as `13.0.3` is the NuGet range `[13.0.3, )`. A resolved version inside that range is not drift. The lockfile `requested` range has to match the pin.

The scanner reads the nearest `Directory.Build.props`, `Directory.Build.targets`, and `Directory.Packages.props`. It does not run a full MSBuild evaluation. Conditions are honored only when they mention `TargetFramework` and quote a TFM.

`NuGetLockFilePath` may use `$(MSBuildProjectDirectory)`, `$(MSBuildProjectName)`, and `$(MSBuildThisFileDirectory)`. Any other `$(...)` is a warning, and the scan reads `packages.lock.json` next to the project.

### `explain` / `why`

```bash
artr-locksight explain
artr-locksight why nu1004
```

### `verify` / `drift --evaluate`

Runs `dotnet restore --locked-mode`. Exit 0 means NuGet accepted the lockfiles. A failed restore exits with NuGet's code even without `--ci`, and the report attaches NU1004 plus static findings for that project. If restore succeeds, leftover static findings are printed and do not fail the run. Needs your NuGet feeds (or a warm cache).

```bash
artr-locksight verify ./My.sln
artr-locksight drift ./My.sln --evaluate --format json
```

### `diff`

Compares two lockfiles, two projects, or two directories. Nothing is restored. `diff --git` compares the working tree with a git revision (default `HEAD`).

```bash
artr-locksight diff ./before/packages.lock.json ./after/packages.lock.json
artr-locksight diff ./left-tree ./right-tree --ci
artr-locksight diff --git . --revision HEAD
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
| 0 | No blocking findings, or `verify` and NuGet accepted the lockfiles. Warnings alone stay 0. |
| 1 | `--ci` and at least one static error (or any warning when `--strict`), or a failed locked restore |
| 2 | Bad arguments, missing path, or a runtime failure |

`--ci` also prints GitHub Actions `::error` and `::warning` lines. `--format json` keeps JSON on stdout and moves those annotations to stderr.

## GitHub Action

The repository root is a composite action. The job needs the .NET 10 SDK first.

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: 10.0.x
- uses: Chookees/ARTR.LockSight@v1.1.0
  with:
    path: Your.sln
    strict: "false"
    evaluate: "false"
```

The action default is `evaluate: true`: drift plus locked restore (`drift --ci --evaluate`). The first consumer wire must set `evaluate: "false"` so the job runs the static drift scan with `--ci` and skips restore. That is still a failing gate when drift finds blocking issues; it is not a report-only mode.

`command: verify` is the same NuGet check as evaluate. `command: explain` prints the NU1004 guide. From this repo, `uses: ./` runs the action in the checkout. Pin examples at `@v1.1.0` so they match the tool Version once that tag exists; until then the published tag remains `v1.0.0`.

## Dogfood

This repo enables `RestorePackagesWithLockFile` and `RestoreLockedMode` in `Directory.Build.props` and commits `packages.lock.json`.

```bash
dotnet test
dotnet restore --locked-mode
dotnet run --project src/ARTR.LockSight/ARTR.LockSight.csproj -- verify ARTR.LockSight.slnx
dotnet run --project src/ARTR.LockSight/ARTR.LockSight.csproj -- drift ARTR.LockSight.slnx --ci
dotnet run --project src/ARTR.LockSight/ARTR.LockSight.csproj -- explain
```

Unit tests under `tests/ARTR.LockSight.Tests` cover parsing and reporting with temp projects, so they do not call `dotnet restore`. `verify`, `drift --evaluate`, and `fix` talk to NuGet.

## License

Apache-2.0 — see [LICENSE](LICENSE).
