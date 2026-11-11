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
# Reuse the already verified unpacked runtime. Expand-Archive -Force clears the
# destination first, which some hosts block as a destructive delete on a cached tree.
if (!(Test-Path -LiteralPath (Join-Path $unpacked 'tools\python.exe'))) {
    Expand-Archive -LiteralPath $package -DestinationPath $unpacked -Force
}
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
New-Item -ItemType Directory -Force $workerDeps | Out-Null
# zipfile -e overwrites individual files, so a re-publish does not need to clear
# the directory (and avoids a destructive delete on an existing install tree).
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

# The free Edge voices reuse edge-tts (LGPLv3, unmodified, kept as a separate
# package so it can be replaced) and its aiohttp stack. They live apart from the
# WeChat worker's deps: worker\edge_speak.py is the only code that imports them.
$ttsWheels = @(
    @('edge_tts-7.3.1-py3-none-any.whl', 'https://files.pythonhosted.org/packages/35/86/3b97d755992777d5a9ca0e45a8ec96cc9858b2d6dd8f65f3f688d60e8709/edge_tts-7.3.1-py3-none-any.whl', '2A3C79D6235A7A736D681233823CE5492E829B43EB47BEAE49AA181237A8F9A4'),
    @('aiohttp-3.14.5-cp312-cp312-win_amd64.whl', 'https://files.pythonhosted.org/packages/ae/ce/1f08114679d49655b30a6e0a29858375c94b82c1de1a0bd0a20c2fee8b02/aiohttp-3.14.5-cp312-cp312-win_amd64.whl', '2CC38A4F2B516BEF1714E690DF87A0E043FAF1A7693C82D860091684453D5111'),
    @('aiohappyeyeballs-2.7.1-py3-none-any.whl', 'https://files.pythonhosted.org/packages/71/43/1947f06babed6b3f1d7f38b0c767f52df66bfb2bc10b468c4a7de9eceff2/aiohappyeyeballs-2.7.1-py3-none-any.whl', '9243213661E29250EB41368E5DAA826FC017156C3B8A11440826B2E3ED376472'),
    @('aiosignal-1.4.0-py3-none-any.whl', 'https://files.pythonhosted.org/packages/fb/76/641ae371508676492379f16e2fa48f4e2c11741bd63c48be4b12a6b09cba/aiosignal-1.4.0-py3-none-any.whl', '053243F8B92B990551949E63930A839FF0CF0B0EBBE0597B0F3FB19E1A0FE82E'),
    @('async_timeout-5.0.1-py3-none-any.whl', 'https://files.pythonhosted.org/packages/fe/ba/e2081de779ca30d473f21f5b30e0e737c438205440784c7dfc81efc2b029/async_timeout-5.0.1-py3-none-any.whl', '39E3809566FF85354557EC2398B55E096C8364BACAC9405A7A1FA429E77FE76C'),
    @('attrs-26.1.0-py3-none-any.whl', 'https://files.pythonhosted.org/packages/64/b4/17d4b0b2a2dc85a6df63d1157e028ed19f90d4cd97c36717afef2bc2f395/attrs-26.1.0-py3-none-any.whl', 'C647AA4A12DFBAD9333CA4E71FE62DDC36F4E63B2D260A37A8B83D2F043AC309'),
    @('certifi-2026.10.23-py3-none-any.whl', 'https://files.pythonhosted.org/packages/32/d1/a3652fe2c19134d0d15922177821d863cbd7e842d53a21bbe7e8e3a1b5ac/certifi-2026.10.23-py3-none-any.whl', 'E9B54DE76E530BB6CF0CCD036787103ACB58FD42BA930EA8BF948DDBAA2F61D1'),
    @('frozenlist-1.8.0-cp312-cp312-win_amd64.whl', 'https://files.pythonhosted.org/packages/b8/af/38e51a553dd66eb064cdf193841f16f077585d4d28394c2fa6235cb41765/frozenlist-1.8.0-cp312-cp312-win_amd64.whl', '34187385B08F866104F0C0617404C8EB08165AB1272E884ABC89C112E9C00746'),
    @('idna-3.20-py3-none-any.whl', 'https://files.pythonhosted.org/packages/58/a2/bb081bab032533a855d44de1d56f8e8426114ff1ba5d1f07a438a0a654f8/idna-3.20-py3-none-any.whl', 'AB7AE7122974553370F0BDB919E1A960B2CD1BC1EF0276416D896DB81C14582C'),
    @('multidict-7.2.1-cp312-cp312-win_amd64.whl', 'https://files.pythonhosted.org/packages/68/d5/64108a33c21bde81454b5a400d16baa1378a7d01599de64b131c3e3e3114/multidict-7.2.1-cp312-cp312-win_amd64.whl', '781CA5DFC2F1D58E47F52216A0C1C7E1BA91CE8AA235459E20DE2D402AF77FB3'),
    @('propcache-0.5.4-cp312-cp312-win_amd64.whl', 'https://files.pythonhosted.org/packages/58/7b/6e8ef26f6d510a7916064fec68d55fcbfbdf7eb01e377480d66a122152d8/propcache-0.5.4-cp312-cp312-win_amd64.whl', '98914DE2C4D7F0F9F4A8C6EA4BF05841F4175796941E3EF7D47EB718F22311FB'),
    @('tabulate-0.10.0-py3-none-any.whl', 'https://files.pythonhosted.org/packages/99/55/db07de81b5c630da5cbf5c7df646580ca26dfaefa593667fc6f2fe016d2e/tabulate-0.10.0-py3-none-any.whl', 'F0B0622E567335C8FABAAA659F1B33BCB6DDFE2E496071B743AA113F8774F2D3'),
    @('typing_extensions-4.16.0-py3-none-any.whl', 'https://files.pythonhosted.org/packages/49/d3/b8441a820a491ddfc024b0b0cf0393375b75ea13866d9c66727e54c2fc80/typing_extensions-4.16.0-py3-none-any.whl', '481CAA481374E813C1B176ADA14E97F1F67A4539CE9CFEB3F350D78D6370C2E8'),
    @('yarl-1.25.1-cp312-cp312-win_amd64.whl', 'https://files.pythonhosted.org/packages/bb/cc/51a7b4abf4ac593b8e7eb3794b28e5a35ae26eed8bc04787628d215af82f/yarl-1.25.1-cp312-cp312-win_amd64.whl', '7D575B54CB3863EF9BC290EA4B009999D55DC237326131E4853CF33E888FEE03')
)
$ttsDeps = Join-Path $output 'worker\tts-deps'
New-Item -ItemType Directory -Force $ttsDeps | Out-Null
foreach ($entry in $ttsWheels) {
    $wheel = Get-VerifiedWheel -Name $entry[0] -Url $entry[1] -Hash $entry[2]
    & (Join-Path $python 'python.exe') -I -B -m zipfile -e $wheel $ttsDeps
    if ($LASTEXITCODE -ne 0) { throw "Wheel extraction failed: $wheel" }
}
$ttsCheck = Join-Path $toolsRoot 'verify-tts-deps.py'
Set-Content -LiteralPath $ttsCheck -Encoding UTF8 -Value @'
import sys
sys.path.insert(0, sys.argv[1])
import edge_tts
from edge_tts import Communicate, exceptions
assert edge_tts.__version__ == "7.3.1", edge_tts.__version__
print("speech dependencies ok")
'@
& (Join-Path $python 'python.exe') -I -B $ttsCheck $ttsDeps
if ($LASTEXITCODE -ne 0) { throw 'The edge-tts dependencies failed their smoke check.' }

# A shipped build must never carry this machine's settings.json, DPAPI store or
# a live API key. Fail the publish before any ZIP is written.
. (Join-Path $PSScriptRoot 'Assert-CleanPayload.ps1')
Assert-CleanPayload -Payload $output

if ($ZipPath) {
    $archive = [IO.Path]::GetFullPath($ZipPath)
    New-Item -ItemType Directory -Force (Split-Path -Parent $archive) | Out-Null
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -CompressionLevel Optimal
    Write-Output $archive
}

Write-Output (Join-Path $output 'WeChatVoiceBridge.exe')
