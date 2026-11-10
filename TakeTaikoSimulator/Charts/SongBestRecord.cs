namespace TakeTaikoSimulator.Charts;

// スコア・王冠・ランクは独立した最高記録。低い結果で前の記録を落とさない。
public readonly record struct SongBestRecord(SongClearType Clear, long Score, int Rank)
{
    public SongRecordUpdate Improve(SongClearType clear, long score, int rank)
    {
        var next = new SongBestRecord(
            (SongClearType)Math.Max((int)Clear, Math.Clamp((int)clear, 0, 3)),
            Math.Max(Score, Math.Max(0, score)), Math.Max(Rank, Math.Clamp(rank, 0, 7)));
        return new SongRecordUpdate(this, next);
    }

    public static string ClearName(SongClearType clear) => clear switch
    {
        SongClearType.Cleared => "クリア",
        SongClearType.FullCombo => "フルコンボ",
        SongClearType.AllPerfect => "ドンダフルコンボ",
        _ => "未クリア",
    };
}

public readonly record struct SongRecordUpdate(SongBestRecord Before, SongBestRecord After)
{
    public bool IsNewBest => After.Score > Before.Score;
    public bool ClearImproved => After.Clear > Before.Clear;
    public bool Saved { get; init; }
}

// 「最高状態」の数。ドンダフルはフルコンやクリアに重複して数えない。
public sealed class SongRecordSummary
{
    public int Cleared { get; private set; }
    public int FullCombo { get; private set; }
    public int DondaFullCombo { get; private set; }
    public int[] Ranks { get; } = new int[7];

    public static SongRecordSummary From(IEnumerable<SongBestRecord> records)
    {
        var summary = new SongRecordSummary();
        foreach (var record in records)
        {
            switch (record.Clear)
            {
                case SongClearType.Cleared: summary.Cleared++; break;
                case SongClearType.FullCombo: summary.FullCombo++; break;
                case SongClearType.AllPerfect: summary.DondaFullCombo++; break;
            }
            if (record.Rank >= 1 && record.Rank <= 7) summary.Ranks[record.Rank - 1]++;
        }
        return summary;
    }
}
