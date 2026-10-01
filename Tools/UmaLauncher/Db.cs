using SQLite;

namespace UmaLauncher
{
    // reads the song, character, and rule tables from the game's master.mdb.
    static class Db
    {
        private static SQLiteConnection? conn;

        public static void Open(string mainPath)
        {
            conn?.Dispose();
            conn = new SQLiteConnection(Path.Combine(mainPath, "master", "master.mdb"),
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.NoMutex);
        }

        public static bool IsOpen => conn is not null;

        private static SQLiteConnection C => conn ?? throw new InvalidOperationException("database not open");

        public static List<LiveData> Songs() => C.Table<LiveData>().OrderBy(l => l.Sort).ToList();

        public static Dictionary<int, string> Titles()
        {
            return C.Table<TextData>().Where(t => t.Category == 16)
                .ToDictionary(t => t.Index, t => t.Text);
        }

        public static Dictionary<int, string> CharaNames()
        {
            return C.Table<TextData>().Where(t => t.Category == 6)
                .ToDictionary(t => t.Index, t => t.Text);
        }

        public static Dictionary<int, string> DressNames()
        {
            return C.Table<TextData>().Where(t => t.Category == 14)
                .ToDictionary(t => t.Index, t => t.Text);
        }

        public static List<int> CharaIds() => C.Table<CharaData>().Select(c => c.Id).ToList();

        public static List<DressData> LiveDresses() =>
            C.Table<DressData>().Where(d => d.UseLive == 1 || d.UseLiveTheater == 1).ToList();

        public static List<int> AllowedCharas(int musicId) =>
            C.Table<LivePermissionData>().Where(p => p.MusicId == musicId).Select(p => p.CharaId).ToList();

        public static Dictionary<int, (int chara, int dress)> RecommendedCast(int musicId)
        {
            return C.Table<LiveRecommendFormation>().Where(r => r.MusicId == musicId)
                .ToDictionary(r => r.PositionId, r => (r.CharaId, r.DressId));
        }
    }
}
