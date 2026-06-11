#!/usr/bin/env bash
#
# Scaffold a new OpenMod Unturned plugin into src/plugins/<PluginId>/ from the
# templates in build/templates/plugin, then add it to the solution.
#
# Usage:
#   scripts/new-plugin.sh <PluginId> ["Display Name"]
#
# Example:
#   scripts/new-plugin.sh well404.AutoMessage "Auto Message"
#
# Conventions enforced:
#   - The project file is <PluginId>.csproj, so RootNamespace == AssemblyName
#     == <PluginId> automatically (required by OpenMod for config/localization).
#   - The plugin class is "<LastSegment>Plugin".
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEMPLATE_DIR="$REPO_ROOT/build/templates/plugin"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <PluginId> [\"Display Name\"]" >&2
  exit 1
fi

PLUGIN_ID="$1"
DISPLAY_NAME="${2:-$PLUGIN_ID}"

# Validate the id: dot-separated alphanumeric segments (e.g. well404.AutoMessage).
if ! [[ "$PLUGIN_ID" =~ ^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*$ ]]; then
  echo "Error: PluginId must be dot-separated alphanumeric segments, e.g. well404.AutoMessage" >&2
  exit 1
fi

LAST_SEGMENT="${PLUGIN_ID##*.}"
PLUGIN_CLASS="${LAST_SEGMENT}Plugin"

DEST_DIR="$REPO_ROOT/src/plugins/$PLUGIN_ID"
if [[ -e "$DEST_DIR" ]]; then
  echo "Error: $DEST_DIR already exists." >&2
  exit 1
fi

mkdir -p "$DEST_DIR"

substitute() {
  sed -e "s/__PLUGIN_ID__/${PLUGIN_ID}/g" \
      -e "s/__PLUGIN_CLASS__/${PLUGIN_CLASS}/g" \
      -e "s/__PLUGIN_DISPLAY_NAME__/${DISPLAY_NAME}/g"
}

substitute < "$TEMPLATE_DIR/Plugin.csproj.template"        > "$DEST_DIR/$PLUGIN_ID.csproj"
substitute < "$TEMPLATE_DIR/Plugin.cs.template"            > "$DEST_DIR/$PLUGIN_CLASS.cs"
substitute < "$TEMPLATE_DIR/config.yaml.template"          > "$DEST_DIR/config.yaml"
substitute < "$TEMPLATE_DIR/translations.yaml.template"    > "$DEST_DIR/translations.yaml"
# Per-plugin README.md is packed as the NuGet package's readme (see Directory.Build.props).
# Keep it in sync with the plugin's user-facing features on every change.
substitute < "$TEMPLATE_DIR/README.md.template"            > "$DEST_DIR/README.md"

echo "Created plugin '$PLUGIN_ID' (class $PLUGIN_CLASS) at $DEST_DIR"

# Add to the solution if the SDK is available.
SLN="$REPO_ROOT/UnturnedMods.sln"
if command -v dotnet >/dev/null 2>&1 && [[ -f "$SLN" ]]; then
  if dotnet sln "$SLN" add "$DEST_DIR/$PLUGIN_ID.csproj" 2>/dev/null; then
    echo "Added to UnturnedMods.sln"
  else
    echo "NOTE: could not add to solution automatically; run:" >&2
    echo "  dotnet sln UnturnedMods.sln add \"$DEST_DIR/$PLUGIN_ID.csproj\"" >&2
  fi
else
  echo "NOTE: dotnet SDK not found. Add to the solution later with:" >&2
  echo "  dotnet sln UnturnedMods.sln add \"$DEST_DIR/$PLUGIN_ID.csproj\"" >&2
fi

echo "Done. Remember to write a memory/changelog entry for this change before committing."
