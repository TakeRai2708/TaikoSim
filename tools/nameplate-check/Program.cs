using System.Text.Json;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
string folder = ConfigManager.ConfigFolder;
string backup = folder + ".nameplate-test-" + Guid.NewGuid().ToString("N");
bool existed = Directory.Exists(folder);
if (existed) Directory.Move(folder, backup);
try
{
    PlayerDataStore.Reload();
    Check(!PlayerDataStore.HasName(1), "未作成のデータは名前入力の対象");
    Check(PlayerDataStore.NamePlate(2).Name == "プレイヤー2", "データ枠ごとの初期名");
    Directory.CreateDirectory(folder);
    string history = Path.Combine(folder, "SongHistory.json");
    File.WriteAllText(history, "{\"Records\":[{\"Key\":\"keep.tja\",\"Course\":3,\"Score\":900000}]}");
    byte[] oldHistory = File.ReadAllBytes(history);
    Directory.CreateDirectory(PlayerDataStore.Folder(1));
    string json = """{"name":"テスト🌸","title":"称号テスト","rank":"6段","isRankGold":true,"namePlateId":"01","rankType":2,"futureSetting":123}""";
    File.WriteAllText(PlayerDataStore.NamePath(1), json);
    PlayerDataStore.Reload();
    var plate = PlayerDataStore.NamePlate(1);
    Check(plate.Name == "テスト🌸" && plate.Title == "称号テスト", "camelCaseの名前と称号を読み込める");
    Check(plate.Rank == "6段" && plate.IsRankGold && plate.RankType == 2, "金合格と虹の枠は独立");
    Check(plate.NamePlateId == "01", "プレートIDの先頭ゼロを維持");
    Check(PlayerDataStore.HasName(1), "既存設定では再度名前を聞かない");
    var copy = PlayerDataStore.Copy(plate); copy.Name = "別名";
    Check(plate.Name == "テスト🌸", "プレビューの編集は適用前の値を変えない");
    copy.NamePlateId = "58"; PlayerDataStore.Save(1, copy);
    using (var doc = JsonDocument.Parse(File.ReadAllText(PlayerDataStore.NamePath(1))))
    {
        Check(doc.RootElement.GetProperty("futureSetting").GetInt32() == 123, "未知のJSON設定を保存時に消さない");
        Check(doc.RootElement.GetProperty("rank").GetString() == "6段", "着せ替えで段位を変えない");
        Check(doc.RootElement.GetProperty("name").GetString() == "別名", "名前の保存はcamelCase");
    }
    PlayerDataStore.Save(2, new NamePlateConfig { Name = "2Pの人", NamePlateId = "02" });
    PlayerDataStore.Save(3, new NamePlateConfig { Name = "3番目" });
    PlayerDataStore.Reload();
    Check(PlayerDataStore.DisplayName(1) == "別名" && PlayerDataStore.DisplayName(2) == "2Pの人", "1P/2Pのデータが独立");
    Check(PlayerDataStore.DisplayName(3) == "3番目", "3つ目のデータも独立");
    Check(File.ReadAllBytes(history).SequenceEqual(oldHistory), "既存スコア記録を移動・変更しない");
    Check(SongHistory.ProfilePath(1) == history, "データ1の既存記録パスを維持");
    File.WriteAllText(PlayerDataStore.AppearancePath(1), """{"donchanType":1,"isUsePuchiChara":true,"puchiCharaType":2,"isShowScore":false}""");
    PlayerDataStore.Reload();
    var appearance = PlayerDataStore.Copy(PlayerDataStore.Appearance(1));
    Check(appearance.DonchanType == 1 && appearance.PuchiCharaType == 2, "旧インデックス形式も読み込める");
    appearance.DonchanId = "02.KatsuChan"; appearance.PuchiCharaId = "01.Sample";
    PlayerDataStore.Save(1, appearance); PlayerDataStore.Reload();
    Check(PlayerDataStore.Appearance(1).DonchanId == "02.KatsuChan", "キャラの選択を再起動後も復元");
    using (var doc = JsonDocument.Parse(File.ReadAllText(PlayerDataStore.AppearancePath(1))))
        Check(!doc.RootElement.GetProperty("isShowScore").GetBoolean(), "他アプリのプレイヤー設定を削除しない");
    Check(!PlayerDataStore.Appearance(2).IsUsePuchiChara, "別プレイヤーへ着せ替えが漏れない");
    File.WriteAllText(PlayerDataStore.NamePath(3), "{invalid");
    PlayerDataStore.Reload();
    bool rejected = false;
    try { PlayerDataStore.NamePlate(3); } catch (JsonException) { rejected = true; }
    Check(rejected && File.ReadAllText(PlayerDataStore.NamePath(3)) == "{invalid", "壊れた名前設定を初期値で上書きしない");
    Check(PlayerDataStore.DisplayName(3) == "プレイヤー3", "描画用の表示名は安全に代替する");
    foreach (int invalid in new[] { 0, 4 })
    {
        bool bad = false; try { PlayerDataStore.NamePath(invalid); } catch (ArgumentOutOfRangeException) { bad = true; }
        Check(bad, "データ枠の範囲を検証");
    }
    foreach (string invalid in new[] { "../01", "..", "C:secret", "a/b", "a\\b", "" })
        Check(!CosmeticCatalog.SafeId(invalid), "フォルダ外を参照するIDを拒否");
    Check(CosmeticCatalog.Choose(new[] { "01.Don", "02.Katsu" }, "02.Katsu") == "02.Katsu", "フォルダ名による選択");
    Check(CosmeticCatalog.Choose(new[] { "01.Don", "02.Katsu" }, "missing", 1) == "02.Katsu", "無いIDは旧インデックスへ代替");
    Check(NamePlateConfig.CleanText(" あ\nい ", 24) == "あい", "名前の制御文字を除去");
    Check(NamePlateConfig.CleanText("Take ", 24, false) == "Take ", "入力中は空白を保持");
    Check(NamePlateConfig.CleanText("🌸🌸🌸", 2) == "🌸🌸", "文字数制限でサロゲートを切らない");
    Check(PlayerDataStore.RankIndex("6段") == PlayerDataStore.RankIndex("六段"), "数字表記の既存段位と比較可能");
    var rank = new NamePlateConfig { Name = "残す名前", Title = "残す称号", NamePlateId = "01" };
    Check(PlayerDataStore.Promote(rank, 10, false, SongClearType.Cleared) && rank.Rank == "六段" && rank.RankType == 0, "通常合格で銀枠");
    Check(PlayerDataStore.Promote(rank, 10, true, SongClearType.FullCombo) && rank.IsRankGold && rank.RankType == 1, "同段位で金合格と金枠へ改善");
    Check(PlayerDataStore.Promote(rank, 10, false, SongClearType.AllPerfect) && rank.IsRankGold && rank.RankType == 2, "全良で虹枠、金合格を維持");
    Check(!PlayerDataStore.Promote(rank, 10, false, SongClearType.Cleared) && rank.IsRankGold && rank.RankType == 2, "同段位の低い結果で格下げしない");
    Check(!PlayerDataStore.Promote(rank, 9, true, SongClearType.AllPerfect) && rank.Rank == "六段", "下位段位の合格で格下げしない");
    Check(PlayerDataStore.Promote(rank, 11, false, SongClearType.Cleared) && rank.Rank == "七段" && !rank.IsRankGold && rank.RankType == 0, "上位段位はその段位の状態で更新");
    Check(rank.Name == "残す名前" && rank.Title == "残す称号" && rank.NamePlateId == "01", "昇段しても名前・称号・プレートを維持");
    Check(!PlayerDataStore.Promote(rank, 19, true, SongClearType.AllPerfect), "上下関係の無い外伝は腕前を書き換えない");
    // 保存失敗でメモリ上だけ設定が変わるのを防ぐ。
    string path = PlayerDataStore.NamePath(2);
    string previousName = PlayerDataStore.DisplayName(2);
    File.Delete(path); Directory.CreateDirectory(path);
    bool failed = false;
    try { PlayerDataStore.Save(2, new NamePlateConfig { Name = "保存失敗" }); } catch (IOException) { failed = true; }
    Check(failed && PlayerDataStore.DisplayName(2) == previousName, "保存失敗時に適用値を変えない");
    // 実画像の無い小さいスキンで、キャラの割り当てだけを検証する。描画や音声は呼ばない。
    string skin = Path.Combine(folder, "FakeSkin");
    foreach (var item in new[] { ("01.DonChan", "Don"), ("02.KatsuChan", "Katsu") })
    {
        string pathToConfig = Path.Combine(skin, "Image", "99.Common", "Chara", item.Item1);
        Directory.CreateDirectory(pathToConfig);
        File.WriteAllText(Path.Combine(pathToConfig, "Config.json"), "{\"name\":\"" + item.Item2 + "\"}");
    }
    CharaBaker.Start(skin, "_NamePlateCheck");
    Check(CharaBaker.ConfigFor(1)?.Name == "Katsu", "データ1のキャラIDを読み込む");
    Check(CharaBaker.ConfigFor(2)?.Name == "Don", "別データのキャラは独立");
    Check(CharaBaker.CacheFor(2) == CharaBaker.CacheFor(3), "同じキャラのキャッシュを共有");
    Check(CharaBaker.CacheFor(1) != CharaBaker.CacheFor(2), "異なるキャラのキャッシュを混ぜない");
    CharaBaker.Stop();
    Console.WriteLine($"PASS: {passed} nameplate/profile/cosmetic/rank checks");
}
finally
{
    CharaBaker.Start(null, "");
    if (Directory.Exists(folder)) Directory.Delete(folder, true);
    if (existed) Directory.Move(backup, folder);
    PlayerDataStore.Reload();
}
