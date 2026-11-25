#!/usr/bin/env bash
# electronics-skills — macOS prerequisite check and (optional) plugin install.
#
#   ./install-macos.sh            check what each plugin needs, print what is missing
#   ./install-macos.sh --install  same, then add this folder as a Claude Code
#                                 marketplace and install the five plugins
#
# Installs nothing else by itself: missing tools are listed with the command or
# link to get them (see INSTALL-macOS.md).
set -u
cd "$(dirname "$0")"
ROOT="$PWD"

ok()   { printf '  \033[32mOK\033[0m   %s\n' "$*"; }
miss() { printf '  \033[31m--\033[0m   %s\n' "$*"; }
opt()  { printf '  \033[33m..\033[0m   %s\n' "$*"; }

echo "electronics-skills — macOS check ($(uname -m), macOS $(sw_vers -productVersion 2>/dev/null))"

echo
echo "Common"
if command -v claude >/dev/null 2>&1; then ok "Claude Code CLI: $(command -v claude)"; else miss "Claude Code CLI — https://claude.ai/code"; fi
if command -v git >/dev/null 2>&1; then ok "git"; else miss "git — xcode-select --install"; fi
if command -v brew >/dev/null 2>&1; then ok "Homebrew"; else opt "Homebrew (recommended for the installs below) — https://brew.sh"; fi
if [ "$(uname -m)" = "arm64" ]; then
  if /usr/bin/pgrep -q oahd 2>/dev/null || [ -f /Library/Apple/usr/share/rosetta/rosetta ]; then ok "Rosetta 2"; else opt "Rosetta 2 (needed by Wine for micro-cap) — softwareupdate --install-rosetta --agree-to-license"; fi
fi

dotnet_sdks="$(dotnet --list-sdks 2>/dev/null || true)"
has_sdk() { printf '%s\n' "$dotnet_sdks" | grep -q "^$1\."; }

echo
echo "parts-search  (.NET 10 SDK; keys in the login Keychain)"
if has_sdk 10; then ok ".NET 10 SDK"; else miss ".NET 10 SDK — brew install --cask dotnet-sdk  or  https://dotnet.microsoft.com/download/dotnet/10.0"; fi

echo
echo "pcb-as-code   (.NET 10 SDK, KiCad 10, Java 17+ and FreeRouting for autorouting)"
if has_sdk 10; then ok ".NET 10 SDK"; else miss ".NET 10 SDK (see above)"; fi
kicad=""
for c in "${KICAD_CLI:-}" /Applications/KiCad/KiCad.app/Contents/MacOS/kicad-cli /opt/homebrew/bin/kicad-cli /usr/local/bin/kicad-cli; do
  [ -n "$c" ] && [ -x "$c" ] && { kicad="$c"; break; }
done
if [ -n "$kicad" ]; then ok "kicad-cli $("$kicad" --version 2>/dev/null) ($kicad)"; else miss "KiCad 10 — brew install --cask kicad  or  https://www.kicad.org/download/macos/"; fi
java=""
for c in "${FREEROUTING_JAVA:-}" "${JAVA_HOME:+$JAVA_HOME/bin/java}" /opt/homebrew/opt/openjdk/bin/java /usr/local/opt/openjdk/bin/java; do
  [ -n "$c" ] && [ -x "$c" ] && { java="$c"; break; }
done
if [ -z "$java" ] && /usr/libexec/java_home >/dev/null 2>&1; then java="$(/usr/libexec/java_home)/bin/java"; fi
if [ -n "$java" ]; then ok "Java: $("$java" -version 2>&1 | head -1) ($java)"; else miss "Java 17+ — brew install openjdk  or  https://adoptium.net/"; fi
jar="${FREEROUTING_JAR:-$HOME/tools/freerouting-1.4.5.1/freerouting-1.4.5.1.jar}"
if [ -f "$jar" ]; then ok "FreeRouting jar ($jar)"; else opt "FreeRouting jar not at $jar — https://github.com/freerouting/freerouting/releases (or set FREEROUTING_JAR)"; fi

echo
echo "3dpart-as-code (.NET 10.0.300+ SDK, Python 3.12; KiCad only for board-mating parts)"
if has_sdk 10; then ok ".NET 10 SDK ($(printf '%s\n' "$dotnet_sdks" | grep '^10\.' | tail -1 | cut -d' ' -f1))"; else miss ".NET 10 SDK (see above)"; fi
if command -v python3.12 >/dev/null 2>&1; then ok "python3.12"; else miss "Python 3.12 — brew install python@3.12  or  https://www.python.org/downloads/macos/"; fi

echo
echo "visa          (.NET 8+ SDK, a macOS VISA library)"
if has_sdk 8 || has_sdk 9 || has_sdk 10; then ok ".NET 8+ SDK"; else miss ".NET 8+ SDK (the .NET 10 SDK covers it)"; fi
visa=""
for c in "${VISA_LIBRARY:-}" /Library/Frameworks/VISA.framework/VISA /Library/Frameworks/RsVisa.framework/RsVisa; do
  [ -n "$c" ] && [ -e "$c" ] && { visa="$c"; break; }
done
if [ -n "$visa" ]; then
  arch="$(lipo -archs "$visa" 2>/dev/null || file -bL "$visa" | head -1)"
  ok "VISA library: $visa ($arch)"
  case "$(uname -m):$arch" in arm64:*arm64*|x86_64:*x86_64*) ;; *) opt "VISA library lacks $(uname -m): run the x64 .NET under Rosetta (see INSTALL-macOS.md)";; esac
else
  miss "VISA library — NI-VISA https://www.ni.com/en/support/downloads/drivers/download.ni-visa.html  or  R&S VISA https://www.rohde-schwarz.com/applications/r-s-visa-application-note_56280-148812.html"
fi

echo
echo "micro-cap     (Wine + Micro-Cap 12 installed in a Wine prefix — not yet verified live)"
wine=""
for c in "${MC12_WINE:-}" "$(command -v wine 2>/dev/null)" "$(command -v wine64 2>/dev/null)" \
  "/Applications/Wine Crossover.app/Contents/Resources/wine/bin/wine" \
  "/Applications/Wine Stable.app/Contents/Resources/wine/bin/wine" \
  "/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine"; do
  [ -n "$c" ] && [ -x "$c" ] && { wine="$c"; break; }
done
if [ -n "$wine" ]; then ok "Wine: $wine"; else miss "Wine — brew install --cask --no-quarantine gcenx/wine/wine-crossover  or  CrossOver https://www.codeweavers.com/crossover"; fi
prefix="${MC12_WINEPREFIX:-$HOME/.wine-mc12}"
if [ -f "$prefix/drive_c/MC12/mc12_64.exe" ]; then ok "Micro-Cap 12 ($prefix/drive_c/MC12)"; else miss "Micro-Cap 12 not in $prefix/drive_c/MC12 — see INSTALL-macOS.md (installer from the Wayback Machine)"; fi

if [ "${1:-}" = "--install" ]; then
  echo
  if ! command -v claude >/dev/null 2>&1; then echo "Claude Code CLI missing; cannot install the plugins." >&2; exit 1; fi
  echo "Adding marketplace from $ROOT"
  claude plugin marketplace add "$ROOT" || exit 1
  for p in parts-search pcb-as-code 3dpart-as-code visa micro-cap; do
    claude plugin install "$p@electronics-skills"
  done
fi
