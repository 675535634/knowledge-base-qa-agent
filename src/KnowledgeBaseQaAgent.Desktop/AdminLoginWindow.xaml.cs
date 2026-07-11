using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using KnowledgeBaseQaAgent.Desktop.ViewModels;

namespace KnowledgeBaseQaAgent.Desktop;

public partial class AdminLoginWindow
{
    private readonly MainViewModel _viewModel;

    public AdminLoginWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(_viewModel.LogoPreviewImagePath) && File.Exists(_viewModel.LogoPreviewImagePath))
        {
            Icon = new BitmapImage(new Uri(_viewModel.LogoPreviewImagePath, UriKind.Absolute));
        }

        Loaded += (_, _) => PinBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ValidateAndClose();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void PinBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ValidateAndClose();
        }
    }

    private void ValidateAndClose()
    {
        if (_viewModel.ValidateAdminPin(PinBox.Password))
        {
            DialogResult = true;
            return;
        }

        ErrorText.Text = "PIN 不正确";
        PinBox.Clear();
        PinBox.Focus();
    }
}
