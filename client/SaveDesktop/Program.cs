using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Enshrouded.SaveCore;
using static Enshrouded.SaveDesktop.Ui;

namespace Enshrouded.SaveDesktop;

internal static class Program
{
    public static RealServices? Real { get; private set; }
    public static string DemoRoot { get; private set; } = "";
    public static bool AlreadyRunning { get; private set; }
    [STAThread]
    public static void Main(string[] args)
    {
        Mutex? instance = null;
        if (OperatingSystem.IsWindows())
        {
            // Windows is the real client. Same data folder as earlier versions, so existing settings and backups carry over.
            var state = new ClientState(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnshroudedSaveManager"));
            Directory.CreateDirectory(state.Root);
            instance = new Mutex(true, "Local\\EnshroudedSaveManager-" + Environment.UserName, out var created); AlreadyRunning = !created;
            Real = new(state, new GameGuard(), Credentials.Read, Credentials.Save);
        }
        else
        {
            // Other platforms run the self-contained demo: a bundled local server and generated sample worlds, never real game files.
            var at = Array.IndexOf(args, "--data-root");
            DemoRoot = at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnshroudedSavePreview");
        }
        try { AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args); }
        finally { instance?.Dispose(); }
    }
}
public sealed class App : Application
{
    public override void Initialize() { Name = "雾锁存档助手"; RequestedThemeVariant = ThemeVariant.Default; Styles.Add(new FluentTheme()); ApplyTheme(Resources); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = Program.AlreadyRunning ? AlreadyRunningWindow() : Program.Real != null ? new MainWindow(Program.Real) : new MainWindow(new DemoBackend(Program.DemoRoot));
        base.OnFrameworkInitializationCompleted();
    }
    private static Window AlreadyRunningWindow()
    {
        var ok = Primary(Btn("知道了")); ok.HorizontalAlignment = HorizontalAlignment.Right;
        var window = new Window { Title = "雾锁存档助手", Width = 380, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = Sans, FontSize = 13 };
        window.Content = Stack(18, Row(12, Mark(32), Text("存档助手已经在运行，请使用已经打开的窗口。", 13.5)), ok); ((StackPanel)window.Content).Margin = new Thickness(24, 22);
        Dyn(window, Window.BackgroundProperty, Surface); ok.Click += (_, _) => window.Close(); return window;
    }
}
