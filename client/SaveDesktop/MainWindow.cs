using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Enshrouded.SaveCore;
using static Enshrouded.SaveDesktop.Ui;

namespace Enshrouded.SaveDesktop;

// Everything real mode needs from the host. Windows passes Credential Manager and the game process guard;
// tests pass in-memory stand-ins and allow a loopback HTTP server.
public sealed record RealServices(ClientState State, IGameGuard Guard, Func<string, string> ReadToken, Action<string, string> SaveToken, bool AllowLoopback = false);

public sealed class MainWindow : Window
{
    private const int PageSize = 4;
    private enum Tone { Neutral, Busy, Attention, Good, Problem, Playing }
    private readonly DemoBackend? demo;
    private readonly RealServices? real;
    private readonly IGameGuard guard;
    private readonly SaveScanner scanner;
    private readonly string dataRoot;
    private ClientState state;
    private ClientSettings settings = new();
    private VaultClient? api;
    private LocalCheck? check;
    private MeResponse? me;
    private bool busy, ready, resetting, gameRunning;
    private int page = 1, total;
    private long snapshot;
    private string user = "me";
    private readonly TextBlock localTitle = Text("正在准备本地存档", 15, true), localNote = Text("请稍候…", 12, color: Muted), checkTime = Text("尚未检查", 11, color: Faint), status = Text("正在启动…", 12), pageText = Text("", 12, color: Muted), hint = Text("还没有选择云端存档", 11, color: Faint), libraryCount = Text("", 11, true, Muted), worldText = Text("", 12, color: Muted);
    private readonly Button checkButton = Btn("检查存档", Icons.Refresh), upload = Btn("上传我的存档", Icons.Upload), use = Btn("使用选中的存档", Icons.Download), useQuiet = Btn("使用选中的存档", Icons.Download), refresh = Btn("刷新", Icons.Refresh), previous = Btn("上一页", Icons.Left), next = Btn("下一页", Icons.Right, iconAfter: true), simulate = Btn("模拟玩完", Icons.Play), settingsButton = Btn("设置", Icons.Tune), backupsButton = Btn("本地备份", Icons.Folder), themeButton = Btn("");
    private readonly Button[] actors = [Btn("我"), Btn("A"), Btn("B")];
    private readonly Border[] actorPills = [new(), new(), new()];
    private readonly Border identityPill = Pill("未连接", Muted, Raised);
    private readonly TextBox search = new() { Watermark = "搜索名称、备注或上传人", MinHeight = 38, FontSize = 13, Padding = new Thickness(6, 9, 10, 9), VerticalContentAlignment = VerticalAlignment.Center, InnerLeftContent = Glyph(Icons.Search, 15, Faint, new Thickness(12, 0, 2, 0)) };
    private readonly ComboBox sort = new() { ItemsSource = new[] { "最新上传", "最早上传", "名称顺序" }, SelectedIndex = 0, MinWidth = 120, MinHeight = 38, FontSize = 13, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly StackPanel list = new() { Spacing = 8 };
    private readonly List<(Border Row, BackupInfo Backup)> rows = [];
    private BackupInfo? selectedBackup;
    private string? selectedId;
    private readonly Border local = new() { Padding = new Thickness(18, 16), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
    private readonly Border localBadge = new() { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), VerticalAlignment = VerticalAlignment.Top };
    private readonly PathIcon localIcon = Glyph(Icons.Info, 19, Muted);
    private readonly Avalonia.Controls.Shapes.Ellipse statusDot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar progress = new() { Height = 2, MinHeight = 2, IsVisible = false, IsIndeterminate = true, Background = Brushes.Transparent };
    private readonly DispatcherTimer gameTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string dotColor = Teal;
    private BackupInfo? Selected => selectedBackup;

    public MainWindow(DemoBackend demo) : this(demo, null) { }
    public MainWindow(RealServices real) : this(null, real) { }
    private MainWindow(DemoBackend? demo, RealServices? real)
    {
        this.demo = demo; this.real = real;
        guard = real?.Guard ?? new DemoGuard(); scanner = demo != null ? new(guard, TimeSpan.FromMilliseconds(400)) : new(guard);
        dataRoot = demo?.Root ?? real!.State.Root; state = real?.State ?? demo!.StateFor(user);
        Title = "雾锁存档助手"; Width = 720; Height = 790; MinWidth = 600; MinHeight = 740; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Sans; FontSize = 13; Dyn(this, BackgroundProperty, Bg); Dyn(this, ForegroundProperty, Ink);
        LoadTheme();
        Primary(upload); Primary(use); foreach (var b in new[] { refresh, settingsButton, backupsButton, simulate }) Ghost(b);
        foreach (var b in new[] { previous, next }) { Ghost(b); b.Padding = new Thickness(10, 5); }
        foreach (var b in new[] { use, useQuiet }) { b.MinHeight = 42; b.Padding = new Thickness(22, 10); b.FontSize = 14; }
        Dyn(progress, ProgressBar.ForegroundProperty, Ember); Dyn(local, Border.BorderBrushProperty, Line); Dyn(local, Border.BackgroundProperty, Surface);

        // Header: product, world, who you are (or the demo identity switch) and the appearance menu.
        worldText.Text = demo?.World.Name ?? "尚未设置共享世界"; worldText.VerticalAlignment = VerticalAlignment.Center;
        var worldLine = Row(8, worldText, demo != null ? Pill("本机试用", Muted, Raised) : identityPill);
        var brand = Row(12, Mark(), Stack(3, Text("雾锁存档助手", 18, true), worldLine)); brand.VerticalAlignment = VerticalAlignment.Center;
        Ghost(themeButton); themeButton.Padding = new Thickness(8); themeButton.MinWidth = themeButton.MinHeight = 36; themeButton.VerticalAlignment = VerticalAlignment.Center;
        var themeMenu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight }; themeButton.Flyout = themeMenu; themeMenu.Opening += (_, _) => FillThemeMenu(themeMenu);
        ActualThemeVariantChanged += (_, _) => PaintThemeButton(); PaintThemeButton();
        var account = Row(10); account.VerticalAlignment = VerticalAlignment.Center;
        if (demo != null)
        {
            var switcher = Box(SwitchTrack, Line); switcher.CornerRadius = new CornerRadius(10); switcher.Padding = new Thickness(3); switcher.Child = Row(2, actorPills);
            for (var i = 0; i < actors.Length; i++)
            {
                var index = i; var b = actors[i]; Ghost(b, Clear); b.MinWidth = 40; b.MinHeight = 30; b.Padding = new Thickness(10, 4);
                actorPills[i].CornerRadius = new CornerRadius(7); actorPills[i].Child = b;
                b.Click += async (_, _) => { if (ready && user != Key(index)) await Run(async () => { user = Key(index); PaintActors(); await SwitchAsync(); }); };
            }
            PaintActors(); var label = Text("身份", 12, color: Faint); label.VerticalAlignment = VerticalAlignment.Center; account.Children.Add(label); account.Children.Add(switcher);
        }
        account.Children.Add(themeButton);
        var header = Columns("*,Auto", brand, account);

        // Local check card: the colour of the card follows the state, so "有新进度" stands out at a glance.
        localBadge.Child = localIcon; localIcon.HorizontalAlignment = HorizontalAlignment.Center;
        var eyebrow = Text("本地存档", 11, true, Faint); eyebrow.LetterSpacing = 1;
        var localText = Stack(4, eyebrow, localTitle, localNote, checkTime); checkTime.Margin = new Thickness(0, 2, 0, 0);
        var localButtons = Row(8, checkButton, upload); localButtons.VerticalAlignment = VerticalAlignment.Center;
        local.Child = Columns("Auto,*,Auto", localBadge, localText, localButtons);
        SetLocal(Tone.Busy, "正在准备本地存档", demo != null ? "连接本机服务…" : "读取设置…");

        // Cloud list: title with refresh, filters, cards, paging.
        var count = Box(Raised); count.CornerRadius = new CornerRadius(9); count.Padding = new Thickness(7, 1); count.VerticalAlignment = VerticalAlignment.Center; count.Child = libraryCount;
        var cloudTitle = Row(8, Text("云端存档", 16, true), count, refresh);
        cloudTitle.Children[0].VerticalAlignment = VerticalAlignment.Center; cloudTitle.Margin = new Thickness(0, 8, 0, 0);
        var filters = Columns("*,Auto", search, sort); filters.ColumnSpacing = 8;
        var scroller = new ScrollViewer { Content = list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        pageText.VerticalAlignment = VerticalAlignment.Center;
        var paging = Columns("*,Auto,Auto", pageText, previous, next); paging.ColumnSpacing = 4;

        // Footer: settings and local backups in the corner, live status, then the one big action.
        statusDot.Margin = new Thickness(0, 0, 2, 0); SetDot(Teal); status.MaxLines = 1; status.TextTrimming = TextTrimming.CharacterEllipsis; status.TextWrapping = TextWrapping.NoWrap;
        hint.TextTrimming = TextTrimming.CharacterEllipsis; hint.TextWrapping = TextWrapping.NoWrap;
        var statusBlock = Columns("Auto,*", statusDot, Stack(2, status, hint)); statusBlock.ColumnSpacing = 8; statusBlock.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(simulate, "仅修改示例文件，模拟游戏结束后产生新进度"); simulate.IsVisible = demo != null;
        ToolTip.SetTip(backupsButton, "打开本地备份目录：恢复前的旧档、上传前的打包文件和下载的归档");
        var actions = Row(6, simulate, use, useQuiet); actions.VerticalAlignment = VerticalAlignment.Center;
        var corner = Stack(0, settingsButton, backupsButton); corner.VerticalAlignment = VerticalAlignment.Center;
        var footerRow = Columns("Auto,*,Auto", corner, statusBlock, actions);
        var footer = Box(Surface, Line, new Thickness(0, 1, 0, 0)); footer.Child = new Grid { Children = { new Border { Padding = new Thickness(12, 10, 20, 10), Child = footerRow }, progress } };
        progress.VerticalAlignment = VerticalAlignment.Top;

        var body = new Grid { Margin = new Thickness(24, 20, 24, 12), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"), RowSpacing = 14 };
        Add(body, header, 0); Add(body, local, 1); Add(body, cloudTitle, 2); Add(body, filters, 3); Add(body, scroller, 4); Add(body, paging, 5);
        var shell = new Grid { RowDefinitions = new RowDefinitions("*,Auto") }; Add(shell, body, 0); Add(shell, footer, 1);
        Content = shell;

        checkButton.Click += async (_, _) => await Run(async () =>
        {
            if (File.Exists(state.JournalPath)) { Say("正在回滚未完成的恢复…"); await new RestoreEngine(state, scanner, guard).RecoverAsync(); Say("未完成的恢复已回滚，原来的世界已还原。"); }
            await CheckAsync();
        }, "没能恢复旧档");
        refresh.Click += async (_, _) => await Run(async () => { if (real != null && me == null) await ConnectAsync(); else { page = 1; snapshot = 0; await RefreshAsync(true); } });
        previous.Click += async (_, _) => await Run(async () => { page--; await RefreshAsync(); }); next.Click += async (_, _) => await Run(async () => { page++; await RefreshAsync(); });
        // Typing never gets dropped: if another operation is running, the search waits and retries.
        var searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        search.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter && ready) { searchDelay.Stop(); searchDelay.Interval = TimeSpan.FromMilliseconds(1); searchDelay.Start(); } };
        search.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Interval = TimeSpan.FromMilliseconds(450); if (ready && !resetting) searchDelay.Start(); };
        searchDelay.Tick += async (_, _) => { searchDelay.Stop(); if (busy) { searchDelay.Interval = TimeSpan.FromMilliseconds(200); searchDelay.Start(); return; } await Run(async () => { page = 1; snapshot = 0; await RefreshAsync(); }); };
        sort.SelectionChanged += async (_, _) => { if (ready && !resetting) await Run(async () => { page = 1; snapshot = 0; await RefreshAsync(); }); };
        simulate.Click += async (_, _) => await Run(async () => { demo!.SimulateProgress(user); await CheckAsync(); Say("已在示例文件中生成新进度，并自动检查。"); });
        upload.Click += async (_, _) => await Run(UploadAsync, "未上传成功");
        use.Click += async (_, _) => await Run(RestoreAsync, "恢复没有完成"); useQuiet.Click += async (_, _) => await Run(RestoreAsync, "恢复没有完成");
        settingsButton.Click += async (_, _) =>
        {
            if (real != null) { await ConfigureAsync(); return; }
            await Info(this, "本机演示设置", $"当前身份：{ActorName}\n服务地址：{demo!.Server}\n\n示例世界目录：\n{settings.SaveDirectory}\n\n本地备份：\n{state.Root}\n\n这次预览不会访问真实游戏目录。切换身份可模拟 A、B 在不同电脑上接手。\nWindows 版使用个人访问码和 HTTPS 服务器。");
        };
        backupsButton.Click += (_, _) => { try { OpenFolder(state.Root); } catch (Exception e) { Say("无法打开本地备份目录：" + e.Message, true); } };
        // Watch the game while the tool is open: block risky actions while it runs, check automatically once it exits.
        gameTimer.Tick += async (_, _) =>
        {
            if (busy || !ready || !settings.Configured) return;
            bool running; try { running = guard.IsRunning(); } catch (Exception e) { running = true; Say("无法确认游戏状态：" + e.Message, true); }
            if (running && !gameRunning) { gameRunning = true; check = null; SetLocal(Tone.Playing, "游戏正在运行", "退出游戏后会自动检查新进度。运行期间不能上传或恢复。"); UpdateButtons(); }
            else if (!running && gameRunning) { gameRunning = false; UpdateButtons(); await Run(CheckAsync); }
        };
        Opened += async (_, _) =>
        {
            if (demo != null) { await Run(async () => { await demo.StartAsync(); ready = true; await SwitchAsync(); Say("本机服务已连接 · 使用示例存档"); }, "本机演示服务启动失败"); return; }
            await StartRealAsync();
        };
        Closing += (_, e) => { if (busy) { e.Cancel = true; Say("存档操作还未完成，请稍候再关闭。", true); } };
        Closed += async (_, _) => { ready = false; searchDelay.Stop(); gameTimer.Stop(); api?.Dispose(); if (demo != null) await demo.DisposeAsync(); };
        UpdateButtons();
    }

    // Theme follows the system until the user picks one; the choice is kept next to the local data.
    private string ThemeFile => Path.Combine(dataRoot, "theme.txt");
    private void LoadTheme()
    {
        if (Application.Current == null) return;
        try { var saved = File.Exists(ThemeFile) ? File.ReadAllText(ThemeFile).Trim() : ""; SetTheme(saved == "light" ? ThemeVariant.Light : saved == "dark" ? ThemeVariant.Dark : ThemeVariant.Default); }
        catch (IOException) { }
    }
    private void FillThemeMenu(MenuFlyout menu)
    {
        var current = RequestedThemeVariant ?? ThemeVariant.Default; menu.Items.Clear();
        foreach (var (label, variant, saved) in new[] { ("跟随系统", ThemeVariant.Default, "system"), ("浅色", ThemeVariant.Light, "light"), ("深色", ThemeVariant.Dark, "dark") })
        {
            var item = new MenuItem { Header = label, Icon = current == variant ? Glyph(Icons.Check, 14, Ember) : null };
            item.Click += (_, _) => { SetTheme(variant); try { Directory.CreateDirectory(dataRoot); File.WriteAllText(ThemeFile, saved); } catch (IOException) { } PaintThemeButton(); };
            menu.Items.Add(item);
        }
    }
    // Set on the window as well: on macOS the application-level change alone can reach an open window late.
    private void SetTheme(ThemeVariant variant) { Application.Current!.RequestedThemeVariant = variant; RequestedThemeVariant = variant; foreach (var w in OwnedWindows) w.RequestedThemeVariant = variant; }
    private void PaintThemeButton()
    {
        var light = ActualThemeVariant == ThemeVariant.Light; themeButton.Content = Glyph(light ? Icons.Sun : Icons.Moon, 17);
        var mode = RequestedThemeVariant == ThemeVariant.Light ? "浅色" : RequestedThemeVariant == ThemeVariant.Dark ? "深色" : $"跟随系统（当前{(light ? "浅色" : "深色")}）"; ToolTip.SetTip(themeButton, "外观：" + mode);
    }
    private static string Key(int index) => index switch { 1 => "a", 2 => "b", _ => "me" };
    private string ActorName => user switch { "a" => "A", "b" => "B", _ => "我" };
    private void PaintActors()
    { for (var i = 0; i < actors.Length; i++) { var on = Key(i) == user; Dyn(actorPills[i], Border.BackgroundProperty, on ? SwitchOn : Clear); actors[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; Paint(actors[i], Clear, Clear, Clear, on ? Ink : Muted, Ink, Clear, Clear, Clear, DisabledFg); } }
    private void SetIdentity(string text, bool warn = false)
    { var label = (TextBlock)identityPill.Child!; label.Text = text; Dyn(label, TextBlock.ForegroundProperty, warn ? Ember : Muted); Dyn(identityPill, Border.BackgroundProperty, warn ? EmberSoft : Raised); }
    private void SetDot(string color) { dotColor = color; Dyn(statusDot, Avalonia.Controls.Shapes.Shape.FillProperty, color); }
    private void Say(string message, bool problem = false) { status.Text = message; ToolTip.SetTip(status, message); SetDot(problem ? Danger : busy ? Ember : Teal); }
    private void SetLocal(Tone tone, string title, string note)
    {
        localTitle.Text = title; localNote.Text = note;
        var (icon, fg, bg, border) = tone switch
        {
            Tone.Attention => (Icons.Upload, Ember, EmberSoft, EmberLine), Tone.Good => (Icons.Check, Teal, TealSoft, Line), Tone.Busy => (Icons.Refresh, Muted, Raised, Line),
            Tone.Problem => (Icons.Info, Danger, DangerSoft, DangerLine), Tone.Playing => (Icons.Game, Teal, TealSoft, Line), _ => (Icons.Info, Muted, Raised, Line)
        };
        localIcon.Data = Geometry.Parse(icon); Dyn(localIcon, PathIcon.ForegroundProperty, fg); Dyn(localBadge, Border.BackgroundProperty, bg); Dyn(local, Border.BorderBrushProperty, border); Dyn(local, Border.BackgroundProperty, tone == Tone.Attention ? AttentionCard : Surface);
    }
    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false }; start.ArgumentList.Add(path);
        Process.Start(start)?.Dispose();
    }
    // Failures go to the status line; only actions where the user must know the outcome also get a dialog.
    private async Task Run(Func<Task> action, string? failureTitle = null)
    {
        if (busy) return; busy = true; progress.IsIndeterminate = true; progress.IsVisible = true; SetDot(Ember); UpdateButtons();
        try { await action(); }
        catch (Exception e)
        {
            Say(e.Message, true);
            if (failureTitle != null) await Info(this, failureTitle, e.Message + (failureTitle == "未上传成功" && state.GetPending(settings) != null ? "\n\n本地打包文件已保留，稍后点“上传我的存档”可以重试，不会产生重复备份。" : ""));
        }
        finally { busy = false; progress.IsVisible = false; if (dotColor == Ember) SetDot(Teal); UpdateButtons(); }
    }
    private IProgress<long> Meter(string verb, long size) => new Progress<long>(n => { progress.IsIndeterminate = false; progress.Value = Math.Clamp(n * 100.0 / Math.Max(size, 1), 0, 100); status.Text = $"{verb}：{Size(n)} / {Size(size)}"; });
    private void UpdateButtons()
    {
        var enabled = ready && !busy; var configured = settings.Configured;
        var recovery = File.Exists(state.JournalPath); SetLabel(checkButton, recovery ? "恢复旧档" : "检查存档");
        checkButton.IsEnabled = enabled && !gameRunning && (configured || recovery);
        refresh.IsEnabled = sort.IsEnabled = enabled && configured; search.IsEnabled = ready && configured; settingsButton.IsEnabled = backupsButton.IsEnabled = enabled; foreach (var b in actors) b.IsEnabled = enabled;
        PendingUpload? pending = null; if (configured) { try { pending = state.GetPending(settings); } catch (IOException) { } }
        upload.IsEnabled = enabled && !gameRunning && !recovery && configured && api != null && settings.World?.CanUpload != false && (check?.HasChanges == true || pending != null);
        ToolTip.SetTip(upload, settings.World?.CanUpload == false ? "你是只读成员，可以查看和恢复，但不能上传。" : null);
        // With unuploaded local progress, uploading is the next step; restoring would replace it, so it stops being the main action.
        var unsaved = check?.HasChanges == true || pending != null;
        use.IsVisible = !unsaved; useQuiet.IsVisible = unsaved; use.IsEnabled = useQuiet.IsEnabled = enabled && !gameRunning && !recovery && api != null && Selected != null;
        previous.IsEnabled = enabled && page > 1; next.IsEnabled = enabled && page * PageSize < total;
        simulate.IsEnabled = enabled && demo != null && configured && File.Exists(Path.Combine(settings.SaveDirectory, settings.World!.SaveKey));
    }

    private async Task SwitchAsync()
    {
        api?.Dispose(); api = demo!.ApiFor(user); settings = demo.SettingsFor(user); state = demo.StateFor(user); check = null; page = 1; snapshot = 0;
        if (File.Exists(state.JournalPath)) await new RestoreEngine(state, scanner, guard).RecoverAsync();
        await RefreshAsync(true);
        if (Directory.EnumerateFiles(settings.SaveDirectory).Any()) await CheckAsync();
        else { SetLocal(Tone.Neutral, "本地还没有这份世界", "在下方选择一份云端存档并恢复，就可以接手当房主。"); checkTime.Text = "尚未检查"; }
        Say($"已切换为「{ActorName}」· 独立的本地目录和访问凭证");
    }
    private async Task StartRealAsync()
    {
        try { if (File.Exists(state.SettingsPath)) settings = JsonData.Read<ClientSettings>(state.SettingsPath); }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { Say("设置文件无法读取，请重新设置：" + e.Message, true); }
        ready = true; UpdateButtons();
        if (settings.Configured) await Run(ConnectAsync, "连接没有完成");
        else
        {
            SetLocal(Tone.Neutral, "欢迎！先完成首次设置", "填写服务器地址和个人访问码，再选择共享世界和本地存档目录。"); checkTime.Text = "设置完成后会自动检查本地存档";
            ShowListMessage("完成设置后，这里会列出大家上传的存档。"); Say("还没有完成设置。"); await ConfigureAsync();
        }
        gameTimer.Start();
    }
    private async Task ConfigureAsync()
    {
        if (busy || real == null) return;
        var result = await SetupDialog.ShowAsync(this, real, settings);
        if (result == null) { if (!settings.Configured) Say("还没有完成设置。点左下角“设置”继续。"); return; }
        settings = result; JsonData.Write(state.SettingsPath, settings); await Run(ConnectAsync, "连接没有完成");
    }
    // Local checks work offline; the server only adds identity and the shared list.
    private async Task ConnectAsync()
    {
        check = null; me = null; page = 1; snapshot = 0; selectedId = null; api?.Dispose(); api = null;
        worldText.Text = settings.World!.Name; SetIdentity("正在连接…");
        if (File.Exists(state.JournalPath)) { Say("正在回滚上次未完成的恢复…"); await new RestoreEngine(state, scanner, guard).RecoverAsync(); }
        await CheckAsync();
        var token = real!.ReadToken(settings.Server);
        if (string.IsNullOrEmpty(token)) { SetIdentity("未连接"); ShowListMessage("没有找到访问码，请在“设置”中重新填写。"); Say("没有找到这台电脑保存的访问码。", true); return; }
        api = new VaultClient(settings.Server, token, real.AllowLoopback);
        try { me = await api.MeAsync(); }
        catch (Exception e) { api.Dispose(); api = null; SetIdentity("未连接"); ShowListMessage("连接不上服务器。本地检查仍然可用，稍后点“刷新”重试。"); Say("连接服务器失败：" + e.Message, true); return; }
        var current = me.Worlds.FirstOrDefault(w => w.Id == settings.World.Id) ?? throw new IOException("当前身份无权访问设置中的世界，请在“设置”中重新选择。");
        if (current.SaveKey != settings.World.SaveKey) throw new IOException("服务器上的世界文件标识发生了变化，请在“设置”中重新核对文件清单。");
        settings = settings with { World = current }; JsonData.Write(state.SettingsPath, settings); worldText.Text = current.Name;
        var expiring = me.Identity.ExpiresAt - DateTimeOffset.UtcNow < TimeSpan.FromDays(7);
        SetIdentity(me.Identity.User.Name + (current.CanUpload ? "" : " · 只读") + (expiring ? " · 访问码即将到期" : ""), expiring);
        await RefreshAsync(true);
        Say($"已连接 · {me.Identity.User.Name}" + (expiring ? $" · 访问码 {me.Identity.ExpiresAt.LocalDateTime:d} 到期，请联系管理员更换" : ""), expiring);
    }
    private async Task CheckAsync()
    {
        if (!settings.Configured) return;
        check = null; SetLocal(Tone.Busy, "正在检查存档…", "对比上次恢复或上传的版本。"); UpdateButtons();
        try { check = await scanner.CheckAsync(settings, state); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A failed check is never "no changes": say why and keep upload disabled until a check succeeds.
            if (e.Message.Contains("没有找到这个世界的文件")) { SetLocal(Tone.Neutral, "本地还没有这份世界", "在下方选择一份云端存档并恢复，就可以接手当房主。"); checkTime.Text = "检查于 " + DateTime.Now.ToString("HH:mm:ss"); Say("本地没有这份世界的文件，可以先从云端恢复。"); return; }
            var title = e is DirectoryNotFoundException ? "找不到存档目录" : e.Message.Contains("游戏仍在运行") ? "游戏还在运行，暂时不能检查" : e.Message.Contains("仍在保存") ? "游戏还在保存存档" : e.Message.Contains("未完成的恢复") ? "上次恢复没有完成" : "检查没有完成";
            SetLocal(Tone.Problem, title, e.Message + " 本地进度尚未确认，暂不能上传。"); checkTime.Text = "检查失败于 " + DateTime.Now.ToString("HH:mm:ss"); Say("本地检查没有完成：" + e.Message, true); return;
        }
        var pending = state.GetPending(settings);
        var summary = settings.Layout.IsKsd
            ? $"{settings.World!.Name} · {settings.Layout.KsdFile} · {Size(check.Scan.TotalSize)}"
            : $"{settings.World!.Name} · {check.Scan.Files.Count} 个文件 · {Size(check.Scan.TotalSize)} · {check.ChangedCount} 项变化";
        if (pending != null) SetLocal(Tone.Attention, "上次上传未完成，可以重试", $"「{pending.Metadata.Name}」已打包保留 · " + summary);
        else if (check.IsFirstBackup) SetLocal(Tone.Attention, "本地存档尚未备份，可以上传", summary);
        else if (check.HasChanges) SetLocal(Tone.Attention, "发现新进度，可以上传", summary);
        else SetLocal(Tone.Good, "没有新变化，不用重复上传", summary);
        checkTime.Text = "上次检查 " + DateTime.Now.ToString("HH:mm:ss");
    }
    private void ShowListMessage(string message)
    {
        list.Children.Clear(); rows.Clear(); total = 0; Select(null); libraryCount.Text = "–"; pageText.Text = "";
        var empty = Stack(10, Glyph(Icons.Empty, 28, Faint), Text(message, 13, color: Muted)); foreach (var c in empty.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
        var box = Box(Clear, Line); box.Padding = new Thickness(20, 48); box.CornerRadius = new CornerRadius(10); box.Child = empty; list.Children.Add(box);
    }
    private async Task RefreshAsync(bool selectFirst = false)
    {
        if (api == null || settings.World == null) return;
        var response = await api.ListAsync(settings.World.Id, search.Text?.Trim() ?? "", sort.SelectedIndex switch { 1 => "oldest", 2 => "name", _ => "newest" }, page, PageSize, snapshot);
        total = response.Total; snapshot = response.Snapshot;
        var newest = page == 1 && sort.SelectedIndex == 0 && string.IsNullOrWhiteSpace(search.Text); var origin = state.GetBaseline(settings)?.BackupId;
        list.Children.Clear(); rows.Clear(); selectedBackup = null; if (selectFirst) selectedId = null;
        foreach (var (backup, index) in response.Items.Select((b, i) => (b, i)))
        {
            var name = Text(backup.Name, 14, true); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            var title = Row(8, name); if (newest && index == 0) title.Children.Add(Pill("最新", Ember, EmberSoft)); if (backup.Id == origin) title.Children.Add(Pill("本地来源", Teal, TealSoft));
            var note = Text(string.IsNullOrWhiteSpace(backup.Note) ? "没有填写备注" : backup.Note, 12.5, color: string.IsNullOrWhiteSpace(backup.Note) ? Faint : Muted); note.MaxLines = 1; note.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(note, backup.Note);
            var when = Stack(4, Text(backup.CreatedAt.LocalDateTime.ToString("MM-dd HH:mm"), 12, color: Muted), Text($"{backup.Uploader} 上传", 11.5, color: Faint)); when.VerticalAlignment = VerticalAlignment.Center;
            foreach (var t in when.Children.OfType<TextBlock>()) t.HorizontalAlignment = HorizontalAlignment.Right;
            var avatar = Avatar(backup.Uploader); avatar.VerticalAlignment = VerticalAlignment.Center;
            var text = Stack(5, title, note); text.VerticalAlignment = VerticalAlignment.Center;
            var row = Box(Surface, Line); row.Padding = new Thickness(14, 12); row.MinHeight = 70; row.CornerRadius = new CornerRadius(10); row.Cursor = new Cursor(StandardCursorType.Hand); row.Child = Columns("Auto,*,Auto", avatar, text, when);
            row.PointerPressed += (_, _) => { if (!busy) Select(backup); };
            row.PointerEntered += (_, _) => { if (selectedBackup?.Id != backup.Id) Dyn(row, Border.BackgroundProperty, Hover); }; row.PointerExited += (_, _) => { if (selectedBackup?.Id != backup.Id) Dyn(row, Border.BackgroundProperty, Surface); };
            rows.Add((row, backup)); list.Children.Add(row);
        }
        // Pre-select the newest only when asked (open, refresh, switch); paging and searching keep the user's choice or select nothing.
        var keep = rows.FirstOrDefault(r => r.Backup.Id == selectedId).Backup;
        if (keep != null) Select(keep); else if (selectFirst && rows.Count > 0) Select(rows[0].Backup); else Select(null);
        if (rows.Count == 0) ShowListMessage(string.IsNullOrWhiteSpace(search.Text) ? "云端还没有存档，先上传一份吧。" : "没有找到匹配的存档，换个关键词试试。");
        libraryCount.Text = total.ToString();
        pageText.Text = total == 0 ? "共 0 份" : $"第 {page} / {(total + PageSize - 1) / PageSize} 页 · 共 {total} 份";
    }
    private void Select(BackupInfo? backup)
    {
        selectedBackup = backup; if (backup != null) selectedId = backup.Id;
        hint.Text = backup == null ? "还没有选择云端存档" : $"已选：{backup.Name} · {backup.Uploader} 上传";
        foreach (var (row, b) in rows) { var chosen = b.Id == backup?.Id; Dyn(row, Border.BackgroundProperty, chosen ? SelectedRow : Surface); Dyn(row, Border.BorderBrushProperty, chosen ? Ember : Line); }
        UpdateButtons();
    }
    private async Task UploadAsync()
    {
        if (api == null) throw new IOException("还没有连接服务器。"); guard.EnsureStopped();
        var world = settings.World!; var pending = state.GetPending(settings);
        if (pending != null)
        {
            var answer = await Choose(this, "有一份上传还没完成", $"「{pending.Metadata.Name}」已经打包好，但还没确认上传成功。\n\n重试会沿用原来的任务，服务器不会产生重复备份。\n放弃任务只清除这条记录，本地打包文件仍会保留。", "暂不处理", "放弃任务", "重试上传");
            if (answer is 0 or -1) return;
            if (answer == 1) { state.ClearPending(settings); await CheckAsync(); Say("已放弃这个上传任务，本地打包文件仍然保留。"); return; }
            if (pending.Server != settings.Server || pending.ProfileKey != settings.ProfileKey) throw new IOException("这个待上传任务不属于当前的服务器和世界设置。");
        }
        else
        {
            await CheckAsync(); if (check?.HasChanges != true) return;
            var inspected = check;
            var draft = await UploadPrompt(inspected); if (draft == null) { Say("已取消上传，没有生成任何文件。"); return; }
            // Package only what the user just confirmed; the snapshot re-verifies every file against that scan.
            guard.EnsureStopped(); Say("正在生成完整的本地备份…"); var outbox = state.NewFolder("outbox"); LocalSnapshot package;
            try { package = await scanner.SnapshotAsync(settings.SaveDirectory, settings.Layout, inspected.Scan, outbox); }
            catch (IOException)
            {
                try { Directory.Delete(outbox, true); } catch (IOException) { }
                await CheckAsync(); Say("确认期间存档又有变化，已重新检查。请核对数量和大小后再上传。", true); return;
            }
            pending = new(settings.Server, settings.ProfileKey, Guid.NewGuid().ToString("N"), new(world.Id, draft.Name, draft.Note, state.GetBaseline(settings)?.BackupId, package.ArchiveSha256, inspected.Scan.Files), package.ArchivePath, inspected.Scan.Fingerprint); state.SavePending(settings, pending);
        }
        Say("正在上传，完成前请不要关闭工具…");
        var saved = await api.UploadAsync(pending, Meter("上传中", new FileInfo(pending.ArchivePath).Length));
        state.SetBaseline(settings, new(saved.Id, pending.Fingerprint, pending.Metadata.Files)); state.ClearPending(settings);
        resetting = true; search.Text = ""; sort.SelectedIndex = 0; resetting = false; page = 1; snapshot = 0; selectedId = saved.Id; progress.IsIndeterminate = true;
        await RefreshAsync(); await CheckAsync(); Say(demo != null ? "上传成功！切换到 B，即可看到并恢复这份存档。" : "上传成功，朋友刷新列表就能看到。");
    }
    private async Task RestoreAsync()
    {
        var selected = Selected; if (selected == null || api == null) return; guard.EnsureStopped();
        var warning = check?.HasChanges == true || state.GetPending(settings) != null ? "\n\n注意：本地有尚未上传的新进度，恢复后会被替换（旧档仍会保留在“本地备份”里）。" : "";
        if (!await Ask(this, "使用这份存档？", $"{selected.Name}\n{selected.Uploader} 上传 · {selected.CreatedAt.LocalDateTime:g}\n\n会下载并恢复到：\n{(settings.Layout.IsKsd ? Path.Combine(settings.SaveDirectory, settings.Layout.KsdFile) : settings.SaveDirectory)}\n\n当前本地世界会先自动备份。{warning}", "下载并恢复")) return;
        Say("正在下载并校验备份…");
        var file = await api.DownloadAsync(selected, state.NewFolder("downloads"), Meter("下载中", selected.ArchiveSize));
        progress.IsIndeterminate = true; Say("正在备份旧档并恢复，请不要启动游戏…");
        await new RestoreEngine(state, scanner, guard).RestoreAsync(settings, selected, file);
        await RefreshAsync(); await CheckAsync(); Say(demo != null ? "恢复完成。可以点“模拟玩完”体验产生新进度后的检查。" : "恢复完成，可以打开游戏当房主了。旧档保存在“本地备份”里。");
    }
    private sealed record Draft(string Name, string Note);
    private async Task<Draft?> UploadPrompt(LocalCheck inspected)
    {
        var hour = DateTime.Now.Hour; var defaultName = $"{DateTime.Now:MM-dd} {(hour < 5 ? "深夜" : hour < 12 ? "上午" : hour < 18 ? "下午" : "晚上")}的进度";
        var name = new TextBox { Text = defaultName, MaxLength = 60, Padding = new Thickness(12, 9), FontSize = 13 };
        var note = new TextBox { Watermark = "装备放哪里？下次准备去哪？", AcceptsReturn = true, Height = 88, MaxLength = 500, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(12, 10), FontSize = 13 };
        // Facts about exactly what will be sent: the same verified scan the confirm button counts.
        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16, RowSpacing = 8 };
        void Fact(string label, Control value) { var row = facts.RowDefinitions.Count; facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); var l = Text(label, 12, color: Faint); Grid.SetRow(l, row); Grid.SetRow(value, row); Grid.SetColumn(value, 1); facts.Children.Add(l); facts.Children.Add(value); }
        Fact("世界", Text(settings.World!.Name, 13, true));
        var layout = settings.Layout;
        Fact("本地路径", Dyn(new SelectableTextBlock { Text = layout.IsKsd ? Path.Combine(settings.SaveDirectory, layout.KsdFile) : settings.SaveDirectory, TextWrapping = TextWrapping.Wrap, FontSize = 12, FontFamily = Mono }, SelectableTextBlock.ForegroundProperty, Muted));
        Fact("上传内容", Text(layout.IsKsd ? $"{layout.KsdFile} → 云端 world.ksd · {Size(inspected.Scan.TotalSize)}（打包前）" : $"{inspected.Scan.Files.Count} 个文件 · {Size(inspected.Scan.TotalSize)}（打包前）", 13, true));
        Fact("变化", Text($"{inspected.ChangedCount} 个文件有变化（含新增、修改、删除）· 最近保存 {inspected.Scan.LastModified.LocalDateTime:MM-dd HH:mm}", 12, color: Muted));
        var previous = state.GetBaseline(settings)?.Files.ToDictionary(f => f.Path, f => f.Sha256) ?? [];
        var fileRows = new Grid { ColumnDefinitions = new ColumnDefinitions("12,*,Auto"), ColumnSpacing = 8, RowSpacing = 6, Margin = new Thickness(0, 4, 12, 0) };
        var fileList = new ScrollViewer { Content = fileRows, MaxHeight = 180, IsVisible = false, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        foreach (var (f, i) in inspected.Scan.Files.Select((f, i) => (f, i)))
        {
            fileRows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var changed = !previous.TryGetValue(f.Path, out var sha) || sha != f.Sha256;
            var dot = Dyn(new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center }, Avalonia.Controls.Shapes.Shape.FillProperty, changed ? Ember : Line); ToolTip.SetTip(dot, changed ? "有变化" : "未变化");
            var path = Text(layout.ToLocal(f.Path), 12, color: changed ? Ink : Muted); path.FontFamily = Mono;
            var size = Text(Size(f.Size), 12, color: Faint);
            foreach (var (c, col) in new Control[] { dot, path, size }.Select((c, col) => (c, col))) { Grid.SetRow(c, i); Grid.SetColumn(c, col); fileRows.Children.Add(c); }
        }
        var toggle = Ghost(Btn("查看文件清单", Icons.Right, iconAfter: true), Clear); toggle.Padding = new Thickness(0, 4); toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Click += (_, _) => { fileList.IsVisible = !fileList.IsVisible; SetLabel(toggle, fileList.IsVisible ? "收起文件清单" : "查看文件清单"); };
        var divider = Box(Line); divider.Height = 1;
        var summary = Box(Bg, Line); summary.Padding = new Thickness(16, 14); summary.CornerRadius = new CornerRadius(10); summary.Child = Stack(10, facts, divider, Stack(2, toggle, fileList));
        var saved = new CheckBox { Content = "游戏已正常保存并完全退出", FontSize = 13 };
        var send = Primary(Btn($"确认上传 {inspected.Scan.Files.Count} 个文件", Icons.Upload)); var cancel = Btn("取消"); send.IsEnabled = false;
        saved.IsCheckedChanged += (_, _) => send.IsEnabled = saved.IsChecked == true;
        var error = Text("", 12, color: Danger); error.IsVisible = false;
        var intro = Text("确认来源无误后填写名称和备注，朋友会在存档列表里看到它们。", 12.5, color: Muted);
        var window = Dialog(this, "上传我的存档", Stack(14, intro, summary, Label("起个名字"), name, Label("给朋友留句话（选填）"), note, saved, error), Buttons(cancel, send), 520);
        window.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        cancel.Click += (_, _) => window.Close(null);
        send.Click += (_, _) => { if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "请先给存档起个名字。"; error.IsVisible = true; return; } window.Close(new Draft(name.Text.Trim(), note.Text?.Trim() ?? "")); };
        return await window.ShowDialog<Draft?>(this);
    }
}
