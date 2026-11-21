namespace TakeTaikoSimulator.Diagnostics
{
    // ログの置き場所（11/22 たけらい「システムのログなのか入力のログなのか分からん」）
    //
    //   ドキュメント\TakeTaikoSimulator\Logs\
    //     TaikoSimLog_20261122_015000\     ← 1回起動するごとに1つ
    //       System\TaikoSim.log          … 黒い画面に出る文（raylib の INFO / WARNING も）
    //       Debug\frames_*.csv, events_*.csv … デバッグ記録(CSV)をONにしたときの重さの記録
    //       Input\input_*.csv            … デバッグ記録(CSV)をONにしたときの演奏中の入力
    //       Input\KeyProbe.txt           … Debug ビルドだけ：キーがどこまで届いたか
    //     古いログ\                        ← 前の形（TaikoSim_*.log・Debug_*・KeyProbe_*）を1回だけ移す
    //
    //   起動のフォルダは新しい順に KeepRuns 個だけ残す
    public static class LogPaths
    {
        private const int KeepRuns = 10;
        public const string SystemKind = "System", DebugKind = "Debug", InputKind = "Input";

        private static string? runFolder;

        public static string Root => Configs.DataPaths.Data("Logs");

        /// <summary>今回の起動のフォルダ（Logs\TaikoSimLog_日付_時間）。最初に呼んだときに作る</summary>
        public static string RunFolder
        {
            get
            {
                if (runFolder != null) return runFolder;
                // 同じ秒に2回起動すると(描画方式で窓を作れず、すぐ OpenGL で起動し直したときなど)、
                // 同じフォルダになって前のログを上書きしていた(11/22)。あれば _2, _3 … を付ける
                string name = $"TaikoSimLog_{DateTime.Now:yyyyMMdd_HHmmss}";
                string path = Path.Combine(Root, name);
                for (int i = 2; Directory.Exists(path); i++) path = Path.Combine(Root, $"{name}_{i}");
                runFolder = path;
                Directory.CreateDirectory(runFolder);
                return runFolder;
            }
        }

        /// <summary>今回の起動のフォルダの中の、種類ごとのフォルダ（作ってから返す）</summary>
        public static string Folder(string kind)
        {
            string path = Path.Combine(RunFolder, kind);
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>起動したときに1回：古い起動のフォルダを消して、前の形のログを「古いログ」へ移す</summary>
        public static void Tidy()
        {
            try
            {
                Directory.CreateDirectory(Root);
                string current = RunFolder;
                foreach (string old in Directory.GetDirectories(Root, "TaikoSimLog_*").Where(p => p != current)
                             .OrderBy(p => p).SkipLast(KeepRuns - 1))
                {
                    try { Directory.Delete(old, true); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                }

                string legacy = Path.Combine(Root, "古いログ");
                foreach (string file in Directory.GetFiles(Root, "TaikoSim_*.log").Concat(Directory.GetFiles(Root, "KeyProbe_*.txt")))
                    Move(() => File.Move(file, Path.Combine(EnsureDir(legacy), Path.GetFileName(file)), true));
                foreach (string dir in Directory.GetDirectories(Root, "Debug_*"))
                    Move(() => Directory.Move(dir, Path.Combine(EnsureDir(legacy), Path.GetFileName(dir))));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Log] ログのフォルダを片付けられませんでした: {ex.Message}");
            }
        }

        private static string EnsureDir(string path) { Directory.CreateDirectory(path); return path; }

        private static void Move(Action move)
        {
            try { move(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }
}
