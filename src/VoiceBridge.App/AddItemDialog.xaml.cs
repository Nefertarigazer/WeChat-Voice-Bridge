using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace VoiceBridge.App;

/// <summary>
/// A small window that asks for a name and a value. It is used to add a voice
/// and to add an API key without showing either of them in the main window.
/// </summary>
public partial class AddItemDialog : Window
{
    public string ItemName { get; private set; } = "";
    public string ItemValue { get; private set; } = "";

    public AddItemDialog(string title, string nameLabel, string valueLabel, bool secret)
    {
        InitializeComponent();
        Title = title;
        NameLabel.Text = nameLabel;
        ValueLabel.Text = valueLabel;
        SecretBox.Visibility = secret ? Visibility.Visible : Visibility.Collapsed;
        ValueBox.Visibility = secret ? Visibility.Collapsed : Visibility.Visible;
        var body = (UIElement)Content;
        Content = null;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(28,8,28,0) });
        panel.Children.Add(body);
        DialogShell.Apply(this, panel);
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var value = SecretBox.Visibility == Visibility.Visible ? SecretBox.Password.Trim() : ValueBox.Text.Trim();
        if (name.Length == 0)
        {
            Show("请给它起个名字。");
            return;
        }
        if (value.Length == 0)
        {
            Show("这一项不能是空的。");
            return;
        }
        if (SecretBox.Visibility == Visibility.Visible && value.Any(c => c < 33 || c > 126))
        {
            Show("密钥只能包含英文、数字和常见符号。");
            return;
        }
        ItemName = name;
        ItemValue = value;
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Show(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
    }
}
