using System.Windows;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using TextCompositionEventArgs = System.Windows.Input.TextCompositionEventArgs;

namespace SlackWake.Views;

/// <summary>
/// Fullscreen, topmost, click-through-to-dismiss notification overlay.
///
/// Notes on multi-monitor: App.ShowOverlay positions one of these per Screen
/// in screen pixels and leaves WindowState=Normal so the explicit Left/Top/Width/Height
/// take effect (Maximized would always pin to the primary display).
/// </summary>
public partial class OverlayWindow : Window
{
    public OverlayWindow(string? sender, string? channel, string? text)
    {
        InitializeComponent();

        SenderText.Text = string.IsNullOrWhiteSpace(sender) ? string.Empty : $"From: {sender}";
        ChannelText.Text = string.IsNullOrWhiteSpace(channel) ? string.Empty : $"Channel: {channel}";
        MessageText.Text = string.IsNullOrWhiteSpace(text) ? "(no preview available)" : text;

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };
    }

    private void Window_KeyDown(object sender, KeyEventArgs e) => Close();
    private void Window_TextInput(object sender, TextCompositionEventArgs e) => Close();
    private void Window_MouseDown(object sender, MouseButtonEventArgs e) => Close();
}
