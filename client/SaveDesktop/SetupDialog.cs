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
        // Game 1.0 worlds: every world is one worldN.ksd and N differs per PC, so each PC says which file is the shared world.
        var slots = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 38, FontSize = 12.5, FontFamily = Mono, PlaceholderText = "请选择这台电脑上对应的世界文件" };
        List<int> slotList = [];
        var slotNote = Text("游戏 1.0 里每个世界是一个 worldN.ksd，编号在每台电脑上可能不同。请选择与共享世界对应的那个文件；本机还没有这个世界就选“新建”，之后从云端恢复。角色（character*.ksd）、global.ksd、backup/、migrated/ 不会被上传或覆盖。", 11.5, color: Faint);
        var slotBlock = Stack(8, Label("这台电脑上的世界文件"), slots, slotNote); slotBlock.IsVisible = false;
        // Says which save format the chosen world uses, and why there is no world-file list when it does not fit this folder.
        var worldFormat = Text("", 11.5, color: Faint); worldFormat.IsVisible = false;
        var mismatch = Text("", 12, color: Danger); mismatch.IsVisible = false;
        var inspect = Btn("检查文件清单", Icons.Search);
        var preview = Text("", 12, color: Muted); preview.FontFamily = Mono;
        var previewBox = Box(Bg, Line); previewBox.Padding = new Thickness(14, 12); previewBox.CornerRadius = new CornerRadius(10); previewBox.Child = new ScrollViewer { Content = preview, MaxHeight = 140 }; previewBox.IsVisible = false;
        var filesOk = new CheckBox { Content = "我已核对：这是目标世界的位置和文件组", FontSize = 13 };
        var cloudOk = new CheckBox { Content = "已处理这个游戏的 Steam Cloud，避免它覆盖世界存档", FontSize = 13, IsChecked = current.SteamCloudAcknowledged };
        var cloudNote = Text("关闭该游戏的 Steam 云同步也可能影响角色存档，请先备份。工具不会自动修改 Steam 设置。", 11.5, color: Faint); cloudNote.Margin = new Thickness(28, -6, 0, 0);
        var error = Text("", 12.5, color: Danger); error.IsVisible = false;
        var save = Primary(Btn("保存并连接")); var cancel = Btn("取消");
        string verifiedServer = "", verifiedToken = ""; (string Folder, string World, int Slot)? inspected = null;

        // Every handler below reports failures inside the dialog; an exception escaping a UI event would end the app.
        var content = Stack(12,
            Text("每位玩家用自己的访问码。访问码保存在这台电脑的系统凭据中，不会写进配置文件。", 12.5, color: Muted),
            Step(1, "服务器与访问码"), Label("服务器 HTTPS 地址"), server, Label("个人访问码"), token, Columns("Auto,*", verify, identity),
            Step(2, "共享世界"), worlds, worldFormat,
            Step(3, "本地存档目录"), Columns("*,Auto", folder, browse), foundBlock, mismatch, slotBlock, inspect, previewBox,
            Step(4, "确认"), filesOk, cloudOk, cloudNote, error);
        ((Grid)content.Children[6]).ColumnSpacing = 12; identity.VerticalAlignment = VerticalAlignment.Center;
        var versionLine = Text($"客户端版本 {AppVersion}", 11, color: Faint); versionLine.Margin = new Thickness(0, 6, 0, 0); content.Children.Add(versionLine);
        var window = Dialog(owner, (current.Configured ? "连接设置" : "首次设置") + $" · v{AppVersion}", content, Buttons(cancel, save), 580, 700);
        void Fail(string message) { error.Text = message; error.IsVisible = true; }
        void ResetFiles() { inspected = null; filesOk.IsChecked = false; previewBox.IsVisible = false; }
        WorldInfo? World() => worlds.SelectedIndex >= 0 && worlds.SelectedIndex < worldList.Count ? worldList[worlds.SelectedIndex] : null;
        int? Slot() => slots.SelectedIndex >= 0 && slots.SelectedIndex < slotList.Count ? slotList[slots.SelectedIndex] : null;
        void FillSlots()
        {
            var w = World(); var dir = folder.Text?.Trim() ?? ""; slotBlock.IsVisible = w?.SaveKey == SaveLayout.KsdKey;
            worldFormat.IsVisible = w != null;
            worldFormat.Text = w == null ? "" : w.SaveKey == SaveLayout.KsdKey ? "格式：游戏 1.0 世界（worldN.ksd），下面选择这台电脑上对应的文件。" : $"格式：抢先体验版世界（8 位标识 {w.SaveKey}）。游戏 1.0 的存档（worldN.ksd）需要管理员用 --save-key world.ksd 新建世界。";
            List<KsdWorld> inFolder; try { inFolder = SaveRules.KsdWorlds(dir); } catch (Exception) { inFolder = []; }
            mismatch.IsVisible = w != null && w.SaveKey != SaveLayout.KsdKey && inFolder.Count > 0;
            mismatch.Text = $"这个目录里有 {inFolder.Count} 个游戏 1.0 世界文件（{string.Join("、", inFolder.Select(k => $"world{k.Slot}.ksd"))}），但第 2 步选的是旧格式世界，所以不会显示世界文件列表，也无法检查和恢复。请让管理员把服务端升级到 1.1.0 以上，用 --save-key world.ksd 新建世界，再点“验证连接”后选择它。";
            if (!slotBlock.IsVisible) { slotList = []; slots.ItemsSource = null; return; }
            List<KsdWorld> existing; try { existing = SaveRules.KsdWorlds(dir); } catch (Exception) { existing = []; }
            var free = Enumerable.Range(0, SaveLayout.MaxSlot + 1).First(n => existing.All(e => e.Slot != n));
            slotList = existing.Select(e => e.Slot).Append(free).ToList();
            slots.ItemsSource = existing.Select(e => $"world{e.Slot}.ksd · {Size(e.Size)} · {e.Modified.LocalDateTime:MM-dd HH:mm} 修改").Append($"新建 world{free}.ksd（本机还没有这个世界）").ToList();
            // Never guess on first setup: picking someone's own solo world by default would get it replaced on restore.
            var same = current.World?.Id == w!.Id && current.World.SaveKey == SaveLayout.KsdKey && string.Equals(Path.GetFullPath(current.SaveDirectory.Length > 0 ? current.SaveDirectory : "."), dir.Length > 0 ? Path.GetFullPath(dir) : "", StringComparison.OrdinalIgnoreCase);
            slots.SelectedIndex = same ? slotList.IndexOf(current.WorldSlot) : -1;
        }
        folder.TextChanged += (_, _) => { ResetFiles(); FillSlots(); }; worlds.SelectionChanged += (_, _) => { ResetFiles(); FillSlots(); }; slots.SelectionChanged += (_, _) => ResetFiles();

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
                identity.Text = $"已验证：{me.Identity.User.Name} · 访问码 {me.Identity.ExpiresAt.LocalDateTime:d} 到期" + (worldList.Count == 0 ? "\n服务器上还没有你可以访问的世界。请让管理员用 vault world add … --save-key world.ksd 建立世界并授权，然后重新验证。" : "");
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
                var layout = new SaveLayout(w.SaveKey, 0);
                if (layout.IsKsd) layout = layout with { Slot = Slot() ?? throw new IOException("请先选择这台电脑上对应的世界文件。") };
                var scan = await new SaveScanner(services.Guard).StableScanAsync(dir, layout, true);
                if (layout.IsKsd)
                {
                    // Show what is used and, just as important, what is left alone.
                    var others = Directory.EnumerateFileSystemEntries(dir).Select(p => Path.GetFileName(p) + (Directory.Exists(p) ? "/" : "")).Where(n => !layout.BelongsLocal(n) && !n.StartsWith(".mist-", StringComparison.Ordinal)).Order(StringComparer.OrdinalIgnoreCase).ToList();
                    preview.Text = (scan.Files.Count == 0 ? $"本机还没有 {layout.KsdFile}：恢复时会新建这个文件。" : $"上传：本机 {layout.KsdFile}（{Size(scan.TotalSize)}）→ 云端 world.ksd\n恢复：云端 world.ksd → 本机 {layout.KsdFile}")
                        + (others.Count > 0 ? "\n\n保持不动：\n" + string.Join("  ", others) : "")
                        + (others.Contains("migrated/") ? "\nmigrated/ 是游戏 1.0 转换前的旧存档，工具不会使用。" : "");
                }
                else preview.Text = $"世界文件标识 {w.SaveKey} · {scan.Files.Count} 个文件 · {Size(scan.TotalSize)}\n" + (scan.Files.Count == 0 ? "这个目录里还没有该世界，可以保存设置后先从云端恢复。" : string.Join("\n", scan.Files.Select(f => $"{f.Path}  {Size(f.Size)}")));
                previewBox.IsVisible = true; filesOk.IsChecked = false; inspected = (dir, w.Id, layout.Slot);
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
                var slot = w.SaveKey == SaveLayout.KsdKey ? Slot() ?? throw new IOException("请先选择这台电脑上对应的世界文件。") : 0;
                if (inspected != (dir, w.Id, slot) || filesOk.IsChecked != true || cloudOk.IsChecked != true) throw new IOException("请点“检查文件清单”，核对后勾选“确认”里的两项。");
                services.SaveToken(verifiedServer, verifiedToken);
                window.Close(new ClientSettings { Server = verifiedServer, World = w, WorldSlot = slot, SaveDirectory = Path.GetFullPath(dir), SteamCloudAcknowledged = true, FilesConfirmed = true });
            }
            catch (Exception e) { Fail(e.Message); }
        };
        return await window.ShowDialog<ClientSettings?>(owner);
    }
}
