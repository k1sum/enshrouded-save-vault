using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Enshrouded.SaveCore;
using static Enshrouded.SaveDesktop.Ui;

namespace Enshrouded.SaveDesktop;

// First-run / connection settings. Nothing is saved until the server, world, file group and Steam Cloud are all confirmed.
internal static class SetupDialog
{
    private static Control Step(int number, string title)
    {
        var badge = Box(EmberSoft); badge.Width = badge.Height = 22; badge.CornerRadius = new CornerRadius(11);
        var digit = Text(number.ToString(), 11.5, true, Ember); digit.HorizontalAlignment = HorizontalAlignment.Center; digit.VerticalAlignment = VerticalAlignment.Center; badge.Child = digit;
        var heading = Text(title, 14, true); heading.VerticalAlignment = VerticalAlignment.Center;
        var row = Row(10, badge, heading); row.Margin = new Thickness(0, 8, 0, 0); return row;
    }
    public static async Task<ClientSettings?> ShowAsync(Window owner, RealServices services, ClientSettings current)
    {
        var server = new TextBox { Text = current.Server, Watermark = "https://saves.example.com", FontSize = 13, Padding = new Thickness(12, 9) };
        var token = new TextBox { PasswordChar = '•', Watermark = "管理员单独发给你的个人访问码", FontSize = 13, Padding = new Thickness(12, 9) };
        try { if (!string.IsNullOrEmpty(current.Server)) token.Text = services.ReadToken(current.Server); } catch (Exception) { }
        var verify = Btn("验证连接", Icons.Check);
        var identity = Text("填写后点“验证连接”，确认你的身份和可以访问的世界。", 12, color: Muted);
        var worlds = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 38, FontSize = 13, PlaceholderText = "先验证连接", IsEnabled = false };
        List<WorldInfo> worldList = [];
        var folder = new TextBox { Watermark = "游戏世界存档所在的文件夹", FontSize = 12.5, FontFamily = Mono, Padding = new Thickness(12, 9) };
        var candidates = SaveRules.Candidates();
        folder.Text = !string.IsNullOrEmpty(current.SaveDirectory) ? current.SaveDirectory : candidates.Count == 1 ? candidates[0] : "";
        var browse = Btn("选择…", Icons.Folder);
        var found = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in candidates) { var chip = Ghost(Btn(c)); chip.FontFamily = Mono; chip.FontSize = 11.5; chip.Click += (_, _) => folder.Text = c; found.Children.Add(chip); }
        var foundBlock = Stack(2, Text("找到的常见位置（点一下即可填入）：", 11.5, color: Faint), found); foundBlock.IsVisible = candidates.Count > 0;
        var inspect = Btn("检查文件清单", Icons.Search);
        var preview = Text("", 12, color: Muted); preview.FontFamily = Mono;
        var previewBox = Box(Bg, Line); previewBox.Padding = new Thickness(14, 12); previewBox.CornerRadius = new CornerRadius(10); previewBox.Child = new ScrollViewer { Content = preview, MaxHeight = 140 }; previewBox.IsVisible = false;
        var filesOk = new CheckBox { Content = "我已核对：这是目标世界的位置和文件组", FontSize = 13 };
        var cloudOk = new CheckBox { Content = "已处理这个游戏的 Steam Cloud，避免它覆盖世界存档", FontSize = 13, IsChecked = current.SteamCloudAcknowledged };
        var cloudNote = Text("关闭该游戏的 Steam 云同步也可能影响角色存档，请先备份。工具不会自动修改 Steam 设置。", 11.5, color: Faint); cloudNote.Margin = new Thickness(28, -6, 0, 0);
        var error = Text("", 12.5, color: Danger); error.IsVisible = false;
        var save = Primary(Btn("保存并连接")); var cancel = Btn("取消");
        string verifiedServer = "", verifiedToken = ""; (string Folder, string World)? inspected = null;

        // Every handler below reports failures inside the dialog; an exception escaping a UI event would end the app.
        var content = Stack(12,
            Text("每位玩家用自己的访问码。访问码保存在这台电脑的系统凭据中，不会写进配置文件。", 12.5, color: Muted),
            Step(1, "服务器与访问码"), Label("服务器 HTTPS 地址"), server, Label("个人访问码"), token, Columns("Auto,*", verify, identity),
            Step(2, "共享世界"), worlds,
            Step(3, "本地存档目录"), Columns("*,Auto", folder, browse), foundBlock, inspect, previewBox,
            Step(4, "确认"), filesOk, cloudOk, cloudNote, error);
        ((Grid)content.Children[6]).ColumnSpacing = 12; identity.VerticalAlignment = VerticalAlignment.Center;
        var window = Dialog(owner, current.Configured ? "连接设置" : "首次设置", content, Buttons(cancel, save), 580, 700);
        void Fail(string message) { error.Text = message; error.IsVisible = true; }
        void ResetFiles() { inspected = null; filesOk.IsChecked = false; previewBox.IsVisible = false; }
        folder.TextChanged += (_, _) => ResetFiles(); worlds.SelectionChanged += (_, _) => ResetFiles();

        verify.Click += async (_, _) =>
        {
            verify.IsEnabled = false; error.IsVisible = false;
            try
            {
                using var api = new VaultClient(server.Text?.Trim() ?? "", token.Text?.Trim() ?? "", services.AllowLoopback);
                var me = await api.MeAsync(); worldList = me.Worlds;
                worlds.ItemsSource = worldList.Select(w => w.Name + (w.CanUpload ? "" : "（只读）")).ToList(); worlds.IsEnabled = worldList.Count > 0;
                worlds.SelectedIndex = worldList.Count == 0 ? -1 : Math.Max(0, worldList.FindIndex(w => w.Id == current.World?.Id));
                verifiedServer = (server.Text ?? "").Trim().TrimEnd('/'); verifiedToken = (token.Text ?? "").Trim();
                identity.Text = $"已验证：{me.Identity.User.Name} · 访问码 {me.Identity.ExpiresAt.LocalDateTime:d} 到期" + (worldList.Count == 0 ? "\n还没有获得任何世界的权限，请联系管理员。" : "");
                Dyn(identity, TextBlock.ForegroundProperty, Teal);
            }
            catch (Exception e) { Dyn(identity, TextBlock.ForegroundProperty, Muted); identity.Text = "未通过验证。"; Fail("连接没有成功：" + e.Message); }
            finally { verify.IsEnabled = true; }
        };
        browse.Click += async (_, _) =>
        {
            try
            {
                var picked = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择游戏世界存档所在的文件夹", AllowMultiple = false });
                if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) folder.Text = path;
            }
            catch (Exception e) { Fail("无法打开文件夹选择：" + e.Message); }
        };
        inspect.Click += async (_, _) =>
        {
            inspect.IsEnabled = false; error.IsVisible = false;
            try
            {
                if (worlds.SelectedIndex < 0) throw new IOException("先验证连接并选择共享世界。");
                var w = worldList[worlds.SelectedIndex]; var dir = folder.Text?.Trim() ?? "";
                if (dir.Length == 0) throw new IOException("请先填写或选择本地存档目录。");
                var scan = await new SaveScanner(services.Guard).StableScanAsync(dir, w.SaveKey, true);
                preview.Text = $"世界文件标识 {w.SaveKey} · {scan.Files.Count} 个文件 · {Size(scan.TotalSize)}\n" + (scan.Files.Count == 0 ? "这个目录里还没有该世界，可以保存设置后先从云端恢复。" : string.Join("\n", scan.Files.Select(f => $"{f.Path}  {Size(f.Size)}")));
                previewBox.IsVisible = true; filesOk.IsChecked = false; inspected = (dir, w.Id);
            }
            catch (Exception e) { Fail(e.Message); }
            finally { inspect.IsEnabled = true; }
        };
        cancel.Click += (_, _) => window.Close(null);
        save.Click += (_, _) =>
        {
            error.IsVisible = false;
            try
            {
                if (verifiedServer != (server.Text ?? "").Trim().TrimEnd('/') || verifiedToken != (token.Text ?? "").Trim() || worlds.SelectedIndex < 0) throw new IOException("请先验证当前填写的服务器地址和访问码，并选择共享世界。");
                var w = worldList[worlds.SelectedIndex]; var dir = folder.Text?.Trim() ?? "";
                if (!Directory.Exists(dir)) throw new IOException("请选择一个存在的本地存档目录。"); SaveRules.NoLinks(dir);
                if (inspected != (dir, w.Id) || filesOk.IsChecked != true || cloudOk.IsChecked != true) throw new IOException("请点“检查文件清单”，核对后勾选“确认”里的两项。");
                services.SaveToken(verifiedServer, verifiedToken);
                window.Close(new ClientSettings { Server = verifiedServer, World = w, SaveDirectory = Path.GetFullPath(dir), SteamCloudAcknowledged = true, FilesConfirmed = true });
            }
            catch (Exception e) { Fail(e.Message); }
        };
        return await window.ShowDialog<ClientSettings?>(owner);
    }
}
