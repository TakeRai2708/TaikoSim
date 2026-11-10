namespace TaikoSim.Scenes;

// 表示用の時刻だけを動かす。音源と判定に使う移動先の時刻とは分ける。
public sealed class TrainingScrollMotion
{
    public double Position { get; private set; }
    private double from, target;
    private float elapsed, duration = 0.24f;
    private int heldDirection;
    private double heldSeconds;

    public void Reset(double position)
    {
        Position = from = target = position;
        elapsed = duration;
        heldDirection = 0;
        heldSeconds = 0;
    }

    public void MoveFrom(double position, double destination)
    {
        Position = from = position;
        target = destination;
        elapsed = 0;
        duration = heldSeconds >= 1.5 ? 0.10f : 0.24f;
    }

    public void Update(float dt)
    {
        elapsed = Math.Min(duration, elapsed + Math.Max(0, dt));
        double t = Math.Clamp(elapsed / duration, 0, 1);
        double ease = 1 - Math.Pow(1 - t, 3);
        Position = from + (target - from) * ease;
    }

    private static int RepeatsAt(double seconds)
    {
        if (seconds < 0.42) return 0;
        if (seconds < 1.5) return 1 + (int)Math.Floor((seconds - 0.42) / 0.30);
        return 5 + (int)Math.Floor((seconds - 1.5) / 0.10);
    }

    // 最初は1小節、0.42秒後からゆっくり反復、1.5秒以降は速くする。
    public int Repeat(int direction, float dt)
    {
        direction = Math.Sign(direction);
        if (direction == 0)
        {
            heldDirection = 0;
            heldSeconds = 0;
            return 0;
        }
        if (direction != heldDirection)
        {
            heldDirection = direction;
            heldSeconds = 0;
            return direction;
        }
        int before = RepeatsAt(heldSeconds);
        heldSeconds += Math.Max(0, dt);
        return direction * Math.Min(8, RepeatsAt(heldSeconds) - before);
    }
}
