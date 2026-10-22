using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gaius.Office;

internal static class SuiteSettings
{
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gaius", "settings.json");
    private static readonly JsonObject Values = Read();
    private static readonly SemaphoreSlim CommitGate = new(1, 1);
    private static bool Flag(string key, bool fallback) => Values[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;
    public static bool SeparateApps { get; private set; } = Flag("separateApps", false);
    public static string Theme { get; private set; } = Values["theme"]?.ToString() is "dark" or "light" ? Values["theme"]!.ToString() : "system";
    public static bool WhitePage { get; private set; } = Flag("whitePage", true);
    public static bool Acrylic { get; private set; } = Flag("acrylic", true);
    private static JsonObject Read() { try { return File.Exists(SettingsPath) ? JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new() : new(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); } }
    public static async Task Show(Window owner)
    {
        if (WindowLifetime.IsClosed(owner)) return;
        var mode = new ToggleSwitch { Header = "Show editors as separate apps", IsOn = SeparateApps, OnContent = "Separate apps", OffContent = "One app" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mode, "Show editors as separate apps");
        var panel = new StackPanel { Spacing = 16, MinWidth = 340 }; panel.Children.Add(mode); panel.Children.Add(UI.Text("Create individual Start menu shortcuts for Write, Sheets, Slides, PDF and Image, and group their file windows separately on the taskbar.", 13));
        var theme = new ComboBox { Header = "Theme", ItemsSource = new[] { "System", "Light", "Dark" }, SelectedIndex = Theme == "dark" ? 2 : Theme == "light" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var white = new ToggleSwitch { Header = "Keep document page white in dark mode", IsOn = WhitePage };
        var acrylic = new ToggleSwitch { Header = "Windows acrylic background", IsOn = Acrylic };
        panel.Children.Add(theme); panel.Children.Add(white); panel.Children.Add(acrylic);
        var dialog = new ContentDialog { Title = "Settings", Content = panel, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" };
        if (await WindowLifetime.ShowDialog(owner, dialog) != ContentDialogResult.Primary || WindowLifetime.IsClosed(owner)) return;
        // Capture the approved values while the dialog is live. Persistence may
        // finish after this owner closes; it must not read native controls then.
        bool separate = mode.IsOn, whitePage = white.IsOn, useAcrylic = acrylic.IsOn;
        string chosenTheme = (theme.SelectedItem as string ?? "System").ToLowerInvariant();
        Exception? failure = null;
        await CommitGate.WaitAsync();
        bool oldSeparate = SeparateApps, shortcutsChanged = false, committed = false;
        string? temp = null;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var next = (JsonObject)Values.DeepClone(); next["separateApps"] = separate; next["theme"] = chosenTheme; next["whitePage"] = whitePage; next["acrylic"] = useAcrylic;
            temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temp, next.ToJsonString());
            if (separate != SeparateApps) { shortcutsChanged = true; UpdateShortcuts(separate); }
            File.Move(temp, SettingsPath, true);
            SeparateApps = separate; Theme = chosenTheme; WhitePage = whitePage; Acrylic = useAcrylic;
            foreach (var pair in next) Values[pair.Key] = pair.Value?.DeepClone();
            committed = true;
            App.RefreshAppIdentities(); Appearance.Refresh();
        } catch (Exception error) {
            if (shortcutsChanged && !committed) try { UpdateShortcuts(oldSeparate); } catch (Exception) { /* Preserve the original failure. */ }
            failure = error;
        } finally {
            if (temp is not null) try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            CommitGate.Release();
        }
        if (failure is not null && !WindowLifetime.IsClosed(owner)) await UI.Error(owner, failure);
    }
    private static void UpdateShortcuts(bool separate)
    {
        var folder = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Gaius Office")); Directory.CreateDirectory(folder);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcuts are unavailable.");
        var shell = Activator.CreateInstance(shellType)!;
        try {
            foreach (string kind in UI.Kinds.Append("suite")) {
                var path = Path.GetFullPath(Path.Combine(folder, kind == "suite" ? "Gaius Office.lnk" : $"Gaius {UI.Labels[kind]}.lnk"));
                if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid shortcut path.");
                bool enabled = kind == "suite" ? !separate : separate;
                if (!enabled) { if (File.Exists(path)) File.Delete(path); continue; }
                dynamic shortcut = ((dynamic)shell).CreateShortcut(path);
                try { shortcut.TargetPath = Environment.ProcessPath!; shortcut.WorkingDirectory = AppContext.BaseDirectory; shortcut.Arguments = kind == "suite" ? "" : "--app " + kind; shortcut.Description = kind == "suite" ? "Gaius Office" : "Gaius " + UI.Labels[kind]; shortcut.IconLocation = WindowIdentity.Icon(kind) + ",0"; shortcut.Save(); } finally { Marshal.FinalReleaseComObject(shortcut); }
                WindowIdentity.SetShortcutId(path, kind);
            }
        } finally { Marshal.FinalReleaseComObject(shell); }
    }
}
