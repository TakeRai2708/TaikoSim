using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Scenes;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.Configs;
using Raylib_cs;

int passed = 0;
void Check(bool ok, string message)
{
    if (!ok) throw new Exception(message);
    passed++;
}
void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 0.01, $"{message}: {actual} != {expected}");

// 実際のTJAパーサーを通す。BPM変更・変拍子・DELAY・非表示小節線・OFFSETを含む。
string path = Path.Combine(Path.GetTempPath(), $"training-{Guid.NewGuid():N}.tja");
try
{
    File.WriteAllText(path, """
        TITLE:特訓テスト
        BPM:120
        OFFSET:1
        COURSE:Oni
        LEVEL:3
        #START
        1000,
        #BPMCHANGE 240
        #MEASURE 3/4
        #BARLINEOFF
        100,
        #DELAY 0.5
        #BARLINEON
        100,
        #END
        """);
    Course course = TjaParser.Load(path).Courses[0];
    var timeline = new TrainingTimeline(course);
    Check(timeline.Count == 3, "非表示小節線も含めて3小節");
    Near(timeline[0], -1000, "OFFSETで曲より前に始まる小節");
    Near(timeline[1], 1000, "120BPMの4拍後");
    Near(timeline[2], 2250, "240BPMの3拍とDELAY");
    Check(timeline.At(1200) == 1, "小節途中の番号");
    Check(timeline.Move(1200, -1) == 0, "前の小節");
    Check(timeline.Move(1200, 1) == 2, "次の小節");
    Check(timeline.Move(-1000, -1) == 0, "先頭を越えない");
    Check(timeline.Move(99999, 1) == 2, "末尾を越えない");
    Near(timeline[99], 2250, "末尾の安全な参照");
    Near(timeline[-99], -1000, "先頭の安全な参照");
    for (int i = 5; i <= 10; i++)
    {
        timeline.SetSpeed(i);
        Near(timeline.Rate, i / 10.0, "0.1刻みの速度");
    }
    timeline.SetSpeed(0); Near(timeline.Rate, 0.5, "速度の下限");
    timeline.SetSpeed(20); Near(timeline.Rate, 1, "速度の上限");

    course.BarLines.Add(new BarLine { TimeMs = 1000, Branch = BranchType.Expert });
    course.BarLines.Add(new BarLine { TimeMs = 1000, Branch = BranchType.Master });
    course.BarLines.Add(new BarLine { TimeMs = 1000, Branch = BranchType.Normal });
    Check(new TrainingTimeline(course).Count == 3, "分岐の同じ小節を重複させない");
    var empty = new TrainingTimeline(new Course());
    Check(empty.Count == 1 && empty[0] == 0 && empty.Move(0, 1) == 0, "空の譜面");
}
finally { File.Delete(path); }

// 0.5倍でも実時間の60Hz枠は16.67ms。通常1倍の動作も変わらないことを確かめる。
foreach (double rate in new[] { 0.5, 0.7, 1.0 })
{
    var scheduler = new DrumInputScheduler();
    var results = new List<ScheduledDrumHit>();
    var input = new DrumInputEvent(DrumPart.LeftDon, KeyboardKey.D, 0, 0, null);
    scheduler.Enqueue(input, InputTimingMode.HighPrecision, true, rate);
    scheduler.Enqueue(input, InputTimingMode.HighPrecision, true, rate);
    scheduler.ProcessReady(100, results.Add);
    Check(results.Count == 2, "打鍵を落とさない");
    Near((results[1].JudgeGameMs - results[0].JudgeGameMs) / rate, 1000.0 / 60, "60打制限の実時間");
    scheduler.Reset(); results.Clear();
    scheduler.Enqueue(input with { EventGameMs = 1 }, InputTimingMode.Compatible60Hz, false, rate);
    scheduler.ProcessReady(0, results.Add);
    Check(results.Count == 0, "入力の予定時刻より前は判定しない");
    scheduler.ProcessReady(100, results.Add);
    Near(results[0].JudgeGameMs, 1000.0 / 60 * rate, "60Hz互換の譜面時刻");
    scheduler.Enqueue(input, InputTimingMode.HighPrecision, true, rate);
    scheduler.Reset();
    Check(scheduler.PendingCount == 0, "一時停止時に待機打鍵を消せる");
}
Console.WriteLine($"PASS: {passed} checks (TJA measures, boundaries, speed, input timing)");

var keys = new KeyConfig();
keys.SystemKeys.Remove(SystemAction.TrainingPause);
keys.Fix();
Check(keys.SystemKeys[SystemAction.TrainingPause] == KeyboardKey.Space, "旧設定に一時停止の初期値を補う");
Check(keys.TrySetSystemKey(SystemAction.TrainingPause, KeyboardKey.P, out _), "一時停止を変更できる");
Check(!keys.TrySetSystemKey(SystemAction.TrainingNextBar, KeyboardKey.P, out _), "重複キーを拒否する");
Check(!keys.TrySetSystemKey(SystemAction.TrainingPause, KeyboardKey.Escape, out _), "Escを予約する");
var json = System.Text.Json.JsonSerializer.Serialize(keys, TrainingJsonContext.Default.KeyConfig);
var restored = System.Text.Json.JsonSerializer.Deserialize(json, TrainingJsonContext.Default.KeyConfig)!;
Check(restored.SystemKeys[SystemAction.TrainingPause] == KeyboardKey.P, "変更したキーがJSON保存・復元できる");
Console.WriteLine($"PASS: {passed} checks including training key configuration");

var motion = new TrainingScrollMotion();
motion.Reset(0);
motion.MoveFrom(0, 2000);
motion.Update(0.12f);
Check(motion.Position > 1500 && motion.Position < 2000, "前半で大きく進むイージング");
motion.Update(0.12f);
Near(motion.Position, 2000, "移動先に止まる");
Check(motion.Repeat(1, 0) == 1, "押し始めに1小節");
Check(motion.Repeat(1, 0.3f) == 0, "長押し開始前は連続移動しない");
Check(motion.Repeat(1, 0.2f) == 1, "低速の連続移動");
motion.Repeat(1, 1.0f);
Check(motion.Repeat(1, 0.21f) >= 2, "1.5秒以降は加速");
Check(motion.Repeat(0, 0) == 0, "離すと停止");
Check(motion.Repeat(-1, 0) == -1, "反対方向は押し始めから");
motion.Reset(12);
Near(motion.Position, 12, "再開前に表示と論理位置をそろえる");
Console.WriteLine($"PASS: {passed} checks including eased scrolling and hold repeat");

[System.Text.Json.Serialization.JsonSerializable(typeof(KeyConfig))]
internal partial class TrainingJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
