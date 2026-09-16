using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AiPet.ToolWindow;

public partial class NotificationInbox : UserControl
{
    public NotificationInbox() => InitializeComponent();

    public void FocusQuery()
    {
        NotificationQueryBox.Focus();
        Keyboard.Focus(NotificationQueryBox);
        NotificationQueryBox.SelectAll();
    }

    private void ClearNotificationQuery_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusQuery));

    private void NotificationItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is TodoViewModel vm && vm.OpenSelectedNotificationCommand.CanExecute(null))
            vm.OpenSelectedNotificationCommand.Execute(null);
    }

    private void NotificationInbox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!NotificationItemsList.IsKeyboardFocusWithin || DataContext is not TodoViewModel vm) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.Enter && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (vm.SnoozeSelectedNotificationCommand.CanExecute(null))
                vm.SnoozeSelectedNotificationCommand.Execute(null);
            e.Handled = true;
        }
        else if (key == Key.Enter && modifiers == ModifierKeys.Control)
        {
            if (vm.CompleteSelectedNotificationCommand.CanExecute(null))
                vm.CompleteSelectedNotificationCommand.Execute(null);
            e.Handled = true;
        }
        else if (key is Key.Enter or Key.F2 && modifiers == ModifierKeys.None)
        {
            if (vm.OpenSelectedNotificationCommand.CanExecute(null))
                vm.OpenSelectedNotificationCommand.Execute(null);
            e.Handled = true;
        }
    }
}
