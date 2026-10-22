using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.CompilerServices;

namespace Gaius.Office;

// Async work may finish after a native Window has been destroyed. Never read its
// Content (including from an error handler) once Closed has fired.
internal static class WindowLifetime
{
    private sealed class State
    {
        public bool Closed;
        public readonly CancellationTokenSource Cancellation = new();
        public ContentDialog? Dialog;
    }
    private static readonly ConditionalWeakTable<Window, State> States = new();
    // Every application window uses the same XAML thread. ContentDialog permits
    // only one active dialog on that thread, including across separate windows.
    private static readonly SemaphoreSlim DialogGate = new(1, 1);
    private static State Track(Window window) => States.GetValue(window, owner => {
        var state = new State();
        owner.Closed += (_, _) => { state.Closed = true; state.Cancellation.Cancel(); state.Dialog = null; };
        return state;
    });
    public static void Register(Window window) => Track(window);
    public static bool IsClosed(Window window) => Track(window).Closed;
    public static XamlRoot? Root(Window window) => IsClosed(window) ? null : (window.Content as FrameworkElement)?.XamlRoot;
    public static void DismissDialog(Window window) { var state = Track(window); if (!state.Closed) state.Dialog?.Hide(); }
    public static async Task<ContentDialogResult> ShowDialog(Window window, ContentDialog dialog)
    {
        var state = Track(window);
        try { await DialogGate.WaitAsync(state.Cancellation.Token); }
        catch (OperationCanceledException) { return ContentDialogResult.None; }
        try {
            if (Root(window) is not { } root) return ContentDialogResult.None;
            dialog.XamlRoot = root; state.Dialog = dialog;
            return await dialog.ShowAsync();
        } catch (Exception) when (state.Closed) { return ContentDialogResult.None; }
        finally { if (state.Dialog == dialog) state.Dialog = null; DialogGate.Release(); }
    }
}
