# 雾锁存档助手 1.1.1

**中文** | [English](README.en.md) · [版本说明](docs/release-notes/v1.1.1.md) · [更新记录](CHANGELOG.md)

雾锁王国（Enshrouded）多人世界的共享存档工具，支持游戏 1.0 的 `worldN.ksd` 存档，也兼容抢先体验版的旧格式。你、A、B 轮流恢复同一个世界并当房主；Linux 服务器只保存备份，不运行游戏。

- **Windows 客户端**：一个 `.exe`，自带 .NET 运行时和界面库，**下载后双击即用，无需安装任何依赖**，也不需要管理员权限。
- **Linux 服务端**：Go 单文件服务，配合 Caddy 提供 HTTPS。
- **macOS 演示版**：与 Windows 共用同一套界面，自带本机服务和示例世界，用来先体验完整流程。

界面支持浅色、深色和跟随系统（右上角切换）。

## 下载

在 GitHub 的 Releases 页面下载：

| 文件 | 用途 |
| --- | --- |
| `EnshroudedSaveManager-1.1.1-win-x64.exe` | Windows 10/11 x64 客户端，双击运行 |
| `save-server-1.1.1-linux-amd64` | 常见 x86_64 Linux 服务器 |
| `save-server-1.1.1-linux-arm64` | ARM64 Linux 服务器 |

Releases 由仓库里的 GitHub Actions 在推送 `v*` 标签时自动构建，构建过程不含任何密钥或访问码。EXE 目前没有代码签名，第一次运行时 Windows SmartScreen 可能提示“未知发布者”，选择“更多信息 → 仍要运行”。

## Windows：首次设置

1. 双击 `EnshroudedSaveManager-1.1.1-win-x64.exe`，自动打开“首次设置”。
2. **服务器与访问码**：填写 `https://你的域名` 和管理员单独发给你的个人访问码，点“验证连接”，确认显示的是你自己的名字。
3. **共享世界**：选择要一起玩的世界。
4. **本地存档目录**：点下方找到的常见位置，或点“选择…”手动指定。游戏 1.0 有两种存档位置，在游戏的世界列表里分别显示为“本地”和“Steam 云”：
   - 本地：`%USERPROFILE%\Saved Games\keen games\Enshrouded\default`
   - Steam 云：Steam 安装目录下的 `userdata\<账号目录>\1203620\remote`（例如 `D:\Program Files (x86)\Steam\userdata\…\1203620\remote`）
   - 抢先体验版旧存档：`%USERPROFILE%\Saved Games\enshrouded`
5. **这台电脑上的世界文件**（游戏 1.0 世界）：游戏 1.0 里每个世界是一个 `worldN.ksd`，编号在每台电脑上可能不同。列表按修改时间排列，选择与共享世界对应的那个文件；本机还没有这个世界就选“新建 worldN.ksd”，之后从云端恢复。首次设置不会自动选择，避免误把你自己的单人世界当成共享世界。如果第 2 步选的是抢先体验版格式的世界，而目录里是 `worldN.ksd`，这里不会出现列表，而是提示让管理员新建 `world.ksd` 世界。窗口标题和设置底部会显示客户端版本号，方便确认用的是哪个版本。
6. 点“检查文件清单”，核对会上传和恢复的文件，以及“保持不动”的文件。
7. 勾选两项确认（文件组已核对、Steam Cloud 已处理），点“保存并连接”。

访问码保存在 Windows 凭据管理器，不写入配置文件。以后点左下角“设置”可以重新修改。

**处理 Steam Cloud**：避免它与工具覆盖同一批文件。关闭游戏的 Cloud 也可能影响角色云同步，先备份角色与现有世界。工具不会自动更改 Steam 设置。

**存档识别边界**：

- **游戏 1.0 世界**（服务器上的世界标识为 `world.ksd`）：只管理你选择的那一个 `worldN.ksd`。云端统一保存为 `world.ksd`，上传时从本机槽位读取，恢复时写回本机槽位，所以两台电脑的编号不同也没关系。`character*.ksd`（角色）、`global.ksd`、`user_settings.json`、其他 `worldN.ksd`，以及游戏自己的 `backup/`、`migrated/` 都不会被上传或覆盖。
- **抢先体验版世界**（8 位小写十六进制标识）：管理该文件及以 `标识_`、`标识-`、`标识.` 开头的同组平面文件，与 1.0.0 相同。
- 工具不解析游戏的二进制格式，也不能根据游戏内的世界名称自动证明对应关系，所以首次必须人工核对。所选槽位会被恢复的存档替换（替换前自动备份）。路径中的符号链接或目录联接会被拒绝。
- `migrated/` 里是游戏 1.0 转换前的旧存档，游戏已经不再读取，不要拿它当最新进度。

## 日常使用

**开玩前**：在列表里搜索或选择一份存档 → “使用选中的存档” → 确认。工具先完整下载并校验，再备份本地世界，然后恢复。看到“恢复完成”后进入游戏开启多人世界，A、B 加入。

**玩完后**：完全退出游戏。工具开着时会发现游戏退出并自动检查；也可以玩完再打开工具，或手动点“检查存档”。发现新进度后，上传按钮会成为主要操作：核对本地路径、文件数、大小和最近保存时间，填写名称和备注，勾选“游戏已正常保存并完全退出”，确认上传。

**下一位房主**：点“刷新”，查看名称、备注、上传人和时间，选择任意一份恢复后开世界。

- 游戏运行时，“检查”“上传”“恢复”都会暂停，卡片显示“游戏正在运行”。
- 本地有尚未上传的进度时，恢复按钮会降为次要样式，确认框里也会提醒；旧档仍会保留在本地备份里。
- “最新”指最新上传，不代表游戏进度最多。两人分开游玩的存档都会保留，不会自动合并。
- 搜索覆盖当前世界的全部授权备份，不只当前页。每页 4 条，默认按上传时间倒序，也可以按最早或名称（Unicode 顺序）。翻页或搜索不会悄悄改变你的选择；当前页没有选中项时，恢复按钮不可用。
- 本地检查失败（目录找不到、游戏仍在保存等）会在卡片上用红色写明原因，这时不能上传；检查失败不会被当作“没有变化”。

## 失败与恢复

- 上传前把完整快照保存在本地。网络失败会保留任务，下次点“上传我的存档”可以选择“重试上传”“放弃任务”或“暂不处理”；重试使用同一个操作 ID，服务端不会重复生成记录。放弃任务只清除记录，本地打包文件保留。
- 打开上传窗口后取消，不会生成任何文件。确认期间存档又发生变化时，工具停止打包并重新检查。
- 恢复失败时自动回滚。如果恢复中途被打断，检查按钮会变成“恢复旧档”；重新打开工具时也会先尝试回滚。
- 左下角“本地备份”打开 `%LOCALAPPDATA%\EnshroudedSaveManager`：`outbox` 是上传前的快照，`restore` 是恢复前的旧档和清单，`downloads` 是下载的归档。不要删除正在使用的 `restore-journal.json` 或对应旧档。
- 文件校验通过只证明传输与字节内容一致，不保证游戏一定能加载；游戏异常退出后的存档应先在游戏内验证。工具不会强制结束游戏。

服务器管理员可以读取存档；1.0 不是端到端加密。

## macOS：先试用演示版

用 `sh scripts/build-mac-preview.sh` 构建 `dist/macos-arm64/雾锁存档助手.app`（Apple Silicon，自包含）。应用会启动随包的 Go 本机服务和独立的示例世界，**不访问真实游戏存档，也不连接正式服务器**。

1. 当前身份“我”已有一份新进度。点“检查存档”，再“上传我的存档”，核对路径、文件数量和大小，填写名称和备注。
2. 右上角切换到 B。B 使用独立的访问码和本地目录，能看到刚上传的备份；选择后点“使用选中的存档”。
3. 点“模拟玩完”，只修改 B 的示例文件并自动检查。再上传后切回“我”，验证交接。

演示数据位于 `~/Library/Application Support/EnshroudedSavePreview`。关闭应用时会停止它启动的本机服务；演示令牌只存在内存中。

## Linux 部署

需要一个指向 Linux 主机的域名和有效 HTTPS 证书。以下以 Debian/Ubuntu、域名 `saves.example.com` 为例，替换成你的域名。服务器可以先用 1 核、1 GB 内存试运行，磁盘按实际存档和保留量配置。

1. 按官方说明安装 Caddy，将匹配主机架构的服务端二进制（改名为 `save-server`）和 `deploy/` 下的文件复制到服务器。
2. 创建独立用户、安装程序和 systemd 配置：

```bash
sudo useradd --system --home /var/lib/enshrouded-save --shell /usr/sbin/nologin enshrouded-save
sudo install -d -m 755 /opt/enshrouded-save
sudo install -m 755 save-server /opt/enshrouded-save/save-server
sudo install -m 644 enshrouded-save.service /etc/systemd/system/enshrouded-save.service
sudo systemctl daemon-reload
sudo systemctl enable --now enshrouded-save
```

3. 把 `deploy/Caddyfile` 里的域名改成自己的，合并到现有 Caddy 配置，验证后重载：

```bash
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

对外开放 443；80 按 Caddy 证书签发和跳转的需要开放。8787 只监听 `127.0.0.1`，不要转发到公网。SSH 使用密钥登录并限制来源。不要把 `/var/lib/enshrouded-save` 配成静态文件目录。

4. 建立玩家、共享库和世界。以下函数只在服务器管理终端中使用：

```bash
vault() {
  sudo -u enshrouded-save /opt/enshrouded-save/save-server --data /var/lib/enshrouded-save "$@"
}

vault user add --id me --name 我
vault user add --id user_a --name A
vault user add --id user_b --name B
vault library add --id friends --name 共同存档库

# 游戏 1.0 的世界：标识固定写 world.ksd（每台电脑在客户端设置里选择自己的 worldN.ksd）。
vault world add --id shared --library friends --name 共同世界 --save-key world.ksd
# 抢先体验版旧存档才使用 8 位小写十六进制标识，例如：--save-key 3ad85aea

vault member grant --user me --library friends
vault member grant --user user_a --library friends
vault member grant --user user_b --library friends

vault token issue --user me --device My-PC --days 180
vault token issue --user user_a --device A-PC --days 180
vault token issue --user user_b --device B-PC --days 180
```

访问码只在签发时完整显示一次。通过可信的私密渠道分别交给本人，不要发到公共群聊，不要放进 EXE、脚本、日志或这个仓库。服务端只保存 SHA-256 摘要。

5. 查看状态、撤销或限制访问：

```bash
vault status
vault token revoke --id 替换为凭证ID
vault user disable --id user_a
vault user disable --id user_a --disabled=false
vault member revoke --user user_b --library friends
vault member grant --user user_b --library friends --write=false
```

只读成员能查看和恢复，但不能上传（客户端会显示“只读”）。凭证的 `last_used_since_start` 是本次服务启动以来最近一次使用的时间，服务重启后归零；完整的操作记录查看 `journalctl -u enshrouded-save`。日志不记录访问码。访问码 7 天内到期时，客户端会提醒联系管理员更换。

## 从 1.0.0 升级（游戏 1.0 存档）

1.0.0 只认识抢先体验版的 8 位标识，无法管理游戏 1.0 的 `worldN.ksd`。升级顺序：

1. **服务端**：备份数据目录，停止服务，替换为 1.1.1 的二进制，再启动。已有的世界和备份不受影响。
2. **建立 1.0 世界**：`vault world add --id <新ID> --library friends --name <名称> --save-key world.ksd`。世界标识创建后不能修改，所以 1.0 世界需要新建，不要沿用旧的 8 位标识世界。
3. **客户端**：每位玩家换成 1.1.1 的 EXE，在“设置”里选择新世界和自己电脑上的 `worldN.ksd`。
4. **先用副本验证**：第一次交接前，先复制一份 `worldN.ksd` 到另一台电脑（可以换一个编号），确认游戏能在世界列表里看到并正常加载。

## 服务限制、备份和升级

默认单个压缩包 512 MiB、解压后 2 GiB、128 个文件；服务端总对象配额 20 GiB、每人 10 GiB，保留至少 256 MiB 空间并预留一次最大上传。最多同时进行 2 个上传。

可以在 systemd 的 ExecStart 中调整 `--max-storage-bytes`、`--max-user-storage-bytes` 等参数。客户端固定使用上述单包和解压上限，调整服务端不会自动放宽客户端限制。普通成员没有删除接口，旧版本不会自动清理；磁盘空间不足时拒绝新上传。

备份整个数据目录时，先停止服务，复制 `state.json` 与完整的 `objects/`，再启动，避免不一致。备份存放到独立位置并加密；不要只保存 ZIP 而丢失索引。升级也先备份数据、停止服务，再替换二进制。

从旧服务备份恢复可能带回当时还没撤销的凭证。第一次恢复启动时先阻止公网入口，在本机执行：

```bash
vault token revoke-all
```

之后重新签发访问码，再恢复公网访问。启动时如果缺少索引引用的归档，服务会拒绝启动，避免把不完整的备份当作正常服务。

## 从源码构建与检查

需要 Go 1.25+、.NET SDK 10。所有测试都使用临时生成的文件，不使用真实游戏存档。

```bash
cd server && go test -race ./... && go vet ./... && cd ..
dotnet run --project client/CoreChecks/CoreChecks.csproj
sh scripts/build-server.sh            # Linux 服务端 → dist/linux-*
sh scripts/build-windows.sh           # Windows 单文件 EXE → dist/windows-x64（可在 macOS/Linux 交叉构建）
sh scripts/build-mac-preview.sh       # macOS 演示版 → dist/macos-arm64
```

Windows 上也可以运行 `scripts/build-client.ps1`。跨端联调：先把当前主机的服务端构建为 `.build/save-server`，编译 CoreChecks，再运行 `python3 scripts/integration-smoke.py`；它临时启动只监听本机的 HTTP 测试服务，不放开正式客户端的 HTTPS 要求。

| 路径 | 内容 |
| --- | --- |
| `client/SaveDesktop/` | 桌面客户端（Avalonia 11.3）：Windows 正式版与 macOS 演示共用 |
| `client/SaveCore/` | 检查、快照、HTTP 与事务恢复核心 |
| `client/CoreChecks/` | 客户端核心与跨端联调检查 |
| `server/` | Go 服务端源码和安全测试 |
| `deploy/` | Caddy HTTPS 与 systemd 配置 |
| `.github/workflows/` | CI 测试与 Release 构建 |

**已验证**：

- Go：身份授权、撤销/过期/停用、跨库隔离、伪造上传者、幂等、搜索分页、配额、损坏归档、链接/路径拒绝、重启与单写进程锁。
- C# 核心：检查变化、只处理选定世界、快照校验、部分恢复失败回滚、重启恢复、拒绝恶意路径与链接、待上传任务和 HTTP 凭证策略；对真实 Go 服务端的身份识别、中文备注上传/搜索、幂等重试、下载、本地恢复和撤销令牌。
- 游戏 1.0 存档：服务端只接受 `world.ksd` 一个文件，拒绝 `world0.ksd`、`character0.ksd`、`global.ksd`、游戏备份等；客户端从槽位 0 上传、恢复到另一台电脑的槽位 2/3/5（含新建槽位），部分写入失败回滚、恢复中断后重启回滚，其他世界、角色、`global.ksd`、`backup/`、`migrated/` 均保持不变；真实 Go 服务端与 C# 客户端之间完成同样的上传、下载和跨槽位恢复。
- 桌面客户端：在 macOS 上以真实 Go 服务端走通正式模式的首次设置 → 连接 → 列表 → 游戏运行时锁定 → 游戏退出后自动检查；访问码不写入设置文件。演示模式走通上传、切换身份、恢复、翻页选择、检查失败提示和浅色/深色切换。

**尚需在 Windows 实机验收**：窗口与高 DPI 显示、Windows 凭据管理器、游戏进程识别、Steam Cloud 冲突处理，以及两台电脑轮流加载。游戏 1.0 方面还要确认：只复制 `worldN.ksd`（不带 `global.ksd`）、并改成另一个编号后，游戏能在世界列表里看到并正常加载；这是依据服务器托管商的迁移说明做出的判断，Keen 官方尚未公开文件格式说明。这里没有把 macOS 上的测试当作 Windows 或游戏实测。请先用世界存档的副本完成两台 Windows 电脑的交接验收，再接入唯一的正式进度。
