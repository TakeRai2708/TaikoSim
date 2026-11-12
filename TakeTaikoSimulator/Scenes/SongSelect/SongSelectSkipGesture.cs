namespace TakeTaikoSimulator.Scenes;

// 最初の側を短時間保持する。逆側は通常移動にも連打判定にも使わない。
internal sealed class SongSelectSkipGesture
{
    public const double WindowSeconds = 0.25;
    private int firstDirection;
    private double firstTime;

    public void Reset() => firstDirection = 0;

    // Direction=0は入力を無視。Skip=falseなら通常1項目、trueならスキップ。
    public (int Direction, bool Skip) Register(int direction, double now)
    {
        if (direction != -1 && direction != 1) { Reset(); return (0, false); }
        if (firstDirection == 0 || now < firstTime || now - firstTime > WindowSeconds)
        {
            firstDirection = direction;
            firstTime = now;
            return (direction, false);
        }
        // 逆側を叩いても、最初の側と判定の開始時刻を変えない。
        if (direction != firstDirection) return (0, false);
        Reset();
        return (direction, true);
    }
}
