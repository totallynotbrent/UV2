using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace UmaLauncher
{
    // checks github for a newer uv2 build and swaps the app folder in place.
    // the rolling experimental release is matched by tag and zip asset, and
    // the running build's commit is compared against the release target
    // instead of version numbers.
    static class Updater
    {
        private const string Repo = "totallynotbrent/UV2";
        private const string ReleaseTag = "experimental";
        private const string AssetName = "UV2-Windows-x64.zip";

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
                // endpoint, so pick the experimental release by tag and zip asset.
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

        // re-extracts entries that were locked while the old process was still
        // alive; runs before the form opens so the app boots fully updated.
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
                    string target = Path.Combine(appDir, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, true);
                }
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

            string exePath = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                File.Copy(exePath, Path.Combine(backupDir, "UmaLauncher.exe.bak"), true);

            List<string> pending = [];
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
                try
                {
                    entry.ExtractToFile(target, true);
                }
                catch (IOException)
                {
                    pending.Add(entry.FullName);
                }
            }

            if (pending.Count > 0)
            {
                File.WriteAllLines(Path.Combine(backupDir, "pending.txt"), pending);
                File.Copy(zipPath, Path.Combine(backupDir, "pending.zip"), true);
            }

            if (!string.IsNullOrEmpty(exePath))
                Process.Start(exePath);
            Environment.Exit(0);
        }
    }
}
