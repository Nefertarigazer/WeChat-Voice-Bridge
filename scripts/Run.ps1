param([string]$Dotnet = 'dotnet', [switch]$Development)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Development) {
    $env:WVB_PYTHON = Join-Path $root '.venv\Scripts\python.exe'
    if (!(Test-Path -LiteralPath $env:WVB_PYTHON)) { throw 'Run scripts\New-WorkerVenv.ps1 first.' }
    & $Dotnet run --project (Join-Path $root 'src\VoiceBridge.App\VoiceBridge.App.csproj')
    exit $LASTEXITCODE
}
$app = Join-Path $root 'artifacts\win-x64\WeChatVoiceBridge.exe'
if (!(Test-Path -LiteralPath $app)) { throw 'Run scripts\Publish.ps1 first.' }
& $app
