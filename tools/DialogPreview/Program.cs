using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VoiceBridge.App;
using VoiceBridge.Audio;
using VoiceBridge.Core;
using Lambda = System.Linq.Expressions.Expression;

// DialogPreview <output folder> [version ...]
// Off screen, with the app's own resources: the release notes for each version (default
// 1.6.0), then the reminder and privacy settings cards of a sample contact. Prints what it
// measured and saves PNGs at 2x. Preview sounds play at volume 0, so nothing is audible.
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "dialog-preview");
var versions = args.Length > 1 ? args[1..] : ["1.6.0"];
Directory.CreateDirectory(output);
var failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
    if (!ok) failures++;
}

var thread = new Thread(() =>
{
    // As in the app, code awaiting in a handler continues on this thread.
    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
    // A plain Application with App.xaml's resources. Never construct App itself: its
    // constructor queues OnStartup, which would knock on a running copy of the app.
    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = AppResources() };
    var owner = new Window
    {
        Width = 1000, Height = 760, Left = -9000, Top = -3000, ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual, WindowStyle = WindowStyle.None
    };
    owner.Show();

    foreach (var version in versions) Centring(version);
    foreach (var version in versions) ReleaseNotes(owner, version);
    Settings(owner);

    owner.Close();
    app.Shutdown();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

// Off screen, WPF pulls the dialog onto the nearest monitor, which hides where it would
// centre. So this check uses an owner in the middle of the work area, with both windows
// fully transparent, click-through and never activated, for well under a second.
void Centring(string version)
{
    var area = SystemParameters.WorkArea;
    var host = new Window
    {
        Width = 1000, Height = 760, Left = area.Left + (area.Width - 1000) / 2, Top = area.Top + (area.Height - 760) / 2,
        WindowStyle = WindowStyle.None, AllowsTransparency = true, Opacity = 0, Background = Brushes.Transparent,
        ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual
    };
    host.Show();
    var dialog = new ReleaseNotesDialog(version) { Owner = host, ShowActivated = false, Opacity = 0 };
    var events = new List<string>();
    dialog.SizeChanged += (_, e) => events.Add($"height {e.NewSize.Height:0.##}");
    dialog.LocationChanged += (_, _) => events.Add($"top {dialog.Top:0.##}");
    dialog.Show();
    Pump(300);
    var centre = host.Top + host.ActualHeight / 2 - dialog.ActualHeight / 2;
    Console.WriteLine($"[{version}] on screen: " + string.Join(" → ", events));
    Check(Math.Abs(dialog.Top - centre) < 1, $"[{version}] centred on its owner (top {dialog.Top:0.##}, centre {centre:0.##})");
    dialog.Close();
    host.Close();
    Pump(100);
}

void ReleaseNotes(Window owner, string version)
{
    var dialog = new ReleaseNotesDialog(version) { Owner = owner };
    var scroller = (ScrollViewer)dialog.FindName("NotesScroller");
    var content = (StackPanel)dialog.FindName("NotesContent");
    dialog.Show();
    Pump(400);
    var bar = (ScrollBar)scroller.Template.FindName("PART_VerticalScrollBar", scroller);
    var tag = "notes-" + version;
    Console.WriteLine($"[{version}] laid out at {VisualTreeHelper.GetDpi(dialog).PixelsPerDip:0.##}x DPI");
    Console.WriteLine($"[{version}] box {scroller.ActualHeight:0.##} (max {scroller.MaxHeight:0.##}), " +
        $"content {scroller.ExtentHeight:0.##}, scrollable {scroller.ScrollableHeight:0.##}");
    Save(dialog, Path.Combine(output, tag + "-1-opened.png"));
    if (scroller.ScrollableHeight <= 0)
    {
        Check(double.IsNaN(scroller.Height), $"[{version}] notes fit: natural height, no cut");
        dialog.Close();
        return;
    }

    // Where the bottom edge cuts: as a fraction of the line it falls in.
    var cut = scroller.ActualHeight;
    var hit = Lines(content).FirstOrDefault(line => line.Top <= cut && cut < line.Top + line.Height);
    if (hit.Block is null) Check(false, $"[{version}] bottom edge cuts a line");
    else
    {
        var fraction = (cut - hit.Top) / hit.Height;
        var words = hit.Block.Text.Length > 14 ? hit.Block.Text[..14] + "…" : hit.Block.Text;
        Console.WriteLine($"[{version}] cut in line {hit.Index + 1} of “{words}” at {fraction:P0} of {hit.Height:0.##}");
        Check(Math.Abs(fraction - 0.5) < 0.05, $"[{version}] bottom edge shows exactly half a line");
    }
    Check(bar.Opacity < 0.01, $"[{version}] scrollbar hidden before scrolling (opacity {bar.Opacity:0.##})");

    scroller.ScrollToVerticalOffset(90);
    Pump(200);
    Check(bar.Opacity > 0.99, $"[{version}] scrollbar shown while scrolling (opacity {bar.Opacity:0.##})");
    Save(dialog, Path.Combine(output, tag + "-2-scrolling.png"));
    Pump(1500);
    Check(bar.Opacity < 0.01, $"[{version}] scrollbar gone 1.5 s after scrolling stops (opacity {bar.Opacity:0.##})");
    Save(dialog, Path.Combine(output, tag + "-3-idle.png"));
    // The app's smooth wheel scrolling moves the notes a little on every frame.
    for (var step = 1; step <= 12; step++)
    {
        scroller.ScrollToVerticalOffset(90 + step * 6);
        Pump(16);
    }
    Check(bar.Opacity > 0.99, $"[{version}] scrollbar fully shown while scrolling frame by frame (opacity {bar.Opacity:0.##})");
    scroller.ScrollToEnd();
    Pump(1500);
    Save(dialog, Path.Combine(output, tag + "-4-end.png"));
    dialog.Close();
    Pump(100);
}

void Settings(Window owner)
{
    var assembly = typeof(ReleaseNotesDialog).Assembly;
    var cardType = assembly.GetType("VoiceBridge.App.ContactCard", true)!;
    var voiceType = assembly.GetType("VoiceBridge.App.VoiceChoice", true)!;
    var dialogType = assembly.GetType("VoiceBridge.App.ModeSettingsDialog", true)!;
    var voices = Activator.CreateInstance(typeof(ObservableCollection<>).MakeGenericType(voiceType))!;
    // ContactCard is internal: build its callbacks for the card type found by reflection.
    var choose = Lambda.Lambda(typeof(Func<,,>).MakeGenericType(cardType, typeof(string), typeof(bool)),
        Lambda.Constant(true), Lambda.Parameter(cardType), Lambda.Parameter(typeof(string))).Compile();
    var changed = Lambda.Lambda(typeof(Action<>).MakeGenericType(cardType),
        Lambda.Empty(), Lambda.Parameter(cardType)).Compile();
    var profile = new ContactProfile
    {
        AccountId = "preview", ConversationId = "wxid_preview", Name = "小林", Selected = true,
        Reminder = new ReminderSettings { Enabled = true }
    };
    var card = Activator.CreateInstance(cardType, profile, voices, choose, changed)!;
    string Read(string property) => (string)cardType.GetProperty(property)!.GetValue(card)!;

    var clock = Stopwatch.StartNew();
    var cancelledAt = 0L;
    var log = new List<string>();
    async Task Preview(string sound, CancellationToken token)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", sound + ".wav");
        var started = clock.ElapsedMilliseconds;
        try
        {
            await new WindowsPlayer("", 0f).PlayAsync(new AudioData(File.ReadAllBytes(path), IsWave: true),
                start => { start(); return true; }, token);
            log.Add($"finished after {clock.ElapsedMilliseconds - started} ms");
        }
        catch (OperationCanceledException)
        {
            log.Add($"stopped {clock.ElapsedMilliseconds - cancelledAt} ms after cancel");
            throw;
        }
    }
    Func<string, CancellationToken, Task> preview = Preview;
    Window Open(ModeKind kind)
    {
        var window = (Window)Activator.CreateInstance(dialogType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new object[] { card, kind, preview }, null)!;
        window.Owner = owner;
        window.Show();
        Pump(400);
        return window;
    }

    Console.WriteLine($"[cards] privacy summary “{Read("PrivacySummary")}”, reminder summary “{Read("ReminderSummary")}”");
    var reminder = Open(ModeKind.Reminder);
    Save(reminder, Path.Combine(output, "settings-1-reminder.png"));
    var sounds = Named<ComboBox>(reminder, "提醒音");
    var words = Named<ComboBox>(reminder, "提醒词");
    var play = Named<Button>(reminder, "试听提醒音");
    Console.WriteLine("[sounds] " + string.Join(" | ", sounds.Items.Cast<object>().Select(Label)));
    Check(Label(sounds.SelectedItem!) == "闹钟 1", $"[sounds] closed box shows the sound in use ({Label(sounds.SelectedItem!)})");
    Check(Label(sounds.Items[^1]!) == "选择本机音频", "[sounds] last item is the 选择本机音频 action");
    Menu(sounds, Path.Combine(output, "settings-2-sound-menu.png"));
    Console.WriteLine("[wording] " + string.Join(" | ", words.Items.Cast<object>().Select(Label)));
    Menu(words, Path.Combine(output, "settings-3-wording-menu.png"));

    Click(play);
    Pump(300);
    Check(Text(play) == "取消" && AutomationProperties.GetName(play) == "取消试听", $"[preview] button reads 取消 while playing ({Text(play)})");
    Save(reminder, Path.Combine(output, "settings-4-previewing.png"));
    cancelledAt = clock.ElapsedMilliseconds;
    Click(play);
    Pump(400);
    Check(Text(play) == "试听", $"[preview] 取消 stops it and the button reads 试听 again ({Text(play)})");
    Console.WriteLine("[preview] " + string.Join("; ", log));
    Check(log.Count == 1 && log[0].StartsWith("stopped"), "[preview] playback was stopped, not left running");

    log.Clear();
    Click(play);
    Pump(300);
    cancelledAt = clock.ElapsedMilliseconds;
    reminder.Close();
    Pump(400);
    Console.WriteLine("[preview] after closing the card: " + string.Join("; ", log));
    Check(log.Count == 1 && log[0].StartsWith("stopped"), "[preview] closing the card stops the sound");

    var privacy = Open(ModeKind.Privacy);
    Save(privacy, Path.Combine(output, "settings-5-privacy.png"));
    Named<CheckBox>(privacy, "开启隐私模式").IsChecked = true;
    Pump(200);
    privacy.Close();
    Console.WriteLine($"[cards] privacy summary “{Read("PrivacySummary")}”");
    var again = Open(ModeKind.Reminder);
    var choices = Named<ComboBox>(again, "提醒词").Items.Cast<object>().Select(Label).ToArray();
    Console.WriteLine("[wording] with privacy on: " + string.Join(" | ", choices));
    Check(!choices.Contains("复述消息内容"), "[wording] privacy on: no 复述消息内容");
    Save(again, Path.Combine(output, "settings-6-reminder-privacy-on.png"));
    again.Close();
}

static ResourceDictionary AppResources()
{
    var folder = new DirectoryInfo(AppContext.BaseDirectory);
    while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "src", "VoiceBridge.App", "App.xaml")))
        folder = folder.Parent;
    if (folder is null) throw new FileNotFoundException("Run DialogPreview from inside the repository.");
    var text = File.ReadAllText(Path.Combine(folder.FullName, "src", "VoiceBridge.App", "App.xaml"));
    const string open = "<Application.Resources>", close = "</Application.Resources>";
    var start = text.IndexOf(open, StringComparison.Ordinal) + open.Length;
    var inner = text[start..text.IndexOf(close, StringComparison.Ordinal)];
    // The converters and attached properties App.xaml uses are public types of the app assembly.
    _ = typeof(SwitchMotion);
    return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(
        "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
        "xmlns:local=\"clr-namespace:VoiceBridge.App;assembly=WeChatVoiceBridge\">" + inner + "</ResourceDictionary>");
}

static IEnumerable<(TextBlock Block, int Index, double Top, double Height)> Lines(StackPanel content)
{
    foreach (var block in Descendants(content).OfType<TextBlock>().Where(block => block.Text != "•"))
    {
        var pitch = double.IsNaN(block.LineHeight) ? block.FontFamily.LineSpacing * block.FontSize : block.LineHeight;
        var count = Math.Max(1, (int)Math.Round(block.ActualHeight / pitch));
        var top = content.Margin.Top + block.TranslatePoint(new Point(), content).Y;
        for (var index = 0; index < count; index++)
            yield return (block, index, top + index * block.ActualHeight / count, block.ActualHeight / count);
    }
}

static T Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
    Descendants(root).OfType<T>().First(element => AutomationProperties.GetName(element) == name);

static IEnumerable<DependencyObject> Descendants(DependencyObject root)
{
    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
        var child = VisualTreeHelper.GetChild(root, index);
        yield return child;
        foreach (var deeper in Descendants(child)) yield return deeper;
    }
}

static string Label(object item) => item.GetType().GetProperty("Label")?.GetValue(item) as string ?? item.ToString() ?? "";

static string Text(Button button) => button.Content as string ?? "";

static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

static void Menu(ComboBox combo, string path)
{
    combo.IsDropDownOpen = true;
    Pump(500);
    var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
    if (popup.Child is FrameworkElement menu)
    {
        Save(menu, path);
        // A long menu opens at the chosen item; its end holds the action row.
        if (Descendants(menu).OfType<ScrollViewer>().FirstOrDefault() is { ScrollableHeight: > 0 } list)
        {
            list.ScrollToEnd();
            Pump(400);
            Save(menu, Path.ChangeExtension(path, null) + "-end.png");
        }
    }
    combo.IsDropDownOpen = false;
    Pump(300);
}

static void Pump(int milliseconds)
{
    var frame = new DispatcherFrame();
    var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
    timer.Start();
    Dispatcher.PushFrame(frame);
}

static void Save(FrameworkElement element, string path)
{
    var root = element is Window { Content: FrameworkElement inner } ? inner : element;
    var size = new Size(root.ActualWidth, root.ActualHeight);
    var visual = new DrawingVisual();
    using (var context = visual.RenderOpen())
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xEB)), null, new Rect(size));
        context.DrawRectangle(new VisualBrush(root), null, new Rect(size));
    }
    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * 2), (int)Math.Ceiling(size.Height * 2), 192, 192, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(path);
    encoder.Save(stream);
    Console.WriteLine($"saved {Path.GetFileName(path)} ({bitmap.PixelWidth}x{bitmap.PixelHeight})");
}
