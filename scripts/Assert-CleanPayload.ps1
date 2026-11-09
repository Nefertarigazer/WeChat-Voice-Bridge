<#
.SYNOPSIS
    Rejects a publish directory that carries per-user data or a live API key.

.DESCRIPTION
    Publish.ps1 calls this before writing the ZIP; Build-Installer.ps1 calls it
    before Inno Setup packs the directory. A shipped build must contain only
    shipped files: no settings.json, no DPAPI credential store, no database, and
    no real credential in any text file or assembly.

    Besides generic secret shapes, the check reads this developer machine's own
    encrypted credential store (never printed and never written anywhere) and
    fails the build if any of those exact keys appears in the payload as plain
    text or as a UTF-16 .NET string literal. That is the precise failure mode
    behind the old factory-default API: an install silently pointing at a key
    the customer never entered.

    On a clean CI machine there is no credential store, so that comparison is
    skipped with a warning; the generic shape scan still runs.
#>

function Get-OwnCredentialValues {
    $values = @()
    $dir = Join-Path $env:LOCALAPPDATA 'WeChatVoiceBridge'
    foreach ($name in @('fish-keys.dpapi', 'fish-key.dpapi')) {
        $path = Join-Path $dir $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        try {
            Add-Type -AssemblyName System.Security -ErrorAction Stop
            $blob = [IO.File]::ReadAllBytes($path)
            $clear = [Security.Cryptography.ProtectedData]::Unprotect($blob, $null,
                [Security.Cryptography.DataProtectionScope]::CurrentUser)
            try {
                $text = [Text.Encoding]::UTF8.GetString($clear)
                if ($name -eq 'fish-keys.dpapi') {
                    foreach ($item in @($text | ConvertFrom-Json)) {
                        if ($item.Value) { $values += [string]$item.Value }
                    }
                } elseif ($text.Length -ge 12) {
                    $values += $text
                }
            } finally {
                [Array]::Clear($clear, 0, $clear.Length)
            }
        } catch {
            Write-Warning "Local credential store could not be read for comparison ($name): $($_.Exception.Message)"
        }
    }
    return @($values | Where-Object { $_ -and $_.Length -ge 12 } | Select-Object -Unique)
}

function Assert-CleanPayload {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Payload)

    $root = (Resolve-Path -LiteralPath $Payload).Path
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)

    # Keep this narrow: the bundled CPython runtime legitimately ships cacert.pem.
    # What must never ship is this machine's settings.json, DPAPI store, a .key
    # file, an .env file, or a WeChat database (including WAL/SHM/journal).
    $privatePattern = '(?i)\.(?:db|sqlite3?)(?:-(?:wal|shm|journal))?$|\.(?:dpapi|key)$|^settings(?:[.-].*)?\.json$|^\.env(?:\..*)?$'
    $private = @($files | Where-Object { $_.Name -match $privatePattern })
    if ($private.Count -gt 0) {
        $sample = ($private | Select-Object -First 8 | ForEach-Object { $_.FullName.Substring($root.Length + 1) }) -join ', '
        throw "Clean-payload check FAILED: $($private.Count) per-user file(s) in the publish directory ($sample). Refusing to package."
    }

    # Deliberately narrow: .NET culture tags (sk-SK) and SPDX license ids are not
    # secrets, and third-party assemblies may legitimately contain "Bearer ".
    $shapes = @(
        'sk-fish-[A-Za-z0-9_-]{12,}',
        '(?:api[_-]?key|api[_-]?secret|access[_-]?token)\s*[:=]\s*["'']?[A-Za-z0-9._-]{24,}'
    )
    $own = @(Get-OwnCredentialValues)
    $ownPatterns = @()
    foreach ($value in $own) {
        $ownPatterns += [regex]::Escape($value)
        # A C# string literal lives in the assembly as UTF-16LE. Under a 1:1
        # Latin-1 view of the bytes, that is each character followed by \x00.
        $ownPatterns += (($value.ToCharArray() | ForEach-Object { [regex]::Escape([string]$_) + '\x00' }) -join '')
    }
    if ($own.Count -eq 0) {
        Write-Warning 'No local credential store found: the exact-key comparison was skipped; generic shape scan still applies.'
    }
    $all = @($shapes) + $ownPatterns
    $regex = [regex]::new('(?i)' + ($all -join '|'),
        [System.Text.RegularExpressions.RegexOptions]::Compiled)

    $hits = @()
    $buffer = New-Object byte[] 1048576
    $latin1 = [Text.Encoding]::GetEncoding(28591)
    foreach ($file in $files) {
        $stream = $null
        try {
            $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open,
                [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            $tail = ''
            while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $text = $latin1.GetString($buffer, 0, $read)
                if ($regex.IsMatch($tail + $text)) {
                    $hits += $file.FullName.Substring($root.Length + 1)
                    break
                }
                $tail = $text.Substring([Math]::Max(0, $text.Length - 512))
            }
        } catch {
            throw "Clean-payload check could not read $($file.Name): $($_.Exception.Message)"
        } finally {
            if ($stream) { $stream.Dispose() }
        }
    }
    if ($hits.Count -gt 0) {
        throw "Clean-payload check FAILED: a live API key or credential-shaped string was found in: $($hits -join ', '). Refusing to package."
    }
    Write-Output ("Clean-payload check: PASS ({0} files, no per-user data, no live API key; {1} local credential(s) compared)" -f $files.Count, $own.Count)
}
