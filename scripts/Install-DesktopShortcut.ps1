param(
  [string]$InstallDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\win-x64-live'),
  [switch]$Remove
)
$ErrorActionPreference = 'Stop'
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcut = Join-Path $desktop '微信语音桥.lnk'
if ($Remove) {
  if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
  Write-Output "removed $shortcut"
  return
}
$executable = Join-Path ([IO.Path]::GetFullPath($InstallDirectory)) 'WeChatVoiceBridge.exe'
if (!(Test-Path -LiteralPath $executable)) { throw "the application is not published at $executable" }
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $executable
$link.WorkingDirectory = Split-Path -Parent $executable
$link.IconLocation = "$executable,0"
$link.Description = 'WeChat Voice Bridge'
$link.WindowStyle = 1
$link.Save()
Write-Output "created $shortcut"
