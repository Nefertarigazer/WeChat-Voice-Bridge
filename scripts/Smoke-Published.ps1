param([string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\win-x64\WeChatVoiceBridge.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class VoiceBridgeTestWindows
{
    private delegate bool Visitor(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, Visitor visitor, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int limit);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static IntPtr Find(uint thread)
    {
        IntPtr found = IntPtr.Zero;
        EnumThreadWindows(thread, delegate(IntPtr window, IntPtr data)
        {
            var title = new StringBuilder(128);
            GetWindowText(window, title, title.Capacity);
            if (!title.ToString().StartsWith("WeChat Voice Bridge", StringComparison.Ordinal))
                return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
'@

function Wait-Until([scriptblock]$Check) {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt 12) {
        if (& $Check) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'Timed out waiting for the expected WPF or Worker state.'
}

function Find-Control([string]$Id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (!$element) { throw "Missing control $Id" }
    return $element
}

function Invoke-Button([string]$Id) {
    $control = Find-Control $Id
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Get-Workers {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$($app.Id) AND Name='python.exe'"
}

function Open-TestWindow {
    $script:app = Start-Process -FilePath $exePath -WindowStyle Hidden -PassThru
    if (!$app.WaitForInputIdle(12000)) { throw 'WPF did not initialize.' }
    $script:handle = [IntPtr]::Zero
    Wait-Until {
        $app.Refresh()
        if ($app.HasExited) { throw ('Published process exited before showing a window: ' + $app.ExitCode) }
        foreach ($thread in $app.Threads) {
            $script:handle = [VoiceBridgeTestWindows]::Find([uint32]$thread.Id)
            if ($script:handle -ne [IntPtr]::Zero) { return $true }
        }
        return $false
    }
    $script:window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    if (@(Get-Workers).Count -ne 0) { throw 'Worker started before consent.' }
}

function Start-TestSession {
    $consent = Find-Control 'Consent'
    $consent.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Invoke-Button 'ModeButton'
    Wait-Until { (Find-Control 'Inject').Current.IsEnabled -and @(Get-Workers).Count -eq 1 }
}

function Close-TestWindow {
    if (![VoiceBridgeTestWindows]::PostMessage($handle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)) {
        throw 'Could not close the WPF window.'
    }
    if (!$app.WaitForExit(12000) -or $app.ExitCode -ne 0) { throw 'Published app did not exit cleanly.' }
}

$exePath = (Resolve-Path -LiteralPath $Exe).Path
$app = $null
try {
    # All operations are scoped to the test process and its own fixture worker.
    Open-TestWindow
    Start-TestSession
    $firstPid = (Get-Workers).ProcessId
    Invoke-Button 'Inject'
    $results = Find-Control 'Results'
    Wait-Until { $results.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition).Count -gt 0 }
    Invoke-Button 'ModeButton'
    Wait-Until { !(Find-Control 'Inject').Current.IsEnabled -and @(Get-Workers).Count -eq 0 }
    Wait-Until { (Find-Control 'Consent').Current.IsEnabled }
    Start-TestSession
    $lastPid = (Get-Workers).ProcessId
    if ($lastPid -eq $firstPid) { throw 'Worker process was reused.' }
    Close-TestWindow
    if (Get-Process -Id $lastPid -ErrorAction SilentlyContinue) { throw 'Worker remains after WPF window close.' }
    Write-Output 'Published WPF startup, Start, injection, End, restart and window close: PASS'

    $app.Dispose()
    Open-TestWindow
    Start-TestSession
    $worker = Get-Workers
    $app.Kill()
    if (!$app.WaitForExit(12000)) { throw 'Test parent did not exit.' }
    Wait-Until { !(Get-Process -Id $worker.ProcessId -ErrorAction SilentlyContinue) }
    $app.Dispose()
    Open-TestWindow
    Start-Sleep -Milliseconds 350
    if ((Find-Control 'Inject').Current.IsEnabled -or @(Get-Workers).Count -ne 0) {
        throw 'Monitoring resumed without consent after restart.'
    }
    Close-TestWindow
    Write-Output 'Published crash kills Worker and relaunch starts idle: PASS'

    # Remove only the known synthetic files from this crashed test session.
    $match = [regex]::Match($worker.CommandLine, '--fixture\s+(?:"([^"]+)"|(\S+))')
    $directory = ($match.Groups[1].Value + $match.Groups[2].Value)
    $cacheRoot = Join-Path $env:LOCALAPPDATA 'WeChatVoiceBridge\fixtures'
    if (!$match.Success -or (Split-Path -Parent $directory) -ne $cacheRoot -or
        (Split-Path -Leaf $directory) -notmatch '^[0-9a-f]{32}$' -or
        !(Test-Path -LiteralPath (Join-Path $directory 'fixture.lease'))) { throw 'Could not validate the test fixture path.' }
    foreach ($file in (Get-ChildItem -LiteralPath $directory -File)) {
        if ($file.Name -match '^(fixture\.lease|message_[01]\.sqlite(?:-wal|-shm|-journal)?)$') {
            Remove-Item -LiteralPath $file.FullName
        }
    }
    [IO.Directory]::Delete($directory, $false)
}
finally {
    if ($app) {
        if (!$app.HasExited) { $app.Kill(); $app.WaitForExit(3000) | Out-Null }
        $app.Dispose()
    }
}
