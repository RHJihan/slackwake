// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SlackWake.Helpers;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace SlackWake.Views;

/// <summary>
/// A modern, Fluent-styled color picker dialog replacing the legacy WinForms
/// <c>ColorDialog</c>. Layout mirrors the Windows 11 spectrum picker: a
/// saturation/value plane, a hue rail, hex + RGB editors, a live preview, and a
/// quick-pick palette.
///
/// The picker works in HSV internally (see <see cref="ColorUtil"/>) — that's the
/// only model where the square + rail map cleanly to drag gestures. RGB is a pure
/// presentation/echo of the same color, so editing any field re-derives HSV and
/// every surface updates in lockstep. <see cref="_syncing"/> guards against the
/// feedback loop that re-entrancy would otherwise cause when we write the text
/// boxes from code.
/// </summary>
public partial class ColorPickerWindow : FluentWindow
{
    // Fixed plane/rail dimensions (the window is non-resizable), so thumb math is
    // deterministic without waiting on a layout pass.
    private const double PlaneWidth = 300;
    private const double PlaneHeight = 200;
    private const double HueWidth = 24;
    private const double HueHeight = 200;

    // High-visibility quick picks: pure black/white (the defaults), saturated
    // primaries/secondaries, and a few neutrals — the colors that actually read as
    // an alert flash.
    private static readonly string[] Presets =
    {
        "#000000", "#FFFFFF", "#FF0000", "#FF7A00", "#FFD400", "#3ACC90",
        "#00C853", "#00B8D4", "#2979FF", "#3D5AFE", "#7C4DFF", "#D500F9",
        "#FF1744", "#FF4081", "#795548", "#9E9E9E",
    };

    private double _hue;          // 0–360
    private double _saturation;   // 0–1
    private double _value;        // 0–1
    private bool _syncing;        // suppress text-box echo while updating from code

    /// <summary>The color the user settled on. Only meaningful once the dialog
    /// returns <c>true</c>.</summary>
    public Color SelectedColor { get; private set; }

    public ColorPickerWindow(Color initial)
    {
        InitializeComponent();
        BuildPresets();

        SelectedColor = initial;
        var (h, s, v) = ColorUtil.ToHsv(initial);
        _hue = h;
        _saturation = s;
        _value = v;

        // First sync happens after layout so ActualWidth/Height are settled and the
        // thumbs land in the right spot.
        Loaded += (_, _) => Refresh();
    }

    private void BuildPresets()
    {
        foreach (var hex in Presets)
        {
            var color = ColorUtil.Parse(hex);
            var swatch = new Button
            {
                Style = (Style)Resources["PresetSwatch"],
                Background = new SolidColorBrush(color),
                ToolTip = hex,
                Tag = color,
            };
            swatch.Click += (_, _) => SetColor((Color)swatch.Tag);
            PresetPanel.Children.Add(swatch);
        }
    }

    // --- State plumbing ----------------------------------------------------

    /// <summary>Adopt a fully-specified color (preset click or valid text entry),
    /// re-deriving the HSV working state, then repaint everything.</summary>
    private void SetColor(Color color)
    {
        var (h, s, v) = ColorUtil.ToHsv(color);
        _hue = h;
        _saturation = s;
        _value = v;
        Refresh();
    }

    /// <summary>Repaint the plane hue, reposition both thumbs, refresh the preview,
    /// and echo the current color into the hex/RGB boxes.</summary>
    private void Refresh()
    {
        var color = ColorUtil.FromHsv(_hue, _saturation, _value);
        SelectedColor = color;

        // Plane base = the pure hue; the overlaid gradients add saturation/value.
        SvHueLayer.Background = new SolidColorBrush(ColorUtil.FromHsv(_hue, 1, 1));

        var planeW = SvBox.ActualWidth > 0 ? SvBox.ActualWidth : PlaneWidth;
        var planeH = SvBox.ActualHeight > 0 ? SvBox.ActualHeight : PlaneHeight;
        Canvas.SetLeft(SvThumb, _saturation * planeW - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - _value) * planeH - SvThumb.Height / 2);

        var hueW = HueBar.ActualWidth > 0 ? HueBar.ActualWidth : HueWidth;
        var hueH = HueBar.ActualHeight > 0 ? HueBar.ActualHeight : HueHeight;
        Canvas.SetLeft(HueThumb, (hueW - HueThumb.Width) / 2);
        Canvas.SetTop(HueThumb, _hue / 360 * hueH - HueThumb.Height / 2);

        PreviewSwatch.Background = new SolidColorBrush(color);
        var contrast = ColorUtil.ContrastingTextColor(color);
        PreviewHex.Foreground = new SolidColorBrush(contrast);
        PreviewHex.Text = ColorUtil.ToHex(color);

        // Echo into the editors, but never clobber the one the user is typing in —
        // rewriting a focused box would reset its caret and could scramble digits
        // mid-entry. The others stay in sync, which is what the user expects.
        _syncing = true;
        if (!HexBox.IsKeyboardFocused) HexBox.Text = ColorUtil.ToHex(color);
        if (!RBox.IsKeyboardFocused) RBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
        if (!GBox.IsKeyboardFocused) GBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
        if (!BBox.IsKeyboardFocused) BBox.Text = color.B.ToString(CultureInfo.InvariantCulture);
        _syncing = false;
    }

    // --- Saturation/Value plane drag ---------------------------------------

    private void SvBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SvBox.CaptureMouse();
        UpdateFromPlane(e.GetPosition(SvBox));
    }

    private void SvBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && SvBox.IsMouseCaptured)
            UpdateFromPlane(e.GetPosition(SvBox));
    }

    private void SvBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => SvBox.ReleaseMouseCapture();

    private void UpdateFromPlane(Point p)
    {
        var w = SvBox.ActualWidth > 0 ? SvBox.ActualWidth : PlaneWidth;
        var h = SvBox.ActualHeight > 0 ? SvBox.ActualHeight : PlaneHeight;
        _saturation = Math.Clamp(p.X / w, 0, 1);
        _value = Math.Clamp(1 - p.Y / h, 0, 1);
        Refresh();
    }

    // --- Hue rail drag -----------------------------------------------------

    private void HueBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HueBar.CaptureMouse();
        UpdateFromHue(e.GetPosition(HueBar));
    }

    private void HueBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && HueBar.IsMouseCaptured)
            UpdateFromHue(e.GetPosition(HueBar));
    }

    private void HueBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => HueBar.ReleaseMouseCapture();

    private void UpdateFromHue(Point p)
    {
        var h = HueBar.ActualHeight > 0 ? HueBar.ActualHeight : HueHeight;
        _hue = Math.Clamp(p.Y / h, 0, 1) * 360;
        Refresh();
    }

    // --- Text editors ------------------------------------------------------

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        // Only commit a complete, valid value — otherwise an intermediate keystroke
        // would yank the working color to black mid-typing.
        if (ColorUtil.TryParseHex(HexBox.Text, out var color))
            SetColor(color);
    }

    private void RgbBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (TryReadByte(RBox.Text, out var r) &&
            TryReadByte(GBox.Text, out var g) &&
            TryReadByte(BBox.Text, out var b))
        {
            SetColor(Color.FromRgb(r, g, b));
        }
    }

    private static bool TryReadByte(string text, out byte value)
    {
        value = 0;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n is >= 0 and <= 255)
        {
            value = (byte)n;
            return true;
        }
        return false;
    }

    // --- Dialog result -----------------------------------------------------

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
