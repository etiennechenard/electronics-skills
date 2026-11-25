#!/usr/bin/env bash
# Micro-Cap 12 batch runs on macOS (or Linux) through Wine.
#
#   mc12-mac.sh winpath <absolute host path>
#       Print the Windows path Micro-Cap sees for a host file (Wine maps the
#       host root to drive Z:), e.g. /Users/me/sim/rc.cir -> Z:\Users\me\sim\rc.cir.
#       Use it for every path written inside a control file.
#
#   mc12-mac.sh run <control file> <expected output file> [timeout seconds, default 60]
#       Launch mc12_64.exe @<control file> under Wine, poll for the output file
#       (host path, e.g. /Users/me/sim/rc_out.ANO), wait 2 s for it to finish
#       writing, then kill Micro-Cap (it never exits on its own).
#       Exit 0 = output found, 1 = timed out, 2 = usage / Wine not found.
#
#   mc12-mac.sh kill
#       Kill any leftover Micro-Cap process from an earlier run.
#
# Environment:
#   MC12_WINE       Wine binary (default: wine or wine64 on PATH, then the Gcenx
#                   Wine Crossover / Wine Stable / Devel / Staging / CrossOver app bundles)
#   MC12_WINEPREFIX Wine prefix holding the install (default ~/.wine-mc12;
#                   ignored with CrossOver, which uses MC12_BOTTLE)
#   MC12_BOTTLE     CrossOver bottle name (default MC12) when the Wine found is CrossOver's
#   MC12_EXE        Windows path of the executable (default C:\MC12\mc12_64.exe;
#                   C:\MC12\mc12.exe is the 32-bit build if the 64-bit one misbehaves under Wine)
set -u

MC12_EXE="${MC12_EXE:-C:\\MC12\\mc12_64.exe}"
MC12_WINEPREFIX="${MC12_WINEPREFIX:-$HOME/.wine-mc12}"
MC12_BOTTLE="${MC12_BOTTLE:-MC12}"

winpath() {
  local p="$1"
  case "$p" in
    /*) ;;
    *) echo "winpath: absolute path required: $p" >&2; return 2 ;;
  esac
  printf 'Z:%s\n' "$(printf '%s' "$p" | tr '/' '\\')"
}

find_wine() {
  if [ -n "${MC12_WINE:-}" ]; then
    if [ -x "$MC12_WINE" ] || command -v "$MC12_WINE" >/dev/null 2>&1; then printf '%s\n' "$MC12_WINE"; return 0; fi
    return 1
  fi
  local c
  for c in wine wine64; do
    if command -v "$c" >/dev/null 2>&1; then command -v "$c"; return 0; fi
  done
  for c in \
    "/Applications/Wine Crossover.app/Contents/Resources/wine/bin/wine" \
    "/Applications/Wine Stable.app/Contents/Resources/wine/bin/wine" \
    "/Applications/Wine Devel.app/Contents/Resources/wine/bin/wine" \
    "/Applications/Wine Staging.app/Contents/Resources/wine/bin/wine" \
    "/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine"; do
    if [ -x "$c" ]; then printf '%s\n' "$c"; return 0; fi
  done
  return 1
}

kill_mc12() {
  [ -n "${1:-}" ] && kill "$1" 2>/dev/null
  pkill -f 'mc12_64\.exe' 2>/dev/null
  return 0
}

run() {
  if [ $# -lt 2 ]; then echo "usage: $0 run <control file> <expected output file> [timeout s]" >&2; return 2; fi
  local control="$1" out="$2" timeout="${3:-60}"
  case "$control" in /*) ;; *) control="$PWD/$control" ;; esac
  case "$out" in /*) ;; *) out="$PWD/$out" ;; esac
  if [ ! -f "$control" ]; then echo "control file not found: $control" >&2; return 2; fi

  local wine
  if ! wine="$(find_wine)"; then
    echo "Wine not found: install Wine (e.g. brew install --cask --no-quarantine gcenx/wine/wine-crossover) or CrossOver, or set MC12_WINE" >&2
    return 2
  fi

  kill_mc12   # a stray Micro-Cap from an earlier run confuses the next one
  rm -f "$out"

  local ctl_win; ctl_win="$(winpath "$control")"
  local pid
  case "$wine" in
    *CrossOver*)
      "$wine" --bottle "$MC12_BOTTLE" "$MC12_EXE" "@$ctl_win" >/dev/null 2>&1 &
      pid=$!; disown "$pid" ;;
    *)
      WINEPREFIX="$MC12_WINEPREFIX" WINEDEBUG="${WINEDEBUG:--all}" "$wine" "$MC12_EXE" "@$ctl_win" >/dev/null 2>&1 &
      pid=$!; disown "$pid" ;;
  esac

  # Poll in 0.5 s steps; the output name may come back uppercase, and APFS is
  # case-insensitive by default, so the plain test also matches RC_OUT.ANO.
  local steps=$((timeout * 2)) i=0 found=0
  while [ $i -lt $steps ]; do
    if [ -f "$out" ]; then found=1; break; fi
    if ! kill -0 "$pid" 2>/dev/null && ! pgrep -f 'mc12_64\.exe' >/dev/null 2>&1; then
      break   # Wine/Micro-Cap died before writing anything
    fi
    sleep 0.5
    i=$((i + 1))
  done
  [ $found -eq 1 ] && sleep 2   # let the file finish writing

  kill_mc12 "$pid"
  if [ -z "${MC12_WINE:-}" ] && [ "${wine#*CrossOver}" = "$wine" ]; then
    local ws; ws="$(dirname "$wine")/wineserver"
    [ -x "$ws" ] && WINEPREFIX="$MC12_WINEPREFIX" "$ws" -k 2>/dev/null
  fi

  if [ $found -eq 1 ]; then
    echo "output: $out"
    return 0
  fi
  echo "timed out after ${timeout}s waiting for $out (netlist error or missing .PRINT? see the .DOC next to the control file)" >&2
  return 1
}

cmd="${1:-}"; [ $# -gt 0 ] && shift
case "$cmd" in
  winpath) [ $# -eq 1 ] || { echo "usage: $0 winpath <absolute path>" >&2; exit 2; }; winpath "$1" ;;
  run) run "$@" ;;
  kill) kill_mc12 ;;
  *) sed -n '2,25p' "$0" >&2; exit 2 ;;
esac
