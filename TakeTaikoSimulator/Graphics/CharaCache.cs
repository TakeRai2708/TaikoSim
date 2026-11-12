using System.Collections.Concurrent;
using System.Text.Json;
using Raylib_cs;
using TakeTaikoSimulator.Configs;

namespace TakeTaikoSimulator.Graphics
{
    // どんちゃんのコマを「小さくした PNG」にして exe の横の Cache フォルダに焼いておく係
    //
    //   A-Style のどんちゃんは 1コマ 820×599 で、演奏画面だけで 3185コマある（15アニメ）。
    //   全部を画像として持つと、余白を切って 0.81倍にしても 627MB になる（GPU のメモリ）。
    //   なので「焼く」→「見せるコマだけ、そのつど読む」にした（読む方は CharaStream.cs）。
    //
    //   焼くときにやること（初回だけ。2回目からはすぐ使える）
    //     ① コマ間引き  …… 2コマに1コマ(30fps 相当)。マスコットなので見て分からない
    //     ② 余白カット  …… 中身は 820×599 のうち 350×300 くらいしかない（アニメごとに、残すコマを全部見て決める）
    //     ③ 縮小        …… 画面に出す倍率(演奏なら 0.81、風船は 0.95)で焼いてしまう
    //   → 1コマ 30〜50KB くらいの PNG になる。置き場所は Cache/Chara/<スキン>/<キャラ>/<アニメ>/<番号>.png
    //
    //   Raylib の Image 系(LoadImage / ImageCrop / ImageResize / ExportImage)は CPU だけで動くので、別スレッドでやれる。
    public static class CharaCache
    {
        private const int MetaVersion = 2;

        // 余白を切るときのアルファのしきい値と、まわりに足す余裕(縮小のにじみ対策)
        private const float AlphaThreshold = 0.02f;
        private const int TrimMargin = 2;

        public static string CacheRoot => Path.Combine(AppContext.BaseDirectory, "Cache", "Chara");

        // そのキャラのキャッシュの置き場所(スキンごとに分ける)
        public static string FolderFor(string skinName, string charaFolder) =>
            Path.Combine(CacheRoot, Safe(skinName), Safe(charaFolder));

        private static string Safe(string name)
        {
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            return name.Length == 0 ? "_" : name;
        }

        public static string MetaPath(string cacheFolder, string animName) => Path.Combine(cacheFolder, animName + ".json");
        public static string FramePath(string cacheFolder, string animName, int index) =>
            Path.Combine(cacheFolder, animName, index + ".png");

        /// <summary>
        /// コマPNGの入ったフォルダから、小さくしたコマを焼く。すでに新しいキャッシュがあれば何もしない。
        /// やめたいときは cancel を true にする。焼けた（焼けていた）ら true。
        /// </summary>
        public static bool BuildIfNeeded(string sourceFolder, string cacheFolder, string animName,
                                         float scale, int frameStep, Func<bool>? cancel = null)
        {
            if (!Directory.Exists(sourceFolder)) return false;
            frameStep = Math.Max(1, frameStep);
            scale = Math.Clamp(scale, 0.05f, 4f);

            int sourceCount = CountFrames(sourceFolder);
            if (sourceCount == 0) return false;
            long stamp = SourceStamp(sourceFolder, sourceCount);
            string metaPath = MetaPath(cacheFolder, animName);

            // すでにある？(元が差し替わっていたら作り直す)
            if (ReadMeta(metaPath) is { } have
                && have.Version == MetaVersion && have.SourceFrameCount == sourceCount
                && have.SourceStamp == stamp && Math.Abs(have.Scale - scale) < 0.0001f
                && have.FrameStep == frameStep && AllFramesExist(cacheFolder, animName, have.FrameCount))
                return true;

            // 作り直すときは、古いメタを先に消す（途中でやめたら「焼けていない」になるように。メタは最後に書く）
            try { if (File.Exists(metaPath)) File.Delete(metaPath); } catch { }
            string frameFolder = Path.Combine(cacheFolder, animName);
            Directory.CreateDirectory(frameFolder);

            int keep = (sourceCount + frameStep - 1) / frameStep;

            // ① 残すコマを全部見て「中身の入っている範囲」を求める
            //    一部だけ見て決めると、見ていないコマがはみ出して切れるので、ここは手を抜かない
            Rectangle box = default;
            bool any = false;
            int srcW = 0, srcH = 0;
            for (int k = 0; k < keep; k++)
            {
                if (cancel?.Invoke() == true) return false;
                Image img = Raylib.LoadImage(Path.Combine(sourceFolder, k * frameStep + ".png"));
                if (img.Width <= 0) continue;
                if (srcW == 0) { srcW = img.Width; srcH = img.Height; }
                Rectangle b = Raylib.GetImageAlphaBorder(img, AlphaThreshold);
                if (b.Width > 0 && b.Height > 0) { box = any ? Union(box, b) : b; any = true; }
                Raylib.UnloadImage(img);
            }
            if (!any || srcW <= 0 || srcH <= 0) return false;

            // 余裕を足して、元画像からはみ出さないように丸める
            int trimX = Math.Max(0, (int)box.X - TrimMargin);
            int trimY = Math.Max(0, (int)box.Y - TrimMargin);
            int trimW = Math.Min(srcW - trimX, (int)box.Width + TrimMargin * 2);
            int trimH = Math.Min(srcH - trimY, (int)box.Height + TrimMargin * 2);
            if (trimW <= 0 || trimH <= 0) return false;

            int cellW = Math.Max(1, (int)MathF.Round(trimW * scale));
            int cellH = Math.Max(1, (int)MathF.Round(trimH * scale));

            // ②③ 切って縮めて、1コマずつ PNG にする（どのコマも同じ大きさ）
            Rectangle crop = new(trimX, trimY, trimW, trimH);
            for (int k = 0; k < keep; k++)
            {
                if (cancel?.Invoke() == true) return false;
                Image img = Raylib.LoadImage(Path.Combine(sourceFolder, k * frameStep + ".png"));
                if (img.Width != srcW || img.Height != srcH)
                {
                    // 読めない・大きさが違うコマは透明にしておく（数がずれると動きがずれるので、抜かない）
                    if (img.Width > 0) Raylib.UnloadImage(img);
                    img = Raylib.GenImageColor(cellW, cellH, new Color(0, 0, 0, 0));
                }
                else
                {
                    Raylib.ImageCrop(ref img, crop);
                    Raylib.ImageResize(ref img, cellW, cellH);
                }
                Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
                Raylib.ExportImage(img, FramePath(cacheFolder, animName, k));
                Raylib.UnloadImage(img);
            }

            WriteMeta(metaPath, new CharaCacheMeta
            {
                Version = MetaVersion,
                FrameCount = keep,
                SourceFrameCount = sourceCount,
                FrameStep = frameStep,
                CellWidth = cellW,
                CellHeight = cellH,
                Scale = scale,
                TrimX = trimX,
                TrimY = trimY,
                TrimWidth = trimW,
                TrimHeight = trimH,
                SourceWidth = srcW,
                SourceHeight = srcH,
                SourceStamp = stamp,
            });
            return true;
        }

        // 0.png から続いているコマの数(途中で抜けていたらそこまで)
        public static int CountFrames(string folder)
        {
            int n = 0;
            while (File.Exists(Path.Combine(folder, n + ".png"))) n++;
            return n;
        }

        // 元フォルダが差し替わったか見るための印(1コマ目と最後のコマの更新時刻＋大きさ)
        private static long SourceStamp(string folder, int count)
        {
            long v = count;
            foreach (int i in new[] { 0, count - 1 })
            {
                FileInfo fi = new(Path.Combine(folder, i + ".png"));
                if (!fi.Exists) continue;
                v = v * 31 + fi.LastWriteTimeUtc.Ticks;
                v = v * 31 + fi.Length;
            }
            return v;
        }

        private static Rectangle Union(Rectangle a, Rectangle b)
        {
            float x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            return new Rectangle(x, y, Math.Max(a.X + a.Width, b.X + b.Width) - x, Math.Max(a.Y + a.Height, b.Y + b.Height) - y);
        }

        private static bool AllFramesExist(string cacheFolder, string animName, int count)
        {
            if (count <= 0) return false;
            // 全部見ると遅いので、最初と最後だけ
            return File.Exists(FramePath(cacheFolder, animName, 0)) && File.Exists(FramePath(cacheFolder, animName, count - 1));
        }

        public static CharaCacheMeta? ReadMeta(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                CharaCacheMeta? meta = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.CharaCacheMeta);
                return meta != null && meta.Version == MetaVersion && meta.FrameCount > 0 ? meta : null;
            }
            catch { return null; }
        }

        private static void WriteMeta(string path, CharaCacheMeta meta)
        {
            try { File.WriteAllText(path, JsonSerializer.Serialize(meta, AppJsonContext.Default.CharaCacheMeta)); }
            catch { /* 書けなくても次回また焼くだけ */ }
        }
    }

    // 焼いたアニメのメタ情報(キャッシュの <アニメ名>.json)
    public sealed class CharaCacheMeta
    {
        public int Version { get; set; }
        public int FrameCount { get; set; }        // キャッシュに入っているコマ数(間引き後)
        public int SourceFrameCount { get; set; }  // 元のコマ数
        public int FrameStep { get; set; } = 1;    // 何コマに1コマ残したか
        public int CellWidth { get; set; }         // 焼いた1コマの大きさ
        public int CellHeight { get; set; }
        public float Scale { get; set; } = 1f;     // 焼いた倍率
        public int TrimX { get; set; }             // 元画像(820×599)の中で、切り出した範囲
        public int TrimY { get; set; }
        public int TrimWidth { get; set; }
        public int TrimHeight { get; set; }
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
        public long SourceStamp { get; set; }
    }

    // スキンを読んだら、裏でどんちゃんのコマを焼きはじめる係
    //   初回だけ1分くらいかかる（画面は止まらない。焼けたアニメから使える）。
    //   焼けていないアニメは描かない（演奏画面では、焼けていなければどんちゃんが出ないだけ）。
    public static class CharaBaker
    {
        // 演奏画面のアニメ（焼く順。よく出るものから）
        public static readonly string[] PlayAnims =
        {
            "normal", "10combo", "gogostart", "gogo", "clearin", "clear", "soulin", "10combomax",
            "combocut", "miss_loop", "failed_down", "failed_loop", "balloon_breaking", "balloon_broke", "balloon_miss",
        };
        public static bool IsBalloonAnim(string anim) => anim.StartsWith("balloon_", StringComparison.Ordinal);

        // メニューの画面のアニメ（モード選択・選曲・段位選択は songselect_loop、リザルトは result_*）
        public static readonly string[] MenuAnims =
        {
            "songselect_loop", "entry_loop", "result_loop", "result_clear_loop", "result_failed_in", "result_failed_loop",
        };

        // メニューのアニメの1周の長さ（ミリ秒。コマを間引く量を決めるのに使う）。時間が決まっていないものは 0
        private static int LoopMsFor(CharaConfig config, string anim) => anim switch
        {
            "songselect_loop" => config.SongSelectLoopTime,
            "entry_loop" => config.EntryModeLoopTime,
            "result_loop" => config.ResultLoopTime,
            "result_clear_loop" => config.ResultClearLoopTime,
            "result_failed_in" => config.ResultFailedInTime,
            "result_failed_loop" => config.ResultFailedLoopTime,
            _ => 0,
        };

        // 何コマに1コマ残すか
        //   11/13 たけらい「モード選択、選曲のどんちゃん、なんか fps 低い」：
        //   songselect_loop は 236コマを 1秒で回すので、2コマに1コマでも 118コマ/秒。画面は 60コマ/秒しか出ないのに、
        //   裏のスレッドが毎秒 118枚の PNG を読むことになって、読みが間に合わず同じコマが続いていた。
        //   → 時間が決まっているアニメは「1秒に 60コマ」になるまで間引く（見た目は同じ。読む量は半分）
        private static int FrameStepFor(CharaConfig config, string anim, int sourceCount)
        {
            int loopMs = LoopMsFor(config, anim);
            if (loopMs <= 0 || sourceCount <= 0) return FrameStep;
            int wanted = Math.Max(1, (int)Math.Ceiling(loopMs / 1000.0 * 60.0));
            return Math.Max(FrameStep, (int)Math.Ceiling(sourceCount / (double)wanted));
        }
        public static bool IsResultAnim(string anim) => anim.StartsWith("result_", StringComparison.Ordinal);

        // 曲を決めたときのジャンプに使うアニメ
        //   11/8 の録画（2026-11-08 00-39-05.mp4 の 141.5 秒〜）で、止まったポーズが 10combo の 66コマめとぴったり一致した（ずれ 0.003）。
        //   Config.json の songSelectSongStartJumpIndex（66）と songSelectSongStartTime（360ms）は「10combo を 360ms で 66コマめまで進めて止める」という意味
        public const string SongStartAnim = "10combo";

        // 焼く順：選曲のどんちゃん（起動してすぐ見る）→ 演奏 → リザルト
        private static IEnumerable<string> BakeOrder =>
            new[] { "songselect_loop" }.Concat(PlayAnims).Concat(MenuAnims.Where(a => a != "songselect_loop"));

        private const int FrameStep = 2;

        private sealed record Bake(CharaConfig Config, string Source, string Cache);
        private static Thread? worker;
        private static CancellationTokenSource? cancellation;
        private static readonly Dictionary<int, Bake> profiles = new();
        private static readonly ConcurrentDictionary<string, bool> ready = new();
        public static int Generation { get; private set; }
        private static int CurrentProfile => TakeTaikoSimulator.Scenes.LocalSession.Count > 0
            ? TakeTaikoSimulator.Scenes.LocalSession.Profiles[TakeTaikoSimulator.Scenes.LocalSession.MenuPlayer]
            : TakeTaikoSimulator.Charts.SongHistory.ActiveProfile;
        public static CharaConfig? Config => ConfigFor(CurrentProfile);
        public static string? SourceFolder => profiles.GetValueOrDefault(CurrentProfile)?.Source;
        public static string? CacheFolder => CacheFor(CurrentProfile);
        public static CharaConfig? ConfigFor(int slot) => profiles.GetValueOrDefault(slot)?.Config;
        public static string? CacheFor(int slot) => profiles.GetValueOrDefault(slot)?.Cache;
        public static bool IsReady(string anim) => CacheFolder is string folder && ready.ContainsKey(folder + "/" + anim);
        public static float PlayScale(string anim) => Config == null ? 1 : IsBalloonAnim(anim) ? Config.EnsoGameBalloonScale : Config.EnsoGameNormalScale;
        public static float BakeScale(string anim) => ScaleFor(Config, anim);
        private static float ScaleFor(CharaConfig? config, string anim) => config == null ? 1
            : anim is "songselect_loop" or "entry_loop" ? Math.Max(config.SongSelectScale, config.EntryScale)
            : anim == SongStartAnim ? Math.Max(config.EnsoGameNormalScale, config.SongSelectScale)
            : IsResultAnim(anim) ? config.ResultScale
            : IsBalloonAnim(anim) ? config.EnsoGameBalloonScale : config.EnsoGameNormalScale;

        // 同じキャラのキャッシュは共有し、1P/2Pで異なるキャラも焼けるようにする。
        public static void Start(string? skinPath, string skinName)
        {
            Stop(); profiles.Clear(); ready.Clear(); Generation++;
            if (skinPath == null) return;
            string root = Path.Combine(skinPath, "Image", "99.Common", "Chara");
            string[] ids = CosmeticCatalog.Folders(root, "Config.json");
            var unique = new Dictionary<string, Bake>(StringComparer.Ordinal);
            for (int slot = PlayerDataStore.GuestSlot; slot <= 3; slot++)   // 0 はゲスト（初期値のどんちゃん）
            {
                PlayerAppearanceConfig appearance;
                try { appearance = PlayerDataStore.Appearance(slot); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { Console.WriteLine($"[Chara] データ{slot}：{ex.Message}"); appearance = new(); }
                string id = CosmeticCatalog.Choose(ids, appearance.DonchanId, appearance.DonchanType);
                if (id.Length == 0) continue;
                if (!unique.TryGetValue(id, out var bake))
                {
                    string folder = Path.Combine(root, id);
                    if (CharaConfig.Load(Path.Combine(folder, "Config.json")) is not CharaConfig config) continue;
                    unique[id] = bake = new(config, folder, CharaCache.FolderFor(skinName, id));
                }
                profiles[slot] = bake;
            }
            var jobs = unique.Values.ToArray();
            cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            worker = new Thread(() => Run(jobs, token)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "CharaBaker" };
            worker.Start();
        }
        public static void Stop()
        {
            cancellation?.Cancel();
            worker?.Join(3000);
            worker = null;
        }
        private static void Run(Bake[] jobs, CancellationToken token)
        {
            foreach (string anim in BakeOrder)
            foreach (Bake job in jobs)
            {
                if (token.IsCancellationRequested) return;
                try
                {
                    string source = Path.Combine(job.Source, anim);
                    int step = FrameStepFor(job.Config, anim, Directory.Exists(source) ? CharaCache.CountFrames(source) : 0);
                    if (CharaCache.BuildIfNeeded(source, job.Cache, anim,
                        ScaleFor(job.Config, anim), step, () => token.IsCancellationRequested)) ready[job.Cache + "/" + anim] = true;
                }
                catch (Exception ex) { Console.WriteLine($"[Chara] {anim} を焼けませんでした：{ex.Message}"); }
            }
        }
    }
}
