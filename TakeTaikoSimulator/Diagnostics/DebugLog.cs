using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using TakeTaikoSimulator.Configs;

namespace TakeTaikoSimulator.Diagnostics
{
    // 1フレームの中で、時間を測る場所
    //   上の段(SongList〜Present)は、1フレームの流れの順番。足すと「作業」と「表示・待ち」になる
    //   Play〜 は「画面の描画」の中の内訳、Text は全部の中にまざっている「文字を描いた時間」
    public enum PerfSection
    {
        SongList,        // 曲の一覧の入れ替え(裏で読み終わったとき)
        SceneUpdate,     // 画面の更新(入力・判定・動き)
        SettingsUpdate,  // 設定パネルの更新
        SceneDraw,       // 画面の描画(キャンバスに描く)
        SettingsDraw,    // 設定パネルの描画
        OverlayDraw,     // FPS・重さの内訳の表示
        CanvasFlush,     // キャンバスへの描画の終わり(たまった描画を GPU に送る)
        Screenshot,      // スクリーンショットの保存
        Composite,       // 画面合成(キャンバスをウィンドウの大きさにして写す)
        Present,         // 表示・待ち(画面を入れ替える。垂直同期・FPS の上限の待ちもここに入る)

        PlayBackground,  // 演奏：背景・踊り子
        PlayLane,        // 演奏：レーン・分岐・判定枠
        PlayGauge,       // 演奏：魂ゲージ
        PlayNotes,       // 演奏：小節線・音符・音符の下の文字・風船
        PlayEffects,     // 演奏：花火・ミニ太鼓・コンボ・ゴーゴー・飛ぶ音符
        PlayHud,         // 演奏：左のパネル・判定の文字・曲名・スコア板・ランク
        PlayEnd,         // 演奏：終わりの演出・暗くする

        Text,            // 文字を描いた時間(上の全部の中にまざっている)
        Count,
    }

    // デバッグ用の記録係(CSV)
    //
    // 記録するもの(Logs/Debug_日付_時間/ の中)：
    //   frames.csv … 1フレームに1行。フレームの間隔・それぞれの場所にかかった時間・文字の数・メモリ・音のズレなど
    //   events.csv … 何か起きたときに1行。画面の切り替え・重いフレーム・画像やフォントの読み込み・GC・黒い画面に出したログ全部
    //   input.csv  … 演奏中の入力(Windows のキーの時刻・判定・音とのズレ)。前の「入力診断CSV」と同じ列
    //
    // 書くのは裏の別スレッドなので、ゲームの動きは止めない(1フレームに足す仕事は、時刻を何回か読むくらい)
    // 設定の「デバッグ記録」が ON の間だけ記録する。途中で ON にすると、そこから新しいフォルダに記録する
    public static class DebugLog
    {
        // 残しておく記録の数(これより古いフォルダは消す)
        private const int KeepSessions = 10;
        // これより長いフレームは「重いフレーム」として events.csv にも書く
        public const double SlowFrameMs = 20.0;

        private static readonly string[] SectionNames =
        {
            "曲一覧ms", "画面の更新ms", "設定パネル更新ms",
            "画面の描画ms", "設定パネル描画ms", "FPS表示ms", "キャンバス送りms", "スクショms", "画面合成ms", "表示・待ちms",
            "演奏:背景ms", "演奏:レーンms", "演奏:ゲージms", "演奏:音符ms", "演奏:演出ms", "演奏:パネル文字ms", "演奏:終わりms",
            "文字ms",
        };

        // 画面に出すときの短い名前
        private static readonly string[] SectionShortNames =
        {
            "曲一覧", "更新", "設定更新", "描画", "設定描画", "FPS表示", "GPUへ送る", "スクショ", "画面合成", "表示・待ち",
            "背景", "レーン", "ゲージ", "音符", "演出", "パネル", "終わり", "文字",
        };

        private const int SectionCount = (int)PerfSection.Count;
        private const int FirstPlaySection = (int)PerfSection.PlayBackground;

        // ===== 状態 =====
        public static bool Active { get; private set; }  // 今、記録しているか
        public static string? SessionFolder { get; private set; }
        public static string SceneName { get; private set; } = "";

        private static long sessionStart;
        private static long frameStart;
        private static long frameIndex;
        private static readonly long[] sectionTicks = new long[SectionCount];
        private static int glyphCount;
        private static int textCalls;
        private static bool countText = true;
        private static double? audioSyncMs, audioOutputMs;
        private static long allocatedAtFrameStart;
        private static readonly int[] gcCountAtStart = new int[3];
        private static bool shutdown;

        // 画面に出す用(1秒ごとの平均・最大)
        private static readonly double[] windowSum = new double[SectionCount];
        private static readonly double[] windowMax = new double[SectionCount];
        private static double windowWorkSum, windowWorkMax, windowAllocSum;
        private static int windowGlyphSum, windowFrames;
        private static double windowTime;
        public static readonly double[] ShownAvg = new double[SectionCount];
        public static readonly double[] ShownMax = new double[SectionCount];
        public static double ShownWorkAvg { get; private set; }
        public static double ShownWorkMax { get; private set; }
        public static double ShownAllocKb { get; private set; }
        public static int ShownGlyphs { get; private set; }

        // ===== 裏で書く係 =====
        private readonly struct Item
        {
            public readonly byte File;       // 0 frames, 1 events, 2 input
            public readonly string? Line;    // events・input は書く1行
            public readonly FrameRow Frame;  // frames は数字のまま渡して、裏で文字にする
            public Item(byte file, string? line, FrameRow frame) { File = file; Line = line; Frame = frame; }
        }

        private readonly struct FrameRow
        {
            public readonly long Index;
            public readonly double TimeMs, DtMs, WorkMs, MemoryMb, AllocKb, BassCpu;
            public readonly double? SyncMs, OutputMs;
            public readonly string Scene;
            public readonly double[]? Sections; // ArrayPool から借りたもの(書いたら返す)
            public readonly int Fps, Glyphs, TextCalls, FontPages, Textures, Gc0, Gc1, Gc2;

            public FrameRow(long index, double timeMs, double dtMs, double workMs, string scene, double[] sections,
                int fps, int glyphs, int textCalls, int fontPages, int textures, double memoryMb, double allocKb,
                int gc0, int gc1, int gc2, double? syncMs, double? outputMs, double bassCpu)
            {
                Index = index; TimeMs = timeMs; DtMs = dtMs; WorkMs = workMs; Scene = scene; Sections = sections;
                Fps = fps; Glyphs = glyphs; TextCalls = textCalls; FontPages = fontPages; Textures = textures;
                MemoryMb = memoryMb; AllocKb = allocKb; Gc0 = gc0; Gc1 = gc1; Gc2 = gc2;
                SyncMs = syncMs; OutputMs = outputMs; BassCpu = bassCpu;
            }
        }

        private static Channel<Item>? channel;
        private static Thread? writerThread;

        // 文字・画像の数を教えてもらう係(DebugLog が Graphics を直接見ないように、Game が入れる)
        public static Func<int>? FontPageCounter { get; set; }
        public static Func<int>? TextureCounter { get; set; }
        public static Func<double>? BassCpuReader { get; set; }

        // ===== フレーム =====

        // フレームの最初(Game のループの一番上)。設定が変わっていたら、記録を始める・やめる
        public static void BeginFrame()
        {
            bool want = ConfigManager.Game.DebugLog && !shutdown;
            if (want && !Active) StartSession();
            else if (!want && Active) StopSession("設定で OFF にした");

            frameStart = Stopwatch.GetTimestamp();
            Array.Clear(sectionTicks);
            glyphCount = 0;
            textCalls = 0;
            audioSyncMs = null;
            audioOutputMs = null;
            allocatedAtFrameStart = GC.GetAllocatedBytesForCurrentThread();
        }

        // 測り始め(返した値を End に渡す)。記録も表示もしていないときは 0 を返して、何もしない
        public static long Begin() => Active || ConfigManager.Game.ShowPerfOverlay ? Stopwatch.GetTimestamp() : 0;

        public static void End(PerfSection section, long started)
        {
            if (started == 0) return;
            sectionTicks[(int)section] += Stopwatch.GetTimestamp() - started;
        }

        // 文字を描いた(DynamicFont から)。glyphs は描いた文字の数
        public static void CountText(int glyphs, long started)
        {
            if (!countText) return;
            glyphCount += glyphs;
            textCalls++;
            End(PerfSection.Text, started);
        }

        // 重さの内訳を描いている間は、文字の数に入れない(表示のせいで数が増えないように)
        public static void PauseTextCount(bool paused) => countText = !paused;

        // 演奏中：曲と画面のズレ・出力の遅れ(わかるときだけ)
        public static void SetAudio(double? syncMs, double? outputMs)
        {
            audioSyncMs = syncMs;
            audioOutputMs = outputMs;
        }

        // フレームの最後(Game のループの一番下)
        //   フレーム間隔 = BeginFrame からここまで(このフレームそのものの長さ。表示・待ちも入る)
        //   作業        = そこから「表示・待ち」を引いたもの(CPU が実際に働いた時間)
        public static void EndFrame(int fps)
        {
            long now = Stopwatch.GetTimestamp();
            double dtMs = TicksToMs(now - frameStart);
            double presentMs = TicksToMs(sectionTicks[(int)PerfSection.Present]);
            double workMs = dtMs - presentMs;
            double allocKb = (GC.GetAllocatedBytesForCurrentThread() - allocatedAtFrameStart) / 1024.0;

            UpdateWindow(dtMs, workMs, allocKb);

            if (!Active) return;
            frameIndex++;

            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            if (gc2 != gcCountAtStart[2]) Event("GC", $"大きいお片付け(第2世代)が {gc2 - gcCountAtStart[2]} 回あった", gc2);
            else if (gc1 != gcCountAtStart[1]) Event("GC", $"お片付け(第1世代)が {gc1 - gcCountAtStart[1]} 回あった", gc1);
            gcCountAtStart[0] = gc0; gcCountAtStart[1] = gc1; gcCountAtStart[2] = gc2;

            double[] sections = ArrayPool<double>.Shared.Rent(SectionCount);
            for (int i = 0; i < SectionCount; i++) sections[i] = TicksToMs(sectionTicks[i]);

            if (dtMs > SlowFrameMs) Event("重いフレーム", $"{dtMs:F1} ms　{TopSections(sections, 3)}", dtMs, workMs);

            Write(new Item(0, null, new FrameRow(frameIndex, ElapsedMs(), dtMs, workMs, SceneName, sections,
                fps, glyphCount, textCalls, FontPageCounter?.Invoke() ?? 0, TextureCounter?.Invoke() ?? 0,
                GC.GetTotalMemory(false) / (1024.0 * 1024.0), allocKb, gc0, gc1, gc2,
                audioSyncMs, audioOutputMs, BassCpuReader?.Invoke() ?? 0)));
        }

        // 1秒ごとの平均・最大(画面の右上に出す用)
        private static void UpdateWindow(double dtMs, double workMs, double allocKb)
        {
            for (int i = 0; i < SectionCount; i++)
            {
                double ms = TicksToMs(sectionTicks[i]);
                windowSum[i] += ms;
                windowMax[i] = Math.Max(windowMax[i], ms);
            }
            windowWorkSum += workMs;
            windowWorkMax = Math.Max(windowWorkMax, workMs);
            windowAllocSum += allocKb;
            windowGlyphSum += glyphCount;
            windowFrames++;
            windowTime += dtMs;
            if (windowTime < 1000.0) return;

            for (int i = 0; i < SectionCount; i++)
            {
                ShownAvg[i] = windowSum[i] / windowFrames;
                ShownMax[i] = windowMax[i];
                windowSum[i] = 0;
                windowMax[i] = 0;
            }
            ShownWorkAvg = windowWorkSum / windowFrames;
            ShownWorkMax = windowWorkMax;
            ShownAllocKb = windowAllocSum / windowFrames;
            ShownGlyphs = windowGlyphSum / windowFrames;
            windowWorkSum = windowWorkMax = windowAllocSum = 0;
            windowGlyphSum = 0;
            windowFrames = 0;
            windowTime = 0;
        }

        // 一番時間がかかった場所を n 個(重いフレームの説明用)
        private static string TopSections(double[] sections, int n)
        {
            var top = Enumerable.Range(0, SectionCount)
                .Where(i => i != (int)PerfSection.Text && i != (int)PerfSection.SceneDraw && sections[i] >= 0.5)
                .OrderByDescending(i => sections[i]).Take(n)
                .Select(i => $"{SectionShortNames[i]} {sections[i]:F1}ms");
            string text = string.Join("・", top);
            double textMs = sections[(int)PerfSection.Text];
            if (textMs >= 0.5) text += $"（うち文字 {textMs:F1}ms）";
            return text;
        }

        // 画面に出す、重さの内訳(平均が 0.05ms 以上か、1ms を超えたフレームがあったものだけ)
        //   毎フレーム呼ばれるので、メモリを確保しないように番号で回す
        public const int SectionTotal = SectionCount;
        public static string SectionShortName(int index) => SectionShortNames[index];
        public static bool IsShown(int index) => ShownAvg[index] >= 0.05 || ShownMax[index] >= 1.0;

        // ===== できごと =====

        // 画面が変わった(SceneManager から)
        public static void SceneChanged(string from, string to)
        {
            SceneName = to;
            Event("画面切替", $"{from} → {to}");
        }

        // できごとを1行書く。value1・value2 は数字(ms など。並べ替えやグラフに使える)
        public static void Event(string kind, string text, double? value1 = null, double? value2 = null)
        {
            if (!Active) return;
            var sb = new StringBuilder(96);
            sb.Append(Format(ElapsedMs())).Append(',').Append(frameIndex).Append(',');
            sb.Append(Escape(SceneName)).Append(',').Append(Escape(kind)).Append(',').Append(Escape(text)).Append(',');
            sb.Append(Format(value1)).Append(',').Append(Format(value2));
            Write(new Item(1, sb.ToString(), default));
        }

        // 黒い画面(コンソール)に出したログ(Program の係から)
        public static void ConsoleLine(string? line)
        {
            if (!Active || string.IsNullOrEmpty(line)) return;
            Event("ログ", line.TrimEnd('\r', '\n'));
        }

        // 演奏中の入力の1行(InputDiagnostics から。列は InputDiagnostics.CsvHeader と同じ)
        public static void Input(string csvLine)
        {
            if (!Active) return;
            Write(new Item(2, Format(ElapsedMs()) + "," + Escape(SceneName) + "," + csvLine, default));
        }

        // ===== 始める・やめる =====

        private static void StartSession()
        {
            try
            {
                string root = Path.Combine(AppContext.BaseDirectory, "Logs");
                Directory.CreateDirectory(root);
                foreach (string old in Directory.GetDirectories(root, "Debug_*").OrderBy(p => p)
                             .SkipLast(KeepSessions - 1))
                {
                    try { Directory.Delete(old, true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }

                string folder = Path.Combine(root, $"Debug_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.CreateDirectory(folder);
                SessionFolder = folder;

                // ログは裏のスレッド(曲の読み込みなど)からも来るので、書く側は1つと決めない
                channel = Channel.CreateUnbounded<Item>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
                sessionStart = Stopwatch.GetTimestamp();
                frameIndex = 0;
                gcCountAtStart[0] = GC.CollectionCount(0);
                gcCountAtStart[1] = GC.CollectionCount(1);
                gcCountAtStart[2] = GC.CollectionCount(2);

                Channel<Item> current = channel;
                writerThread = new Thread(() => WriterLoop(current, folder)) { IsBackground = true, Name = "DebugLog" };
                writerThread.Start();
                Active = true;

                Event("記録", $"デバッグ記録を始めた（{Path.GetFileName(folder)}）");
                Event("環境", $"{Environment.OSVersion}　.NET {Environment.Version}　CPU {Environment.ProcessorCount}");
                Event("設定", $"ウィンドウ {ConfigManager.Game.WindowWidth}×{ConfigManager.Game.WindowHeight}　" +
                    $"フルスクリーン {ConfigManager.Game.Fullscreen}　垂直同期 {ConfigManager.Game.VSync}　FPS上限 {ConfigManager.Game.TargetFps}");
                Event("設定", $"音の出力 {ConfigManager.Sound.OutputType}　スキン {ConfigManager.Skin.SkinName}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Active = false;
                // ここは Console に書くと自分に戻ってくるので、Debug にだけ出す
                System.Diagnostics.Debug.WriteLine($"[DebugLog] 記録を始められませんでした: {ex.Message}");
            }
        }

        private static void StopSession(string reason)
        {
            if (!Active) return;
            Event("記録", $"デバッグ記録をやめた（{reason}）");
            Active = false;
            channel?.Writer.TryComplete();
            writerThread?.Join(3000);
            channel = null;
            writerThread = null;
        }

        // 終了するとき(Game の最後)。書き残しを全部ファイルに書いてから戻る
        public static void Shutdown()
        {
            StopSession("終了");
            shutdown = true;
        }

        private static void Write(Item item)
        {
            // 裏のスレッドから来たときに、途中で channel が null になっても大丈夫なように、先に取っておく
            Channel<Item>? current = channel;
            if (current == null || !current.Writer.TryWrite(item))
            {
                if (item.Frame.Sections != null) ArrayPool<double>.Shared.Return(item.Frame.Sections);
            }
        }

        // 裏のスレッド：届いた行を3つのファイルに書く(0.5秒ごとにファイルへ流す)
        private static void WriterLoop(Channel<Item> source, string folder)
        {
            var utf8 = new UTF8Encoding(true); // Excel で文字化けしにくいように BOM つき
            using var frames = new StreamWriter(Path.Combine(folder, "frames.csv"), false, utf8, 1 << 16);
            using var events = new StreamWriter(Path.Combine(folder, "events.csv"), false, utf8, 1 << 14);
            using var input = new StreamWriter(Path.Combine(folder, "input.csv"), false, utf8, 1 << 14);

            frames.WriteLine("フレーム,経過ms,画面,フレーム間隔ms,作業ms," + string.Join(",", SectionNames) +
                ",FPS,文字の数,文字を描いた回数,文字のページ,画像の数,メモリMB,このフレームで確保KB,GC0,GC1,GC2,曲と画面のズレms,出力遅延ms,BASSのCPU%");
            events.WriteLine("経過ms,フレーム,画面,種類,内容,値1,値2");
            input.WriteLine("経過ms,画面," + TakeTaikoSimulator.Input.InputDiagnostics.CsvHeader);

            var reader = source.Reader;
            var flushWatch = Stopwatch.StartNew();
            var sb = new StringBuilder(512);
            try
            {
                while (true)
                {
                    bool more = reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult();
                    while (reader.TryRead(out Item item))
                    {
                        switch (item.File)
                        {
                            case 0:
                                sb.Clear();
                                AppendFrame(sb, item.Frame);
                                frames.WriteLine(sb);
                                break;
                            case 1: events.WriteLine(item.Line); break;
                            default: input.WriteLine(item.Line); break;
                        }
                    }
                    if (flushWatch.ElapsedMilliseconds >= 500 || !more)
                    {
                        frames.Flush(); events.Flush(); input.Flush();
                        flushWatch.Restart();
                    }
                    if (!more) break;
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                System.Diagnostics.Debug.WriteLine($"[DebugLog] 書き込みに失敗しました: {ex.Message}");
            }
        }

        private static void AppendFrame(StringBuilder sb, FrameRow r)
        {
            sb.Append(r.Index).Append(',').Append(Format(r.TimeMs)).Append(',').Append(Escape(r.Scene)).Append(',');
            sb.Append(Format(r.DtMs)).Append(',').Append(Format(r.WorkMs));
            if (r.Sections != null)
            {
                for (int i = 0; i < SectionCount; i++) sb.Append(',').Append(Format(r.Sections[i]));
                ArrayPool<double>.Shared.Return(r.Sections);
            }
            sb.Append(',').Append(r.Fps).Append(',').Append(r.Glyphs).Append(',').Append(r.TextCalls);
            sb.Append(',').Append(r.FontPages).Append(',').Append(r.Textures);
            sb.Append(',').Append(Format(r.MemoryMb)).Append(',').Append(Format(r.AllocKb));
            sb.Append(',').Append(r.Gc0).Append(',').Append(r.Gc1).Append(',').Append(r.Gc2);
            sb.Append(',').Append(Format(r.SyncMs)).Append(',').Append(Format(r.OutputMs)).Append(',').Append(Format(r.BassCpu));
        }

        // ===== 小さい道具 =====

        public static double ElapsedMs() => TicksToMs(Stopwatch.GetTimestamp() - sessionStart);
        private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        private static string Format(double? n) => n.HasValue ? Format(n.Value) : "";
        private static string Format(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
        public static string Escape(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
