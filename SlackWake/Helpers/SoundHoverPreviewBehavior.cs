// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Windows;
using System.Windows.Threading;
using SlackWake.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using ItemsControl = System.Windows.Controls.ItemsControl;
using Popup = System.Windows.Controls.Primitives.Popup;
using KeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using KeyboardFocusChangedEventHandler = System.Windows.Input.KeyboardFocusChangedEventHandler;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using ICommand = System.Windows.Input.ICommand;

namespace SlackWake.Helpers;

/// <summary>
/// Attached behavior that turns a sound <see cref="ComboBox"/> into a hover-to-preview
/// picker — the established pattern in the iOS ringtone picker and the Slack/Discord
/// notification-sound pickers. While the dropdown is open:
///   * resting the mouse on an entry previews it,
///   * arrowing onto an entry with the keyboard previews it,
///   * moving to another entry stops the previous preview and starts the new one,
///   * closing the dropdown (whether the user picks an item or dismisses it) stops.
///
/// How items are detected — and why NOT per-container hooks:
///   An earlier version attached MouseEnter/GotKeyboardFocus to each generated
///   <see cref="ComboBoxItem"/> off <c>ItemContainerGenerator.StatusChanged</c>. That is
///   unreliable for a ComboBox: the popup virtualizes, containers aren't always resolvable
///   at the instant the status flips, and <see cref="UIElement.MouseEnter"/> is a direct
///   (non-routed) event so a missed hook means a silent row. Instead we hook the dropdown
///   <see cref="Popup"/> ONCE and listen at its root:
///     * <b>Mouse</b> — <see cref="UIElement.PreviewMouseMove"/> on the popup, resolving the
///       row under the cursor via <see cref="ItemsControl.ContainerFromElement"/>.
///     * <b>Keyboard</b> — <see cref="UIElement.GotKeyboardFocus"/> bubbles up the popup tree,
///       so a single handler catches focus landing on any row as the user arrows. (While the
///       dropdown is OPEN, WPF moves keyboard focus between rows; it does NOT commit
///       SelectedItem until Enter/Tab/click — which is why SelectionChanged alone never
///       previewed on arrow keys.)
///     * <b>SelectionChanged</b> — kept only as a guarded fallback, deduplicated.
///   This is virtualization-proof and independent of container-generation timing.
///
/// Robustness choices:
///   * <b>Debounce.</b> A preview starts only after the highlight rests on a row for
///     <see cref="DelayMillisecondsProperty"/> (default 200 ms). Re-reporting the same row
///     (mouse jitter) does NOT restart the timer, so resting reliably fires. The timer runs
///     at <see cref="DispatcherPriority.Input"/> so it isn't starved while the open popup
///     holds mouse capture.
///   * <b>No blast on open.</b> Opening focuses the already-selected row; we seed it as
///     "already previewing" so it stays silent until the user moves to a different row.
///   * <b>One owner of the audio.</b> Preview lives in the view-model's single player; this
///     behavior only decides <i>when</i>, forwarding the row to <see cref="PreviewCommandProperty"/>.
/// </summary>
public static class SoundHoverPreviewBehavior
{
    /// <summary>Command invoked when the highlight settles on an item; parameter is that item's data context.</summary>
    public static readonly DependencyProperty PreviewCommandProperty =
        DependencyProperty.RegisterAttached(
            "PreviewCommand", typeof(ICommand), typeof(SoundHoverPreviewBehavior),
            new PropertyMetadata(null, OnPreviewCommandChanged));

    public static ICommand? GetPreviewCommand(DependencyObject d) => (ICommand?)d.GetValue(PreviewCommandProperty);
    public static void SetPreviewCommand(DependencyObject d, ICommand? value) => d.SetValue(PreviewCommandProperty, value);

    /// <summary>Command invoked when the dropdown closes, to silence any active preview.</summary>
    public static readonly DependencyProperty StopCommandProperty =
        DependencyProperty.RegisterAttached(
            "StopCommand", typeof(ICommand), typeof(SoundHoverPreviewBehavior),
            new PropertyMetadata(null));

    public static ICommand? GetStopCommand(DependencyObject d) => (ICommand?)d.GetValue(StopCommandProperty);
    public static void SetStopCommand(DependencyObject d, ICommand? value) => d.SetValue(StopCommandProperty, value);

    /// <summary>How long the highlight must rest on an item before its preview starts. Default 200 ms.</summary>
    public static readonly DependencyProperty DelayMillisecondsProperty =
        DependencyProperty.RegisterAttached(
            "DelayMilliseconds", typeof(int), typeof(SoundHoverPreviewBehavior),
            new PropertyMetadata(200));

    public static int GetDelayMilliseconds(DependencyObject d) => (int)d.GetValue(DelayMillisecondsProperty);
    public static void SetDelayMilliseconds(DependencyObject d, int value) => d.SetValue(DelayMillisecondsProperty, value);

    // Per-ComboBox state holder so the static attached property can find the live
    // timer and subscriptions for its owner.
    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State", typeof(HoverState), typeof(SoundHoverPreviewBehavior),
            new PropertyMetadata(null));

    private static void OnPreviewCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox combo) return;

        // Tear down any previous wiring before re-wiring (or clearing).
        if (combo.GetValue(StateProperty) is HoverState old)
        {
            old.Detach();
            combo.SetValue(StateProperty, null);
        }

        if (e.NewValue is ICommand)
        {
            combo.SetValue(StateProperty, new HoverState(combo));
        }
    }

    /// <summary>
    /// Owns the debounce timer and event subscriptions for one ComboBox.
    /// </summary>
    private sealed class HoverState
    {
        private readonly ComboBox _combo;
        private readonly DispatcherTimer _timer;
        private Popup? _popup;
        private SoundLibrary.SoundOption? _pending;  // item awaiting the debounce
        private SoundLibrary.SoundOption? _playing;   // item currently being previewed

        public HoverState(ComboBox combo)
        {
            _combo = combo;
            // Input priority (not the parameterless ctor's Background) so the tick fires
            // promptly even while the open popup holds mouse capture.
            _timer = new DispatcherTimer(DispatcherPriority.Input, combo.Dispatcher);
            _timer.Tick += OnTick;

            combo.DropDownOpened += OnDropDownOpened;
            combo.DropDownClosed += OnDropDownClosed;
            combo.SelectionChanged += OnSelectionChanged;  // guarded fallback only

            // The popup lives in the control template; it exists once the template is applied.
            if (combo.IsLoaded) HookPopup();
            else combo.Loaded += OnComboLoaded;

            Log.Write("[hover] HoverState created");
        }

        public void Detach()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _combo.Loaded -= OnComboLoaded;
            _combo.DropDownOpened -= OnDropDownOpened;
            _combo.DropDownClosed -= OnDropDownClosed;
            _combo.SelectionChanged -= OnSelectionChanged;
            if (_popup != null)
            {
                _popup.PreviewMouseMove -= OnPopupMouseMove;
                _popup.RemoveHandler(UIElement.GotKeyboardFocusEvent, (KeyboardFocusChangedEventHandler)OnPopupGotKeyboardFocus);
                _popup = null;
            }
        }

        private void OnComboLoaded(object? sender, RoutedEventArgs e)
        {
            _combo.Loaded -= OnComboLoaded;
            HookPopup();
        }

        private void HookPopup()
        {
            if (_popup != null) return;

            _combo.ApplyTemplate();
            _popup = _combo.Template?.FindName("PART_Popup", _combo) as Popup;
            if (_popup == null)
            {
                Log.Write("[hover] PART_Popup not found — hover preview unavailable");
                return;
            }

            // PreviewMouseMove tunnels to the popup before children, so we see every move
            // regardless of which inner visual is hit. GotKeyboardFocus bubbles up from the
            // focused row; handledEventsToo=true so we still see it even if ComboBox marks
            // it handled. Both resolve the owning row via ContainerFromElement.
            _popup.PreviewMouseMove += OnPopupMouseMove;
            _popup.AddHandler(UIElement.GotKeyboardFocusEvent, (KeyboardFocusChangedEventHandler)OnPopupGotKeyboardFocus, handledEventsToo: true);
            Log.Write("[hover] popup hooked");
        }

        private void OnDropDownOpened(object? sender, EventArgs e)
        {
            _timer.Stop();
            _pending = null;
            // Opening focuses the selected row, which would otherwise fire GotKeyboardFocus
            // and blast a sound the instant the list appears. Seed it as "already previewing"
            // so it stays silent until the user moves to a different row.
            _playing = _combo.SelectedItem as SoundLibrary.SoundOption;
            Log.Write($"[hover] dropdown opened (seed='{_playing?.DisplayName}')");
        }

        private void OnPopupMouseMove(object sender, MouseEventArgs e)
        {
            if (ResolveOption(e.OriginalSource as DependencyObject) is { } sound)
                QueuePreview(sound, "mouse");
        }

        private void OnPopupGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (ResolveOption(e.NewFocus as DependencyObject) is { } sound)
                QueuePreview(sound, "keyboard");
        }

        private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_combo.IsDropDownOpen) return;  // ignore programmatic/initial selection
            if (_combo.SelectedItem is SoundLibrary.SoundOption sound)
                QueuePreview(sound, "selection");
        }

        /// <summary>Walk up from a hit visual to the owning row and return its sound, or null.</summary>
        private SoundLibrary.SoundOption? ResolveOption(DependencyObject? source)
        {
            if (source == null) return null;
            if (ItemsControl.ContainerFromElement(_combo, source) is ComboBoxItem
                { DataContext: SoundLibrary.SoundOption sound })
                return sound;
            return null;
        }

        private void QueuePreview(SoundLibrary.SoundOption sound, string via)
        {
            // Same row already queued — let the running timer finish (don't reset it, or
            // continuous mouse movement over one row would never settle).
            if (ReferenceEquals(sound, _pending)) return;
            // Same row already previewing and nothing else pending — nothing to do.
            if (ReferenceEquals(sound, _playing) && _pending == null) return;

            Log.Write($"[hover] queue '{sound.DisplayName}' via {via}");
            _pending = sound;
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, GetDelayMilliseconds(_combo)));
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _timer.Stop();
            if (_pending == null) return;
            if (ReferenceEquals(_pending, _playing)) return;  // settled back on current

            _playing = _pending;
            Log.Write($"[hover] preview -> '{_playing.DisplayName}'");
            GetPreviewCommand(_combo)?.Execute(_playing);
        }

        private void OnDropDownClosed(object? sender, EventArgs e)
        {
            _timer.Stop();
            _pending = null;
            _playing = null;
            Log.Write("[hover] dropdown closed -> stop");
            GetStopCommand(_combo)?.Execute(null);
        }
    }
}
