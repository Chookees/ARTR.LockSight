#!/usr/bin/env bash
# Runs ARTR.LockSight from this action's own source tree.
# The job must already have the .NET 10 SDK (actions/setup-dotnet).
set -euo pipefail

if ! command -v dotnet >/dev/null 2>&1; then
  echo "::error::.NET SDK 10 is required. Add actions/setup-dotnet with dotnet-version: 10.0.x before this action."
  exit 2
fi

action_path="${LOCKSIGHT_ACTION_PATH:?LOCKSIGHT_ACTION_PATH is not set}"
project="$action_path/src/ARTR.LockSight/ARTR.LockSight.csproj"
if [[ ! -f "$project" ]]; then
  echo "::error::Could not find the ARTR.LockSight project at $project"
  exit 2
fi

target="${LOCKSIGHT_PATH:-.}"
command_name="${LOCKSIGHT_COMMAND:-drift}"
strict="${LOCKSIGHT_STRICT:-false}"

args=()
case "$command_name" in
  drift|scan)
    args+=(drift "$target" --ci --format text)
    ;;
  explain|why)
    args+=(explain)
    ;;
  *)
    echo "::error::Unsupported command '$command_name'. Use drift or explain."
    exit 2
    ;;
esac

if [[ "$strict" == "true" || "$strict" == "True" ]]; then
  args+=(--strict)
fi

dotnet run --project "$project" -c Release --no-launch-profile -- "${args[@]}"
