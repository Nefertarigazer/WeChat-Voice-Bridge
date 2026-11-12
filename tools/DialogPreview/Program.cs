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

// DialogPreview [--theme Light|Dark/Default|Green|Pink|Blue] <output folder> [version ...]
// Off screen, with the app's own resources: the release notes for each version (default
// 1.6.0), then the reminder and privacy settings cards of a sample contact. Prints what it
// measured and saves PNGs at 2x. Preview sounds play at volume 0, so nothing is audible.
// The theme defaults to the app's own default, 浅色 + 默认.
var appearance = new AppearanceSettings();
if (Array.IndexOf(args, "--theme") is var themeAt and >= 0 && themeAt + 1 < args.Length)
{
    var parts = args[themeAt + 1].Split('/');
    appearance = new AppearanceSettings
    {
        Mode = Enum.Parse<ThemeMode>(parts[0], true),
        Tint = parts.Length > 1 ? Enum.Parse<ThemeTint>(parts[1], true) : ThemeTint.Default
    };
    args = [.. args[..themeAt], .. args[(themeAt + 2)..]];
}
var realClick = args.Length > 0 && args[0] == "--realclick";
if (realClick) args = args[1..];
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
    // As App.OnStartup does: the colors come from the theme, not from App.xaml.
    typeof(ReleaseNotesDialog).Assembly.GetType("VoiceBridge.App.Theme", true)!
        .GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [appearance]);
    Console.WriteLine($"[theme] {appearance.Mode} / {appearance.Tint}");
    var owner = new Window
    {
        Width = 1000, Height = 760, Left = -9000, Top = -3000, ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual, WindowStyle = WindowStyle.None
    };
    owner.Show();

    if (realClick)
    {
        // A real mouse click on 完成, which a raised Click event cannot reproduce. The card is
        // moved so 完成 sits under the pointer where it already is; the pointer never moves,
        // and nothing is clicked unless this window is the one under it.
        // As in the app: a real click on a ⋮ button whose Click handler opens the card with ShowDialog.
        var launcher = new Window
        {
            Width = 140, Height = 80, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Background = Brushes.White
        };
        var more = new Button { Content = "⋮", Width = 60, Height = 40 };
        launcher.Content = more;
        more.Click += (_, _) => Settings(launcher, ("自定义暗语", "two clicks"));
        launcher.Show();
        Pump(300);
        var spot = PresentationSource.FromVisual(launcher)!.CompositionTarget!.TransformFromDevice.Transform(RealMouse.Position());
        launcher.Left = spot.X - 70;
        launcher.Top = spot.Y - 40;
        Pump(300);
        if (RealMouse.Over(new System.Windows.Interop.WindowInteropHelper(launcher).Handle))
        {
            RealMouse.Down(); Pump(90); RealMouse.Up(); Pump(200);
        }
        else Console.WriteLine("[real] launcher: skipped, the pointer is not over it");
        launcher.Close();
    }
    else
    {
        foreach (var version in versions) Centring(version);
        foreach (var version in versions) ReleaseNotes(owner, version);
        Settings(owner);
        Guide();
    }

    owner.Close();
    app.Shutdown();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

// The 使用说明 page at the main window's content width (660 DIP) on the page background.
void Guide()
{
    // No window: a window is capped at the screen's height, and the part beyond it draws badly.
    var page = new Border
    {
        Width = 660 + 68, Padding = new Thickness(34, 24, 34, 24),
        Background = (Brush)Application.Current.Resources["PageBg"], Child = new UserGuide()
    };
    TextOptions.SetTextFormattingMode(page, TextFormattingMode.Display);
    void Layout()
    {
        page.Measure(new Size(page.Width, double.PositiveInfinity));
        page.Arrange(new Rect(new Size(page.Width, page.DesiredSize.Height)));
        page.UpdateLayout();
    }
    Layout();
    // The page opens the newest release itself once loaded; there is no Loaded without a window.
    if (Descendants(page).OfType<Expander>().FirstOrDefault() is { } newest) newest.IsExpanded = true;
    Layout();
    var expanders = Descendants(page).OfType<Expander>().ToArray();
    var more = Descendants(page).OfType<Button>().FirstOrDefault(button => (button.Content as string)?.StartsWith("查看更早") == true);
    Console.WriteLine($"[guide] {page.ActualHeight:0} DIP tall; {expanders.Length} release rows shown; “{more?.Content}”");
    var chips = Descendants(page).OfType<ItemsControl>().Where(list => list.Name.EndsWith("Vendors")).Sum(list => list.Items.Count);
    Check(chips == ProviderCatalog.All.Count, $"[guide] every service in the catalogue has a chip ({chips} of {ProviderCatalog.All.Count})");
    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth * 2), (int)Math.Ceiling(page.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
    bitmap.Render(page);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using (var stream = File.Create(Path.Combine(output, "guide.png"))) encoder.Save(stream);
    Console.WriteLine($"saved guide.png ({bitmap.PixelWidth}x{bitmap.PixelHeight})");
}

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
    var view = (FrameworkElement)scroller.Template.FindName("PART_ScrollContentPresenter", scroller);
    // Alpha of the text mask at its first and last stop: 1 = drawn, 0 = faded out.
    (double First, double Last)? Mask() => view.OpacityMask is LinearGradientBrush { GradientStops: { Count: > 1 } stops }
        ? (stops[0].Color.A / 255.0, stops[^1].Color.A / 255.0) : null;
    if (scroller.ScrollableHeight <= 0)
    {
        Check(double.IsNaN(scroller.Height), $"[{version}] notes fit: natural height, no cut");
        Check(Mask() is null, $"[{version}] notes fit: no fade at either edge");
        dialog.Close();
        return;
    }
    Check(Mask() is (1, 0), $"[{version}] opened: the bottom fades out, the top does not ({Mask()})");

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
    Check(Mask() is (0, 0), $"[{version}] mid-way: both edges fade ({Mask()})");
    scroller.ScrollToEnd();
    Pump(1500);
    Check(Mask() is (0, 1), $"[{version}] at the end: only the top fades ({Mask()})");
    Save(dialog, Path.Combine(output, tag + "-4-end.png"));
    dialog.Close();
    Pump(100);
}

void Settings(Window owner, (string Choice, string Text)? probe = null)
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

    if (probe is { } wanted)
    {
        var card2 = (Window)Activator.CreateInstance(dialogType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new object[] { card, ModeKind.Privacy, preview }, null)!;
        card2.Topmost = true;
        card2.Owner = owner;
        card2.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Modal, as the app opens it: the probe runs inside ShowDialog's loop.
        var outcome = "not run";
        card2.Dispatcher.BeginInvoke(new Action(() => outcome = RunProbe()), DispatcherPriority.ApplicationIdle);
        try { card2.ShowDialog(); }
        catch (Exception error) { outcome += $"; ShowDialog threw {error.GetType().Name}: {error.Message}"; }
        Console.WriteLine(outcome);
        Pump(200);
        return;

        string RunProbe()
        {
        Pump(400);
        var mine = new System.Windows.Interop.WindowInteropHelper(card2).Handle;
        var fromDevice = PresentationSource.FromVisual(card2)!.CompositionTarget!.TransformFromDevice;
        // Slide the card so the given element's centre is under the (unmoved) pointer.
        bool Under(FrameworkElement target)
        {
            var pointer = fromDevice.Transform(RealMouse.Position());
            var offset = target.TranslatePoint(new Point(target.ActualWidth / 2, target.ActualHeight / 2), card2);
            card2.Left = pointer.X - offset.X;
            card2.Top = pointer.Y - offset.Y;
            Pump(300);
            return RealMouse.Over(mine);
        }
        var row = Named<Border>(card2, wanted.Choice);
        if (wanted.Text == "two clicks")
        {
            // As a person does it: a real click on the row first (activates the card,
            // focuses the empty box and the IME), then a real click on 完成.
            if (!Under(row)) { card2.Close(); return "[real] two clicks: skipped, the pointer is not over the card"; }
            RealMouse.Down(); Pump(90); RealMouse.Up(); Pump(800);
            wanted = (wanted.Choice, "");
            // Then a real click into the empty box, where a person would type, with the input
            // method switched to Chinese the way a Chinese user's usually is.
            var field = Descendants(card2).OfType<TextBox>().First();
            if (Under(field)) { RealMouse.Down(); Pump(90); RealMouse.Up(); }
            Pump(300);
            System.Windows.Input.InputMethod.Current.ImeState = System.Windows.Input.InputMethodState.On;
            System.Windows.Input.InputMethod.Current.ImeConversionMode = System.Windows.Input.ImeConversionModeValues.Native;
            Pump(600);
            Console.WriteLine($"[ime] state {System.Windows.Input.InputMethod.Current.ImeState}, mode {System.Windows.Input.InputMethod.Current.ImeConversionMode}");
            // The app's card is not topmost: an input method window may sit above it.
            card2.Topmost = false;
            var finishSpot = Descendants(card2).OfType<Button>().First(button => button.Content as string == "完成");
            var topLeft = finishSpot.PointToScreen(new Point());
            var bottomRight = finishSpot.PointToScreen(new Point(finishSpot.ActualWidth, finishSpot.ActualHeight));
            var box = Descendants(card2).OfType<TextBox>().First();
            var boxTop = box.PointToScreen(new Point());
            for (var wait = 0; wait < 6; wait++)
            {
                Pump(500);
                var screen = finishSpot.PointToScreen(new Point(finishSpot.ActualWidth / 2, finishSpot.ActualHeight / 2));
                Console.WriteLine($"[real] {500 * (wait + 1)} ms after the row click, the window at 完成 is: {RealMouse.Describe(screen)}");
            }
            Console.WriteLine($"[real] 完成 spans {topLeft.X:0},{topLeft.Y:0} – {bottomRight.X:0},{bottomRight.Y:0}; the box starts at {boxTop.X:0},{boxTop.Y:0}");
            foreach (var line in RealMouse.InputMethodWindows()) Console.WriteLine("[ime] " + line);
        }
        else
        {
            row.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            Pump(300);
            if (wanted.Text.Length > 0) Descendants(card2).OfType<TextBox>().First().Text = wanted.Text;
            Pump(500);
        }
        var finish = Descendants(card2).OfType<Button>().First(button => button.Content as string == "完成");
        Under(finish);
        var shut = false;
        card2.Closed += (_, _) => shut = true;
        var focus = System.Windows.Input.Keyboard.FocusedElement?.GetType().Name;
        string said;
        if (!RealMouse.Over(mine)) said = $"[real] {wanted.Choice}/“{wanted.Text}”: skipped, the pointer is not over the card";
        else
        {
            // Like a person: the window handles the press (focus moves, layout settles) before the release.
            RealMouse.Down();
            Pump(90);
            RealMouse.Up();
            Pump(500);
            said = $"[real modal] {wanted.Choice}/“{wanted.Text}” (focus {focus}): 完成 {(shut ? "closed the card" : "did NOT close the card")}";
        }
        if (!shut) card2.Close();
        return said;
        }
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

    // 自定义暗语 picked but left empty: 完成 must still close the card.
    var secretCard = Open(ModeKind.Privacy);
    var secretRow = Named<Border>(secretCard, "自定义暗语");
    secretRow.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
        System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
    Pump(400);
    var focusAfterChoice = System.Windows.Input.Keyboard.FocusedElement?.GetType().Name ?? "nothing";
    var done = Descendants(secretCard).OfType<Button>().First(button => button.Content as string == "完成");
    var middle = done.TranslatePoint(new Point(done.ActualWidth / 2, done.ActualHeight / 2), secretCard);
    var hit = secretCard.InputHitTest(middle) as DependencyObject;
    var hitsDone = hit is not null && (hit == done || done.IsAncestorOf(hit));
    Console.WriteLine($"[secret] style {Read("PrivacySummary")}; 完成 enabled {done.IsEnabled}, visible {done.IsVisible}, " +
        $"hit at its centre: {hit?.GetType().Name ?? "nothing"} (inside 完成: {hitsDone}); focus {System.Windows.Input.Keyboard.FocusedElement?.GetType().Name}");
    Save(secretCard, Path.Combine(output, "settings-7-secret-empty.png"));
    var closed = false;
    secretCard.Closed += (_, _) => closed = true;
    Click(done);
    Pump(300);
    Check(hitsDone, "[secret] nothing covers 完成 when 自定义暗语 is empty");
    Check(closed, "[secret] 完成 closes the card when 自定义暗语 is empty");
    if (!closed) secretCard.Close();
    var style = ((PrivacySettings)cardType.GetProperty("Privacy")!.GetValue(card)!).Style;
    Check(style == PrivacyStyle.WeChat, $"[secret] an empty 自定义暗语 is kept as 只报来消息 ({style})");
    Check(focusAfterChoice != "TextBox", $"[secret] picking 自定义暗语 does not focus the box ({focusAfterChoice})");
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
        // The page the dialog opens over, in the theme in use.
        context.DrawRectangle((Brush)Application.Current.Resources["SidebarBg"], null, new Rect(size));
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

/// <summary>A real left click where the pointer already is (it is never moved).</summary>
internal static class RealMouse
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Spot { public int X, Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Spot spot);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Spot spot);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    public static Point Position() => GetCursorPos(out var spot) ? new Point(spot.X, spot.Y) : new Point();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);

    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, IntPtr data);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Box { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Box box);

    /// <summary>Visible top-level windows of input method processes, with their screen rectangles.</summary>
    public static IEnumerable<string> InputMethodWindows()
    {
        var found = new List<string>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out var pid);
            string process;
            try { process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { return true; }
            if (!System.Text.RegularExpressions.Regex.IsMatch(process, "wetype|ChsIME|TextInputHost|ImeBroker|ctfmon", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
            GetWindowRect(window, out var box);
            var name = new System.Text.StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            found.Add($"{process} ({name}) {box.Left},{box.Top} – {box.Right},{box.Bottom}");
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Which top-level window owns a screen point (device pixels), and whose it is.</summary>
    public static string Describe(Point screen)
    {
        var top = GetAncestor(WindowFromPoint(new Spot { X = (int)screen.X, Y = (int)screen.Y }), 2);
        GetWindowThreadProcessId(top, out var pid);
        var name = new System.Text.StringBuilder(128);
        GetClassName(top, name, name.Capacity);
        string process;
        try { process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { process = "?"; }
        return $"{process} ({name}){(pid == Environment.ProcessId ? " = this card" : "")}";
    }

    /// <summary>Whether the top-level window under the pointer is this one.</summary>
    public static bool Over(IntPtr window) =>
        GetCursorPos(out var spot) && GetAncestor(WindowFromPoint(spot), 2) == window;

    public static void Down() => mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);

    public static void Up() => mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
}
