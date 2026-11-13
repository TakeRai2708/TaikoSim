using TakeTaikoSimulator.Scenes;
int passed = 0;
void Series(string name, params (int Input, double Time, int Direction, bool Skip)[] inputs)
{
    var gesture = new SongSelectSkipGesture();
    foreach (var input in inputs)
    {
        var actual = gesture.Register(input.Input, input.Time);
        if (actual != (input.Direction, input.Skip))
            throw new Exception($"{name}: t={input.Time}, expected=({input.Direction},{input.Skip}), actual={actual}");
        passed++;
    }
}
Series("右2打", (1, 0, 1, false), (1, 0.1, 1, true));
Series("左2打", (-1, 0, -1, false), (-1, 0.1, -1, true));
Series("左→右→右は左の通常移動だけ", (-1, 0, -1, false), (1, 0.05, 0, false), (1, 0.1, 0, false));
Series("右→左→左は右の通常移動だけ", (1, 0, 1, false), (-1, 0.05, 0, false), (-1, 0.1, 0, false));
Series("逆側を挟んで同じ側", (-1, 0, -1, false), (1, 0.05, 0, false), (-1, 0.1, -1, true));
Series("150ms境界", (1, 0, 1, false), (1, 0.15, 1, true));
Series("0.2秒間かくはスキップしない", (1, 0, 1, false), (1, 0.2, 1, false));
Series("時間切れのあとは逆側も通常受付", (-1, 0, -1, false), (1, 0.151, 1, false), (1, 0.3, 1, false));
Series("逆側で期限を延ばさない", (-1, 0, -1, false), (1, 0.14, 0, false), (1, 0.16, 1, false));
Series("遅い同方向", (1, 0, 1, false), (1, 0.3, 1, false), (1, 0.6, 1, false));
Series("送り続けている途中のタタッはスキップしない", (1, 0, 1, false), (1, 0.2, 1, false), (1, 0.4, 1, false), (1, 0.5, 1, false));
Series("0.3秒休んでからのタタッはスキップ", (1, 0, 1, false), (1, 0.2, 1, false), (1, 0.5, 1, false), (1, 0.6, 1, true));
Series("スキップ直後の逆側は通常移動", (-1, 0, -1, false), (-1, 0.05, -1, true), (1, 0.1, 1, false), (1, 0.15, 1, false));
var reset = new SongSelectSkipGesture();
reset.Register(-1, 0); reset.Reset();
if (reset.Register(1, .02) != (1, false)) throw new Exception("リセット前の側が残っています");
passed++;
Console.WriteLine($"PASS: {passed} first-side double-tap assertions");
