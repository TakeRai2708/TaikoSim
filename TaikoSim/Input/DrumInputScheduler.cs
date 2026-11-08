using Raylib_cs;
using TaikoSim.Configs;

namespace TaikoSim.Input
{
    // 1回の入力。EventGameMs はWindowsのコールバック時刻をもとにした推定値。
    // Windows監視が使えない環境ではRaylibのキー取得時刻が代わりになる。
    public readonly record struct DrumInputEvent(
        DrumPart Part, KeyboardKey Key, double EventGameMs,
        long CollectedAt, WindowsKeyEvent? NativeEvent);

    // 判定する時刻を計算し、必要なら一定間隔にそろえて入力を処理する。
    // 画面を60FPSに制限するわけではない。描画FPSと入力スケジューラは独立。
    public readonly record struct ScheduledDrumHit(
        DrumInputEvent Input, double JudgeGameMs, string Mode, bool Limited);

    public sealed class DrumInputScheduler
    {
        private const double TickMs = 1000.0 / 60.0;
        private readonly Queue<ScheduledDrumHit> pending = new();
        private double nextLimitedMs = double.NegativeInfinity;
        private double lastScheduledMs = double.NegativeInfinity;

        public int PendingCount => pending.Count;

        public static string ModeName(InputTimingMode mode) => mode == InputTimingMode.Compatible60Hz
            ? "60Hz互換" : "高精度";

        public void Reset()
        {
            pending.Clear();
            nextLimitedMs = double.NegativeInfinity;
            lastScheduledMs = double.NegativeInfinity;
        }

        public void Enqueue(DrumInputEvent item, InputTimingMode mode, bool limitTo60, double clockRate = 1.0)
        {
            // 低速再生でも、実時間での60Hz・60打/秒は変えない。
            double tick = TickMs * Math.Clamp(clockRate, 0.5, 1.0);
            double judgeMs = mode == InputTimingMode.Compatible60Hz
                // 最も近い次の60Hz境界にそろえる。遅延は理論上0〜16.67ms。
                ? Math.Ceiling(item.EventGameMs / tick) * tick
                : item.EventGameMs;

            if (limitTo60)
            {
                // 4つのキーで共有する枠を作る。余った打鍵は捨てず次の枠へ。
                judgeMs = Math.Max(judgeMs, nextLimitedMs);
                nextLimitedMs = judgeMs + tick;
            }
            // 切替直前の入力が待機している場合も、キューの順番は逆転させない。
            judgeMs = Math.Max(judgeMs, lastScheduledMs);
            lastScheduledMs = judgeMs;
            pending.Enqueue(new ScheduledDrumHit(item, judgeMs, ModeName(mode), limitTo60));
        }

        public void ProcessReady(double gameMs, Action<ScheduledDrumHit> onHit)
        {
            while (pending.Count > 0 && pending.Peek().JudgeGameMs <= gameMs + 0.00001)
            {
                onHit(pending.Dequeue());
            }
            // 待機中の打鍵がなくなった後の次の入力は、通常の入力時刻で処理できる。
            if (pending.Count == 0) lastScheduledMs = double.NegativeInfinity;
        }
    }
}
