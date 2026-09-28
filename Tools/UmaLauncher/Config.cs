using System.Text;

namespace UmaLauncher
{
    // the shared Config.json next to the exe: game data root + ui language.
    static class Config
    {
        private static string _main_path;
        private static int _language = -1;

        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.json");

        public static string MainPath
        {
            get
            {
                if (_main_path != null) return _main_path;
                _main_path = read_field("main_path") ?? DefaultPath;
                return _main_path;
            }
        }

        // ui language index: 0=japanese 1=english 2=chinese; -1 when unset.
        public static int Language
        {
            get
            {
                if (_language >= 0) return _language;
                string raw = read_field("language");
                _language = int.TryParse(raw, out int lang) && lang >= 0 && lang <= 2 ? lang : 0;
                return _language;
            }
            set
            {
                _language = value;
                write_config(MainPath, value);
            }
        }

        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Umamusume", "umamusume_Data", "Persistent");

        // reads one string field from the json without a parser dependency.
        private static string read_field(string field)
        {
            try
            {
                if (!File.Exists(ConfigPath)) return null;
                string body = File.ReadAllText(ConfigPath).Trim();
                string marker = "\"" + field + "\"";
                int at = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (at < 0) return null;
                int colon = body.IndexOf(':', at);
                if (colon < 0) return null;
                int open = body.IndexOf('"', colon);
                if (open < 0) return null;
                int close = body.IndexOf('"', open + 1);
                if (close <= open) return null;
                return body.Substring(open + 1, close - open - 1);
            }
            catch { return null; }
        }

        // writes the whole config with both fields, keeping the player's shape.
        private static void write_config(string main_path, int language)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"    \"main_path\": \"{escape(main_path)}\",");
            sb.AppendLine($"    \"language\": {language}");
            sb.AppendLine("}");
            try { File.WriteAllText(ConfigPath, sb.ToString()); }
            catch { }
        }

        private static string escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // regenerates the file when the player has not written one yet.
        public static void Ensure()
        {
            if (!File.Exists(ConfigPath)) write_config(MainPath, Language);
        }
    }
}
