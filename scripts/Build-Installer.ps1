<#
.SYNOPSIS
    编译微信语音桥的安装包（Inno Setup 6）。

.DESCRIPTION
    需要先有发布版输出，默认使用 artifacts\win-x64。
    编译后在 artifacts 中生成带版本号的 Setup.exe，可直接转发。

.EXAMPLE
    .\scripts\Build-Installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Iscc = "",
    [string]$Output = "",
    [string]$PayloadDirectory = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$iss = Join-Path $PSScriptRoot 'installer\WeChatVoiceBridge.iss'
$properties = [xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z')
{
    throw 'Directory.Build.props 中缺少有效的正式版本号。'
}
if ($PayloadDirectory)
{
    $payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
}
else
{
    $payload = Join-Path $root 'artifacts\win-x64'
}
$setup = Join-Path $root "artifacts\WeChat-Voice-Bridge-V$version-Setup.exe"

if (-not (Test-Path $payload))
{
    throw "没有找到发布版目录：$payload。先运行 scripts\Publish.ps1。"
}

foreach ($requiredFile in @('WeChatVoiceBridge.exe', 'hostfxr.dll', 'PresentationFramework.dll',
    'runtime\python\python.exe', 'worker\main.py', 'worker\deps\sqlcipher3\_sqlite3.cp312-win_amd64.pyd'))
{
    if (-not (Test-Path -LiteralPath (Join-Path $payload $requiredFile) -PathType Leaf))
    {
        throw "发布目录缺少 $requiredFile。请先运行 Publish.ps1，或通过 -PayloadDirectory 指定完整的发布目录。"
    }
}

$app = Get-Item -LiteralPath (Join-Path $payload 'WeChatVoiceBridge.exe')
$payloadVersion = ($app.VersionInfo.ProductVersion -split '\+', 2)[0]
if ($payloadVersion -ne $version)
{
    throw "发布目录里的程序版本是 $payloadVersion，期望版本是 $version。"
}

# The publish directory is packed recursively. Reject per-user data before compiling.
$privateFiles = @(Get-ChildItem -LiteralPath $payload -Recurse -File -Force | Where-Object {
    $_.Name -match '(?i)\.(?:db|sqlite(?:3)?)(?:-(?:wal|shm|journal))?$|\.(?:dpapi|key)$|^settings(?:[.-].*)?\.json$|^\.env(?:\..*)?$'
})
if ($privateFiles.Count -gt 0)
{
    throw "发布目录内含 $($privateFiles.Count) 个数据库或个人配置文件，已停止打包。请使用干净的发布目录。"
}

# Validate UTF-8 and preserve BOM for Windows PowerShell and the installer sources.
$language = Join-Path $PSScriptRoot 'installer\ChineseSimplified.isl'
$strictUtf8 = New-Object Text.UTF8Encoding($false, $true)
foreach ($textFile in @($iss, $language))
{
    if (-not (Test-Path $textFile)) { throw "缺少文件：$textFile" }
    $bytes = [IO.File]::ReadAllBytes($textFile)
    $content = $strictUtf8.GetString($bytes)
    if ($textFile -eq $language -and $content -notmatch '(?m)^LanguageID=\$0804\r?$')
    {
        throw '简体中文语言包缺少有效的 LanguageID。'
    }
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    if (-not $hasBom)
    {
        [IO.File]::WriteAllText($textFile, $content, (New-Object Text.UTF8Encoding($true)))
        Write-Host "已补 UTF-8 BOM：$textFile"
    }
}

if (-not $Iscc)
{
    $found = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($found)
    {
        $Iscc = $found.Source
    }
    else
    {
        $candidates = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
            'C:\Program Files\Inno Setup 6\ISCC.exe')
        $Iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
}

if (-not $Iscc -or -not (Test-Path $Iscc))
{
    throw '没有找到 Inno Setup 6 的 ISCC.exe。可以用 winget install JRSoftware.InnoSetup 安装。'
}

Write-Host "使用编译器：$Iscc"
& $Iscc "/DAppVersion=$version" "/DPayloadDir=$payload" $iss
if ($LASTEXITCODE -ne 0)
{
    throw "编译失败，退出码 $LASTEXITCODE。"
}

if (-not (Test-Path $setup))
{
    throw '编译结束，但没有找到安装包。'
}

if ($Output)
{
    [IO.File]::Copy($setup, $Output, $true)
    Write-Host "安装包已复制到：$Output"
}

$item = Get-Item $setup
Write-Host ("安装包：{0}（{1:N0} 字节）" -f $item.FullName, $item.Length)
