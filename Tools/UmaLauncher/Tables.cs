using SQLite;

namespace UmaLauncher
{
    // master.mdb row types the launcher reads.
    [Table("live_data")]
    public class LiveData
    {
        [Column("music_id"), NotNull, PrimaryKey] public int MusicId { get; set; }
        [Column("sort"), NotNull] public int Sort { get; set; }
        [Column("live_member_number"), NotNull] public int LiveMemberNumber { get; set; }
        [Column("default_main_dress"), NotNull] public int DefaultMainDress { get; set; }
        [Column("backdancer_dress"), NotNull] public int BackdancerDress { get; set; }
        [Column("has_live"), NotNull] public int HasLive { get; set; }

        public string DisplayTitle { get; set; } = "";

        public override string ToString() =>
            string.IsNullOrEmpty(DisplayTitle) ? $"music {MusicId}" : $"{DisplayTitle}  ({MusicId})";
    }

    [Table("text_data")]
    public class TextData
    {
        [Column("id"), NotNull, PrimaryKey] public int Id { get; set; }
        [Column("category"), NotNull] public int Category { get; set; }
        [Column("index"), NotNull] public int Index { get; set; }
        [Column("text"), NotNull] public string Text { get; set; } = "";
    }

    [Table("chara_data")]
    public class CharaData
    {
        [Column("id"), NotNull, PrimaryKey] public int Id { get; set; }
    }

    [Table("dress_data")]
    public class DressData
    {
        [Column("id"), NotNull, PrimaryKey] public int Id { get; set; }
        [Column("chara_id"), NotNull] public int CharaId { get; set; }
        [Column("use_live"), NotNull] public int UseLive { get; set; }
        [Column("use_live_theater"), NotNull] public int UseLiveTheater { get; set; }
    }

    [Table("live_permission_data")]
    public class LivePermissionData
    {
        [Column("music_id"), NotNull] public int MusicId { get; set; }
        [Column("chara_id"), NotNull] public int CharaId { get; set; }
    }

    [Table("live_recommend_formation")]
    public class LiveRecommendFormation
    {
        [Column("music_id"), NotNull] public int MusicId { get; set; }
        [Column("position_id"), NotNull] public int PositionId { get; set; }
        [Column("chara_id"), NotNull] public int CharaId { get; set; }
        [Column("dress_id"), NotNull] public int DressId { get; set; }
    }
}
