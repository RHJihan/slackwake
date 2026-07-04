// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Wpf.Ui.Appearance;

namespace SlackWake.Helpers;

/// <summary>
/// A <see cref="ToolStripRenderer"/> that paints a WinForms <see cref="ContextMenuStrip"/>
/// to match the modern Windows 11 flyout style: a flat themed background, a soft rounded
/// highlight that hugs each item, hairline separators, and a clean check glyph instead of
/// the legacy raised checkbox. Purely presentational — it changes nothing about what the
/// menu items do.
/// </summary>
internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    // Win11 flyout metrics: items sit inside a small inset so the rounded highlight
    // never touches the menu edge, matching the system look.
    private const int ItemInset = 4;
    private const int HighlightRadius = 4;
    private const int MenuCornerRadius = 8;

    private readonly bool _dark;
    private readonly Color _background;
    private readonly Color _text;
    private readonly Color _textDisabled;
    private readonly Color _highlight;
    private readonly Color _separator;
    private readonly Color _check;

    public ModernMenuRenderer(bool dark) : base(new ModernColorTable(dark))
    {
        _dark = dark;
        if (dark)
        {
            _background = Color.FromArgb(43, 43, 43);   // #2B2B2B
            _text = Color.FromArgb(255, 255, 255);
            _textDisabled = Color.FromArgb(120, 120, 120);
            _highlight = Color.FromArgb(58, 58, 58);     // subtle lighten on hover
            _separator = Color.FromArgb(64, 64, 64);
            _check = Color.FromArgb(255, 255, 255);
        }
        else
        {
            _background = Color.FromArgb(249, 249, 249); // #F9F9F9
            _text = Color.FromArgb(26, 26, 26);
            _textDisabled = Color.FromArgb(160, 160, 160);
            _highlight = Color.FromArgb(237, 237, 237);  // subtle darken on hover
            _separator = Color.FromArgb(229, 229, 229);
            _check = Color.FromArgb(26, 26, 26);
        }
    }

    /// <summary>
    /// Dresses a context menu in the modern Windows 11 flyout style: themed palette,
    /// rounded item highlights, hairline separators, roomier rows, and DWM-rounded
    /// corners that track the current light/dark system theme. The one entry point for
    /// the menu's appearance — callers only build items and behavior.
    /// </summary>
    public static void Apply(ContextMenuStrip menu)
    {
        var dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        var renderer = new ModernMenuRenderer(dark);

        // Assigning Renderer implicitly sets RenderMode to Custom; do NOT also set
        // RenderMode afterwards or WinForms swaps our renderer back out for the stock
        // (Windows 7-style) gradient one.
        menu.Renderer = renderer;
        menu.DropShadowEnabled = true;
        menu.BackColor = renderer._background;
        menu.Font = ModernFont(9.5f);
        // A small frame inset plus taller rows match native Win11 menu density and give
        // the rounded item highlight room to breathe.
        menu.ImageScalingSize = new Size(16, 16);
        menu.Padding = new Padding(2, 4, 2, 4);

        // Apply row padding as items arrive so callers never repeat it per item.
        menu.ItemAdded += (_, e) =>
        {
            if (e.Item is ToolStripMenuItem item)
                item.Padding = new Padding(0, 4, 0, 4);
        };

        // Rounded corners + dark backdrop need the dropdown's native window, which only
        // exists once it's shown — (re)apply them on each open.
        menu.Opened += (_, _) => renderer.ApplyWindowStyle(menu.Handle);
    }

    /// <summary>Segoe UI Variable Text when present (Win11), gracefully falling back to
    /// Segoe UI on older systems.</summary>
    private static Font ModernFont(float size)
    {
        foreach (var family in new[] { "Segoe UI Variable Text", "Segoe UI" })
        {
            try
            {
                using var test = new FontFamily(family);
                return new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
                // Family not installed — try the next fallback.
            }
        }
        return new Font("Segoe UI", size);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(_background);
    }

    // No hard 3D border — DWM rounds the corners and the flat fill carries the edge.
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
    }

    // The legacy gray image gutter doesn't belong in a modern flyout; blend it into
    // the background so checkmarks float over a flat surface.
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem) return;

        var bounds = new Rectangle(
            ItemInset,
            e.Item.ContentRectangle.Top + 1,
            e.Item.Bounds.Width - (ItemInset * 2),
            e.Item.Bounds.Height - 2);

        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (e.Item.Selected && e.Item.Enabled)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(bounds, HighlightRadius);
            using var brush = new SolidBrush(_highlight);
            e.Graphics.FillPath(brush, path);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _text : _textDisabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var bounds = e.Item.Bounds;
        int y = bounds.Height / 2;
        using var pen = new Pen(_separator);
        e.Graphics.DrawLine(pen, bounds.Left + ItemInset + 4, y, bounds.Right - ItemInset - 4, y);
    }

    // Flat check glyph drawn as a stroke, replacing the legacy boxed checkmark.
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;

        int size = Math.Min(r.Width, r.Height);
        var box = new Rectangle(
            r.Left + (r.Width - size) / 2,
            r.Top + (r.Height - size) / 2,
            size, size);

        var g = e.Graphics;
        var prev = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(_check, Math.Max(1.4f, size / 9f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        var p1 = new PointF(box.Left + size * 0.26f, box.Top + size * 0.52f);
        var p2 = new PointF(box.Left + size * 0.43f, box.Top + size * 0.68f);
        var p3 = new PointF(box.Left + size * 0.74f, box.Top + size * 0.32f);
        g.DrawLines(pen, new[] { p1, p2, p3 });

        g.SmoothingMode = prev;
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Applies the DWM rounded-corner and dark-backdrop attributes to a dropdown once its
    /// native window exists, so the flyout reads as a real Windows 11 menu rather than a
    /// hard-edged WinForms popup.
    /// </summary>
    public void ApplyWindowStyle(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;

        int round = NativeMethods.DWMWCP_ROUNDSMALL;
        NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        int dark = _dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    /// <summary>
    /// Minimal color table so the base professional renderer's few remaining surfaces
    /// (margins, any system-drawn edges) match the flat themed palette above.
    /// </summary>
    private sealed class ModernColorTable : ProfessionalColorTable
    {
        private readonly Color _bg;
        private readonly Color _line;

        public ModernColorTable(bool dark)
        {
            UseSystemColors = false;
            _bg = dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249);
            _line = dark ? Color.FromArgb(64, 64, 64) : Color.FromArgb(229, 229, 229);
        }

        public override Color ToolStripDropDownBackground => _bg;
        public override Color ImageMarginGradientBegin => _bg;
        public override Color ImageMarginGradientMiddle => _bg;
        public override Color ImageMarginGradientEnd => _bg;
        public override Color MenuBorder => _line;
        public override Color MenuItemBorder => _bg;
        public override Color SeparatorDark => _line;
        public override Color SeparatorLight => _line;
    }
}
