using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

// Two modes:
//   (no args) / <pid>   drives one "试听这段文字" run and reports the notice.
//   menu <pid> <png>    opens the first service row's "⋮" menu and screenshots it,
//                       so the menu's real rendering can be inspected.
// Exit codes: 0 = ok, 4 = UI element missing, 5 = synthesis error notice,
// 3 = timed out waiting for a result.

if (args.Length > 0 && string.Equals(args[0], "menu", StringComparison.Ordinal))
    return MenuShot(args.Length > 1 ? args[1] : "", args.Length > 2 ? args[2] : "menu.png");

// shot <pid> <png> [navId]: selects a nav page and screenshots the window, so a
// page's real rendering (not a preview mock) can be inspected.
if (args.Length > 0 && string.Equals(args[0], "shot", StringComparison.Ordinal))
    return Shot(args.Length > 1 ? args[1] : "", args.Length > 2 ? args[2] : "shot.png",
                args.Length > 3 ? args[3] : "NavAdvanced");

// menuclick <pid> <item>: opens the ⋮ menu and invokes one item, then reports
// whether the expected dialog appeared (the menu styling must not break clicks).
if (args.Length > 0 && string.Equals(args[0], "menuclick", StringComparison.Ordinal))
    return MenuClick(args.Length > 1 ? args[1] : "", args.Length > 2 ? args[2] : "编辑");

var pid = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 0;
if (pid == 0)
{
    var procs = Process.GetProcessesByName("WeChatVoiceBridge");
    if (procs.Length == 0) { Console.WriteLine("FATAL: WeChatVoiceBridge is not running"); return 4; }
    pid = procs[0].Id;
}

var process = Process.GetProcessById(pid);
process.Refresh();
if (process.MainWindowHandle == IntPtr.Zero)
{
    for (var i = 0; i < 20 && process.MainWindowHandle == IntPtr.Zero; i++)
    {
        Thread.Sleep(1000);
        process.Refresh();
    }
    if (process.MainWindowHandle == IntPtr.Zero) { Console.WriteLine("FATAL: no main window"); return 4; }
}

var root = AutomationElement.FromHandle(process.MainWindowHandle);
Console.WriteLine($"window pid={pid} title=[{root.Current.Name}]");

var navAdvanced = ByAutomationId(root, "NavAdvanced")
    ?? throw new InvalidOperationException("NavAdvanced not found");
(navAdvanced.GetCurrentPattern(SelectionItemPattern.Pattern) as SelectionItemPattern)!.Select();
Thread.Sleep(1500);

var testButton = ByNameContains(root, "试听这段文字", ControlType.Button);
if (testButton is null) { Console.WriteLine("FATAL: 试听 button not found"); return 4; }
Console.WriteLine("invoking 试听这段文字 ...");
(testButton.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern)!.Invoke();
Thread.Sleep(1500);

var navHome = ByAutomationId(root, "NavHome");
(navHome!.GetCurrentPattern(SelectionItemPattern.Pattern) as SelectionItemPattern)!.Select();
Thread.Sleep(500);

var terminal = new Regex("试听完成|遇到异常|配置有误|已取消|失效|失败|错误");
var text = "";
for (var i = 0; i < 90; i++)
{
    Thread.Sleep(1000);
    var notice = ByAutomationId(root, "NoticeText");
    text = notice?.Current.Name ?? "";
    if (terminal.IsMatch(text)) break;
}

Console.WriteLine($"notice: [{text}]");
if (text.Contains("试听完成")) { Console.WriteLine("SMOKE TEST PASS"); return 0; }
if (text.Length == 0) { Console.WriteLine("SMOKE TEST TIMEOUT"); return 3; }
Console.WriteLine("SMOKE TEST ERROR NOTICE");
return 5;

// Opens the first "⋮" menu on the 高级 page and screenshots the popup area.
static int MenuShot(string pidText, string outPath)
{
    var pid = int.TryParse(pidText, out var parsed) && parsed > 0
        ? parsed
        : Process.GetProcessesByName("WeChatVoiceBridge").FirstOrDefault()?.Id ?? 0;
    if (pid == 0) { Console.WriteLine("FATAL: WeChatVoiceBridge is not running"); return 4; }

    var process = Process.GetProcessById(pid);
    for (var i = 0; i < 20 && process.MainWindowHandle == IntPtr.Zero; i++)
    {
        Thread.Sleep(500);
        process.Refresh();
    }
    if (process.MainWindowHandle == IntPtr.Zero) { Console.WriteLine("FATAL: no main window"); return 4; }

    var root = AutomationElement.FromHandle(process.MainWindowHandle);
    var navAdvanced = WaitFor(() => ByAutomationId(root, "NavAdvanced"));
    if (navAdvanced is null) { Console.WriteLine("FATAL: NavAdvanced not found"); return 4; }
    (navAdvanced.GetCurrentPattern(SelectionItemPattern.Pattern) as SelectionItemPattern)!.Select();
    Thread.Sleep(1200);

    var more = WaitFor(() => ByNameContains(root, "⋮", ControlType.Button));
    if (more is null) { Console.WriteLine("FATAL: ⋮ button not found"); return 4; }
    (more.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern)!.Invoke();
    Thread.Sleep(900);

    var menu = AutomationElement.RootElement.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu));
    if (menu is null) { Console.WriteLine("FATAL: menu popup not found"); return 4; }

    var rect = menu.Current.BoundingRectangle;
    var row = more.Current.BoundingRectangle;
    // A little room around the panel so the shadow and the gap are visible.
    var left = (int)Math.Max(0, Math.Min(rect.X, row.X) - 40);
    var top = (int)Math.Max(0, Math.Min(rect.Y, row.Y) - 40);
    var right = (int)Math.Max(rect.Right, row.Right) + 40;
    var bottom = (int)rect.Bottom + 40;
    var w = right - left;
    var h = bottom - top;

    using var bmp = new Bitmap(w, h);
    using (var g = Graphics.FromImage(bmp))
        g.CopyFromScreen(left, top, 0, 0, new Size(w, h));
    bmp.Save(outPath, ImageFormat.Png);

    // Full screen too: it tells whether the capture pipeline itself returns content
    // (a headless/disconnected session comes back blank).
    var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
    using var full = new Bitmap(screen.Width, screen.Height);
    using (var g = Graphics.FromImage(full))
        g.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size);
    full.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(outPath) ?? ".", "menu-shot-full.png"), ImageFormat.Png);

    Console.WriteLine($"menu rect: {rect.X},{rect.Y} {rect.Width}x{rect.Height}");
    Console.WriteLine($"screen: {screen.X},{screen.Y} {screen.Width}x{screen.Height}");
    Console.WriteLine($"saved: {outPath} ({w}x{h})");
    return 0;
}

// Opens a nav page and screenshots the whole window rect.
static int Shot(string pidText, string outPath, string navId)
{
    var pid = int.TryParse(pidText, out var parsed) && parsed > 0
        ? parsed
        : Process.GetProcessesByName("WeChatVoiceBridge").FirstOrDefault()?.Id ?? 0;
    if (pid == 0) { Console.WriteLine("FATAL: WeChatVoiceBridge is not running"); return 4; }

    var process = Process.GetProcessById(pid);
    for (var i = 0; i < 20 && process.MainWindowHandle == IntPtr.Zero; i++)
    {
        Thread.Sleep(500);
        process.Refresh();
    }
    if (process.MainWindowHandle == IntPtr.Zero) { Console.WriteLine("FATAL: no main window"); return 4; }

    var root = AutomationElement.FromHandle(process.MainWindowHandle);
    var nav = WaitFor(() => ByAutomationId(root, navId));
    if (nav is null) { Console.WriteLine($"FATAL: {navId} not found"); return 4; }
    (nav.GetCurrentPattern(SelectionItemPattern.Pattern) as SelectionItemPattern)!.Select();
    Thread.Sleep(2500);

    var rect = root.Current.BoundingRectangle;
    var left = (int)rect.X;
    var top = (int)rect.Y;
    var w = (int)rect.Width;
    var h = (int)rect.Height;
    using var bmp = new Bitmap(w, h);
    using (var g = Graphics.FromImage(bmp))
        g.CopyFromScreen(left, top, 0, 0, new Size(w, h));
    bmp.Save(outPath, ImageFormat.Png);
    Console.WriteLine($"window rect: {left},{top} {w}x{h}");
    Console.WriteLine($"saved: {outPath}");
    return 0;
}

// Opens the ⋮ menu, clicks one item, and checks the dialog it should raise.
static int MenuClick(string pidText, string itemName)
{
    var pid = int.TryParse(pidText, out var parsed) && parsed > 0
        ? parsed
        : Process.GetProcessesByName("WeChatVoiceBridge").FirstOrDefault()?.Id ?? 0;
    if (pid == 0) { Console.WriteLine("FATAL: WeChatVoiceBridge is not running"); return 4; }

    var process = Process.GetProcessById(pid);
    for (var i = 0; i < 20 && process.MainWindowHandle == IntPtr.Zero; i++)
    {
        Thread.Sleep(500);
        process.Refresh();
    }
    var root = AutomationElement.FromHandle(process.MainWindowHandle);
    var navAdvanced = WaitFor(() => ByAutomationId(root, "NavAdvanced"));
    if (navAdvanced is null) { Console.WriteLine("FATAL: NavAdvanced not found"); return 4; }
    (navAdvanced.GetCurrentPattern(SelectionItemPattern.Pattern) as SelectionItemPattern)!.Select();
    Thread.Sleep(1200);

    var more = WaitFor(() => ByNameContains(root, "⋮", ControlType.Button));
    if (more is null) { Console.WriteLine("FATAL: ⋮ button not found"); return 4; }
    (more.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern)!.Invoke();
    Thread.Sleep(900);

    var menu = AutomationElement.RootElement.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu));
    if (menu is null) { Console.WriteLine("FATAL: menu popup not found"); return 4; }

    Console.WriteLine($"⋮ rect: {more.Current.BoundingRectangle}");
    Console.WriteLine($"menu rect: {menu.Current.BoundingRectangle}");
    var target = ByNameContains(menu, itemName, ControlType.MenuItem);
    if (target is null) { Console.WriteLine($"FATAL: menu item [{itemName}] not found"); return 4; }
    var bounds = target.Current.BoundingRectangle;
    // Click near the right edge of the row on purpose: the template's padding must
    // still be hit-testable, not just the two characters of text.
    var x = (int)(bounds.Right - bounds.Width * 0.18);
    var y = (int)(bounds.Y + bounds.Height / 2);
    Console.WriteLine($"clicking [{itemName}] at {x},{y} (row {bounds.Width}x{bounds.Height})");
    MouseClick(x, y);    Thread.Sleep(1500);

    var dialog = AutomationElement.RootElement.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.NameProperty, "编辑语音服务"));
    if (dialog is null)
    {
        Console.WriteLine("diagnostic: top-level windows of this process:");
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(
                     TreeScope.Children, Condition.TrueCondition))
        {
            if (window.Current.ProcessId != pid) continue;
            Console.WriteLine($"  [{window.Current.ControlType.ProgrammaticName}] name=[{window.Current.Name}] " +
                              $"class={window.Current.ClassName} offscreen={window.Current.IsOffscreen}");
        }
        Console.WriteLine("FAIL: 编辑语音服务 dialog did not open");
        return 6;
    }
    Console.WriteLine("PASS: 编辑语音服务 opened");

    var cancel = ByNameContains(dialog, "取消", ControlType.Button);
    if (cancel is not null)
        (cancel.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern)!.Invoke();
    Thread.Sleep(600);
    return 0;
}

// UIA lookups race the page's first render; poll for a few seconds instead of
// assuming the element is already there.
static AutomationElement? WaitFor(Func<AutomationElement?> probe, int seconds = 12)
{
    for (var i = 0; i < seconds * 2; i++)
    {
        var found = probe();
        if (found is not null) return found;
        Thread.Sleep(500);
    }
    return null;
}

static void MouseClick(int x, int y) => Native.Click(x, y);

static AutomationElement? ByAutomationId(AutomationElement parent, string id) =>
    parent.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.AutomationIdProperty, id));

static AutomationElement? ByNameContains(AutomationElement parent, string fragment, ControlType type)
{
    var all = parent.FindAll(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, type));
    foreach (AutomationElement item in all)
        if (item.Current.Name.Contains(fragment, StringComparison.Ordinal))
            return item;
    return null;
}
