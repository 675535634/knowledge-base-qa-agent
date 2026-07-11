using System.Windows;
using System.Collections.Specialized;
using System.Windows.Input;
using KnowledgeBaseQaAgent.Desktop.ViewModels;

namespace KnowledgeBaseQaAgent.Desktop;

public partial class VisitorWindow
{
    public VisitorWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is MainViewModel oldVm)
            {
                oldVm.Conversation.CollectionChanged -= Conversation_CollectionChanged;
            }

            if (args.NewValue is MainViewModel newVm)
            {
                newVm.Conversation.CollectionChanged += Conversation_CollectionChanged;
            }
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.RequestAdminLogin(this);
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.D1 || e.Key == Key.NumPad1))
        {
            e.Handled = true;
            AdminButton_Click(sender, e);
        }
    }

    private void EndSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ClearVisitorSessionCommand.Execute(null);
        }

        Hide();
    }

    private void QuestionBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift && DataContext is MainViewModel vm)
        {
            e.Handled = true;
            if (vm.AskCommand.CanExecute(null))
            {
                vm.AskCommand.Execute(null);
            }
        }
    }

    private void Conversation_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (ConversationList.Items.Count > 0)
            {
                ConversationList.ScrollIntoView(ConversationList.Items[^1]);
            }
        });
    }
}
