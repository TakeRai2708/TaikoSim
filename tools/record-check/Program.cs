using TaikoSim.Charts;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}
var record = new SongBestRecord(SongClearType.None, 0, 0);
var update = record.Improve(SongClearType.Cleared, 700000, 2);
Check(update.IsNewBest && update.ClearImproved, "初クリアと自己ベスト");
record = update.After;
update = record.Improve(SongClearType.FullCombo, 650000, 1);
Check(!update.IsNewBest && update.ClearImproved, "スコアが低くてもフルコンに昇格");
Check(update.After.Score == 700000 && update.After.Rank == 2, "スコアとランクは下げない");
var totals = SongRecordSummary.From(new[] { update.After });
Check(totals.Cleared == 0 && totals.FullCombo == 1 && totals.DondaFullCombo == 0, "クリアからフルコンへ数を移す");
record = update.After;
update = record.Improve(SongClearType.AllPerfect, 800000, 4);
totals = SongRecordSummary.From(new[] { update.After });
Check(totals.Cleared == 0 && totals.FullCombo == 0 && totals.DondaFullCombo == 1, "ドンダフルとFCを重複させない");
Check(totals.Ranks.Sum() == 1 && totals.Ranks[3] == 1, "最高ランクだけ1つ数える");
record = update.After;
update = record.Improve(SongClearType.None, 850000, 5);
Check(update.IsNewBest && !update.ClearImproved && update.After.Clear == SongClearType.AllPerfect, "失敗でもスコアだけ更新できる");
record = update.After;
update = record.Improve(SongClearType.Cleared, 850000, 5);
Check(!update.IsNewBest && !update.ClearImproved && update.After == record, "同点では更新しない");
Check(record.Improve(SongClearType.None, 1, 0).After == record, "低い結果で記録を落とさない");
var anotherDifficulty = new SongBestRecord(SongClearType.Cleared, 1, 1);
totals = SongRecordSummary.From(new[] { record, anotherDifficulty });
Check(totals.Cleared == 1 && totals.DondaFullCombo == 1 && totals.Ranks.Sum() == 2, "難易度ごとの独立記録");
Console.WriteLine($"PASS: {passed} best-record checks");
