using TaikoSim.Charts;

namespace TaikoSim.Scenes;

// 小節線を隠す指定でも移動先には使う。分岐の重複は普通譜面を基準にまとめる。
public sealed class TrainingTimeline
{
    private readonly double[] starts;
    public int Count => starts.Length;
    public int SpeedTenths { get; private set; } = 10;
    public double Rate => SpeedTenths / 10.0;
    public double this[int index] => starts[Math.Clamp(index, 0, Count - 1)];

    public TrainingTimeline(Course course)
    {
        starts = course.BarLines.Where(b => b.Branch == null || b.Branch == BranchType.Normal)
            .Select(b => b.TimeMs).Where(double.IsFinite).Distinct().Order().ToArray();
        if (starts.Length == 0) starts = new[] { 0.0 };
    }

    public int At(double timeMs)
    {
        int found = Array.BinarySearch(starts, timeMs + 0.001);
        return Math.Clamp(found >= 0 ? found : ~found - 1, 0, Count - 1);
    }

    public int Move(double timeMs, int direction) => Math.Clamp(At(timeMs) + Math.Sign(direction), 0, Count - 1);
    public void SetSpeed(int tenths) => SpeedTenths = Math.Clamp(tenths, 5, 10);
}
