#!/usr/bin/env bash
#
# Get or set a plugin's <Version> (the single source of truth for releases).
#
# Usage:
#   scripts/plugin-version.sh <PluginId>              # print current version
#   scripts/plugin-version.sh <PluginId> <newVersion> # set version
#
# Examples:
#   scripts/plugin-version.sh well404.Economy
#   scripts/plugin-version.sh well404.Economy 0.2.0
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <PluginId> [newVersion]" >&2
  exit 1
fi

PLUGIN_ID="$1"
if [[ "$PLUGIN_ID" == "well404.UnturnedMods.Shared" ]]; then
  CSPROJ="$REPO_ROOT/src/Shared/UnturnedMods.Shared/UnturnedMods.Shared.csproj"
else
  CSPROJ="$REPO_ROOT/src/plugins/$PLUGIN_ID/$PLUGIN_ID.csproj"
fi

if [[ ! -f "$CSPROJ" ]]; then
  echo "Error: no such plugin project: $CSPROJ" >&2
  exit 1
fi

current_version() {
  grep -oP '(?<=<Version>)[^<]+' "$CSPROJ" | head -1
}

if [[ $# -eq 1 ]]; then
  current_version
  exit 0
fi

NEW_VERSION="$2"
# Loosely validate SemVer (major.minor.patch with optional pre-release/build).
if ! [[ "$NEW_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-+].+)?$ ]]; then
  echo "Error: '$NEW_VERSION' is not a valid SemVer (e.g. 1.2.3)." >&2
  exit 1
fi

OLD_VERSION="$(current_version)"
sed -i -E "s#<Version>[^<]+</Version>#<Version>${NEW_VERSION}</Version>#" "$CSPROJ"
echo "Set $PLUGIN_ID version: ${OLD_VERSION} -> ${NEW_VERSION}"
echo "Next: commit the bump, then run scripts/release-plugin.sh $PLUGIN_ID"
