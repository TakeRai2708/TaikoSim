using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

// 改名前後のソースビルドを読み、実際のAOT用JSONで互換性を比較する検証用の道具。
// 本体には組み込まない。ゲームの起動や音声DLLの読み込みは行わない。
if (args.Length != 2)
{
    Console.Error.WriteLine("dotnet run --project tools/rename-check -- <old Check.dll> <new TakeTaikoSimulator.dll>");
    return 2;
}
Assembly before = Assembly.LoadFrom(Path.GetFullPath(args[0]));
Assembly after = Assembly.LoadFrom(Path.GetFullPath(args[1]));
int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}
Type T(Assembly assembly, string prefix, string suffix) => assembly.GetType(prefix + "." + suffix, throwOnError: true)!;
string RoundTrip(Assembly assembly, string prefix, string suffix, string json)
{
    var context = (JsonSerializerContext)T(assembly, prefix, "Configs.AppJsonContext")
        .GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    var info = context.GetTypeInfo(T(assembly, prefix, suffix)) ?? throw new Exception("JSON型がありません：" + suffix);
    object value = JsonSerializer.Deserialize(json, info) ?? throw new Exception("JSONを読み込めません：" + suffix);
    return JsonSerializer.Serialize(value, info);
}
var samples = new (string Type, string Json)[]
{
    ("Configs.GameConfig", """{"AudioOffsetMs":-23,"ChartOffsetMs":17,"HiSpeed":1.7,"AutoGoodPercent":95,"Fullscreen":true}"""),
    ("Configs.SoundConfig", """{"MasterVolume":77,"BgmVolume":28,"VoiceVolume":65,"OutputType":"Bass","BassBufferMs":15}"""),
    ("Configs.KeyConfig", """{"DrumKeys":{"LeftKa":["D"],"LeftDon":["F"],"RightDon":["J"],"RightKa":["K"]},"SystemKeys":{"Settings":"F1"}}"""),
    ("Configs.SkinConfig", """{"SkinsFolder":"D:/TaikoSkins","SkinName":"TaikoSim-Simple"}"""),
    ("Charts.SongHistoryFile", """{"Recent":["曲/test.tja"],"Favorites":["曲/test.tja"],"Records":[{"Key":"曲/test.tja","Course":3,"Clear":2,"Score":900000,"Rank":6}],"DanRecords":[{"Key":"dan/test.json","Gold":true,"Combo":3}]}"""),
    ("Charts.SongCacheFile", """{"Version":3,"Songs":[{"Path":"曲/test.tja","WriteTime":123456,"Size":2048,"Title":"テスト","Subtitle":"裏","Courses":[{"Type":"Edit","Level":10}],"Error":""}]}"""),
    ("Graphics.CharaCacheMeta", """{"Version":1,"FrameCount":33,"SourceFrameCount":66,"FrameStep":2,"CellWidth":200,"CellHeight":150,"Scale":0.5,"TrimX":100,"TrimY":50,"SourceWidth":820,"SourceHeight":599,"SourceStamp":12345}"""),
};
foreach (var sample in samples)
{
    string oldJson = RoundTrip(before, "TaikoSim", sample.Type, sample.Json);
    string newJson = RoundTrip(after, "TakeTaikoSimulator", sample.Type, oldJson);
    Check(oldJson == newJson, sample.Type + "：従来JSONの内容が変わっています");
    // 両方とも知らない項目を無視して一致した、という見逃しを防ぐ。
    using var original = JsonDocument.Parse(sample.Json);
    using var stored = JsonDocument.Parse(oldJson);
    foreach (var property in original.RootElement.EnumerateObject())
        if (!stored.RootElement.TryGetProperty(property.Name, out var value)
            || !JsonElement.DeepEquals(property.Value, value))
            throw new Exception(sample.Type + "：比較用の値が失われています：" + property.Name);
}
foreach (string name in new[] { "GameConfig", "SoundConfig", "KeyConfig", "SkinConfig" })
{
    object? OldName() => T(before, "TaikoSim", "Configs." + name).GetField("FileName")!.GetRawConstantValue();
    object? NewName() => T(after, "TakeTaikoSimulator", "Configs." + name).GetField("FileName")!.GetRawConstantValue();
    Check(Equals(OldName(), NewName()), name + "：設定ファイル名が変わっています");
}
foreach (var item in new[] { ("Configs.ConfigManager", "ConfigFolder"), ("Graphics.Screen", "ScreenshotFolder") })
    Check(Equals(T(before, "TaikoSim", item.Item1).GetField(item.Item2)!.GetValue(null),
        T(after, "TakeTaikoSimulator", item.Item1).GetField(item.Item2)!.GetValue(null)), item.Item2 + "：保存先が変わっています");
foreach (var item in new[] { ("Charts.SongList", "SongsFolder"), ("Charts.SongList", "CacheFile"), ("Graphics.CharaCache", "CacheRoot") })
    Check(Equals(T(before, "TaikoSim", item.Item1).GetProperty(item.Item2)!.GetValue(null),
        T(after, "TakeTaikoSimulator", item.Item1).GetProperty(item.Item2)!.GetValue(null)), item.Item2 + "：保存先が変わっています");
for (int slot = 1; slot <= 3; slot++)
    Check(Equals(T(before, "TaikoSim", "Charts.SongHistory").GetMethod("ProfilePath")!.Invoke(null, new object[] { slot }),
        T(after, "TakeTaikoSimulator", "Charts.SongHistory").GetMethod("ProfilePath")!.Invoke(null, new object[] { slot })), "データ枠の保存先が変わっています：" + slot);
Check(after.GetName().Name == "TakeTaikoSimulator", "アセンブリ名が違います");
Check(!after.GetTypes().Any(t => t.Namespace == "TaikoSim" || t.Namespace?.StartsWith("TaikoSim.") == true), "旧名前空間が残っています");
var expected = before.GetTypes().Where(t => t.FullName?.StartsWith("TaikoSim.") == true)
    .Select(t => "TakeTaikoSimulator" + t.FullName!["TaikoSim".Length..]).OrderBy(n => n).ToArray();
var actual = after.GetTypes().Where(t => t.FullName?.StartsWith("TakeTaikoSimulator.") == true)
    .Select(t => t.FullName!).OrderBy(n => n).ToArray();
Check(expected.SequenceEqual(actual), "改名以外の型の追加・削除があります");
Console.WriteLine($"PASS: {passed} rename compatibility checks (generated JSON, paths, namespace, assembly)");
return 0;
