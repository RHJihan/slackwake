using System.ComponentModel;
using System.Windows;

namespace SlackWake.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Intercept the close button: hide instead of dispose. The app keeps living
    /// in the tray; the user exits via the tray menu. This also preserves the
    /// view-model state across open/close cycles.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
