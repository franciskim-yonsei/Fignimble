# Set up the private Python environment for a packaged Fignimble app.
[CmdletBinding()]
param([string]$Python = '')

$ErrorActionPreference = 'Stop'
$requirements = Join-Path $PSScriptRoot 'engine\requirements.txt'
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Fignimble.exe')) -or !(Test-Path -LiteralPath $requirements)) {
    throw 'Run this script from the installed app folder or the fully extracted Fignimble ZIP.'
}
$venv = Join-Path $env:LOCALAPPDATA 'Fignimble\python'
$interpreter = Join-Path $venv 'Scripts\python.exe'
function Check-Exit([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed (exit $LASTEXITCODE)." }
}
if (!(Test-Path -LiteralPath $interpreter)) {
    if ($Python) { & $Python -c 'import sys; assert sys.version_info >= (3, 10), sys.version'; Check-Exit 'Checking Python'; & $Python -m venv $venv }
    elseif (Get-Command py -ErrorAction SilentlyContinue) { py -3 -c 'import sys; assert sys.version_info >= (3, 10), sys.version'; Check-Exit 'Checking Python'; py -3 -m venv $venv }
    else { python -c 'import sys; assert sys.version_info >= (3, 10), sys.version'; Check-Exit 'Checking Python'; python -m venv $venv }
    Check-Exit 'Creating a Python 3.10+ environment'
}
& $interpreter -c 'import sys; assert sys.version_info >= (3, 10), sys.version'
Check-Exit 'Checking the private Python environment'
& $interpreter -X utf8 -m pip install -r $requirements
Check-Exit 'Installing Fignimble engine dependencies'
Write-Output "Python is ready: $interpreter"
Write-Output 'You can now open Fignimble.exe.'
