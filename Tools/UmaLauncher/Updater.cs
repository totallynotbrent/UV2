using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace UmaLauncher
{
    // checks github for a newer uv2 build and swaps the app folder in place.
    // the rolling experimental prerelease is matched by tag and zip asset, and
    // the running build's commit is compared against the release target
    // instead of version numbers.
    static class Updater
    {
        private const string Repo = "totallynotbrent/UmaLive";
        private const string ReleaseTag = "experimental";
        private const string AssetName = "UmaLive-Windows-x64.zip";

        private static readonly HttpClient http = new()
        {
            DefaultRequestHeaders = { { "User-Agent", "UmaLauncher-Updater" } }
        };

        // the commit this launcher was built from, stamped by CI.
        private static string? BuildCommit()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }

        // runs the flow on a background thread, marshals messages to the parent form.
        public static async void Check(Control parent)
        {
            try
            {
                string body = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases");
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                // the prerelease flag is unreliable on the releases list
                // endpoint, so pick the experimental prerelease by tag and zip asset.
                JsonElement? release = null;
                foreach (JsonElement r in root.EnumerateArray())
                {
                    if (r.GetProperty("tag_name").GetString() != ReleaseTag) continue;
                    bool hasAsset = false;
                    foreach (JsonElement asset in r.GetProperty("assets").EnumerateArray())
                    {
                        if (asset.GetProperty("name").GetString() == AssetName) { hasAsset = true; break; }
                    }
                    if (!hasAsset) continue;
                    release = r;
                    break;
                }
                if (release is null)
                {
                    parent.BeginInvoke(() => MessageBox.Show(parent,
                        "No published build found on github.", LanguageManager.T("update_title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information));
                    return;
                }

                string target = release.Value.GetProperty("target_commitish").GetString() ?? "";
                string? url = null;
                foreach (JsonElement asset in release.Value.GetProperty("assets").EnumerateArray())
                {
                    if (asset.GetProperty("name").GetString() == AssetName)
                    {
                        url = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                string mine = BuildCommit() ?? "unknown";
                if (url is null || target == mine)
                {
                    parent.BeginInvoke(() => MessageBox.Show(parent,
                        LanguageManager.T("latest"), LanguageManager.T("update_title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information));
                    return;
                }

                DialogResult choice = MessageBox.Show(parent,
                    $"{LanguageManager.T("update_available")}\n\n{LanguageManager.T("installed")}: {mine}\n{LanguageManager.T("latest_label")}: {target}\n\n{LanguageManager.T("download_now")}",
                    LanguageManager.T("update_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (choice != DialogResult.Yes) return;

                string zipPath = Path.Combine(Path.GetTempPath(), AssetName);
                await File.WriteAllBytesAsync(zipPath, await http.GetByteArrayAsync(url));

                InstallAndRestart(parent, zipPath);
            }
            catch (Exception ex)
            {
                parent.BeginInvoke(() => MessageBox.Show(parent,
                    LanguageManager.T("update_failed") + ex.Message,
                    LanguageManager.T("update_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning));
            }
        }

        // files the app/launcher write at runtime; the prune pass never
        // touches them (everything else in the folder belongs to the
        // shipped build and may be deleted when it disappears from the
        // zip, so renamed or removed files stop accumulating forever).
        private static readonly HashSet<string> UserFiles =
        [
            "Config.json", "selection.json", "control.json", "status.json",
            "apply_update.cmd",
        ];

        // deletes every file under appDir that the update zip did not
        // ship, except the user-written set and the backup folder. the
        // old extract-over update left stale files behind forever
        // (renamed assemblies, removed plugins), which is why parts of
        // the app stayed on old versions after an "update".
        private static void PruneStaleFiles(string appDir, ZipArchive archive)
        {
            var shipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in archive.Entries)
                if (!entry.FullName.EndsWith('/'))
                    shipped.Add(entry.FullName.Replace('/', Path.DirectorySeparatorChar));

            string backupDir = Path.Combine(appDir, "backup");
            foreach (string file in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(appDir, file);
                if (rel.StartsWith("backup", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.Equals("UmaLauncher.exe", StringComparison.OrdinalIgnoreCase)) continue; // the swap script owns it
                if (UserFiles.Contains(rel)) continue;
                if (shipped.Contains(rel)) continue;
                try { File.Delete(file); } catch { }
            }
            // drop now-empty directories the zip no longer ships.
            foreach (string dir in Directory.EnumerateDirectories(appDir, "*", SearchOption.AllDirectories)
                                    .OrderByDescending(d => d.Length))
            {
                if (dir.StartsWith(backupDir, StringComparison.OrdinalIgnoreCase)) continue;
                try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
            }
        }

        // re-extracts entries that were locked while the old process was still
        // alive; runs before the form opens so the app boots fully updated.
        // the update flow now kills player + attach windows before extract,
        // so this only carries leftovers from an unusually stubborn lock
        // (e.g. an antivirus scan holding a file); it must skip its own exe
        // (the cmd swap owns that now).
        public static void ApplyPending()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string pendingPath = Path.Combine(appDir, "backup", "pending.txt");
                string zipPath = Path.Combine(appDir, "backup", "pending.zip");
                if (!File.Exists(pendingPath) || !File.Exists(zipPath)) return;

                var pending = new HashSet<string>(File.ReadAllLines(pendingPath));
                using ZipArchive archive = ZipFile.OpenRead(zipPath);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!pending.Contains(entry.FullName)) continue;
                    if (Path.GetFileName(entry.FullName) == "UmaLauncher.exe") continue;
                    string target = Path.Combine(appDir, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, true);
                }
                try { PruneStaleFiles(appDir, archive); } catch { }
                File.Delete(pendingPath);
                File.Delete(zipPath);
            }
            catch
            {
                // a failed catch-up must never block boot.
            }
        }

        // extracts over the app folder; locked files land in a pending list for the next boot.
        private static void InstallAndRestart(Control parent, string zipPath)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string backupDir = Path.Combine(appDir, "backup");
            Directory.CreateDirectory(backupDir);

            // a running concert player (and any attach-mode control window)
            // locks UmaLive.exe and GameAssembly.dll, so an update with a concert
            // open silently skipped them and the player ran the old build
            // until a manual reinstall. kill every uv2/launcher instance
            // except this process before extracting; the files unlock the
            // moment the processes die.
            int self = Environment.ProcessId;
            foreach (string name in new[] { "UmaLive", "UmaLauncher" })
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try { if (proc.Id != self) proc.Kill(); }
                    catch { }
                }
            }
            // give windows a moment to release the file locks.
            try { Thread.Sleep(1500); } catch { }

            string exePath = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                File.Copy(exePath, Path.Combine(backupDir, "UmaLauncher.exe.bak"), true);

            List<string> pending = [];
            // the launcher's own payload is locked by this running process:
            // the exe is the image we execute from, and the dll is mapped the
            // moment Main starts — Main lives inside it. an extract-over can
            // never succeed for either, and the boot-time pending pass cannot
            // rescue the dll either (it also runs inside Main). every
            // launcher file lands in a stage dir and the restart script
            // moves them into place after this process dies.
            string stageDir = Path.Combine(backupDir, "stage");
            Directory.CreateDirectory(stageDir);
            try { foreach (string f in Directory.GetFiles(stageDir)) File.Delete(f); } catch { }
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string target = Path.Combine(appDir, entry.FullName);
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string file = Path.GetFileName(entry.FullName);
                bool launcher_payload = string.IsNullOrEmpty(Path.GetDirectoryName(entry.FullName)) &&
                                       file.StartsWith("UmaLauncher.", StringComparison.OrdinalIgnoreCase);
                if (launcher_payload)
                {
                    entry.ExtractToFile(Path.Combine(stageDir, file), true);
                    continue;
                }
                try
                {
                    entry.ExtractToFile(target, true);
                }
                catch (IOException)
                {
                    pending.Add(entry.FullName);
                }
            }

            // the zip is the full truth about what the app should be:
            // anything it no longer ships gets removed so a renamed or
            // deleted file can never survive an update.
            try { PruneStaleFiles(appDir, archive); } catch { }

            if (pending.Count > 0)
            {
                File.WriteAllLines(Path.Combine(backupDir, "pending.txt"), pending);
                File.Copy(zipPath, Path.Combine(backupDir, "pending.zip"), true);
            }

            // swap the launcher payload and relaunch: the script waits for
            // this process to die, moves every staged file over its target
            // (locks release the moment the process exits; a short retry
            // loop rides out an antivirus scan holding a file), starts the
            // launcher, and deletes itself. falls back to starting the old
            // exe when nothing staged so the user is never left stranded.
            if (!string.IsNullOrEmpty(exePath) && Directory.Exists(stageDir) && Directory.GetFiles(stageDir).Length > 0)
            {
                string script =
                    "@echo off\r\n" +
                    ":wait\r\n" +
                    "tasklist /fi \"PID eq " + self + "\" 2>nul | find \"" + self + "\" >nul\r\n" +
                    "if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto wait )\r\n" +
                    "set movedok=1\r\n" +
                    ":move\r\n" +
                    "for %%F in (\"" + stageDir + "\\*\") do (\r\n" +
                    "  move /y \"%%~fF\" \"" + appDir + "\\%%~nxF\" >nul 2>nul || set movedok=0\r\n" +
                    ")\r\n" +
                    "if \"%movedok%\"==\"1\" goto done\r\n" +
                    "set /a tries=tries+1\r\n" +
                    "if %tries% geq 30 goto done\r\n" +
                    "timeout /t 1 /nobreak >nul\r\n" +
                    "goto move\r\n" +
                    ":done\r\n" +
                    "start \"\" \"" + exePath + "\"\r\n" +
                    "del /q \"%~f0\" >nul 2>nul\r\n";
                string scriptPath = Path.Combine(appDir, "apply_update.cmd");
                File.WriteAllText(scriptPath, script);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"" + scriptPath + "\"",
                    WorkingDirectory = appDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            else if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(exePath);
            }
            Environment.Exit(0);
        }
    }
}
