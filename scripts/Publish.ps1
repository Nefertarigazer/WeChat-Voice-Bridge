param(
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\win-x64'),
    [string]$ZipPath,
    [string]$PythonPackage,
    [string]$SqlCipherPackage,
    [string]$ZstandardPackage
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\VoiceBridge.App\VoiceBridge.App.csproj'
$output = [IO.Path]::GetFullPath($OutputDirectory)

& $Dotnet publish $project -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$toolsRoot = Join-Path $root 'artifacts\build-tools'
New-Item -Path $toolsRoot -ItemType Directory -Force | Out-Null

function Get-VerifiedWheel {
    param([string]$Name, [string]$Url, [string]$Hash, [string]$LocalPath)
    $wheel = Join-Path $toolsRoot $Name
    if ($LocalPath) {
        $resolved = (Resolve-Path -LiteralPath $LocalPath).Path
        if ($resolved -ne $wheel) { Copy-Item -LiteralPath $resolved -Destination $wheel -Force }
    } elseif (!(Test-Path -LiteralPath $wheel)) {
        & curl.exe -fsSL --retry 2 --max-time 180 $Url -o $wheel
        if ($LASTEXITCODE -ne 0) { throw "$Name download failed." }
    }
    if ((Get-FileHash -LiteralPath $wheel -Algorithm SHA256).Hash -ne $Hash) { throw "$Name hash mismatch." }
    return $wheel
}

# CPython's official Windows x64 NuGet package. Keep the runtime separate from app and worker source.
$package = Join-Path $toolsRoot 'python.3.12.10.zip'
if ($PythonPackage) {
    $resolvedPackage = (Resolve-Path -LiteralPath $PythonPackage).Path
    if ($resolvedPackage -ne $package) { Copy-Item -LiteralPath $resolvedPackage -Destination $package -Force }
} elseif (!(Test-Path -LiteralPath $package)) {
    & curl.exe -fsSL --retry 2 --max-time 120 'https://api.nuget.org/v3-flatcontainer/python/3.12.10/python.3.12.10.nupkg' -o $package
    if ($LASTEXITCODE -ne 0) { throw 'CPython download failed.' }
}
$expected = '0EB85C2DFCCCCF1B17352DE4C397F69194035B7D37149EACC16F1147D93DE3B8'
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expected) {
    throw 'CPython package hash mismatch.'
}
& $Dotnet nuget verify $package
if ($LASTEXITCODE -ne 0) { throw 'CPython package signature verification failed.' }

$unpacked = Join-Path $toolsRoot 'python-3.12.10'
Expand-Archive -LiteralPath $package -DestinationPath $unpacked -Force
$python = Join-Path $output 'runtime\python'
New-Item -ItemType Directory -Force $python | Out-Null
foreach ($item in @('python.exe', 'python312.dll', 'python3.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'Lib', 'DLLs', 'LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $unpacked ('tools\' + $item)) -Destination $python -Recurse -Force
}
Set-Content -LiteralPath (Join-Path $python 'python312._pth') -Encoding ASCII -Value @('.', 'Lib', 'DLLs')
& (Join-Path $python 'python.exe') -I -B -c "import sqlite3, struct, sys; assert sys.version_info[:2] == (3, 12) and struct.calcsize('P') == 8; print(sys.version, sqlite3.sqlite_version)"
if ($LASTEXITCODE -ne 0) { throw 'The isolated x64 worker runtime failed its smoke check.' }

# SQLCipher opens the encrypted WeChat copies; zstandard expands long text payloads.
$sqlCipher = Get-VerifiedWheel -Name 'sqlcipher3-0.6.4-cp312-cp312-win_amd64.whl' `
    -Url 'https://files.pythonhosted.org/packages/7d/99/ab20279f6598cdd3aacadef04200819c1e70dfca195381987d875fc6423a/sqlcipher3-0.6.4-cp312-cp312-win_amd64.whl' `
    -Hash 'FF612C1F731A1870457A3FA4C0095BF27CE13AD7E2035EC3F98A489E2D6CCF5C' -LocalPath $SqlCipherPackage
$zstandard = Get-VerifiedWheel -Name 'zstandard-0.25.0-cp312-cp312-win_amd64.whl' `
    -Url 'https://files.pythonhosted.org/packages/79/3b/fa54d9015f945330510cb5d0b0501e8253c127cca7ebe8ba46a965df18c5/zstandard-0.25.0-cp312-cp312-win_amd64.whl' `
    -Hash 'FFEF5A74088F1E09947AECF91011136665152E0B4B359C42BE3373897FB39B01' -LocalPath $ZstandardPackage

$workerDeps = Join-Path $output 'worker\deps'
if (Test-Path -LiteralPath $workerDeps) { Remove-Item -LiteralPath $workerDeps -Recurse -Force }
New-Item -ItemType Directory -Force $workerDeps | Out-Null
foreach ($wheel in @($sqlCipher, $zstandard)) {
    & (Join-Path $python 'python.exe') -I -B -m zipfile -e $wheel $workerDeps
    if ($LASTEXITCODE -ne 0) { throw "Wheel extraction failed: $wheel" }
}
$dependencyCheck = Join-Path $toolsRoot 'verify-worker-deps.py'
Set-Content -LiteralPath $dependencyCheck -Encoding UTF8 -Value @'
import sys
sys.path.insert(0, sys.argv[1])
from sqlcipher3 import dbapi2
assert dbapi2.connect(":memory:").execute("PRAGMA cipher_version").fetchone()
import zstandard
frame = zstandard.ZstdCompressor().compress("long message".encode("utf-8") * 40)
assert frame[:4] == b"\x28\xb5\x2f\xfd"
assert zstandard.ZstdDecompressor().decompress(frame) == "long message".encode("utf-8") * 40
print("worker dependencies ok")
'@
& (Join-Path $python 'python.exe') -I -B $dependencyCheck $workerDeps
if ($LASTEXITCODE -ne 0) { throw 'The worker dependencies failed their smoke check.' }

if ($ZipPath) {
    $archive = [IO.Path]::GetFullPath($ZipPath)
    New-Item -ItemType Directory -Force (Split-Path -Parent $archive) | Out-Null
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -CompressionLevel Optimal
    Write-Output $archive
}

Write-Output (Join-Path $output 'WeChatVoiceBridge.exe')
