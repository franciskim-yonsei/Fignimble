# Install or update Fignimble for the current Windows user from this checkout.
[CmdletBinding()]
param(
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'Fignimble'),
    [string]$Python = '',
    [switch]$SkipShortcut
)
$ErrorActionPreference = 'Stop'
$Root = [IO.Path]::GetFullPath($Root)
$project = Join-Path $PSScriptRoot 'Fignimble.csproj'
$app = Join-Path $Root 'app'
$venv = Join-Path $Root 'python'
$interpreter = Join-Path $venv 'Scripts\python.exe'
$token = [guid]::NewGuid().ToString('N')
$stage = Join-Path $Root "app-stage-$token"
$backup = Join-Path $Root "app-backup-$token"
$marker = Join-Path $app 'Fignimble.exe'

function CheckExit([string]$What) {
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit $LASTEXITCODE)." }
}
if ((Test-Path -LiteralPath $app) -and !(Test-Path -LiteralPath $marker)) {
    throw "Refusing to replace an unrecognized directory: $app"
}
$running = Get-Process Fignimble -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $marker }
if ($running) { throw 'Close the installed Fignimble app before updating.' }
New-Item -Type Directory -Path $Root -Force | Out-Null
try {
    dotnet publish $project --configuration Release --output $stage --nologo
    CheckExit 'Building Fignimble (.NET 10 SDK required)'
    if (!(Test-Path -LiteralPath $interpreter)) {
        if ($Python) { & $Python -m venv $venv }
        elseif (Get-Command py -ErrorAction SilentlyContinue) { py -3 -m venv $venv }
        else { python -m venv $venv }
        CheckExit 'Creating the Python environment (Python 3.10 or newer required)'
    }
    & $interpreter -c 'import sys; assert sys.version_info >= (3, 10), sys.version'
    CheckExit 'Checking the Python version'
    & $interpreter -X utf8 -m pip install -r (Join-Path $stage 'engine\requirements.txt')
    CheckExit 'Installing Python dependencies'
    if (Test-Path -LiteralPath $app) { Move-Item -LiteralPath $app -Destination $backup }
    try { Move-Item -LiteralPath $stage -Destination $app }
    catch {
        if (Test-Path -LiteralPath $backup) { Move-Item -LiteralPath $backup -Destination $app }
        throw
    }
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
    if (!$SkipShortcut) {
        $shortcutFile = Join-Path ([Environment]::GetFolderPath('Programs')) 'Fignimble.lnk'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutFile)
        $shortcut.TargetPath = $marker
        $shortcut.WorkingDirectory = $app
        $shortcut.Save()
    }
    Write-Output $marker
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
