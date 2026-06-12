#!/usr/bin/env bash
#
# Local build / test / debug helper for the monorepo.
#
# Assembles each plugin's ready-to-deploy files — the plugin dll plus only the
# dependencies an OpenMod server does NOT already ship (e.g. LiteDB.dll) — FLAT
# into the output directory. Flat layout is required: OpenMod's plugin loader
# scans "<plugins>/*.dll" at the top level only (no subfolders), so a server's
# openmod/plugins/ folder is exactly this output dir's layout.
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
# Output: <output>/<PluginId>.dll [+ extra deps], all flat.
#
# Clean-before / prune-after: the files this run produces are removed from the
# output dir before the build (clearing the previous run's residue) and only
# target files are written after (intermediate publish output is staged in a temp
# dir and discarded). Unrelated files in the output dir are left untouched, so
# pointing --deploy at a server's plugins/ folder only refreshes THIS repo's
# plugins. The default build/ dir additionally has its loose *.dll cleared on a
# full (all-plugins) build; the version-controlled templates/ is never touched.
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

print_help() { sed -n '2,46p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

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

IS_DEFAULT_OUTPUT=false
if [[ -z "$OUTPUT" ]]; then OUTPUT="$REPO_ROOT/build"; IS_DEFAULT_OUTPUT=true; fi

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
  [[ ${#PLUGINS[@]} -eq 0 ]] && { echo "Error: no plugins found under src/plugins/." >&2; exit 1; }
fi

if $DO_CLEAN; then
  echo "==> Cleaning bin/obj ($CONFIG)"
  dotnet clean "$SLN" -c "$CONFIG" --nologo >/dev/null
fi

if $DO_TEST; then
  echo "==> Testing"
  dotnet test "$SLN" -c "$CONFIG" --nologo
fi

mkdir -p "$OUTPUT"
# On a full build into the default build/ dir, clear all prior artifacts (loose
# dlls and any stale subfolders) but never the version-controlled templates/.
if $IS_DEFAULT_OUTPUT && [[ -z "$PLUGIN_ID" ]]; then
  find "$OUTPUT" -mindepth 1 -maxdepth 1 ! -name templates -exec rm -rf {} +
fi

# A plugin's non-host third-party deps = its own PackageReferences minus anything
# the OpenMod Unturned host already provides. These ship next to the plugin dll.
extra_deps() {
  grep -oE '<PackageReference Include="[^"]+"' "$1" \
    | sed -E 's/.*Include="([^"]+)".*/\1/' \
    | grep -ivE '^(OpenMod|Legacy2CPSWorkaround|Microsoft\.SourceLink)' || true
}

# A plugin's in-repo ProjectReferences (e.g. UnturnedMods.Shared) also need their
# compiled dll deployed flat next to the plugin. By convention AssemblyName == the
# project file name, so the dll basename is the .csproj name without its extension.
project_deps() {
  grep -oE '<ProjectReference Include="[^"]+"' "$1" \
    | sed -E 's/.*Include="([^"]+)".*/\1/' \
    | sed -E 's#.*[\\/]##; s#\.csproj$##' || true
}

# Force fresh version stamps. A stale obj/ from a prior <Version> can otherwise stamp
# the deployed dll with an old version (the assembly's Version/InformationalVersion),
# which then shows up wrong in OpenMod's "[loading] <name> v<version>" logs even though
# the package version is correct. Dropping the GenerateAssemblyInfo input cache forces
# the stamps to be re-derived from the current <Version> + git HEAD on the next build;
# thanks to WriteOnlyWhenDifferent it only rewrites (and recompiles) when actually stale.
# CI is unaffected — it always builds from a fresh checkout with no obj/.
find "$REPO_ROOT/src" -path "*/obj/$CONFIG/*" -name "*.AssemblyInfoInputs.cache" -delete 2>/dev/null || true

echo "==> Output (flat): $OUTPUT"
declare -A WRITTEN=()
for id in "${PLUGINS[@]}"; do
  proj="$REPO_ROOT/src/plugins/$id/$id.csproj"
  staging="$(mktemp -d)"

  echo "==> Building $id ($CONFIG)"
  dotnet publish "$proj" -c "$CONFIG" -o "$staging" --nologo >/dev/null

  copy_flat() {
    local name="$1"
    [[ -f "$staging/$name.dll" ]] || return 0
    [[ -n "${WRITTEN[$name]:-}" ]] && return 0      # dedup shared deps (e.g. LiteDB)
    rm -f "$OUTPUT/$name.dll"                        # clean previous residue (safe: only our files)
    cp "$staging/$name.dll" "$OUTPUT/"
    WRITTEN[$name]=1
    echo "    + $name.dll"
  }

  copy_flat "$id"
  while IFS= read -r dep; do
    [[ -n "$dep" ]] && copy_flat "$dep"
  done < <(extra_deps "$proj")
  while IFS= read -r dep; do
    [[ -n "$dep" ]] && copy_flat "$dep"
  done < <(project_deps "$proj")

  rm -rf "$staging"
done

if $DO_PACK; then
  echo "==> Packages (.nupkg in each plugin's bin/$CONFIG):"
  find "$REPO_ROOT/src/plugins" -path "*/bin/$CONFIG/*.nupkg" | sort | sed 's/^/    /'
fi

echo "==> Done. ${#WRITTEN[@]} file(s) in: $OUTPUT"
echo "    Deploy by copying the dll(s) into the server's openmod/plugins/ (flat),"
echo "    then run 'openmod reload'. (If a third-party dep has its own sub-deps, copy"
echo "    those too, or use 'openmod install <PackageId>'.)"
