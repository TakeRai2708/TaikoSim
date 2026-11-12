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
Series("250ms境界", (1, 0, 1, false), (1, 0.25, 1, true));
Series("時間切れで逆側も通常受付", (-1, 0, -1, false), (1, 0.251, 1, false), (1, 0.3, 1, true));
Series("逆側で期限を延ばさない", (-1, 0, -1, false), (1, 0.24, 0, false), (1, 0.26, 1, false));
Series("遅い同方向", (1, 0, 1, false), (1, 0.3, 1, false), (1, 0.6, 1, false));
Series("発動後の新しい側", (-1, 0, -1, false), (-1, 0.05, -1, true), (1, 0.1, 1, false), (1, 0.15, 1, true));
var reset = new SongSelectSkipGesture();
reset.Register(-1, 0); reset.Reset();
if (reset.Register(1, .02) != (1, false)) throw new Exception("リセット前の側が残っています");
passed++;
Console.WriteLine($"PASS: {passed} first-side double-tap assertions");
