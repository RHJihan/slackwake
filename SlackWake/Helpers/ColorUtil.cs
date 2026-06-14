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
    /// Lenient hex parse for the picker's text field. Accepts "#RRGGBB", "RRGGBB",
    /// "#RGB", and named colors; the leading '#' is optional. Unlike <see cref="Parse"/>
    /// this reports failure instead of silently falling back to black, so the picker
    /// can leave the current color untouched while the user is mid-typing an
    /// incomplete value.
    /// </summary>
    public static bool TryParseHex(string text, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('#')) trimmed = "#" + trimmed;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(trimmed);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Convert an RGB color to Hue (0–360), Saturation (0–1), Value (0–1). HSV is the
    /// space the picker manipulates: the saturation/value plane is the big square and
    /// hue is the side rail, matching the Windows 11 spectrum picker.
    /// </summary>
    public static (double Hue, double Saturation, double Value) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue = 0;
        if (delta > 0)
        {
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * (((b - r) / delta) + 2);
            else hue = 60 * (((r - g) / delta) + 4);
        }
        if (hue < 0) hue += 360;

        double saturation = max <= 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    /// <summary>Inverse of <see cref="ToHsv"/>: build an opaque color from HSV.</summary>
    public static Color FromHsv(double hue, double saturation, double value)
    {
        hue %= 360;
        if (hue < 0) hue += 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        double m = value - chroma;

        double r, g, b;
        switch ((int)(hue / 60) % 6)
        {
            case 0: (r, g, b) = (chroma, x, 0); break;
            case 1: (r, g, b) = (x, chroma, 0); break;
            case 2: (r, g, b) = (0, chroma, x); break;
            case 3: (r, g, b) = (0, x, chroma); break;
            case 4: (r, g, b) = (x, 0, chroma); break;
            default: (r, g, b) = (chroma, 0, x); break;
        }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

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
