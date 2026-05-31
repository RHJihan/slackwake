using System.Windows;
using System.Windows.Controls;
using DataObject = System.Windows.DataObject;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using TextCompositionEventArgs = System.Windows.Input.TextCompositionEventArgs;

namespace SlackWake.Helpers;

/// <summary>
/// Attached behavior turning a <see cref="TextBox"/> into a sensible numeric
/// editor for use alongside a slider:
///   * non-digit keystrokes are dropped,
///   * non-digit pasted content is rejected,
///   * pressing Enter commits the current text to the binding source so the
///     view-model's clamping path kicks in and the display snaps to bounds.
///
/// The companion binding should use <c>UpdateSourceTrigger=LostFocus</c> (the
/// WPF default for <c>TextBox.Text</c>) so the user can transit through
/// intermediate invalid states — e.g., deleting the "1" from "100" before
/// typing "2" to make "200" — without being immediately clamped on every
/// keystroke.
/// </summary>
public static class NumericTextBoxBehavior
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(NumericTextBoxBehavior),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        if ((bool)e.NewValue)
        {
            tb.PreviewTextInput += OnPreviewTextInput;
            tb.KeyDown += OnKeyDown;
            DataObject.AddPastingHandler(tb, OnPaste);
        }
        else
        {
            tb.PreviewTextInput -= OnPreviewTextInput;
            tb.KeyDown -= OnKeyDown;
            DataObject.RemovePastingHandler(tb, OnPaste);
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var c in e.Text)
        {
            if (!char.IsDigit(c))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }
        var text = (string)e.DataObject.GetData(typeof(string));
        foreach (var c in text)
        {
            if (!char.IsDigit(c))
            {
                e.CancelCommand();
                return;
            }
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox tb) return;
        // Force a write to the binding source even though focus hasn't moved —
        // hits the VM setter so any clamping resolves and the displayed text
        // snaps to bounds.
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}
