#!/usr/bin/env bash
#
# Local build / test / debug helper for the monorepo.
#
# Usage:
#   scripts/build.sh [PluginId] [options]
#
#   (no PluginId)        operate on the whole solution
#   <PluginId>           operate on a single plugin (e.g. well404.Economy)
#
# Options:
#   -c, --configuration <cfg>   Release (default) | Debug
#   -t, --test                  run the unit tests (tests/*.Tests)
#   -p, --pack                  report the produced .nupkg paths
#   -d, --deploy <dir>          publish a single plugin and copy the plugin dll +
#                               its non-host third-party deps (e.g. LiteDB.dll)
#                               into <dir>/<PluginId>/ for a local OpenMod server
#       --clean                 clean before building
#   -h, --help                  show this help
#
# Examples:
#   scripts/build.sh                              # build everything (Release)
#   scripts/build.sh --test                       # build + run tests
#   scripts/build.sh well404.Economy -c Debug     # build one plugin in Debug
#   scripts/build.sh well404.Economy -d ~/unturned/U3DS/Servers/MyServer/Rocket/Plugins
#
# Notes on --deploy:
#   * It copies ONLY the plugin and dependencies an OpenMod server does NOT already
#     ship (the host already provides OpenMod.*, Unity, Steamworks, Autofac, etc.).
#     For well404.Economy this is the plugin dll + LiteDB.dll; for well404.Shop just
#     the plugin dll.
#   * After deploying, run `openmod reload` (or restart) on the server.
#   * For a clean production install prefer `openmod install <PackageId>` instead,
#     which resolves dependencies from NuGet automatically.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SLN="$REPO_ROOT/UnturnedMods.sln"

CONFIG="Release"
DO_TEST=false
DO_PACK=false
DO_CLEAN=false
DEPLOY_DIR=""
PLUGIN_ID=""

print_help() { sed -n '2,40p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--configuration) CONFIG="${2:?}"; shift 2 ;;
    -t|--test) DO_TEST=true; shift ;;
    -p|--pack) DO_PACK=true; shift ;;
    -d|--deploy) DEPLOY_DIR="${2:?}"; shift 2 ;;
    --clean) DO_CLEAN=true; shift ;;
    -h|--help) print_help; exit 0 ;;
    -*) echo "Unknown option: $1" >&2; exit 1 ;;
    *)
      if [[ -n "$PLUGIN_ID" ]]; then echo "Unexpected argument: $1" >&2; exit 1; fi
      PLUGIN_ID="$1"; shift ;;
  esac
done

target_project() {
  if [[ -n "$PLUGIN_ID" ]]; then
    echo "$REPO_ROOT/src/plugins/$PLUGIN_ID/$PLUGIN_ID.csproj"
  else
    echo "$SLN"
  fi
}

PROJECT="$(target_project)"
if [[ -n "$PLUGIN_ID" && ! -f "$PROJECT" ]]; then
  echo "Error: no such plugin project: $PROJECT" >&2
  exit 1
fi

if $DO_CLEAN; then
  echo "==> Cleaning ($CONFIG)"
  dotnet clean "$PROJECT" -c "$CONFIG" --nologo >/dev/null
fi

echo "==> Building $(basename "$PROJECT") ($CONFIG)"
dotnet build "$PROJECT" -c "$CONFIG" --nologo

if $DO_TEST; then
  echo "==> Testing"
  dotnet test "$SLN" -c "$CONFIG" --no-build --nologo
fi

if $DO_PACK; then
  echo "==> Packages:"
  find "$REPO_ROOT/src/plugins" -path "*/bin/$CONFIG/*.nupkg" | sort | sed 's/^/    /'
fi

if [[ -n "$DEPLOY_DIR" ]]; then
  if [[ -z "$PLUGIN_ID" ]]; then
    echo "Error: --deploy requires a PluginId." >&2
    exit 1
  fi

  STAGING="$(mktemp -d)"
  trap 'rm -rf "$STAGING"' EXIT
  echo "==> Publishing $PLUGIN_ID for deploy"
  dotnet publish "$PROJECT" -c "$CONFIG" -o "$STAGING" --nologo >/dev/null

  DEST="$DEPLOY_DIR/$PLUGIN_ID"
  mkdir -p "$DEST"

  # The dependencies a server does NOT already ship are exactly this plugin's own
  # non-OpenMod PackageReferences (everything else — OpenMod, Unity, Steamworks,
  # UniTask, Autofac, ... — is part of the OpenMod Unturned host). Read them from
  # the .csproj so the copy stays correct as dependencies change.
  mapfile -t EXTRA_DEPS < <(
    grep -oE '<PackageReference Include="[^"]+"' "$PROJECT" \
      | sed -E 's/.*Include="([^"]+)".*/\1/' \
      | grep -ivE '^(OpenMod|Legacy2CPSWorkaround|Microsoft\.SourceLink)'
  )

  echo "==> Copying to $DEST"
  copied=0
  copy_one() {
    local name="$1"
    if [[ -f "$STAGING/$name.dll" ]]; then
      cp "$STAGING/$name.dll" "$DEST/"
      echo "    + $name.dll"
      copied=$((copied + 1))
    fi
  }

  copy_one "$PLUGIN_ID"
  for dep in "${EXTRA_DEPS[@]}"; do
    copy_one "$dep"
  done

  # Embedded config.yaml/translations.yaml live inside the plugin dll.
  echo "==> Deployed $copied file(s). Run 'openmod reload' on the server."
  echo "    (If a third-party dependency pulls its own sub-dependencies, copy those"
  echo "     too, or use 'openmod install <PackageId>' which resolves deps from NuGet.)"
fi

echo "==> Done."
