using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SlackWake.Helpers;

/// <summary>
/// Renders the tray icons in-memory so we don't have to ship .ico assets. One bell
/// silhouette, two treatments:
///   - Active:   solid fill (monitoring is on)
///   - Inactive: outlined with a diagonal slash (universal "muted" affordance)
/// Drawn at 32px with anti-aliasing — Windows downsamples to 16px cleanly. Pure
/// white reads on the Win10/11 default dark taskbar.
/// </summary>
internal static class TrayIconFactory
{
    private const int Size = 32;

    public static Icon CreateActive() => Render(active: true);

    public static Icon CreateInactive() => Render(active: false);

    private static Icon Render(bool active)
    {
        using var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            using var bell = BuildBellPath();
            var color = Color.White;

            if (active)
            {
                using var fill = new SolidBrush(color);
                g.FillPath(fill, bell);
            }
            else
            {
                using var stroke = new Pen(color, 2.2f) { LineJoin = LineJoin.Round };
                g.DrawPath(stroke, bell);

                // Punch a transparent gap along the slash line so it sits cleanly
                // through the bell outline instead of doubling up the stroke.
                g.CompositingMode = CompositingMode.SourceCopy;
                using var gap = new Pen(Color.Transparent, 5f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                g.DrawLine(gap, 27f, 5f, 5f, 27f);

                g.CompositingMode = CompositingMode.SourceOver;
                using var slash = new Pen(color, 2.6f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                g.DrawLine(slash, 27f, 5f, 5f, 27f);
            }
        }

        // GetHicon returns an unmanaged handle that Icon.FromHandle does NOT take
        // ownership of — Clone() gives us an Icon that owns its own copy, then we
        // destroy the original handle to avoid the GDI leak.
        var hicon = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hicon).Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hicon);
        }
    }

    private static GraphicsPath BuildBellPath()
    {
        var p = new GraphicsPath();

        // Bell silhouette on a 32x32 canvas, traversed clockwise from the top of
        // the stem. Beziers give the shoulders a soft, professional curve; the
        // base/rim is straight to give the icon a stable visual footing.
        p.StartFigure();
        p.AddLine(14f, 4f, 18f, 4f);                            // stem top
        p.AddLine(18f, 4f, 18f, 7f);                            // stem right
        p.AddBezier(18f, 7f, 23f, 7.5f, 24f, 12f, 24f, 16f);    // right shoulder
        p.AddLine(24f, 16f, 24f, 22f);                          // bell side right
        p.AddLine(24f, 22f, 27f, 24f);                          // right flare
        p.AddLine(27f, 24f, 27f, 25.5f);                        // rim right edge
        p.AddLine(27f, 25.5f, 5f, 25.5f);                       // rim bottom
        p.AddLine(5f, 25.5f, 5f, 24f);                          // rim left edge
        p.AddLine(5f, 24f, 8f, 22f);                            // left flare
        p.AddLine(8f, 22f, 8f, 16f);                            // bell side left
        p.AddBezier(8f, 16f, 8f, 12f, 9f, 7.5f, 14f, 7f);       // left shoulder
        p.AddLine(14f, 7f, 14f, 4f);                            // stem left
        p.CloseFigure();

        // Clapper: small disc just below the rim. Filled separately as its own
        // figure so the closed bell silhouette stays a single shape.
        p.AddEllipse(14.5f, 26.5f, 3f, 3f);

        return p;
    }
}
