using TaikoSim.Diagnostics;

namespace TaikoSim.Scenes
{
    // 画面(シーン)の切り替え係
    public static class SceneManager
    {
        // 今の画面
        public static Scene? Current { get; private set; }

        // 次に切り替える画面(フレームの途中で切り替えると危ないので、次のフレームの最初に切り替える)
        private static Scene? next;

        // 画面を切り替える予約をする
        public static void ChangeScene(Scene scene)
        {
            next = scene;
        }

        // 予約されていたら実際に切り替える(Game が毎フレーム最初に呼ぶ)
        public static void ApplyChange()
        {
            if (next == null) return;

            string from = Current?.GetType().Name ?? "なし";
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            Current?.Exit();
            Current = next;
            next = null;
            DebugLog.SceneChanged(from, Current.GetType().Name);
            Current.Enter();
            DebugLog.Event("画面の準備", $"{Current.GetType().Name} の準備(Exit + Enter)にかかった時間",
                System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}