# Enshrouded Save Vault 1.1.0

[中文](README.md) | **English** · [Release notes](docs/release-notes/v1.1.0.md) · [Changelog](CHANGELOG.md)

A shared-save tool for Enshrouded multiplayer worlds. It supports the game 1.0 `worldN.ksd` saves as well as the older Early Access format. You and your friends take turns restoring the same world and hosting it; the Linux server only stores backups and never runs the game.

- **Windows client**: a single `.exe` with the .NET runtime and UI libraries built in. **Download and double-click — nothing to install**, no administrator rights needed.
- **Linux server**: a single Go binary, served over HTTPS by Caddy.
- **macOS demo**: the same interface as Windows, bundled with a local server and sample worlds so you can try the whole flow first.

The interface supports light, dark and follow-system appearance (top-right menu). The UI text is in Chinese.

## Download

From the GitHub Releases page:

| File | Purpose |
| --- | --- |
| `EnshroudedSaveManager-1.1.0-win-x64.exe` | Windows 10/11 x64 client — double-click to run |
| `save-server-1.1.0-linux-amd64` | Server for common x86_64 Linux hosts |
| `save-server-1.1.0-linux-arm64` | Server for ARM64 Linux hosts |
| `SHA256SUMS.txt` | Checksums of the files above |

Releases are built by GitHub Actions in this repository whenever a `v*` tag is pushed; the build uses no secrets or access codes. The exe is not code-signed yet, so Windows SmartScreen may warn about an "unknown publisher" on first run — choose "More info → Run anyway".

## Windows: first-run setup

1. Double-click the exe; the first-run setup opens automatically.
2. **Server and access code**: enter `https://your-domain` and the personal access code your admin sent you privately, then click "验证连接" (Verify). Make sure it shows your own name.
3. **Shared world**: pick the world you play together.
4. **Local save folder**: click one of the detected locations, or "选择…" (Browse). Game 1.0 keeps saves in two places, shown in the in-game world list as "Local" and "Steam Cloud":
   - Local: `%USERPROFILE%\Saved Games\keen games\Enshrouded\default`
   - Steam Cloud: `userdata\<account>\1203620\remote` under the Steam install folder (e.g. `D:\Program Files (x86)\Steam\userdata\…\1203620\remote`)
   - Early Access saves: `%USERPROFILE%\Saved Games\enshrouded`
5. **World file on this PC** (game 1.0 worlds): every world is one `worldN.ksd`, and N can differ between PCs. The list is sorted by modified time; pick the file that is the shared world, or "新建 worldN.ksd" (new) if this PC doesn't have it yet and you'll restore from the cloud. Nothing is preselected on first setup, so your own solo world is never mistaken for the shared one.
6. Click "检查文件清单" (Check file list) and review what will be uploaded and restored, and what is left untouched.
7. Tick both confirmations (file group checked, Steam Cloud handled) and click "保存并连接" (Save and connect).

The access code is stored in Windows Credential Manager, never in the settings file. Use "设置" (Settings) in the bottom-left corner to change it later.

**Steam Cloud**: make sure it doesn't overwrite the same files as this tool. Turning off Cloud for the game may also affect character sync, so back up characters and existing worlds first. The tool never changes Steam settings.

**What counts as the world**:

- **Game 1.0 worlds** (world key `world.ksd` on the server): only the one `worldN.ksd` you picked. The cloud always stores it as `world.ksd`; uploads read it from this PC's slot and restores write it back to this PC's slot, so different slot numbers on different PCs are fine. `character*.ksd`, `global.ksd`, `user_settings.json`, other `worldN.ksd` files and the game's own `backup/` and `migrated/` folders are never uploaded or overwritten.
- **Early Access worlds** (8 lowercase hex digits): the main file plus flat files starting with `key_`, `key-` or `key.`, exactly as in 1.0.0.
- The tool does not parse the game's binary format and cannot prove a match from the in-game world name, so check manually the first time. The chosen slot is replaced by the restored save (after an automatic backup). Symlinks and junctions in the path are rejected.
- `migrated/` holds the pre-1.0 saves the game converted and no longer reads; never treat them as the latest progress.

## Day-to-day use

**Before playing**: search or pick a backup → "使用选中的存档" (Use selected) → confirm. The tool downloads and verifies it, backs up your local world, then restores. After "恢复完成" (Restore complete), start the game and host; friends join.

**After playing**: quit the game completely. If the tool is open it notices the game exit and checks automatically; you can also open it later or click "检查存档" (Check). When new progress is found, upload becomes the main action: review the local path, file count, size and last save time, enter a name and note, tick "游戏已正常保存并完全退出" (game saved and fully exited), and confirm.

**Next host**: click "刷新" (Refresh), look at names, notes, uploaders and times, restore any backup and host.

- While the game runs, check, upload and restore are paused and the card shows "游戏正在运行" (game running).
- If you have local progress that hasn't been uploaded, the restore button becomes secondary and the confirmation warns you; the old save is still kept in local backups.
- "最新" (Latest) means most recently uploaded, not furthest progress. Separate playthroughs are all kept and never merged.
- Search covers all authorised backups of the world, not just the current page. 4 per page, newest first by default; oldest-first and name (Unicode order) are also available. Paging or searching never silently changes your selection; if nothing on the page is selected, restore is disabled.
- A failed local check (folder missing, game still saving, …) shows the reason in red on the card and blocks uploading; a failure is never treated as "no changes".

## Failures and recovery

- A full snapshot is saved locally before uploading. If the network fails the task is kept; next time "上传我的存档" offers retry, discard or not now. A retry reuses the same operation ID so the server never creates duplicates. Discarding only clears the record; the local package stays.
- Cancelling the upload dialog leaves no files behind. If the save changes while you confirm, packaging stops and the tool re-checks.
- A failed restore rolls back automatically. If a restore is interrupted, the check button becomes "恢复旧档" (Restore old save); reopening the tool also tries to roll back first.
- "本地备份" (Local backups) opens `%LOCALAPPDATA%\EnshroudedSaveManager`: `outbox` holds pre-upload snapshots, `restore` holds pre-restore saves and manifests, `downloads` holds downloaded archives. Do not delete an active `restore-journal.json` or its old save.
- Passing checksums only proves the bytes arrived intact, not that the game can load them; verify a save in-game after an abnormal game exit. The tool never force-quits the game.

The server admin can read the saves; 1.0 is not end-to-end encrypted.

## macOS: try the demo first

Build `dist/macos-arm64/雾锁存档助手.app` (Apple Silicon, self-contained) with `sh scripts/build-mac-preview.sh`. It starts its bundled Go server and separate sample worlds — **it never touches real game saves or connects to a real server**.

1. Identity "我" (me) already has new progress. Click check, then upload; review the path, count and size, and enter a name and note.
2. Switch to B in the top-right. B has its own access code and local folder, sees the new backup, and can restore it with "使用选中的存档".
3. Click "模拟玩完" (Simulate a session) to modify B's sample files and check automatically. Upload again and switch back to "我" to try the hand-over.

Demo data lives in `~/Library/Application Support/EnshroudedSavePreview`. Closing the app stops the server it started; demo tokens only exist in memory.

## Linux deployment

You need a domain pointing at the Linux host and a valid HTTPS certificate. The example uses Debian/Ubuntu and `saves.example.com`; replace it with your own domain. Start with 1 CPU and 1 GB RAM; size the disk for your saves and retention.

1. Install Caddy following the official guide. Copy the server binary for your architecture (renamed to `save-server`) and the files in `deploy/` to the server.
2. Create a dedicated user and install the binary and systemd unit:

```bash
sudo useradd --system --home /var/lib/enshrouded-save --shell /usr/sbin/nologin enshrouded-save
sudo install -d -m 755 /opt/enshrouded-save
sudo install -m 755 save-server /opt/enshrouded-save/save-server
sudo install -m 644 enshrouded-save.service /etc/systemd/system/enshrouded-save.service
sudo systemctl daemon-reload
sudo systemctl enable --now enshrouded-save
```

3. Put your domain into `deploy/Caddyfile`, merge it into your Caddy config, validate and reload:

```bash
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

Open port 443; open 80 only as Caddy needs it for certificates and redirects. Port 8787 listens on `127.0.0.1` only — never forward it. Use key-based SSH and restrict its sources. Never serve `/var/lib/enshrouded-save` as static files.

4. Create players, a shared library and the world. Use this function only in the server's admin shell:

```bash
vault() {
  sudo -u enshrouded-save /opt/enshrouded-save/save-server --data /var/lib/enshrouded-save "$@"
}

vault user add --id me --name 我
vault user add --id user_a --name A
vault user add --id user_b --name B
vault library add --id friends --name 共同存档库

# Game 1.0 world: the key is always world.ksd (each PC picks its own worldN.ksd in the client).
vault world add --id shared --library friends --name 共同世界 --save-key world.ksd
# Only Early Access saves use an 8-digit lowercase hex key, e.g. --save-key 3ad85aea

vault member grant --user me --library friends
vault member grant --user user_a --library friends
vault member grant --user user_b --library friends

vault token issue --user me --device My-PC --days 180
vault token issue --user user_a --device A-PC --days 180
vault token issue --user user_b --device B-PC --days 180
```

An access code is shown in full only once, when issued. Give each one privately to its owner — never in a group chat, the exe, scripts, logs or this repository. The server stores only SHA-256 digests.

5. Check status, revoke or restrict access:

```bash
vault status
vault token revoke --id <credential-id>
vault user disable --id user_a
vault user disable --id user_a --disabled=false
vault member revoke --user user_b --library friends
vault member grant --user user_b --library friends --write=false
```

Read-only members can browse and restore but not upload (the client shows "只读", read-only). A credential's `last_used_since_start` is the last use since the service started and resets on restart; see `journalctl -u enshrouded-save` for the full audit log. Access codes are never logged. The client warns when a code expires within 7 days.

## Upgrading from 1.0.0 (game 1.0 saves)

1.0.0 only understands Early Access 8-hex keys and cannot manage game 1.0 `worldN.ksd` files. Upgrade in this order:

1. **Server**: back up the data directory, stop the service, replace the binary with 1.1.0, start it. Existing worlds and backups are unaffected.
2. **Create a 1.0 world**: `vault world add --id <new-id> --library friends --name <name> --save-key world.ksd`. A world's key cannot change, so create a new world instead of reusing an old 8-hex one.
3. **Clients**: every player switches to the 1.1.0 exe and, in Settings, picks the new world and their own `worldN.ksd`.
4. **Verify on a copy first**: before the first hand-over, copy a `worldN.ksd` to another PC (a different number is fine) and confirm the game lists and loads it.

## Limits, backups and upgrades

Defaults: 512 MiB per archive, 2 GiB unpacked, 128 files; 20 GiB total storage, 10 GiB per user, at least 256 MiB free plus room for one maximum upload. At most 2 concurrent uploads.

Adjust `--max-storage-bytes`, `--max-user-storage-bytes` and friends in the systemd `ExecStart`. The client keeps its fixed archive and unpacked limits; raising server limits does not raise them. Members cannot delete, and old versions are never pruned automatically; uploads are refused when disk space runs low.

To back up the data directory, stop the service, copy `state.json` and the whole `objects/`, then start it again. Store backups separately and encrypted; never keep the ZIPs without the index. Before upgrading, back up, stop the service, then replace the binary.

Restoring an old server backup can bring back credentials that were not yet revoked. On the first start after a restore, block public access and run locally:

```bash
vault token revoke-all
```

Then issue new access codes and reopen public access. If an archive referenced by the index is missing, the server refuses to start rather than serve an incomplete store.

## Building and testing from source

Requires Go 1.25+ and .NET SDK 10. All tests use generated temporary files, never real game saves.

```bash
cd server && go test -race ./... && go vet ./... && cd ..
dotnet run --project client/CoreChecks/CoreChecks.csproj
sh scripts/build-server.sh            # Linux servers → dist/linux-*
sh scripts/build-windows.sh           # Windows single-file exe → dist/windows-x64 (cross-builds on macOS/Linux)
sh scripts/build-mac-preview.sh       # macOS demo → dist/macos-arm64
```

On Windows you can also run `scripts/build-client.ps1`. For the cross-stack check, build the host server as `.build/save-server`, build CoreChecks, then run `python3 scripts/integration-smoke.py`; it starts a loopback-only HTTP test server and does not relax the client's HTTPS requirement.

| Path | Contents |
| --- | --- |
| `client/SaveDesktop/` | Desktop client (Avalonia 11.3), shared by the Windows release and the macOS demo |
| `client/SaveCore/` | Check, snapshot, HTTP and transactional restore core |
| `client/CoreChecks/` | Client core and cross-stack checks |
| `server/` | Go server and security tests |
| `deploy/` | Caddy HTTPS and systemd configuration |
| `.github/workflows/` | CI tests and release builds |

**Verified**:

- Go: authentication, revocation/expiry/disabling, cross-library isolation, forged uploaders, idempotency, search and paging, quotas, corrupt archives, link/path rejection, restarts and the single-writer lock.
- C# core: change detection, selected-world isolation, snapshot verification, rollback of partial restores, recovery after restart, rejection of malicious paths and links, pending uploads and the HTTP credential policy; against the real Go server: identity, Chinese-note upload/search, idempotent retry, download, local restore and token revocation.
- Game 1.0 saves: the server accepts only the single `world.ksd` file and rejects `world0.ksd`, `character0.ksd`, `global.ksd`, game backups and the like; the client uploads from slot 0 and restores into slots 2/3/5 on another PC (including a new slot), rolls back partial writes and interrupted restores after a restart, and leaves other worlds, characters, `global.ksd`, `backup/` and `migrated/` untouched; the same upload, download and cross-slot restore pass between the real Go server and the C# client.
- Desktop client: on macOS against the real Go server, the full real-mode flow — first-run setup → connect → list → locked while the game runs → automatic check after it exits; the access code is never written to the settings file. Demo mode: upload, identity switching, restore, selection across pages, check-failure messages and light/dark switching.

**Still to verify on real Windows**: window and high-DPI display, Windows Credential Manager, game process detection, Steam Cloud conflicts, and two PCs taking turns. For game 1.0 also confirm that copying only `worldN.ksd` (without `global.ksd`) under a different number still shows up in the in-game world list and loads; this follows server hosts' migration guides, as Keen has not published the file format. macOS tests are not presented as Windows or in-game testing. Run the hand-over between two Windows PCs on a copy of your world first, before using your only real progress.
