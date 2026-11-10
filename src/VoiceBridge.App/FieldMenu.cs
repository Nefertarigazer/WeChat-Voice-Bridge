using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace VoiceBridge.App;

/// <summary>
/// One context menu for every text field in the app.
/// </summary>
/// <remarks>
/// Text boxes and password boxes build their own cut/copy/paste menu deep inside WPF.
/// That menu ignores the app's look on some controls and is localised by the framework
/// rather than by the app, which is why a right click could still show a grey boxy menu
/// with English labels. Registering a class handler and supplying our own menu keeps
/// every field — current and future — on the same white rounded panel, with the same
/// Chinese wording and the same shortcuts, and it never touches the commands themselves.
/// </remarks>
internal static class FieldMenu
{
    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(TextBoxBase), FrameworkElement.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(OnOpening));
        EventManager.RegisterClassHandler(typeof(PasswordBox), FrameworkElement.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(OnOpening));
    }

    private static void OnOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Control field || field.ContextMenu is not null)
            return;
        if (!field.IsEnabled)
            return;

        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("RowMenu"),
            // At the pointer, like every other context menu: anchoring it to the whole
            // text box put it far below a wide field and it read as misplaced.
            Placement = PlacementMode.MousePoint,
        };
        if (field is TextBoxBase)
        {
            menu.Items.Add(Item("剪切", ApplicationCommands.Cut, "Ctrl+X", field));
            menu.Items.Add(Item("复制", ApplicationCommands.Copy, "Ctrl+C", field));
        }
        menu.Items.Add(Item("粘贴", ApplicationCommands.Paste, "Ctrl+V", field));
        menu.Items.Add(Item("全选", ApplicationCommands.SelectAll, "Ctrl+A", field));

        field.ContextMenu = menu;
        e.Handled = true;
        // Opening a popup from inside the opening event itself gets swallowed, so the
        // menu is shown right after the event unwinds.
        field.Dispatcher.BeginInvoke(new Action(() => menu.IsOpen = true), DispatcherPriority.Input);
    }

    private static MenuItem Item(string header, RoutedUICommand command, string gesture, Control field) =>
        new()
        {
            Header = header,
            Command = command,
            CommandTarget = field,
            InputGestureText = gesture,
            Style = (Style)Application.Current.FindResource("RowMenuItem"),
        };
}
