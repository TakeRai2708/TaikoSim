using TaikoSim.Charts;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}
var bestRecord = new SongBestRecord(SongClearType.None, 0, 0);
var update = bestRecord.Improve(SongClearType.Cleared, 700000, 2);
Check(update.IsNewBest && update.ClearImproved, "初クリアと自己ベスト");
bestRecord = update.After;
update = bestRecord.Improve(SongClearType.FullCombo, 650000, 1);
Check(!update.IsNewBest && update.ClearImproved, "スコアが低くてもフルコンに昇格");
Check(update.After.Score == 700000 && update.After.Rank == 2, "スコアとランクは下げない");
var totals = SongRecordSummary.From(new[] { update.After });
Check(totals.Cleared == 0 && totals.FullCombo == 1 && totals.DondaFullCombo == 0, "クリアからフルコンへ数を移す");
bestRecord = update.After;
update = bestRecord.Improve(SongClearType.AllPerfect, 800000, 4);
totals = SongRecordSummary.From(new[] { update.After });
Check(totals.Cleared == 0 && totals.FullCombo == 0 && totals.DondaFullCombo == 1, "ドンダフルとFCを重複させない");
Check(totals.Ranks.Sum() == 1 && totals.Ranks[3] == 1, "最高ランクだけ1つ数える");
bestRecord = update.After;
update = bestRecord.Improve(SongClearType.None, 850000, 5);
Check(update.IsNewBest && !update.ClearImproved && update.After.Clear == SongClearType.AllPerfect, "失敗でもスコアだけ更新できる");
bestRecord = update.After;
update = bestRecord.Improve(SongClearType.Cleared, 850000, 5);
Check(!update.IsNewBest && !update.ClearImproved && update.After == bestRecord, "同点では更新しない");
Check(bestRecord.Improve(SongClearType.None, 1, 0).After == bestRecord, "低い結果で記録を落とさない");
var anotherDifficulty = new SongBestRecord(SongClearType.Cleared, 1, 1);
totals = SongRecordSummary.From(new[] { bestRecord, anotherDifficulty });
Check(totals.Cleared == 1 && totals.DondaFullCombo == 1 && totals.Ranks.Sum() == 2, "難易度ごとの独立記録");
// テスト実行ファイルの隣だけを使い、前のテスト用ファイルがあれば最後に戻す。
string historyPath = Path.Combine(AppContext.BaseDirectory, "Config", SongHistory.FileName);
byte[]? previous = File.Exists(historyPath) ? File.ReadAllBytes(historyPath) : null;
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
    File.WriteAllText(historyPath, """
        {"Records":[
          {"Key":"test.tja","Course":0,"Clear":1,"Score":600000,"Rank":1},
          {"Key":"test.tja","Course":3,"Clear":1,"Score":700000,"Rank":2},
          {"Key":"test.tja","Course":4,"Clear":2,"Score":800000,"Rank":4}
        ]}
        """);
    var oni = SongHistory.GetSummary(CourseType.Oni);
    Check(oni.Cleared == 1 && oni.FullCombo == 1 && oni.Ranks.Sum() == 2, "おに・裏おにの集計");
    Check(ReferenceEquals(oni, SongHistory.GetSummary(CourseType.Edit)), "表裏は同じ集計パネル");
    Check(SongHistory.GetSummary(CourseType.Easy).Ranks.Sum() == 1, "かんたんは別に集計");
    Check(SongHistory.GetSummary(CourseType.Normal).Ranks.Sum() == 0, "未プレイの難易度は0");
    var saved = SongHistory.SaveRecord("test.tja", CourseType.Oni, SongClearType.AllPerfect, 900000, 6);
    oni = SongHistory.GetSummary(CourseType.Oni);
    Check(saved.Saved && oni.Cleared == 0 && oni.FullCombo == 1 && oni.DondaFullCombo == 1, "保存後に集計を更新し、王冠を移す");
    Check(oni.Ranks[1] == 0 && oni.Ranks[5] == 1, "前のランクの数も残さない");
}
finally
{
    if (previous == null) File.Delete(historyPath);
    else File.WriteAllBytes(historyPath, previous);
}
Console.WriteLine($"PASS: {passed} best-record checks");

