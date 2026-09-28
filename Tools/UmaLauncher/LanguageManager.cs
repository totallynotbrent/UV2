using System.Text.Json;

namespace UmaLauncher
{
    // languages the launcher ui can show; character and song names fall back to the
    // game's own japanese text unless a translation file provides them.
    enum Language
    {
        Japanese,
        English,
        Chinese
    }

    // loads ui strings and optional name translations for the current language.
    static class LanguageManager
    {
        private static Dictionary<string, string> names = [];
        private static Dictionary<string, string> ui = [];

        public static Language Current { get; private set; } = Language.Japanese;

        // hardcoded ui strings per language; name translations come from the json file.
        private static readonly Dictionary<Language, Dictionary<string, string>> ui_strings = new()
        {
            [Language.Japanese] = new()
            {
                ["launch"] = "コンサートを開始",
                ["check_update"] = "更新を確認",
                ["select_member"] = "メンバーを選択",
                ["search"] = "検索...",
                ["ok"] = "OK",
                ["cancel"] = "キャンセル",
                ["latest"] = "最新のビルドです。",
                ["update_available"] = "新しいビルドがあります。",
                ["installed"] = "インストール済み",
                ["latest_label"] = "最新",
                ["download_now"] = "今すぐダウンロードしますか？",
                ["downloading"] = "更新をダウンロード中です。完了後に再起動します。",
                ["update_failed"] = "更新の確認に失敗しました: ",
                ["update_title"] = "更新の確認",
                ["members"] = "メンバー",
                ["empty"] = "空き"
            },
            [Language.English] = new()
            {
                ["launch"] = "Launch concert",
                ["check_update"] = "Check for update",
                ["select_member"] = "Select member",
                ["search"] = "Search...",
                ["ok"] = "OK",
                ["cancel"] = "Cancel",
                ["latest"] = "You are on the latest build.",
                ["update_available"] = "A newer build is available.",
                ["installed"] = "Installed",
                ["latest_label"] = "Latest",
                ["download_now"] = "Download and install now?",
                ["downloading"] = "Downloading the update, the app will restart when it is done.",
                ["update_failed"] = "Update check failed: ",
                ["update_title"] = "Update check",
                ["members"] = "members",
                ["empty"] = "empty"
            },
            [Language.Chinese] = new()
            {
                ["launch"] = "开始演唱会",
                ["check_update"] = "检查更新",
                ["select_member"] = "选择成员",
                ["search"] = "搜索...",
                ["ok"] = "确定",
                ["cancel"] = "取消",
                ["latest"] = "已是最新版本。",
                ["update_available"] = "有更新的版本。",
                ["installed"] = "已安装",
                ["latest_label"] = "最新",
                ["download_now"] = "现在下载并安装吗？",
                ["downloading"] = "正在下载更新，完成后将重新启动。",
                ["update_failed"] = "检查更新失败： ",
                ["update_title"] = "更新检查",
                ["members"] = "成员",
                ["empty"] = "空闲"
            }
        };

        // switches the language and loads its name translations if a file exists.
        public static void Set(Language lang)
        {
            Current = lang;
            ui = ui_strings[lang];

            names = [];
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang",
                lang switch
                {
                    Language.English => "en.json",
                    Language.Chinese => "zh.json",
                    _ => ""
                });

            if (file.Length > 0 && File.Exists(file))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                    foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                        names[prop.Name] = prop.Value.GetString() ?? "";
                }
                catch { }
            }
        }

        // ui string lookup for the current language.
        public static string T(string key) => ui.GetValueOrDefault(key, key);

        // localized name lookup: the json file's key first, then the game's own text.
        public static string Name(string gameText) =>
            names.GetValueOrDefault(gameText, gameText);
    }
}
