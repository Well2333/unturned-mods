#!/usr/bin/env bash
#
# Cut a release for a single plugin. Reads the plugin's <Version>, creates the
# tag "<PluginId>/v<Version>" and a matching GitHub Release, which triggers the
# publish workflow to build, pack and push ONLY that plugin to NuGet.
#
# Usage:
#   scripts/release-plugin.sh <PluginId> [--notes "release notes"]
#
# Requirements:
#   - clean working tree (the version bump should already be committed)
#   - gh CLI authenticated; NuGet Trusted Publishing configured for publish.yml
#
# Example:
#   scripts/plugin-version.sh well404.Economy 0.2.0
#   git commit -am "release(well404.Economy): 0.2.0"
#   scripts/release-plugin.sh well404.Economy --notes "Add /pay tax option"
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <PluginId> [--notes \"...\"]" >&2
  exit 1
fi

PLUGIN_ID="$1"
shift
case "$PLUGIN_ID" in
  well404.AdminTools|well404.AutoSave|well404.Economy|well404.Essentials|well404.Shop|well404.Vault|well404.WebPanel|well404.UnturnedMods.Shared) ;;
  *) echo "Error: plugin is not in the release allowlist: $PLUGIN_ID" >&2; exit 1 ;;
esac
NOTES=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --notes) NOTES="${2:-}"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

if [[ "$PLUGIN_ID" == "well404.UnturnedMods.Shared" ]]; then
  CSPROJ="$REPO_ROOT/src/Shared/UnturnedMods.Shared/UnturnedMods.Shared.csproj"
else
  CSPROJ="$REPO_ROOT/src/plugins/$PLUGIN_ID/$PLUGIN_ID.csproj"
fi
if [[ ! -f "$CSPROJ" ]]; then
  echo "Error: no such plugin project: $CSPROJ" >&2
  exit 1
fi

if ! command -v gh >/dev/null 2>&1; then
  echo "Error: gh CLI is required." >&2
  exit 1
fi

if [[ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]]; then
  echo "Error: working tree is not clean. Commit the version bump first." >&2
  exit 1
fi

if [[ "$(git -C "$REPO_ROOT" branch --show-current)" != "main" ]]; then
  echo "Error: releases may only be created from the local main branch." >&2
  exit 1
fi

git -C "$REPO_ROOT" fetch origin main --no-tags
LOCAL_HEAD="$(git -C "$REPO_ROOT" rev-parse HEAD)"
REMOTE_HEAD="$(git -C "$REPO_ROOT" rev-parse origin/main)"
if [[ "$LOCAL_HEAD" != "$REMOTE_HEAD" ]]; then
  echo "Error: local main must exactly match origin/main before release." >&2
  exit 1
fi

VERSION="$("$REPO_ROOT/scripts/plugin-version.sh" "$PLUGIN_ID")"
TAG="${PLUGIN_ID}/v${VERSION}"

if git -C "$REPO_ROOT" rev-parse "$TAG" >/dev/null 2>&1; then
  echo "Error: tag '$TAG' already exists. Bump the version first." >&2
  exit 1
fi

echo "Releasing $PLUGIN_ID $VERSION (tag: $TAG)"
[[ -z "$NOTES" ]] && NOTES="Release $PLUGIN_ID $VERSION"

gh release create "$TAG" \
  --repo Well2333/unturned-mods \
  --title "$PLUGIN_ID $VERSION" \
  --notes "$NOTES" \
  --target "$LOCAL_HEAD"

echo "Created release $TAG. The publish workflow will pack & push $PLUGIN_ID to NuGet."
