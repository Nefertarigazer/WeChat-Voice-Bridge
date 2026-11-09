$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    # Git prints harmless CRLF notices to stderr. Under Stop, Windows PowerShell
    # 5.1 would abort on them; judge the command by its exit code instead.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & git diff --check; $checkExit = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previousPreference }
    if ($checkExit -ne 0) { throw 'Whitespace check failed.' }

    # The scan uses PowerShell's own matcher so it runs on a clean machine,
    # without asking for ripgrep to be installed first. Cover every text file
    # that can end up in a shipped build, not just C# and Python.
    $scanRoots = @((Join-Path $root 'src'), (Join-Path $root 'worker'),
        (Join-Path $root 'scripts'), (Join-Path $PSScriptRoot 'installer'))
    $sources = @(Get-ChildItem -Path $scanRoots -Recurse -File -Include `
        *.cs, *.py, *.xaml, *.ps1, *.iss, *.isl, *.json, *.props, *.config, *.txt |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' -and
            $_.Name -ne 'Verify-Source.ps1' })  # the rule file quotes the forbidden words itself
    $sources += @(Get-Item -LiteralPath (Join-Path $root 'Directory.Build.props'))

    $forbidden = 'WriteProcessMemory|CreateRemoteThread|SetWindowsHookEx|sk-fish-[A-Za-z0-9_-]{12,}|Bearer\s+[A-Za-z0-9_-]{24,}'
    $findings = @($sources | Select-String -Pattern $forbidden |
        Select-Object -ExpandProperty Path -Unique)
    if ($findings) {
        $names = $findings | ForEach-Object { Split-Path -Leaf $_ }
        throw ('Review the flagged source paths: ' + ($names -join ', '))
    }

    # 1.4.7 regression guards: a fresh install must never seed a factory API
    # service, and upgraded settings must always pass through the migration.
    $library = Join-Path $root 'src\VoiceBridge.App\MainWindow.Library.cs'
    $settings = Join-Path $root 'src\VoiceBridge.App\LocalSettings.cs'
    $libraryText = [IO.File]::ReadAllText($library)
    $settingsText = [IO.File]::ReadAllText($settings)
    if ($libraryText -match 'providers\.Insert\s*\(' -or
        $libraryText -match 'SyncDefaultFish|DefaultFishId|默认 Fish 连接') {
        throw 'A factory API service is being seeded again; fresh installs must start with an empty list.'
    }
    if ($settingsText -notmatch 'ProviderMigration\.RemoveFactoryDefault') {
        throw 'LocalSettings.Load() must run the factory-default migration on upgraded settings.'
    }
    $strayIds = @($sources | Where-Object { $_.FullName -match '\\src\\' } |
        Select-String -Pattern 'fish-default' |
        Select-Object -ExpandProperty Path -Unique |
        Where-Object { (Split-Path -Leaf $_) -ne 'ProviderMigration.cs' })
    if ($strayIds.Count -gt 0) {
        throw ('The factory id must only live in ProviderMigration.cs, found in: ' +
            (($strayIds | ForEach-Object { Split-Path -Leaf $_ }) -join ', '))
    }

    $readers = @($sources | Select-String -Pattern 'ReadProcessMemory' |
        Select-Object -ExpandProperty Path -Unique)
    if ($readers.Count -ne 1 -or (Split-Path -Leaf $readers[0]) -ne 'audited_wechat_keys.py') {
        throw 'ReadProcessMemory must remain scoped to the audited worker excerpt.'
    }
    Write-Output 'Static boundary and obvious-secret scan: PASS'
}
finally { Pop-Location }
