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
var extraBackups = new Dictionary<string, byte[]?>();
foreach (int slot in new[] { 2, 3 })
{
    string path = SongHistory.ProfilePath(slot);
    extraBackups[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
}
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
    foreach (string path in extraBackups.Keys)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
    }
    SongHistory.SelectProfile(2);
    Check(SongHistory.GetSummary().Ranks.Sum() == 0, "新しい枠に他人の記録を混ぜない");
    SongHistory.SaveRecord("test.tja", CourseType.Oni, SongClearType.FullCombo, 300000, 1);
    SongHistory.ToggleFavorite("second.tja");
    SongHistory.AddPlayed("second.tja");
    Check(SongHistory.PreviewProfile(1).DondaFullCombo == 1 && SongHistory.ActiveProfile == 2, "プレビューしても操作中のプレイヤーは変わらない");
    SongHistory.SelectProfile(3);
    Check(SongHistory.GetSummary(CourseType.Oni).Ranks.Sum() == 0 && SongHistory.Favorites.Count == 0, "3枠目も独立");
    SongHistory.SelectProfile(2);
    Check(SongHistory.IsFavorite("second.tja") && SongHistory.Recent.Contains("second.tja"), "お気に入りと最近の曲も枠ごとに読み戻せる");
    SongHistory.SelectProfile(3);
    File.WriteAllText(SongHistory.ProfilePath(2), "broken json");
    try { SongHistory.SelectProfile(2); Check(false, "壊れた記録を選べてしまった"); }
    catch (System.Text.Json.JsonException) { Check(SongHistory.ActiveProfile == 3, "読めない記録では現在の枠を変えない"); }
    // 保存済みの2枠目を復元して、再読み込みも確認する。
    File.WriteAllText(SongHistory.ProfilePath(2), "{\"Records\":[{\"Key\":\"test.tja\",\"Course\":3,\"Clear\":2,\"Score\":300000,\"Rank\":1}]}");
    SongHistory.SelectProfile(2);
    Check(SongHistory.GetRecord("test.tja", CourseType.Oni).Score == 300000, "プレイヤー別のスコアを読み戻せる");
    SongHistory.SelectProfile(1);
    Check(SongHistory.GetRecord("test.tja", CourseType.Oni).Score == 900000, "従来の記録を1枠目として維持");
    Check(SongHistory.GetSummary(CourseType.Oni).DondaFullCombo == 1, "切り替え後の集計キャッシュも独立");
    bool invalid = false;
    try { SongHistory.SelectProfile(4); } catch (ArgumentOutOfRangeException) { invalid = true; }
    Check(invalid && SongHistory.ActiveProfile == 1, "4枠目は作らない");
}
finally
{
    foreach (var pair in extraBackups)
    {
        if (pair.Value == null) File.Delete(pair.Key);
        else File.WriteAllBytes(pair.Key, pair.Value);
    }
    if (previous == null) File.Delete(historyPath);
    else File.WriteAllBytes(historyPath, previous);
}
Console.WriteLine($"PASS: {passed} best-record checks");


// 画面やネイティブ音声なしで、参加・キー移行・設定の分離を確認する。
var sharedConfig = TaikoSim.Configs.ConfigManager.Game;
TaikoSim.Scenes.LocalSession.Reset();
Check(!TaikoSim.Scenes.LocalSession.Don(0) && TaikoSim.Scenes.LocalSession.Count == 1, "1Pの初ドンは参加だけ");
Check(TaikoSim.Scenes.LocalSession.Don(0) && TaikoSim.Scenes.LocalSession.Count == 1, "参加済みのドンで1人確定");
TaikoSim.Scenes.LocalSession.Reset();
Check(!TaikoSim.Scenes.LocalSession.Don(1) && TaikoSim.Scenes.LocalSession.Controller == 1, "2P側だけでも参加して操作できる");
Check(!TaikoSim.Scenes.LocalSession.Don(0) && TaikoSim.Scenes.LocalSession.Count == 2, "反対側の初ドンでは人数確定しない");
Check(TaikoSim.Scenes.LocalSession.Don(1) && TaikoSim.Scenes.LocalSession.Don(0), "2人参加後はどちらでも人数確定");
TaikoSim.Scenes.LocalSession.Reset();
Check(!TaikoSim.Scenes.LocalSession.Don(0) & !TaikoSim.Scenes.LocalSession.Don(1), "同時初参加で即確定しない");
bool invalidSide = false;
try { TaikoSim.Scenes.LocalSession.Don(2); } catch (ArgumentOutOfRangeException) { invalidSide = true; }
Check(invalidSide && TaikoSim.Scenes.LocalSession.Count == 2, "不正な側を参加させない");
var localOptions = TaikoSim.Scenes.LocalSession.Options;
Check(!ReferenceEquals(localOptions[0], localOptions[1]) && !ReferenceEquals(localOptions[0], sharedConfig), "両側のオプションを共有しない");
localOptions[0].HiSpeed = 1.5f;
localOptions[1].HiSpeed = 2.5f;
float sharedSpeed = sharedConfig.HiSpeed;
TaikoSim.Scenes.LocalSession.WithOptions(1, () =>
{
    Check(TaikoSim.Configs.ConfigManager.Game.HiSpeed == 2.5f && TaikoSim.Scenes.LocalSession.MenuPlayer == 1, "2P更新中は2Pの設定と入力");
    TaikoSim.Scenes.LocalSession.WithOptions(0, () => Check(TaikoSim.Configs.ConfigManager.Game.HiSpeed == 1.5f, "入れ子でも側別設定を使う"));
    Check(TaikoSim.Configs.ConfigManager.Game.HiSpeed == 2.5f, "入れ子を抜けると外側へ戻る");
});
Check(sharedConfig.HiSpeed == sharedSpeed && TaikoSim.Scenes.LocalSession.OptionsContext == null, "画面更新後は共通設定へ戻す");
try { TaikoSim.Scenes.LocalSession.WithOptions(0, () => throw new InvalidOperationException()); } catch (InvalidOperationException) { }
Check(sharedConfig.HiSpeed == sharedSpeed && TaikoSim.Scenes.LocalSession.OptionsContext == null && TaikoSim.Scenes.LocalSession.InputOverride == null, "例外が起きても共通設定と入力を復元");
int sharedOffset = sharedConfig.AudioOffsetMs;
sharedConfig.AudioOffsetMs = 37;
TaikoSim.Scenes.LocalSession.WithOptions(1, () => Check(TaikoSim.Configs.ConfigManager.Game.AudioOffsetMs == 37, "エントリー後の共通オフセット変更も反映"));
sharedConfig.AudioOffsetMs = sharedOffset;
var oldKeys = System.Text.Json.JsonSerializer.Deserialize("""
{"DrumKeys":{"LeftKa":[68],"LeftDon":[70],"RightDon":[74],"RightKa":[75]}}
""", KeyCheckContext.Default.KeyConfig)!;
oldKeys.Fix();
Check(!oldKeys.NeedsSetup() && oldKeys.DrumKeys.Count == 8, "旧1P設定を保ちセットアップを強制しない");
Check(oldKeys.DrumKeys[TaikoSim.Configs.DrumPart.P2LeftKa].Single() == Raylib_cs.KeyboardKey.C, "旧ファイルにも未定義の2P初期値を補完");
Check(!oldKeys.TryAddDrumKey(TaikoSim.Configs.DrumPart.P2LeftDon, Raylib_cs.KeyboardKey.F, out _), "両側で同じキーの登録を拒否");
Check(oldKeys.TryReplaceDrumKey(TaikoSim.Configs.DrumPart.P2LeftDon, Raylib_cs.KeyboardKey.B, out _), "2P独立キーを登録できる");
string keyJson = System.Text.Json.JsonSerializer.Serialize(oldKeys, KeyCheckContext.Default.KeyConfig);
var restored = System.Text.Json.JsonSerializer.Deserialize(keyJson, KeyCheckContext.Default.KeyConfig)!;
restored.Fix();
Check(restored.DrumKeys[TaikoSim.Configs.DrumPart.P2LeftDon].Single() == Raylib_cs.KeyboardKey.B, "2PキーをJSONで往復保存できる");
var defaults = new TaikoSim.Configs.KeyConfig();
Check(defaults.DrumKeys[TaikoSim.Configs.DrumPart.P2LeftDon].Single() == Raylib_cs.KeyboardKey.V
    && defaults.DrumKeys[TaikoSim.Configs.DrumPart.P2RightDon].Single() == Raylib_cs.KeyboardKey.N
    && defaults.DrumKeys[TaikoSim.Configs.DrumPart.P2LeftKa].Single() == Raylib_cs.KeyboardKey.C
    && defaults.DrumKeys[TaikoSim.Configs.DrumPart.P2RightKa].Single() == Raylib_cs.KeyboardKey.M, "2P初期値はC V N M");
Check(!oldKeys.TryReplaceDrumKey(TaikoSim.Configs.DrumPart.P2LeftDon, Raylib_cs.KeyboardKey.F, out _)
    && oldKeys.DrumKeys[TaikoSim.Configs.DrumPart.P2LeftDon].Single() == Raylib_cs.KeyboardKey.B, "衝突で変更を拒否しても元のキーを残す");
Check(TaikoSim.Configs.ConfigManager.Copy(oldKeys).DrumKeys[TaikoSim.Configs.DrumPart.P2LeftDon].Single() == Raylib_cs.KeyboardKey.B, "本番AOT用JSONでも2Pキーを往復できる");
oldKeys.DrumKeys[TaikoSim.Configs.DrumPart.LeftKa] = new() { Raylib_cs.KeyboardKey.C };
oldKeys.Fix();
Check(oldKeys.DrumKeys[TaikoSim.Configs.DrumPart.LeftKa].Contains(Raylib_cs.KeyboardKey.C)
    && oldKeys.DrumKeys[TaikoSim.Configs.DrumPart.P2LeftKa].Count == 0, "旧1Pのキーを2P初期値で奪わない");
Console.WriteLine($"PASS: {passed} total record/local-player checks");

[System.Text.Json.Serialization.JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(TaikoSim.Configs.KeyConfig))]
internal partial class KeyCheckContext : System.Text.Json.Serialization.JsonSerializerContext { }
