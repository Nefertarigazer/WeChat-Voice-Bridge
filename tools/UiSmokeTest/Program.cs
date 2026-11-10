using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Automation;

// Drives the real WeChatVoiceBridge window through one "试听这段文字" run:
//   高级 -> invoke 试听这段文字 -> 首页 -> read the NoticeText result.
// Exit codes: 0 = 试听完成, 4 = UI element missing, 5 = synthesis error notice,
// 3 = timed out waiting for a result.

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
