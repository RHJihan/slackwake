// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace SlackWake.Helpers;

/// <summary>
/// Color parsing and contrast helpers for the configurable flash colors. Kept
/// separate so both the settings view-model (for live swatch previews) and the
/// overlay (for the animated foreground/background pair) can reuse them.
/// </summary>
internal static class ColorUtil
{
    /// <summary>Parse a hex color string (#RRGGBB, #AARRGGBB, named, …). Falls back
    /// to black on malformed input so a corrupted settings file can't crash us.</summary>
    public static Color Parse(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Colors.Black;
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Colors.Black;
        }
    }

    /// <summary>"#RRGGBB" string for a Color. Used to round-trip picker output into
    /// settings.</summary>
    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>
    /// Pick black or white — whichever maximizes legibility against <paramref name="background"/>.
    /// Uses the WCAG 2.x relative-luminance formula with the standard 0.179 threshold
    /// recommended for binary fg-color decisions.
    /// </summary>
    public static Color ContrastingTextColor(Color background)
    {
        double r = Channel(background.R / 255.0);
        double g = Channel(background.G / 255.0);
        double b = Channel(background.B / 255.0);
        var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        return luminance > 0.179 ? Colors.Black : Colors.White;
    }

    /// <summary>Return <paramref name="c"/> with its alpha replaced. Used to build
    /// the translucent message-box / hint variants of the contrast color.</summary>
    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static double Channel(double srgb)
        => srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
}
