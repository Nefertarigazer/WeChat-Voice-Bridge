param(
    [string]$Dotnet = 'dotnet',
    [string]$Python,
    [switch]$Benchmark,
    [switch]$AudioDevice,
    # Real calls to free online services (Edge read-aloud). Needs WVB_TTS_DEPS.
    [switch]$Network
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$Python) {
    $Python = @(
        (Join-Path $root '.venv\Scripts\python.exe'),
        (Join-Path $root 'artifacts\win-x64\runtime\python\python.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$Python) { throw 'Run scripts\New-WorkerVenv.ps1 or scripts\Publish.ps1 first.' }
$env:WVB_PYTHON = (Resolve-Path -LiteralPath $Python).Path
& $env:WVB_PYTHON -I -B -c "import sqlite3, struct, sys; assert sys.version_info[:2] == (3, 12) and struct.calcsize('P') == 8"
if ($LASTEXITCODE -ne 0) { throw 'Python 3.12 x64 is required.' }
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null
$env:WVB_BENCHMARK_PATH = Join-Path $artifacts 'benchmark.json'
$filter = @()
if (!$Benchmark) { $filter += 'Category!=Benchmark' }
if (!$AudioDevice) { $filter += 'Category!=AudioDevice' }
if (!$Network) { $filter += 'Category!=Network' }
$argsForTest = @()
if ($filter.Count -gt 0) { $argsForTest = @('--filter', ($filter -join '&')) }

& $Dotnet build (Join-Path $root 'WeChatVoiceBridge.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build: FAIL' }
& $Dotnet test (Join-Path $root 'WeChatVoiceBridge.sln') -c Release --no-build @argsForTest `
    --logger 'trx;LogFileName=automated.trx' --results-directory (Join-Path $artifacts 'TestResults')
if ($LASTEXITCODE -ne 0) { throw '.NET tests: FAIL' }
Write-Output '.NET tests: PASS'
# unittest -v writes its progress to stderr. Windows PowerShell 5.1 wraps native
# stderr in an ErrorRecord, which aborts a script running with Stop, so relax the
# preference for this one call and judge the run by its exit code instead.
$previousPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try { & $env:WVB_PYTHON -B -X utf8 -m unittest discover -s (Join-Path $root 'tests\python') -v 2>&1 |
        ForEach-Object { if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message } else { "$_" } } }
finally { $ErrorActionPreference = $previousPreference }
if ($LASTEXITCODE -ne 0) { throw 'Python tests: FAIL' }
Write-Output 'Python tests: PASS'
