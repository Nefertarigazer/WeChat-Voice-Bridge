$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    & git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace check failed.' }

    # The scan uses PowerShell's own matcher so it runs on a clean machine,
    # without asking for ripgrep to be installed first.
    $sources = Get-ChildItem -Path (Join-Path $root 'src'), (Join-Path $root 'worker') `
        -Recurse -File -Include *.cs, *.py |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }

    $forbidden = 'WriteProcessMemory|CreateRemoteThread|SetWindowsHookEx|sk-fish-[A-Za-z0-9_-]{12,}|Bearer\s+[A-Za-z0-9_-]{24,}'
    $findings = @($sources | Select-String -Pattern $forbidden |
        Select-Object -ExpandProperty Path -Unique)
    if ($findings) {
        $names = $findings | ForEach-Object { Split-Path -Leaf $_ }
        throw ('Review the flagged source paths: ' + ($names -join ', '))
    }

    $readers = @($sources | Select-String -Pattern 'ReadProcessMemory' |
        Select-Object -ExpandProperty Path -Unique)
    if ($readers.Count -ne 1 -or (Split-Path -Leaf $readers[0]) -ne 'audited_wechat_keys.py') {
        throw 'ReadProcessMemory must remain scoped to the audited worker excerpt.'
    }
    Write-Output 'Static boundary and obvious-secret scan: PASS'
}
finally { Pop-Location }
