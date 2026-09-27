using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace UmaLauncher
{
    // checks github for a newer uv2 release and swaps the app folder in place.
    static class Updater
    {
        private const string Repo = "totallynotbrent/UV2";
        private const string AssetName = "UV2-Windows-x64.zip";

        private static readonly HttpClient http = new()
        {
            DefaultRequestHeaders = { { "User-Agent", "UmaLauncher-Updater" } }
        };

        // runs the flow on a background thread, marshals messages to the parent form.
        public static async void Check(Control parent)
        {
            Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

            try
            {
                string body = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString() ?? "";
                string? url = null;
                foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
                {
                    if (asset.GetProperty("name").GetString() == AssetName)
                    {
                        url = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                if (!TryParseTag(tag, out Version? latest) || latest is null || latest <= current || url is null)
                {
                    parent.BeginInvoke(() => MessageBox.Show(parent, "You are on the latest build.", "Update check",
                        MessageBoxButtons.OK, MessageBoxIcon.Information));
                    return;
                }

                DialogResult choice = MessageBox.Show(parent,
                    $"A newer build is available.\n\nInstalled: {current}\nLatest: {latest}\n\nDownload and install now?",
                    "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (choice != DialogResult.Yes) return;

                string zipPath = Path.Combine(Path.GetTempPath(), AssetName);
                await File.WriteAllBytesAsync(zipPath, await http.GetByteArrayAsync(url));

                InstallAndRestart(parent, zipPath);
            }
            catch (Exception ex)
            {
                parent.BeginInvoke(() => MessageBox.Show(parent, "Update check failed: " + ex.Message, "Update check",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning));
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
                File.WriteAllLines(Path.Combine(backupDir, "pending.txt"), pending);

            if (!string.IsNullOrEmpty(exePath))
                Process.Start(exePath);
            Environment.Exit(0);
        }

        // vX.Y.Z tags become comparable versions; anything else means no update.
        private static bool TryParseTag(string tag, out Version? version)
        {
            version = null;
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith('v')) return false;
            if (Version.TryParse(tag[1..], out Version? parsed))
            {
                version = parsed;
                return true;
            }
            return false;
        }
    }
}
