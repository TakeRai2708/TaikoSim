namespace TakeTaikoSimulator.Scenes;

// 選曲の連打だけを数える。アニメの進み具合ではなく、最初から3打目までの時間で判定する。
internal sealed class SongSelectSkipGesture
{
    public const double WindowSeconds = 0.25;
    private readonly Queue<double> presses = new(3);
    private int direction;

    public void Reset()
    {
        presses.Clear();
        direction = 0;
    }

    public bool Register(int nextDirection, double now)
    {
        if (nextDirection != -1 && nextDirection != 1) { Reset(); return false; }
        if (direction != nextDirection) Reset();
        direction = nextDirection;
        // 古い打鍵だけ外すので、直近3打が0.25秒以内なら反応する。
        while (presses.Count > 0 && now - presses.Peek() > WindowSeconds) presses.Dequeue();
        presses.Enqueue(now);
        if (presses.Count < 3) return false;
        Reset();
        return true;
    }
}
