// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System.ComponentModel;
using Wpf.Ui.Controls;

namespace SlackWake.Views;

public partial class MainWindow : FluentWindow
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
