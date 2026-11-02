$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
# One self-contained exe (runtime and native libraries embedded): copy it anywhere and double-click, nothing to install.
dotnet publish "$ProjectRoot/client/SaveDesktop/SaveDesktop.csproj" -c Release -r win-x64 -o "$ProjectRoot/dist/windows-x64"
if ($LASTEXITCODE -ne 0) { throw 'Client publish failed' }
