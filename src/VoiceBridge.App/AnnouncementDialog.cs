using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceBridge.Core;

namespace VoiceBridge.App;

internal sealed class AnnouncementDialog : Window
{
    private readonly TextBox phrase;
    private readonly ComboBox position;
    public string Phrase => phrase.Text.Trim();
    public AnnouncementPosition Position => position.SelectedIndex == 1 ? AnnouncementPosition.End : AnnouncementPosition.Beginning;
    public AnnouncementDialog(ContactProfile profile, bool newPhrase = false)
    {
        Title = "自定义提示词"; Width = 420; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 14;
        var panel = new StackPanel { Margin = new(28,8,28,24) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,20) });
        panel.Children.Add(new TextBlock { Text = "提示词", Style = (Style)FindResource("FieldLabel") });
        // A fresh phrase every time: never prefill the last one, so "添加并使用" always adds what is typed now.
        phrase = new TextBox { Text = "", MaxLength = 128, Tag = "例如：小熊说" };
        panel.Children.Add(phrase);
        panel.Children.Add(new TextBlock { Text = "放置位置", Style = (Style)FindResource("FieldLabel"), Margin = new(0,16,0,6) });
        position = new ComboBox { ItemsSource = new[] { "句首", "句尾" }, SelectedIndex = newPhrase ? 0 : (int)profile.CustomPosition };
        panel.Children.Add(position);
        var error = new TextBlock { Foreground = (Brush)FindResource("Danger"), Margin = new(0,10,0,0) };
        panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0,16,0,0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Style = (Style)FindResource("SecondaryButton") };
        cancel.Click += (_, _) => DialogResult = false;
        var add = new Button { Content = "添加并使用", IsDefault = true, Style = (Style)FindResource("PrimaryButton"), Margin = new(10,0,0,0) };
        add.Click += (_, _) => { if (Phrase.Length == 0) { error.Text = "请输入提示词。"; phrase.Focus(); } else DialogResult = true; };
        actions.Children.Add(cancel); actions.Children.Add(add); panel.Children.Add(actions); DialogShell.Apply(this, panel);
        Loaded += (_, _) => { phrase.Focus(); phrase.SelectAll(); };
    }
}
