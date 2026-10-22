using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using System.Text.RegularExpressions;
namespace Gaius.Office;
public partial class App : Application
{
    private static readonly List<Window> Windows = [];
    private static HomeWindow? home;
    public App() {
        PdfSharp.Fonts.GlobalFontSettings.FontResolver = new WindowsFontResolver(); InitializeComponent();
        var queue = DispatcherQueue.GetForCurrentThread();
        AppInstance.GetCurrent().Activated += (_, args) => queue.TryEnqueue(() => {
            var launch = args.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs;
            ActivateCommand(launch?.Arguments ?? "");
        });
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ActivateCommand(string.Join(" ", Environment.GetCommandLineArgs().Skip(1)));
    }
    private static void ActivateCommand(string command) {
        var match = Regex.Match(command, @"(?:^|\s)--app\s+(document|spreadsheet|presentation|pdf|image)(?:\s|$)");
        if (match.Success) Show(new EditorWindow(OfficeFile.Create(match.Groups[1].Value)));
        else ShowHome();
    }
    public static HomeWindow ShowHome() {
        if (home is null) { home = new HomeWindow(); home.Closed += (_, _) => home = null; Show(home); }
        else { if (home.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized) presenter.Restore(); home.AppWindow.Show(); home.Activate(); }
        return home;
    }
    public static void Show(Window window)
    {
        if (window is HomeWindow && home is not null && home != window) { ShowHome(); window.Close(); return; }
        Windows.Add(window);
        Appearance.Register(window);
        window.Closed += (_, _) => { Windows.Remove(window); if (Windows.Count == 0) AppInstance.GetCurrent().UnregisterKey(); };
        window.Activate();
    }
    public static void RefreshAppIdentities() { foreach (var window in Windows.ToArray()) if (!WindowLifetime.IsClosed(window)) WindowIdentity.Apply(window, window is EditorWindow editor ? editor.Kind : null); }
    public static void ShowToolWindow(Window child, Window owner)
    {
        bool ownerClosed = false, childClosed = false;
        if (owner is EditorWindow editor) WindowIdentity.Apply(child, editor.Kind);
        owner.Closed += (_, _) => { ownerClosed = true; if (!childClosed) child.Close(); };
        child.Closed += (_, _) => { childClosed = true; if (!ownerClosed) owner.Activate(); }; Show(child);
    }
}
