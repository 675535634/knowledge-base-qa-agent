using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using KnowledgeBaseQaAgent.Desktop.ViewModels;

namespace KnowledgeBaseQaAgent.Desktop;

public partial class DesktopPetWindow
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    public event EventHandler? InteractionRequested;
    private System.Windows.Point _mouseDownPoint;
    private bool _dragged;
    private readonly DispatcherTimer _topmostTimer = new();

    public DesktopPetWindow()
    {
        InitializeComponent();
        Left = SystemParameters.WorkArea.Right - Width - 20;
        Top = SystemParameters.WorkArea.Bottom - Height - 20;
        Loaded += (_, _) =>
        {
            UpdateHintPlacement();
            ForceTopMost();
        };
        LocationChanged += (_, _) => UpdateHintPlacement();
        _topmostTimer.Interval = TimeSpan.FromSeconds(1);
        _topmostTimer.Tick += (_, _) => ForceTopMost();
        _topmostTimer.Start();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDownPoint = e.GetPosition(this);
        _dragged = false;
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _mouseDownPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _mouseDownPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragged = true;
        DragMove();
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !_dragged)
        {
            InteractionRequested?.Invoke(this, EventArgs.Empty);
        }

        _dragged = false;
    }

    private void UpdateHintPlacement()
    {
        var workArea = GetCurrentWorkArea();
        var centerX = Left + Width / 2;
        var centerY = Top + Height / 2;
        var nearLeft = centerX < workArea.Left + workArea.Width * 0.34;
        var nearRight = centerX > workArea.Left + workArea.Width * 0.66;
        var nearTop = centerY < workArea.Top + workArea.Height * 0.34;
        var nearBottom = centerY > workArea.Top + workArea.Height * 0.66;

        if (nearRight)
        {
            PlacePetAndHint(petX: 205, petY: 55, hintX: 24, hintY: 118);
        }
        else if (nearLeft)
        {
            PlacePetAndHint(petX: 15, petY: 55, hintX: 224, hintY: 118);
        }
        else if (nearTop)
        {
            PlacePetAndHint(petX: 110, petY: 0, hintX: 127, hintY: 246);
        }
        else if (nearBottom)
        {
            PlacePetAndHint(petX: 110, petY: 104, hintX: 127, hintY: 8);
        }
        else
        {
            PlacePetAndHint(petX: 110, petY: 16, hintX: 127, hintY: 262);
        }
    }

    private void PlacePetAndHint(double petX, double petY, double hintX, double hintY)
    {
        System.Windows.Controls.Canvas.SetLeft(PetContainer, petX);
        System.Windows.Controls.Canvas.SetTop(PetContainer, petY);
        System.Windows.Controls.Canvas.SetLeft(HintBubble, hintX);
        System.Windows.Controls.Canvas.SetTop(HintBubble, hintY);
    }

    private Rect GetCurrentWorkArea()
    {
        try
        {
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(
                (int)Math.Round(Left + Width / 2),
                (int)Math.Round(Top + Height / 2)));
            return new Rect(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    private void OpenVisitorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        InteractionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ForceTopMost()
    {
        Topmost = true;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.D1 || e.Key == Key.NumPad1) && System.Windows.Application.Current is App app)
        {
            e.Handled = true;
            app.RequestAdminLogin(this);
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.ExitApplication();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
