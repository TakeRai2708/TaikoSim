using TakeTaikoSimulator.Scenes;
int passed = 0;
void Series(string name, params (int Direction, double Time, bool Skip)[] inputs)
{
    var gesture = new SongSelectSkipGesture();
    foreach (var input in inputs)
    {
        bool actual = gesture.Register(input.Direction, input.Time);
        if (actual != input.Skip) throw new Exception($"{name}: t={input.Time}, expected={input.Skip}, actual={actual}");
        passed++;
    }
}
Series("2打だけでは飛ばない", (1, 0, false), (1, 0.02, false));
Series("右3打", (1, 0, false), (1, 0.08, false), (1, 0.16, true));
Series("左3打", (-1, 0, false), (-1, 0.08, false), (-1, 0.16, true));
Series("250ms境界", (1, 0, false), (1, 0.125, false), (1, 0.25, true));
Series("250ms超え", (1, 0, false), (1, 0.125, false), (1, 0.251, false));
Series("左右混在", (1, 0, false), (1, 0.05, false), (-1, 0.1, false), (-1, 0.15, false), (-1, 0.2, true));
Series("交互打ち", (1, 0, false), (-1, 0.02, false), (1, 0.04, false), (-1, 0.06, false));
Series("遅い連打", (1, 0, false), (1, 0.2, false), (1, 0.4, false), (1, 0.6, false));
Series("直近3打", (1, 0, false), (1, 0.24, false), (1, 0.26, false), (1, 0.4, true));
Series("発動後のカウント", (1, 0, false), (1, 0.03, false), (1, 0.06, true), (1, 0.09, false), (1, 0.12, false));
Series("不正方向でリセット", (1, 0, false), (1, 0.02, false), (0, 0.03, false), (1, 0.04, false));
var reset = new SongSelectSkipGesture();
reset.Register(1, 0); reset.Register(1, .02); reset.Reset();
if (reset.Register(1, .04)) throw new Exception("明示リセット後に古い打鍵が残っています");
passed++;
Console.WriteLine($"PASS: {passed} directional triple-tap assertions");
