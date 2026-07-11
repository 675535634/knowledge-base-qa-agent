using KnowledgeBaseQaAgent.Desktop.ViewModels;

namespace KnowledgeBaseQaAgent.Desktop;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        await Task.CompletedTask;
    }
}
