namespace TakeTaikoSimulator.Scenes;

// 選曲のスキップ：同じ側のカッを「タタッ」と素早く2回たたいたときだけ、7曲とばす
//   11/14 たけらい「ちょっとカッのたたく速度が速いだけでスキップされて選曲しずらい」
//   前は「1打目から 0.25秒以内に同じ側の2打目」でスキップだったので、1曲ずつ速めに送っているだけでもスキップになっていた
//   今は次の2つを両方みたすときだけスキップ：
//     ① 2打目が1打目から WindowSeconds（0.15秒）以内（わざと「タタッ」とたたく速さ）
//     ② 1打目の前に QuietSeconds（0.3秒）以上たたいていない（1曲ずつ送り続けている途中ではない）
//   推測なんだけど、1曲ずつ速めに送るときの間かくは 0.15〜0.25秒くらい。合わなければこの2つの数字を変えてね
//   逆側は通常移動にも連打判定にも使わない（今まで通り）
internal sealed class SongSelectSkipGesture
{
    public const double WindowSeconds = 0.15;
    public const double QuietSeconds = 0.3;
    private int firstDirection;
    private double firstTime;
    private double lastHitTime = double.NegativeInfinity;

    public void Reset() => firstDirection = 0;

    // Direction=0は入力を無視。Skip=falseなら通常1項目、trueならスキップ。
    public (int Direction, bool Skip) Register(int direction, double now)
    {
        if (direction != -1 && direction != 1) { Reset(); return (0, false); }
        double sinceLast = now - lastHitTime;
        lastHitTime = now;
        if (firstDirection == 0 || now < firstTime || now - firstTime > WindowSeconds)
        {
            // 送り続けている途中の1打目は、スキップの1打目にしない
            firstDirection = sinceLast >= QuietSeconds || sinceLast < 0 ? direction : 0;
            firstTime = now;
            return (direction, false);
        }
        // 逆側を叩いても、最初の側と判定の開始時刻を変えない。
        if (direction != firstDirection) return (0, false);
        Reset();
        return (direction, true);
    }
}
