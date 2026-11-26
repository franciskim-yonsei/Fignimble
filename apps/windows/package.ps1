# Build an unsigned, self-contained Fignimble app ZIP. Does not install, sign or publish it.
[CmdletBinding()]
param(
    [string]$Output = (Join-Path $env:TEMP 'Fignimble-Windows-x64.zip'),
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw "Choose a new ZIP path: $Output already exists." }
if ([IO.Path]::GetExtension($Output) -ne '.zip') { throw 'Output must have a .zip extension.' }
$parent = Split-Path -Parent $Output
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$stage = Join-Path $env:TEMP ('fignimble-package-' + [Guid]::NewGuid().ToString('N'))
$bundle = Join-Path $stage 'Fignimble'
$zip = Join-Path $parent ('.fignimble-archive-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    dotnet publish (Join-Path $PSScriptRoot 'Fignimble.csproj') --configuration Release --runtime $Runtime --self-contained true --output $bundle --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'setup-python.ps1') -Destination $bundle
    @'
Fignimble for Windows (unsigned preview)

1. Extract the entire ZIP. The Windows .NET runtime is bundled.
2. Use Python 3.10 or newer. Run this once from the extracted Fignimble folder:
     powershell -NoProfile -ExecutionPolicy Bypass -File .\setup-python.ps1
   Or open Fignimble.exe and use File > Python Setup to select an existing
   Python environment, then File > Check Python Packages.
3. Run Fignimble.exe. The embedded terminal requires Microsoft Edge WebView2.

The ZIP does not install a shortcut or automatically add Python packages.
Only open trusted figures: their code is run as Python.
'@ | Set-Content -LiteralPath (Join-Path $bundle 'START-HERE.txt') -Encoding UTF8
    Compress-Archive -LiteralPath $bundle -DestinationPath $zip -CompressionLevel Optimal
    # Move rather than overwrite, including if the destination was created during the build.
    [IO.File]::Move($zip, $Output)
    Write-Output $Output
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
}
