namespace UmaLauncher
{
    // the game data root, resolved from config or the default install location.
    static class Config
    {
        public static string MainPath
        {
            get
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string json = Path.Combine(exeDir, "Config.json");
                if (File.Exists(json))
                {
                    try
                    {
                        string body = File.ReadAllText(json).Trim();
                        string marker = "\"main_path\"";
                        int at = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                        if (at >= 0)
                        {
                            int colon = body.IndexOf(':', at);
                            int open = body.IndexOf('"', colon);
                            int close = body.IndexOf('"', open + 1);
                            if (open >= 0 && close > open)
                                return body.Substring(open + 1, close - open - 1);
                        }
                    }
                    catch { }
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Umamusume", "umamusume_Data", "Persistent");
            }
        }
    }
}
