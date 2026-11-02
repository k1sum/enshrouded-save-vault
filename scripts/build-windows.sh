#!/bin/sh
# Cross-publish the Windows client from macOS or Linux. The result is one self-contained exe, nothing to install.
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
rm -rf "$root/dist/windows-x64"
"${DOTNET:-dotnet}" publish "$root/client/SaveDesktop/SaveDesktop.csproj" -c Release -r win-x64 -o "$root/dist/windows-x64" --nologo
ls -l "$root/dist/windows-x64"
