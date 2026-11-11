using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VoiceBridge.Core;

namespace VoiceBridge.App;

/// <summary>
/// The settings card behind a mode's ⋮, in the release notes' look. Every change applies at
/// once (like iOS settings), so 完成 and the close button only close it.
/// </summary>
internal sealed class ModeSettingsDialog : Window
{
    private const string PickSound = "\u0001pick";
    private static readonly int[] Steps = Enumerable.Range(1, 10).Concat(Enumerable.Range(3, 10).Select(step => step * 5))
        .Concat(Enumerable.Range(5, 8).Select(step => step * 15)).Distinct().Order().ToArray();
    private static readonly Brush Accent = Brush("#0B8F66"), Ring = Brush("#B8C2BD"), Hover = Brush("#F3F7F5");

    private readonly ContactCard card;
    private readonly Func<string, Task> previewSound;
    private readonly List<(PrivacyStyle Style, Border Row, Ellipse Dot)> choices = new();
    private TextBox? secret;
    private FrameworkElement? schedule;
    private TextBlock? count, saying;
    private ComboBox? wording;

    public ModeSettingsDialog(ContactCard card, ModeKind kind, Func<string, Task> previewSound)
    {
        this.card = card;
        this.previewSound = previewSound;
        var privacy = kind == ModeKind.Privacy;
        Title = privacy ? "隐私模式" : "提醒模式";
        Width = 500; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 14;

        var panel = new StackPanel { Margin = new(28, 8, 28, 24) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = privacy ? $"{card.Name}来消息时只报信，不读内容" : $"{card.Name}的消息没回时，按这里的方式再提醒你",
            Style = (Style)FindResource("Subtitle"), Margin = new(0, 6, 0, 18), TextTrimming = TextTrimming.CharacterEllipsis
        });
        var box = new Border
        {
            Background = Brushes.White, BorderBrush = Brush("#E4E9E5"), BorderThickness = new(1),
            CornerRadius = new(12), Padding = new(16, 12, 16, 16),
            Child = privacy ? BuildPrivacy() : BuildReminder()
        };
        panel.Children.Add(box);
        var done = new Button
        {
            Content = "完成", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new(0, 20, 0, 0), IsDefault = true, IsCancel = true
        };
        done.Click += (_, _) => Close();
        panel.Children.Add(done);
        DialogShell.Apply(this, panel);
    }

    // ------------------------------------------------------------------ privacy

    private UIElement BuildPrivacy()
    {
        var stack = new StackPanel();
        stack.Children.Add(SwitchRow("开启隐私模式", "开着时，只报信，不读消息内容",
            () => card.PrivacyOn, on => card.PrivacyOn = on));
        stack.Children.Add(Hairline());
        stack.Children.Add(Heading("播报方式"));
        AddChoice(stack, PrivacyStyle.WeChat, "只报来消息", "“微信来消息了”");
        AddChoice(stack, PrivacyStyle.Name, "播报名字", $"“{card.Name}给你发消息了”");
        AddChoice(stack, PrivacyStyle.Secret, "自定义暗语", "随便写，比如「天王盖地虎」就代表来消息了");
        secret = new TextBox { Text = card.Privacy.Secret, MaxLength = 40, Tag = "例如：天王盖地虎", Margin = new(38, 4, 0, 4) };
        AutomationProperties.SetName(secret, "暗语");
        secret.TextChanged += (_, _) =>
        {
            if (card.Privacy.Style == PrivacyStyle.Secret)
                card.UpdateAttention(card.Privacy with { Secret = secret.Text }, card.Reminder);
        };
        stack.Children.Add(secret);
        stack.Children.Add(new TextBlock
        {
            Text = $"几条消息排着队时会合成一句，比如“微信来了 3 条消息”。朗读记录里也只显示“{card.Name}发来一条新消息”。",
            Style = (Style)FindResource("Hint"), Margin = new(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 19
        });
        PaintChoices();
        return stack;
    }

    private void AddChoice(Panel stack, PrivacyStyle style, string title, string example)
    {
        var dot = new Ellipse { Width = 8, Height = 8, Fill = Accent };
        var ring = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new(2, 2, 12, 0) };
        ring.Children.Add(new Ellipse { Stroke = Ring, StrokeThickness = 1.5, Fill = Brushes.White, Tag = "ring" });
        ring.Children.Add(dot);
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = title });
        words.Children.Add(new TextBlock { Text = example, Style = (Style)FindResource("Hint"), Margin = new(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        var layout = new DockPanel();
        DockPanel.SetDock(ring, Dock.Left);
        layout.Children.Add(ring);
        layout.Children.Add(words);
        var row = new Border
        {
            Child = layout, Padding = new(8, 8, 8, 8), CornerRadius = new(9), Background = Brushes.Transparent,
            Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = null
        };
        AutomationProperties.SetName(row, title);
        row.MouseEnter += (_, _) => row.Background = Hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => Choose(style);
        row.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Choose(style); e.Handled = true; } };
        choices.Add((style, row, dot));
        stack.Children.Add(row);
    }

    private void Choose(PrivacyStyle style)
    {
        // A different way of announcing brings back the reminder wording that matches it.
        if (style != card.Privacy.Style)
            card.UpdateAttention(card.Privacy with { Style = style, Secret = secret?.Text ?? card.Privacy.Secret },
                card.Reminder with { Wording = null });
        PaintChoices();
        if (style == PrivacyStyle.Secret && secret is not null)
        {
            secret.Focus();
            secret.SelectAll();
        }
    }

    private void PaintChoices()
    {
        foreach (var (style, row, dot) in choices)
        {
            var chosen = style == card.Privacy.Style;
            dot.Visibility = chosen ? Visibility.Visible : Visibility.Hidden;
            if (((Grid)((DockPanel)row.Child).Children[0]).Children[0] is Ellipse outline)
            {
                outline.Stroke = chosen ? Accent : Ring;
                outline.StrokeThickness = chosen ? 2 : 1.5;
            }
        }
        if (secret is not null)
            secret.Visibility = card.Privacy.Style == PrivacyStyle.Secret ? Visibility.Visible : Visibility.Collapsed;
    }

    // ----------------------------------------------------------------- reminder

    private UIElement BuildReminder()
    {
        var stack = new StackPanel();
        stack.Children.Add(SwitchRow("开启提醒模式", "没回复时，按下面的时间再提醒你",
            () => card.ReminderOn, on => card.ReminderOn = on));
        stack.Children.Add(Hairline());
        stack.Children.Add(Heading("提醒时间"));
        var times = new StackPanel();
        times.Children.Add(StepperRow("每隔", () => card.Reminder.IntervalMinutes, minutes => card.Reminder with
        {
            IntervalMinutes = minutes, DurationMinutes = Math.Max(minutes, card.Reminder.DurationMinutes)
        }));
        times.Children.Add(StepperRow("最多持续", () => card.Reminder.DurationMinutes, minutes => card.Reminder with
        {
            DurationMinutes = minutes, IntervalMinutes = Math.Min(minutes, card.Reminder.IntervalMinutes)
        }));
        count = new TextBlock { Style = (Style)FindResource("Hint"), Margin = new(0, 6, 0, 0) };
        times.Children.Add(count);
        schedule = times;
        stack.Children.Add(times);
        stack.Children.Add(Hairline());
        stack.Children.Add(SwitchRow("持续提醒", "一直提醒，直到你回复或确认已读，不用设上面的时间",
            () => card.Reminder.Continuous, on => Apply(card.Reminder with { Continuous = on })));
        stack.Children.Add(Hairline());
        stack.Children.Add(Heading("提醒音"));
        stack.Children.Add(SoundRow());
        stack.Children.Add(new TextBlock
        {
            Text = "每次提醒先响提醒音，再说提醒词；持续提醒时每隔 5 秒一次。", Style = (Style)FindResource("Hint"),
            Margin = new(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(Hairline());
        stack.Children.Add(Heading("提醒词"));
        wording = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Value" };
        AutomationProperties.SetName(wording, "提醒词");
        wording.SelectionChanged += (_, _) =>
        {
            if (wording.SelectedValue is ReminderWording chosen && chosen != AttentionText.WordingFor(card.Privacy, card.Reminder))
                Apply(card.Reminder with { Wording = chosen });
            PaintReminder();
        };
        stack.Children.Add(wording);
        saying = new TextBlock { Style = (Style)FindResource("Hint"), Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
        stack.Children.Add(saying);
        PaintReminder();
        return stack;
    }

    private void Apply(ReminderSettings reminder)
    {
        card.UpdateAttention(card.Privacy, reminder);
        PaintReminder();
    }

    private void PaintReminder()
    {
        var reminder = card.Reminder;
        if (count is not null)
            count.Text = reminder.Continuous ? "持续提醒开着，这两个时间暂时不用。"
                : $"最短 1 分钟，最长 3 小时。按现在的设置，最多提醒 {reminder.Duration / reminder.Interval} 次。";
        if (schedule is not null)
        {
            schedule.IsEnabled = !reminder.Continuous;
            schedule.Opacity = reminder.Continuous ? 0.45 : 1;
        }
        if (wording is null || saying is null) return;
        var options = AttentionText.WordingChoices(card.Privacy).Select(value => new Labeled(value, value switch
        {
            ReminderWording.Content => "读出没回的消息",
            ReminderWording.WeChat => "只说微信来了几条",
            _ => "说是谁发来了几条"
        })).ToArray();
        var current = AttentionText.WordingFor(card.Privacy, reminder);
        if (wording.Items.Count != options.Length)
            wording.ItemsSource = options;
        if (!Equals(wording.SelectedValue, current))
            wording.SelectedValue = current;
        var minutes = TimeSpan.FromMinutes(reminder.Continuous ? 1 : reminder.Interval);
        saying.Text = "提醒时会说：“" + AttentionText.Reminder(current, card.Name, minutes, 2, ["……"]) + "”" +
            (card.PrivacyOn ? "\n开着隐私模式，所以不会读出消息内容。" : "");
    }

    private UIElement SoundRow()
    {
        var sounds = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Id" };
        AutomationProperties.SetName(sounds, "提醒音");
        void Fill()
        {
            var items = ReminderSounds.BuiltIn.ToList();
            var current = card.Reminder.Sound;
            if (current.Length > 0 && items.All(choice => !choice.Id.Equals(current, StringComparison.OrdinalIgnoreCase)))
                items.Add(new(current, "本机：" + ReminderSounds.LabelOf(current)));
            items.Add(new(PickSound, "选择本机音频…"));
            sounds.ItemsSource = items;
            sounds.SelectedValue = items.FirstOrDefault(choice => choice.Id.Equals(current, StringComparison.OrdinalIgnoreCase))?.Id
                ?? ReminderSettings.DefaultSound;
        }
        Fill();
        sounds.SelectionChanged += (_, _) =>
        {
            if (sounds.SelectedValue is not string chosen || chosen.Equals(card.Reminder.Sound, StringComparison.OrdinalIgnoreCase))
                return;
            if (chosen == PickSound)
            {
                var picker = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择提醒音", Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3", CheckFileExists = true
                };
                if (picker.ShowDialog(this) == true && ReminderSounds.PathOf(picker.FileName) is { } path)
                    Apply(card.Reminder with { Sound = path });
                Dispatcher.BeginInvoke(new Action(Fill));
                return;
            }
            Apply(card.Reminder with { Sound = chosen });
        };
        var play = new Button { Content = "试听", Style = (Style)FindResource("SecondaryButton"), Margin = new(10, 0, 0, 0), MinWidth = 72 };
        AutomationProperties.SetName(play, "试听提醒音");
        play.Click += async (_, _) =>
        {
            play.IsEnabled = false;
            try { await previewSound(card.Reminder.Sound); }
            finally { play.IsEnabled = true; }
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(sounds);
        Grid.SetColumn(play, 1);
        row.Children.Add(play);
        return row;
    }

    private UIElement StepperRow(string label, Func<int> read, Func<int, ReminderSettings> write)
    {
        var value = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        void Show() => value.Text = Minutes(read());
        Button Step(string glyph, int direction, string name)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = glyph, FontSize = 16, Foreground = Brush("#3A4440"), Margin = new(0, -2, 0, 0) },
                Width = 32, Height = 32, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent, BorderThickness = new(0)
            };
            AutomationProperties.SetName(button, label + name);
            button.Click += (_, _) =>
            {
                var now = read();
                var index = Array.FindIndex(Steps, step => step >= now);
                if (index < 0) index = Steps.Length - 1;
                if (direction < 0 && Steps[index] >= now) index--;
                else if (direction > 0 && Steps[index] <= now) index++;
                var next = Steps[Math.Clamp(index, 0, Steps.Length - 1)];
                Apply(write(next));
                Show();
            };
            return button;
        }
        var stepper = new Grid { Width = 168, Height = 32 };
        stepper.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        stepper.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        stepper.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var minus = Step("−", -1, "减少");
        var plus = Step("+", 1, "增加");
        stepper.Children.Add(minus);
        Grid.SetColumn(value, 1); stepper.Children.Add(value);
        Grid.SetColumn(plus, 2); stepper.Children.Add(plus);
        var frame = new Border { Background = Brush("#F0F4F2"), CornerRadius = new(9), Child = stepper };
        var row = new Grid { Margin = new(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(frame, 1); row.Children.Add(frame);
        // Both rows depend on each other (the interval never exceeds the duration).
        LayoutUpdated += (_, _) => { if (value.Text != Minutes(read())) Show(); };
        Show();
        return row;
    }

    // ------------------------------------------------------------------ shared

    private UIElement SwitchRow(string title, string detail, Func<bool> read, Action<bool> write)
    {
        var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = read(), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, title);
        toggle.Checked += (_, _) => { write(true); PaintAll(); };
        toggle.Unchecked += (_, _) => { write(false); PaintAll(); };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) };
        words.Children.Add(new TextBlock { Text = title });
        words.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("Hint"), Margin = new(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        var row = new Grid { Margin = new(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(words);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    private void PaintAll()
    {
        PaintChoices();
        if (wording is not null) PaintReminder();
    }

    private TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 6) };

    private static Border Hairline() => new() { Height = 1, Background = Brush("#E6EBE9"), Margin = new(0, 12, 0, 10) };

    private static string Minutes(int minutes) => minutes < 60 ? $"{minutes} 分钟" :
        minutes % 60 == 0 ? $"{minutes / 60} 小时" : $"{minutes / 60} 小时 {minutes % 60} 分";

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private sealed record Labeled(ReminderWording Value, string Label);
}
