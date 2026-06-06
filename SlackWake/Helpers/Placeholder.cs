using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;
using Control = System.Windows.Controls.Control;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;

namespace SlackWake.Helpers;

/// <summary>
/// Attached property that overlays a top-aligned placeholder hint on a
/// <see cref="TextBox"/>.
///
/// WPF-UI's built-in <c>PlaceholderText</c> hardcodes
/// <c>VerticalAlignment="Center"</c> and does not wrap, so on a multi-line box
/// the hint floats in the vertical middle while typed text starts at the top.
/// This draws the hint via an adorner pinned to the top-left of the text area —
/// inside the box's padding and border so it lands exactly where the caret
/// begins — wrapping like real entries, and shown only while the box is empty.
///
/// Usage: <c>&lt;ui:TextBox helpers:Placeholder.Text="Type here…" /&gt;</c>
/// Leave WPF-UI's own <c>PlaceholderText</c> unset when using this.
/// </summary>
public static class Placeholder
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(Placeholder),
            new PropertyMetadata(null, OnPlaceholderTextChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

    // Holds the live adorner for a box so we can update text/visibility without
    // re-creating it and so we don't add a second copy on a property re-set.
    private static readonly DependencyProperty AdornerProperty =
        DependencyProperty.RegisterAttached(
            "Adorner", typeof(PlaceholderAdorner), typeof(Placeholder));

    private static void OnPlaceholderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;

        // Detach any prior wiring before (re)attaching so re-setting the
        // property — or clearing it — doesn't double-subscribe.
        tb.Loaded -= OnLoaded;
        tb.TextChanged -= OnBoxTextChanged;

        if (string.IsNullOrEmpty((string?)e.NewValue))
        {
            if (tb.GetValue(AdornerProperty) is PlaceholderAdorner stale)
            {
                AdornerLayer.GetAdornerLayer(tb)?.Remove(stale);
                tb.ClearValue(AdornerProperty);
            }
            return;
        }

        tb.Loaded += OnLoaded;
        tb.TextChanged += OnBoxTextChanged;

        // The adorner layer only exists once the box is in a rendered visual
        // tree; if it isn't loaded yet, OnLoaded will pick it up.
        if (tb.IsLoaded)
            EnsureAdorner(tb);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) EnsureAdorner(tb);
    }

    private static void OnBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) UpdateVisibility(tb);
    }

    private static void EnsureAdorner(TextBox tb)
    {
        var layer = AdornerLayer.GetAdornerLayer(tb);
        if (layer is null) return;

        if (tb.GetValue(AdornerProperty) is not PlaceholderAdorner adorner)
        {
            adorner = new PlaceholderAdorner(tb);
            tb.SetValue(AdornerProperty, adorner);
            layer.Add(adorner);
        }

        adorner.SetText(GetText(tb));
        UpdateVisibility(tb);
    }

    private static void UpdateVisibility(TextBox tb)
    {
        if (tb.GetValue(AdornerProperty) is PlaceholderAdorner adorner)
            adorner.SetHintVisible(string.IsNullOrEmpty(tb.Text));
    }

    /// <summary>
    /// Renders the placeholder <see cref="TextBlock"/> on top of the box,
    /// offset by the box's padding + border so it aligns with typed text, and
    /// re-flows it to the box's current width.
    /// </summary>
    private sealed class PlaceholderAdorner : Adorner
    {
        private readonly TextBlock _hint;
        private readonly VisualCollection _children;

        public PlaceholderAdorner(TextBox adorned) : base(adorned)
        {
            _hint = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
            };
            // Mirror the box's typography so the hint sits on the same line metrics.
            _hint.SetBinding(TextBlock.FontFamilyProperty, new Binding(nameof(Control.FontFamily)) { Source = adorned });
            _hint.SetBinding(TextBlock.FontSizeProperty, new Binding(nameof(Control.FontSize)) { Source = adorned });
            _hint.SetBinding(TextBlock.FontStyleProperty, new Binding(nameof(Control.FontStyle)) { Source = adorned });
            _hint.SetBinding(TextBlock.FontWeightProperty, new Binding(nameof(Control.FontWeight)) { Source = adorned });
            _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextControlPlaceholderForeground");
            _children = new VisualCollection(this) { _hint };

            // Set last: changing IsHitTestVisible makes WPF walk this adorner's
            // visual children, which reads _children — so it must already exist.
            IsHitTestVisible = false;
        }

        public void SetText(string? text) => _hint.Text = text;

        public void SetHintVisible(bool visible) =>
            _hint.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        protected override int VisualChildrenCount => _children.Count;

        protected override Visual GetVisualChild(int index) => _children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            var tb = (TextBox)AdornedElement;
            _hint.Measure(new Size(InnerWidth(tb), double.PositiveInfinity));
            return tb.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var tb = (TextBox)AdornedElement;
            var x = tb.Padding.Left + tb.BorderThickness.Left;
            var y = tb.Padding.Top + tb.BorderThickness.Top;
            _hint.Arrange(new Rect(x, y, InnerWidth(tb), _hint.DesiredSize.Height));
            return finalSize;
        }

        // Box width minus horizontal padding + border = the room real text gets.
        private static double InnerWidth(TextBox tb) => Math.Max(
            0,
            tb.RenderSize.Width
                - tb.Padding.Left - tb.Padding.Right
                - tb.BorderThickness.Left - tb.BorderThickness.Right);
    }
}
