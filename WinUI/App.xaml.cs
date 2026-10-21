using Microsoft.UI.Xaml;
namespace Gaius.Office;
public partial class App : Application
{
    private static readonly List<Window> Windows = [];
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Show(new HomeWindow());
    }
    public static void Show(Window window)
    {
        Windows.Add(window);
        window.Closed += (_, _) => Windows.Remove(window);
        window.Activate();
    }
}
