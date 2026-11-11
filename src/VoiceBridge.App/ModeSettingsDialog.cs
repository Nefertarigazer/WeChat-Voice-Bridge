using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
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
    private readonly Func<string, CancellationToken, Task> previewSound;
    private readonly List<(PrivacyStyle Style, Border Row, Ellipse Outline, Ellipse Dot)> choices = new();
    private CancellationTokenSource? playing;
    private TextBox? secret;
    private FrameworkElement? schedule;
    private TextBlock? count, saying;
    private ComboBox? wording;

    public ModeSettingsDialog(ContactCard card, ModeKind kind, Func<string, CancellationToken, Task> previewSound)
    {
        this.card = card;
        this.previewSound = previewSound;
        var privacy = kind == ModeKind.Privacy;
        Title = privacy ? "隐私模式" : "提醒模式";
        Width = 480; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 14;

        var panel = new StackPanel { Margin = new(28, 8, 28, 24) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = privacy ? $"{card.Name}来消息时只报信，不读内容" : $"{card.Name}的消息没回时再提醒你",
            Style = (Style)FindResource("Subtitle"), Margin = new(0, 6, 0, 18), TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new Border
        {
            Background = Brushes.White, BorderBrush = Brush("#E4E9E5"), BorderThickness = new(1),
            CornerRadius = new(12), Padding = new(16, 12, 16, 14),
            Child = privacy ? BuildPrivacy() : BuildReminder()
        });
        var done = new Button
        {
            Content = "完成", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new(0, 20, 0, 0), IsDefault = true, IsCancel = true
        };
        done.Click += (_, _) => Close();
        panel.Children.Add(done);
        DialogShell.Apply(this, panel);
        Closed += (_, _) => playing?.Cancel();
    }

    // ------------------------------------------------------------------ privacy

    private UIElement BuildPrivacy()
    {
        var stack = new StackPanel();
        stack.Children.Add(SwitchRow("开启隐私模式", null, () => card.PrivacyOn, on => card.PrivacyOn = on));
        stack.Children.Add(Hairline());
        stack.Children.Add(Heading("播报方式"));
        AddChoice(stack, PrivacyStyle.WeChat, "只报来消息", "“微信来消息了”");
        AddChoice(stack, PrivacyStyle.Name, "播报名字", $"“{card.Name}给你发消息了”");
        AddChoice(stack, PrivacyStyle.Secret, "自定义暗语", "一句只有你懂的话");
        secret = new TextBox { Text = card.Privacy.Secret, MaxLength = 40, Tag = "例如：天王盖地虎", Margin = new(38, 4, 0, 2) };
        AutomationProperties.SetName(secret, "暗语");
        secret.TextChanged += (_, _) =>
        {
            if (card.Privacy.Style == PrivacyStyle.Secret)
                card.UpdateAttention(card.Privacy with { Secret = secret.Text }, card.Reminder);
        };
        stack.Children.Add(secret);
        PaintChoices();
        return stack;
    }

    private void AddChoice(Panel stack, PrivacyStyle style, string title, string example)
    {
        var outline = new Ellipse { Stroke = Ring, StrokeThickness = 1.5, Fill = Brushes.White };
        var dot = new Ellipse { Width = 8, Height = 8, Fill = Accent };
        var ring = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new(2, 2, 12, 0) };
        ring.Children.Add(outline);
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
            Child = layout, Padding = new(8), CornerRadius = new(9), Background = Brushes.Transparent,
            Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = null
        };
        AutomationProperties.SetName(row, title);
        row.MouseEnter += (_, _) => row.Background = Hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => Choose(style);
        row.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Choose(style); e.Handled = true; } };
        choices.Add((style, row, outline, dot));
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
        foreach (var (style, _, outline, dot) in choices)
        {
            var chosen = style == card.Privacy.Style;
            dot.Visibility = chosen ? Visibility.Visible : Visibility.Hidden;
            outline.Stroke = chosen ? Accent : Ring;
            outline.StrokeThickness = chosen ? 2 : 1.5;
        }
        if (secret is not null)
            secret.Visibility = card.Privacy.Style == PrivacyStyle.Secret ? Visibility.Visible : Visibility.Collapsed;
    }

    // ----------------------------------------------------------------- reminder

    private UIElement BuildReminder()
    {
        var stack = new StackPanel();
        stack.Children.Add(SwitchRow("开启提醒模式", null, () => card.ReminderOn, on => card.ReminderOn = on));
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
        count = new TextBlock { Style = (Style)FindResource("Hint"), Margin = new(0, 4, 0, 0) };
        times.Children.Add(count);
        schedule = times;
        stack.Children.Add(times);
        stack.Children.Add(Hairline());
        stack.Children.Add(SwitchRow("持续提醒", "直到回复或标为已读",
            () => card.Reminder.Continuous, on => Apply(card.Reminder with { Continuous = on })));
        stack.Children.Add(Hairline());
        stack.Children.Add(Heading("提醒音"));
        stack.Children.Add(SoundRow());
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
        saying = new TextBlock { Style = (Style)FindResource("Hint"), Margin = new(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
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
        {
            count.Text = $"共提醒 {reminder.Duration / reminder.Interval} 次";
            count.Visibility = reminder.Continuous ? Visibility.Collapsed : Visibility.Visible;
        }
        if (schedule is not null)
        {
            schedule.IsEnabled = !reminder.Continuous;
            schedule.Opacity = reminder.Continuous ? 0.45 : 1;
        }
        if (wording is null || saying is null) return;
        var options = AttentionText.WordingChoices(card.Privacy).Select(value => new Labeled(value, value switch
        {
            ReminderWording.Content => "复述消息内容",
            ReminderWording.WeChat => "仅播报条数",
            _ => "播报联系人与条数"
        })).ToArray();
        var current = AttentionText.WordingFor(card.Privacy, reminder);
        if (wording.Items.Count != options.Length)
            wording.ItemsSource = options;
        if (!Equals(wording.SelectedValue, current))
            wording.SelectedValue = current;
        var minutes = TimeSpan.FromMinutes(reminder.Continuous ? 1 : reminder.Interval);
        saying.Text = "示例：“" + AttentionText.Reminder(current, card.Name, minutes, 2, ["……"]) + "”";
    }

    private UIElement SoundRow()
    {
        var sounds = new ComboBox { ItemTemplate = SoundTemplate() };
        AutomationProperties.SetName(sounds, "提醒音");
        var items = new List<SoundItem>();
        var filling = false;
        void ShowCurrent()
        {
            filling = true;
            sounds.SelectedItem = items.FirstOrDefault(item => !item.IsAction && Same(item.Id, card.Reminder.Sound))
                ?? items.First(item => item.Id == ReminderSettings.DefaultSound);
            filling = false;
        }
        void Fill()
        {
            var current = card.Reminder.Sound;
            items = ReminderSounds.BuiltIn.Select(choice => new SoundItem(choice.Id, choice.Label, false)).ToList();
            if (current.Length > 0 && items.All(item => !Same(item.Id, current)))
                items.Add(new(current, "本机：" + ReminderSounds.LabelOf(current), false));
            // Like 自定义 in the 提示词 menu: an action under a rule, not one more sound.
            items.Add(new(PickSound, "选择本机音频", true));
            filling = true;
            sounds.ItemsSource = items;
            filling = false;
            ShowCurrent();
        }
        Fill();
        sounds.SelectionChanged += (_, e) =>
        {
            if (filling || e.AddedItems.OfType<SoundItem>().FirstOrDefault() is not { } picked) return;
            if (!picked.IsAction)
            {
                if (!Same(picked.Id, card.Reminder.Sound)) Apply(card.Reminder with { Sound = picked.Id });
                return;
            }
            // The closed box goes straight back to the sound in use; the action only opens the picker.
            ShowCurrent();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var picker = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择提醒音", Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3", CheckFileExists = true
                };
                if (picker.ShowDialog(this) == true && ReminderSounds.PathOf(picker.FileName) is { } path)
                {
                    Apply(card.Reminder with { Sound = path });
                    Fill();
                }
            }));
        };
        var play = new Button { Content = "试听", Style = (Style)FindResource("SecondaryButton"), Margin = new(10, 0, 0, 0), MinWidth = 72 };
        AutomationProperties.SetName(play, "试听提醒音");
        play.Click += async (_, _) =>
        {
            if (playing is not null)
            {
                playing.Cancel();
                return;
            }
            playing = new();
            play.Content = "取消";
            AutomationProperties.SetName(play, "取消试听");
            try { await previewSound(card.Reminder.Sound, playing.Token); }
            catch (OperationCanceledException) { }
            finally
            {
                playing.Dispose();
                playing = null;
                play.Content = "试听";
                AutomationProperties.SetName(play, "试听提醒音");
            }
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(sounds);
        Grid.SetColumn(play, 1);
        row.Children.Add(play);
        return row;
    }

    /// <summary>The 提示词 menu's look: an action item gets a rule above it and a green plus.</summary>
    private static DataTemplate SoundTemplate() => (DataTemplate)XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <StackPanel>
            <Border x:Name="Rule" Height="1" Margin="0,5,0,5" Background="#E6EBE9" Visibility="Collapsed"/>
            <StackPanel Orientation="Horizontal">
              <TextBlock x:Name="Plus" Text="＋" Foreground="#0B8F66" FontWeight="SemiBold" FontSize="15"
                         Margin="0,0,7,0" VerticalAlignment="Center" Visibility="Collapsed"/>
              <TextBlock x:Name="Caption" Text="{Binding Label}" VerticalAlignment="Center"
                         TextWrapping="NoWrap" TextTrimming="CharacterEllipsis"/>
            </StackPanel>
          </StackPanel>
          <DataTemplate.Triggers>
            <DataTrigger Binding="{Binding IsAction}" Value="True">
              <Setter TargetName="Rule" Property="Visibility" Value="Visible"/>
              <Setter TargetName="Plus" Property="Visibility" Value="Visible"/>
              <Setter TargetName="Caption" Property="Foreground" Value="#0B8F66"/>
              <Setter TargetName="Caption" Property="FontWeight" Value="SemiBold"/>
            </DataTrigger>
          </DataTemplate.Triggers>
        </DataTemplate>
        """);

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
                Apply(write(Steps[Math.Clamp(index, 0, Steps.Length - 1)]));
                Show();
            };
            return button;
        }
        var stepper = new Grid { Width = 168, Height = 32 };
        stepper.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        stepper.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        stepper.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        stepper.Children.Add(Step("−", -1, "减少"));
        Grid.SetColumn(value, 1); stepper.Children.Add(value);
        var plus = Step("+", 1, "增加");
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

    private UIElement SwitchRow(string title, string? detail, Func<bool> read, Action<bool> write)
    {
        var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = read(), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, title);
        toggle.Checked += (_, _) => { write(true); PaintAll(); };
        toggle.Unchecked += (_, _) => { write(false); PaintAll(); };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) };
        words.Children.Add(new TextBlock { Text = title });
        if (detail is not null)
            words.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("Hint"), Margin = new(0, 2, 0, 0) });
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

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 6) };

    private static Border Hairline() => new() { Height = 1, Background = Brush("#E6EBE9"), Margin = new(0, 12, 0, 10) };

    private static bool Same(string sound, string other) => sound.Equals(other, StringComparison.OrdinalIgnoreCase);

    private static string Minutes(int minutes) => minutes < 60 ? $"{minutes} 分钟" :
        minutes % 60 == 0 ? $"{minutes / 60} 小时" : $"{minutes / 60} 小时 {minutes % 60} 分";

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private sealed record Labeled(ReminderWording Value, string Label);
    private sealed record SoundItem(string Id, string Label, bool IsAction);
}
