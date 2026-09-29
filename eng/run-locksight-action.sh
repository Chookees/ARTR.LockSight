#!/usr/bin/env bash
# Packs this action's source, installs the tool, and runs it.
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
evaluate="${LOCKSIGHT_EVALUATE:-true}"

args=()
case "$command_name" in
  drift|scan)
    args+=(drift "$target" --ci --format text)
    if [[ "$evaluate" == "true" || "$evaluate" == "True" ]]; then
      args+=(--evaluate)
    fi
    ;;
  verify)
    args+=(verify "$target" --ci)
    ;;
  explain|why)
    args+=(explain)
    ;;
  *)
    echo "::error::Unsupported command '$command_name'. Use drift, verify, or explain."
    exit 2
    ;;
esac

if [[ "$strict" == "true" || "$strict" == "True" ]]; then
  args+=(--strict)
fi

pack_dir="$(mktemp -d)"
tool_dir="$(mktemp -d)"
config_file="$(mktemp)"
cleanup() {
  rm -rf "$pack_dir" "$tool_dir" "$config_file"
}
trap cleanup EXIT

dotnet pack "$project" -c Release -o "$pack_dir" --nologo
shopt -s nullglob
nupkgs=( "$pack_dir"/ARTR.LockSight.*.nupkg )
if [[ ${#nupkgs[@]} -eq 0 ]]; then
  echo "::error::dotnet pack did not produce an ARTR.LockSight nupkg."
  exit 2
fi
nupkg="${nupkgs[0]}"

base="$(basename "$nupkg" .nupkg)"
version="${base#ARTR.LockSight.}"
cat > "$config_file" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="locksight-pack" value="$pack_dir" />
  </packageSources>
</configuration>
EOF

dotnet tool install ARTR.LockSight --tool-path "$tool_dir" --configfile "$config_file" --version "$version"
"$tool_dir/artr-locksight" "${args[@]}"
