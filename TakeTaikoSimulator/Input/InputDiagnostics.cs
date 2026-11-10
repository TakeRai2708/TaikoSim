using System.Diagnostics;
using System.Globalization;
using System.Text;
using TakeTaikoSimulator.Diagnostics;

namespace TakeTaikoSimulator.Input
{
    // 演奏中の入力処理と音声同期を調べるための記録係。
    // 記録した行は、デバッグ記録(DebugLog)が ON なら Logs/Debug_…/input.csv にそのまま書く。
    // キーを実際に押した瞬間の時刻は Raylib.GetKeyPressed からは取得できない。
    // 「入力取得時刻」は、キューから取り出した直後の Stopwatch 時刻を意味する。
    public sealed class InputDiagnostics
    {
        private const int MaxRows = 20000;
        private const double SampleEveryMs = 250.0;
        private readonly List<DiagnosticRow> rows = new();
        private long startTimestamp = Stopwatch.GetTimestamp();
        private double elapsedInWindowMs;
        private double windowMaxFrameMs;
        private double windowMaxCorrectionMs;

        private readonly record struct DiagnosticRow(
            string Kind, long Frame, double GameMs, double ObservedMs,
            string Key, string Part, int NoteNumber, string Result,
            double? JudgeDeltaMs, double? HandlingMs, double? FrameMs,
            double? AudioSyncMs, double? ClockCorrectionMs, double? OutputEstimateMs,
            uint? NativeVk = null, uint? NativeScan = null, double? NativeOsMs = null,
            double? NativeCallbackMs = null, double? NativeToRaylibMs = null,
            double? MessageQueueAgeMs = null, string NativeMatch = "",
            double? EventGameMs = null, double? JudgeGameMs = null, double? WaitForJudgeMs = null,
            string TimingMode = "", bool? HitLimitEnabled = null);

        public bool Enabled { get; set; }
        public int InputCount { get; private set; }
        public int WindowsDownCount { get; private set; }
        public int WindowsUpCount { get; private set; }
        public int WindowsRepeatCount { get; private set; }
        public int NativeMatchedCount { get; private set; }
        public int NativeUnmatchedCount { get; private set; }
        public double? LastNativeToRaylibMs { get; private set; }
        public int RowCount => rows.Count;
        public double RecentMaxFrameMs { get; private set; }
        public double RecentMaxCorrectionMs { get; private set; }
        public double? LatestSyncErrorMs { get; private set; }
        public double? LatestOutputEstimateMs { get; private set; }
        public double? LastInputHandlingMs { get; private set; }
        public double? LastScheduledWaitMs { get; private set; }

        // リスタート時に前の曲の記録が混ざらないようにする。診断ON/OFFの状態は維持。
        public void Reset()
        {
            rows.Clear();
            startTimestamp = Stopwatch.GetTimestamp();
            elapsedInWindowMs = 0;
            windowMaxFrameMs = 0;
            windowMaxCorrectionMs = 0;
            InputCount = 0;
            WindowsDownCount = 0;
            WindowsUpCount = 0;
            WindowsRepeatCount = 0;
            NativeMatchedCount = 0;
            NativeUnmatchedCount = 0;
            LastNativeToRaylibMs = null;
            RecentMaxFrameMs = 0;
            RecentMaxCorrectionMs = 0;
            LatestSyncErrorMs = null;
            LatestOutputEstimateMs = null;
            LastInputHandlingMs = null;
            LastScheduledWaitMs = null;
        }

        // 1フレームごとに呼ぶ。250msごとだけ音声関連を詳しく調べるので負荷を抑えられる。
        public bool TrackFrame(double frameMs, double correctionMs)
        {
            elapsedInWindowMs += Math.Max(0, frameMs);
            windowMaxFrameMs = Math.Max(windowMaxFrameMs, frameMs);
            if (Math.Abs(correctionMs) > Math.Abs(windowMaxCorrectionMs))
                windowMaxCorrectionMs = correctionMs;
            return elapsedInWindowMs >= SampleEveryMs;
        }

        public void RecordFrame(long frame, double gameMs, double? audioSyncMs, double? outputEstimateMs)
        {
            RecentMaxFrameMs = windowMaxFrameMs;
            RecentMaxCorrectionMs = windowMaxCorrectionMs;
            LatestSyncErrorMs = audioSyncMs;
            LatestOutputEstimateMs = outputEstimateMs;
            Add(new DiagnosticRow("frame", frame, gameMs, ObservedMs(), "", "", 0, "",
                null, null, windowMaxFrameMs, audioSyncMs, windowMaxCorrectionMs, outputEstimateMs));
            // 長い処理落ちでも次回の測定が極端に遅れないよう、区間はここで区切る。
            elapsedInWindowMs = 0;
            windowMaxFrameMs = 0;
            windowMaxCorrectionMs = 0;
        }

        // Windowsの押下と解放を独立して記録。Raylibで処理されなかった入力も残せる。
        public void RecordWindowsEvent(long frame, double gameMs, WindowsKeyEvent item)
        {
            if (item.IsDown)
            {
                if (item.IsRepeat) WindowsRepeatCount++;
                else WindowsDownCount++;
            }
            else WindowsUpCount++;

            string kind = item.IsRepeat ? "win-repeat" : item.IsDown ? "win-down" : "win-up";
            string key = item.Key?.ToString() ?? $"VK_0x{item.VirtualKey:X2}";
            double osMs = RelativeMs(item.OsEstimatedAt);
            double callbackMs = RelativeMs(item.CapturedAt);
            Add(new DiagnosticRow(kind, frame, gameMs, callbackMs, key, "", 0, "",
                null, null, null, null, null, null, item.VirtualKey, item.ScanCode,
                osMs, callbackMs, null, item.QueueAgeMs, "Windows単独"));
        }

        // ここで計っている処理時間はRaylibから入力を取り出してからの時間。
        // Windows側の時刻が見つかれば、その差も一緒に保存する。
        public void RecordInput(long frame, double gameMs, string key, string part, int noteNumber,
            string result, double? judgeDeltaMs, long collectedAt, long processedAt,
            WindowsKeyEvent? windowsEvent, long processingStartedAt,
            double eventGameMs, double judgeGameMs, double waitMs,
            string timingMode, bool limiterEnabled)
        {
            // 入力をキューから取り出した時刻と、実際に判定を始めた時刻は別。
            double processingMs = Stopwatch.GetElapsedTime(processingStartedAt, processedAt).TotalMilliseconds;
            LastScheduledWaitMs = waitMs;
            LastInputHandlingMs = processingMs;
            InputCount++;
            double relativeMs = RelativeMs(collectedAt);
            double? osMs = null, callbackMs = null, delayMs = null, queueMs = null;
            uint? vk = null, scan = null;
            string status = "未照合";

            if (windowsEvent is WindowsKeyEvent item)
            {
                NativeMatchedCount++;
                vk = item.VirtualKey;
                scan = item.ScanCode;
                osMs = RelativeMs(item.OsEstimatedAt);
                callbackMs = RelativeMs(item.CapturedAt);
                delayMs = Stopwatch.GetElapsedTime(item.OsEstimatedAt, collectedAt).TotalMilliseconds;
                queueMs = item.QueueAgeMs;
                LastNativeToRaylibMs = delayMs;
                status = "一致";
            }
            else NativeUnmatchedCount++;

            Add(new DiagnosticRow("input", frame, gameMs, relativeMs, key, part, noteNumber, result,
                judgeDeltaMs, processingMs, null, LatestSyncErrorMs, RecentMaxCorrectionMs, LatestOutputEstimateMs,
                vk, scan, osMs, callbackMs, delayMs, queueMs, status,
                eventGameMs, judgeGameMs, waitMs, timingMode, limiterEnabled));
        }

        // CSV の見出し(デバッグ記録の input.csv。前の「入力診断CSV」と同じ列)
        public const string CsvHeader = "種類,フレーム番号,ゲーム時刻ms,入力取得または記録時刻ms,キー,叩く場所,音符番号,結果,判定ズレms,入力処理時間ms,最大フレーム時間ms,音声と画面のズレms,時計補正ms,出力遅延推定ms,Windows仮想キーVK,Windowsスキャンコード,Windowsイベント時刻推定ms,Windows処理時刻ms,WindowsからRaylib取得までms,Windowsメッセージ待ち推定ms,Windows照合結果,入力イベント譜面時刻ms,判定実行譜面時刻ms,判定待ちms,入力方式,60打制限";

        // 1行を CSV の文字にする
        private static string FormatRow(DiagnosticRow r)
        {
            var sb = new StringBuilder(256);
            sb.Append(Escape(r.Kind)).Append(',');
            sb.Append(r.Frame).Append(',');
            sb.Append(Format(r.GameMs)).Append(',');
            sb.Append(Format(r.ObservedMs)).Append(',');
            sb.Append(Escape(r.Key)).Append(',').Append(Escape(r.Part)).Append(',');
            sb.Append(r.NoteNumber).Append(',').Append(Escape(r.Result)).Append(',');
            sb.Append(Format(r.JudgeDeltaMs)).Append(',').Append(Format(r.HandlingMs)).Append(',');
            sb.Append(Format(r.FrameMs)).Append(',').Append(Format(r.AudioSyncMs)).Append(',');
            sb.Append(Format(r.ClockCorrectionMs)).Append(',').Append(Format(r.OutputEstimateMs)).Append(',');
            sb.Append(r.NativeVk?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(r.NativeScan?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(Format(r.NativeOsMs)).Append(',').Append(Format(r.NativeCallbackMs)).Append(',');
            sb.Append(Format(r.NativeToRaylibMs)).Append(',').Append(Format(r.MessageQueueAgeMs)).Append(',');
            sb.Append(Escape(r.NativeMatch)).Append(',');
            sb.Append(Format(r.EventGameMs)).Append(',');
            sb.Append(Format(r.JudgeGameMs)).Append(',');
            sb.Append(Format(r.WaitForJudgeMs)).Append(',');
            sb.Append(Escape(r.TimingMode)).Append(',');
            sb.Append(r.HitLimitEnabled.HasValue ? (r.HitLimitEnabled.Value ? "ON" : "OFF") : "");
            return sb.ToString();
        }

        public static string FormatMs(double? ms) => ms.HasValue ? $"{ms.Value:+0.###;-0.###;0} ms" : "未取得";

        private static string Format(double? n) => n.HasValue ? Format(n.Value) : "";
        private static string Format(double n) => n.ToString("0.######", CultureInfo.InvariantCulture);
        private static string Escape(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
        private double ObservedMs() => RelativeMs(Stopwatch.GetTimestamp());
        private double RelativeMs(long ticks) => (ticks - startTimestamp) * 1000.0 / Stopwatch.Frequency;

        // 画面の表示用に覚えておいて、デバッグ記録が ON なら input.csv にもすぐ書く
        private void Add(DiagnosticRow row)
        {
            if (rows.Count >= MaxRows) rows.RemoveAt(0);
            rows.Add(row);
            if (DebugLog.Active) DebugLog.Input(FormatRow(row));
        }
    }
}
