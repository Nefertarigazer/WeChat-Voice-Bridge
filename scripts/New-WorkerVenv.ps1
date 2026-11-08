$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& py -3.12-64 -m venv (Join-Path $root '.venv')
if ($LASTEXITCODE -ne 0) { throw 'Install Python 3.12 x64, including the py launcher.' }
Write-Output (Join-Path $root '.venv\Scripts\python.exe')
