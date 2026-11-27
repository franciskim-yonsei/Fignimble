# Build an unsigned, self-contained Fignimble ZIP and optionally a Windows installer.
[CmdletBinding()]
param(
    [string]$Output = (Join-Path $env:TEMP 'Fignimble-Windows-x64.zip'),
    [string]$InstallerOutput,
    [string]$InstallerVersion,
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw "Choose a new ZIP path: $Output already exists." }
if ([IO.Path]::GetExtension($Output) -ne '.zip') { throw 'Output must have a .zip extension.' }
if ($InstallerOutput) {
    $InstallerOutput = [IO.Path]::GetFullPath($InstallerOutput)
    if ($Runtime -ne 'win-x64') { throw 'The installer supports the win-x64 runtime.' }
    if (Test-Path -LiteralPath $InstallerOutput) { throw "Choose a new installer path: $InstallerOutput already exists." }
    if ([IO.Path]::GetExtension($InstallerOutput) -ne '.exe') { throw 'InstallerOutput must have an .exe extension.' }
    if (!$InstallerVersion) { $InstallerVersion = (Get-Content (Join-Path $PSScriptRoot '..\..\VERSION') -Raw).Trim() }
    if ($InstallerVersion -notmatch '^[a-zA-Z0-9][-a-zA-Z0-9.]*$') { throw 'Invalid installer version.' }
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    if (!$iscc) { $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    if (!(Test-Path -LiteralPath $iscc)) { throw 'Install Inno Setup 6 to build the installer.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $InstallerOutput) -Force | Out-Null
}
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

1. Start Fignimble from the Start menu after installation, or extract the entire
   ZIP and run Fignimble.exe. The Windows .NET runtime is bundled.
2. Use Python 3.10 or newer. In the app, use File > Python Setup to select an
   existing Python environment, then File > Check Python Packages. To create
   Fignimble's private environment, run this from the app folder:
     powershell -NoProfile -ExecutionPolicy Bypass -File .\setup-python.ps1
3. For SSH, choose Open remote project and enter the server's Python separately.
   The embedded terminal requires Microsoft Edge WebView2.

Only open trusted figures: their code is run as Python.
'@ | Set-Content -LiteralPath (Join-Path $bundle 'START-HERE.txt') -Encoding UTF8
    if ($InstallerOutput) {
        & $iscc '/Qp' "/DAppSource=$bundle" "/DAppVersion=$InstallerVersion" "/O$stage" '/FFignimble-Setup' (Join-Path $PSScriptRoot 'setup.iss')
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit $LASTEXITCODE)." }
    }
    Compress-Archive -LiteralPath $bundle -DestinationPath $zip -CompressionLevel Optimal
    # Move rather than overwrite, including if the destination was created during the build.
    [IO.File]::Move($zip, $Output)
    Write-Output $Output
    if ($InstallerOutput) {
        [IO.File]::Move((Join-Path $stage 'Fignimble-Setup.exe'), $InstallerOutput)
        Write-Output $InstallerOutput
    }
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
}
