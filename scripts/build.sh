#!/usr/bin/env bash
#
# Local build / test / debug helper for the monorepo.
#
# Assembles each plugin's ready-to-deploy artifact set — the plugin dll plus only
# the dependencies an OpenMod server does NOT already ship (e.g. LiteDB.dll) — into
# a per-plugin folder under the output directory.
#
# Usage:
#   scripts/build.sh [PluginId] [options]
#
#   (no PluginId)        build every plugin
#   <PluginId>           build a single plugin (e.g. well404.Economy)
#
# Options:
#   -c, --configuration <cfg>   Release (default) | Debug
#   -t, --test                  run the unit tests (tests/*.Tests)
#   -p, --pack                  also report the produced .nupkg paths
#   -o, --output <dir>          output directory (default: <repo>/build)
#   -d, --deploy <dir>          alias for --output; e.g. a local server's
#                               openmod/plugins directory
#       --clean                 also `dotnet clean` (bin/obj) before building
#   -h, --help                  show this help
#
# Output layout:  <output>/<PluginId>/<PluginId>.dll [+ extra deps]
#
# The output folder for each built plugin is cleared BEFORE the build (removing
# the previous run's residue) and only target files are placed in it AFTER
# (intermediate publish output is staged in a temp dir and discarded). The default
# build/ directory keeps only the version-controlled `templates/` plus freshly
# built plugin folders.
#
# Examples:
#   scripts/build.sh                              # all plugins -> build/
#   scripts/build.sh --test                       # build all + run tests
#   scripts/build.sh well404.Economy -c Debug     # one plugin (Debug) -> build/
#   scripts/build.sh well404.Economy -d ~/server/openmod/plugins
#
# For a clean production install prefer `openmod install <PackageId>`, which
# resolves dependencies from NuGet automatically.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SLN="$REPO_ROOT/UnturnedMods.sln"

CONFIG="Release"
DO_TEST=false
DO_PACK=false
DO_CLEAN=false
OUTPUT=""
PLUGIN_ID=""

print_help() { sed -n '2,45p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--configuration) CONFIG="${2:?}"; shift 2 ;;
    -t|--test) DO_TEST=true; shift ;;
    -p|--pack) DO_PACK=true; shift ;;
    -o|--output|-d|--deploy) OUTPUT="${2:?}"; shift 2 ;;
    --clean) DO_CLEAN=true; shift ;;
    -h|--help) print_help; exit 0 ;;
    -*) echo "Unknown option: $1" >&2; exit 1 ;;
    *)
      if [[ -n "$PLUGIN_ID" ]]; then echo "Unexpected argument: $1" >&2; exit 1; fi
      PLUGIN_ID="$1"; shift ;;
  esac
done

# Default output is the repo's build/ directory.
[[ -z "$OUTPUT" ]] && OUTPUT="$REPO_ROOT/build"

# Resolve the list of plugins to build.
declare -a PLUGINS=()
if [[ -n "$PLUGIN_ID" ]]; then
  if [[ ! -f "$REPO_ROOT/src/plugins/$PLUGIN_ID/$PLUGIN_ID.csproj" ]]; then
    echo "Error: no such plugin project: src/plugins/$PLUGIN_ID/$PLUGIN_ID.csproj" >&2
    exit 1
  fi
  PLUGINS+=("$PLUGIN_ID")
else
  for dir in "$REPO_ROOT"/src/plugins/*/; do
    id="$(basename "$dir")"
    [[ -f "$dir/$id.csproj" ]] && PLUGINS+=("$id")
  done
  if [[ ${#PLUGINS[@]} -eq 0 ]]; then
    echo "Error: no plugins found under src/plugins/." >&2
    exit 1
  fi
fi

if $DO_CLEAN; then
  echo "==> Cleaning bin/obj ($CONFIG)"
  dotnet clean "$SLN" -c "$CONFIG" --nologo >/dev/null
fi

if $DO_TEST; then
  echo "==> Testing"
  dotnet test "$SLN" -c "$CONFIG" --nologo
fi

# Returns the plugin's non-host third-party deps (its own PackageReferences minus
# anything an OpenMod Unturned server already provides). These are the only deps
# that need to ship next to the plugin dll.
extra_deps() {
  local proj="$1"
  grep -oE '<PackageReference Include="[^"]+"' "$proj" \
    | sed -E 's/.*Include="([^"]+)".*/\1/' \
    | grep -ivE '^(OpenMod|Legacy2CPSWorkaround|Microsoft\.SourceLink)' || true
}

echo "==> Output: $OUTPUT"
for id in "${PLUGINS[@]}"; do
  proj="$REPO_ROOT/src/plugins/$id/$id.csproj"
  staging="$(mktemp -d)"
  dest="$OUTPUT/$id"

  echo "==> Building $id ($CONFIG)"
  dotnet publish "$proj" -c "$CONFIG" -o "$staging" --nologo >/dev/null

  # Clean this plugin's previous output, then place only target files.
  rm -rf "$dest"
  mkdir -p "$dest"

  copied=0
  copy_one() {
    local name="$1"
    if [[ -f "$staging/$name.dll" ]]; then
      cp "$staging/$name.dll" "$dest/"
      echo "    + $id/$name.dll"
      copied=$((copied + 1))
    fi
  }

  copy_one "$id"
  while IFS= read -r dep; do
    [[ -n "$dep" ]] && copy_one "$dep"
  done < <(extra_deps "$proj")

  rm -rf "$staging"
  echo "    -> $copied file(s) in $dest"
done

if $DO_PACK; then
  echo "==> Packages (.nupkg in each plugin's bin/$CONFIG):"
  find "$REPO_ROOT/src/plugins" -path "*/bin/$CONFIG/*.nupkg" | sort | sed 's/^/    /'
fi

echo "==> Done. Artifacts in: $OUTPUT"
echo "    Deploy by copying <output>/<PluginId>/ into the server's openmod/plugins/,"
echo "    then run 'openmod reload'. (If a third-party dep has its own sub-deps, copy"
echo "    those too, or use 'openmod install <PackageId>'.)"
