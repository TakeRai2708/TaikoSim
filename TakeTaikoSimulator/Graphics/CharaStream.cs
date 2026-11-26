using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;
using TakeTaikoSimulator.Diagnostics;

namespace TakeTaikoSimulator.Graphics
{
    // 焼いたどんちゃんのコマ（CharaCache）を、見せるぶんだけ読んで描く係
    //
    //   全部のコマを GPU に載せると 600MB 以上になるので、アニメ1本につきテクスチャは1枚だけ持つ。
    //   コマが変わったら、そのコマの PNG を裏のスレッドで読んで（デコード）、テクスチャを上書きする。
    //     ・裏のスレッドは「今のコマから少し先まで」を前もって読んでおく（ガタつかないように）
    //     ・どのアニメも最初の数コマは読みっぱなしにしておく（10コンボなどで急に切りかわっても、すぐ出せる）
    //     ・読み終わっていないコマは、前に出したコマをそのまま出す（1フレームも消えない）
    //   コマの絵の大きさは 300×280 くらい（約300KB）なので、テクスチャの上書きは一瞬。
    public sealed class CharaStream : IDisposable
    {
        // 今のコマから何コマ先まで読んでおくか。メニューは 10（読みが間に合わないコマを減らす。11/13）、
        // 演奏画面は 6 のまま（演奏中に読む量・メモリを増やさない）
        private readonly int Ahead;
        private const int Pinned = 4;  // 最初の何コマを読みっぱなしにするか

        private sealed class Clip
        {
            public required string Name;
            public required CharaCacheMeta Meta;
            public Texture2D Texture;
            public int Uploaded = -1;   // 今テクスチャに入っているコマ
            public readonly ConcurrentDictionary<int, byte[]> Decoded = new();
        }

        private readonly string cacheFolder;
        private readonly Dictionary<string, Clip> clips = new();
        private readonly object wantLock = new();
        private Clip? wantClip;
        private int wantFrame;
        private Clip? lastDrawn;

        private readonly Thread worker;
        private readonly AutoResetEvent wake = new(false);
        private volatile bool stop;
        private readonly ConcurrentQueue<Clip> added = new();

        // ===== デバッグ記録（11/26 たけらい「ほかのキャラだと FPS が低い」の原因さがし） =====
        //   1秒ごとに events.csv へ「キャラ」の行を書く（デバッグ記録が ON のときだけ）：
        //     読んだコマの数・1コマを読む（PNG をほどく）のにかかった平均と最大・ファイルの大きさの平均・
        //     読みが間に合わず前のコマを出したフレームの数・テクスチャの上書きにかかった合計
        //   描く時間そのものは frames.csv の「キャラms」
        private long statDecodes, statDecodeTicks, statDecodeMaxTicks, statDecodeBytes;   // 裏のスレッドが足す（Interlocked）
        private int statDraws, statStale, statUploads;
        private long statUploadTicks;
        private long statSince = Stopwatch.GetTimestamp();
        private string statAnim = "";

        public CharaStream(string cacheFolder, int ahead = 10)
        {
            this.cacheFolder = cacheFolder;
            Ahead = Math.Max(2, ahead);
            worker = new Thread(Run) { IsBackground = true, Name = "CharaStream", Priority = ThreadPriority.BelowNormal };
            worker.Start();
        }

        /// <summary>そのアニメが使えるか（焼けていれば読む。メインスレッドで呼ぶ）</summary>
        public bool Has(string anim) => GetClip(anim) != null;

        /// <summary>アニメのコマ数（無ければ 0）</summary>
        public int FrameCount(string anim) => GetClip(anim)?.Meta.FrameCount ?? 0;

        /// <summary>元のコマ数（間引く前。時間で動かすアニメの長さを決めるのに使う）</summary>
        public int SourceFrameCount(string anim) => GetClip(anim)?.Meta.SourceFrameCount ?? 0;

        // 焼けていたらテクスチャを作って持つ（テクスチャはメインスレッドでしか作れない）
        private Clip? GetClip(string anim)
        {
            if (clips.TryGetValue(anim, out Clip? have)) return have;
            if (!CharaBaker.IsReady(anim)) return null;
            CharaCacheMeta? meta = CharaCache.ReadMeta(CharaCache.MetaPath(cacheFolder, anim));
            if (meta == null || meta.CellWidth <= 0 || meta.CellHeight <= 0) return null;

            Image blank = Raylib.GenImageColor(meta.CellWidth, meta.CellHeight, new Color(0, 0, 0, 0));
            Texture2D texture = Raylib.LoadTextureFromImage(blank);
            Raylib.UnloadImage(blank);
            if (texture.Id == 0) return null;
            Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);

            Clip clip = new() { Name = anim, Meta = meta, Texture = texture };
            clips[anim] = clip;
            added.Enqueue(clip);
            wake.Set();
            return clip;
        }

        /// <summary>
        /// コマを描く。(x, y) は元の絵（820×599）の左上、scale は画面での倍率（Config.json の値）。
        /// まだ読めていなければ、前に出したコマを出す。何も出せなければ false。
        /// </summary>
        public int SourceWidth(string anim)
        {
            int width = GetClip(anim)?.Meta.SourceWidth ?? 0;
            return width > 0 ? width : 820;
        }

        public bool Draw(string anim, int frame, float x, float y, float scale, Color tint, bool flip = false)
        {
            long t = DebugLog.Begin();
            try { return DrawCore(anim, frame, x, y, scale, tint, flip); }
            finally
            {
                DebugLog.End(PerfSection.Chara, t);
                if (DebugLog.Active) ReportStats();
            }
        }

        private bool DrawCore(string anim, int frame, float x, float y, float scale, Color tint, bool flip)
        {
            Clip? clip = GetClip(anim);
            if (clip == null) return false;
            int count = clip.Meta.FrameCount;
            frame = ((frame % count) + count) % count;

            lock (wantLock)
            {
                if (wantClip != clip || wantFrame != frame)
                {
                    wantClip = clip;
                    wantFrame = frame;
                    wake.Set();
                }
            }

            if (clip.Uploaded != frame && clip.Decoded.TryGetValue(frame, out byte[]? pixels))
            {
                long up = Stopwatch.GetTimestamp();
                Raylib.UpdateTexture(clip.Texture, pixels);
                clip.Uploaded = frame;
                statUploads++;
                statUploadTicks += Stopwatch.GetTimestamp() - up;
            }
            statDraws++;
            statAnim = anim;
            if (clip.Uploaded != frame) statStale++;   // 読みが間に合わず、前のコマのまま

            // このアニメのコマがまだ1つも入っていないときは、前のアニメの最後のコマを出しておく
            Clip? show = clip.Uploaded >= 0 ? clip : lastDrawn;
            if (show == null || show.Uploaded < 0) return false;
            lastDrawn = show;

            CharaCacheMeta m = show.Meta;
            // 焼いた倍率と違う倍率で出すときは、そのぶん伸び縮みさせる
            float ratio = m.Scale > 0 ? scale / m.Scale : 1f;
            Rectangle dest = new(
                MathF.Round(x + m.TrimX * scale), MathF.Round(y + m.TrimY * scale),
                m.CellWidth * ratio, m.CellHeight * ratio);
            if (flip) dest.X = MathF.Round(x + ((m.SourceWidth > 0 ? m.SourceWidth : 820) - m.TrimX) * scale - dest.Width);
            Raylib.DrawTexturePro(show.Texture, new Rectangle(0, 0, flip ? -m.CellWidth : m.CellWidth, m.CellHeight), dest, Vector2.Zero, 0f, tint);
            return true;
        }

        // ===== 裏のスレッド：PNG を読んで、色の並び(RGBA)にしておく =====

        private void Run()
        {
            List<Clip> all = new();
            while (!stop)
            {
                while (added.TryDequeue(out Clip? c)) all.Add(c);

                Clip? clip;
                int frame;
                lock (wantLock) { clip = wantClip; frame = wantFrame; }

                bool didWork = false;

                // ① 今のアニメの、今のコマから少し先まで
                if (clip != null)
                {
                    int count = clip.Meta.FrameCount;
                    for (int k = 0; k < Ahead && !stop; k++)
                    {
                        int i = (frame + k) % count;
                        if (clip.Decoded.ContainsKey(i)) continue;
                        Decode(clip, i);
                        didWork = true;
                        // 先を読んでいる間に、見たいコマが変わったら、そっちを先に
                        lock (wantLock) { if (wantClip != clip || wantFrame != frame) break; }
                    }
                }

                // ② どのアニメも、最初の数コマは読んでおく
                if (!didWork)
                {
                    foreach (Clip c in all)
                    {
                        for (int i = 0; i < Pinned && i < c.Meta.FrameCount; i++)
                        {
                            if (c.Decoded.ContainsKey(i)) continue;
                            Decode(c, i);
                            didWork = true;
                            break;
                        }
                        if (didWork) break;
                    }
                }

                // ③ 要らなくなったコマは捨てる（今のコマの少し前〜先までと、最初の数コマだけ残す）
                foreach (Clip c in all)
                {
                    foreach (int i in c.Decoded.Keys)
                    {
                        if (i < Pinned) continue;
                        if (c == clip && InWindow(i, frame, c.Meta.FrameCount)) continue;
                        c.Decoded.TryRemove(i, out _);
                    }
                }

                if (!didWork) wake.WaitOne(20);
            }
        }

        // 今のコマの2つ前〜Ahead先（ループするので、最後と最初はつながっている）
        private bool InWindow(int i, int frame, int count)
        {
            int d = ((i - frame) % count + count) % count;
            return d <= Ahead || d >= count - 2;
        }

        // 1秒ぶんをまとめて events.csv に書く（メインスレッドから）
        private void ReportStats()
        {
            long now = Stopwatch.GetTimestamp();
            double seconds = (now - statSince) / (double)Stopwatch.Frequency;
            if (seconds < 1.0) return;
            long decodes = Interlocked.Exchange(ref statDecodes, 0);
            long decodeTicks = Interlocked.Exchange(ref statDecodeTicks, 0);
            long decodeMax = Interlocked.Exchange(ref statDecodeMaxTicks, 0);
            long bytes = Interlocked.Exchange(ref statDecodeBytes, 0);
            double ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
            double avg = decodes > 0 ? ms(decodeTicks) / decodes : 0;
            string size = clips.TryGetValue(statAnim, out Clip? c) ? $"{c.Meta.CellWidth}x{c.Meta.CellHeight}" : "?";
            string folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(cacheFolder));
            DebugLog.Event("キャラ",
                $"{folder}/{statAnim} {size} 読んだコマ{decodes}（平均{avg:F1}ms・最大{ms(decodeMax):F1}ms・平均{(decodes > 0 ? bytes / decodes / 1024 : 0)}KB）" +
                $" 前のコマのまま{statStale}/{statDraws}フレーム 上書き{statUploads}回 {ms(statUploadTicks):F1}ms / {seconds:F1}秒",
                avg, statDraws > 0 ? 100.0 * statStale / statDraws : 0);
            statDraws = statStale = statUploads = 0;
            statUploadTicks = 0;
            statSince = now;
        }

        private unsafe void Decode(Clip clip, int index)
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                string path = CharaCache.FramePath(cacheFolder, clip.Name, index);
                byte[] data = File.ReadAllBytes(path);
                Interlocked.Add(ref statDecodeBytes, data.Length);
                Image img = Raylib.LoadImageFromMemory(".png", data);
                if (img.Width <= 0) { clip.Decoded[index] = new byte[clip.Meta.CellWidth * clip.Meta.CellHeight * 4]; return; }
                if (img.Format != PixelFormat.UncompressedR8G8B8A8) Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
                if (img.Width != clip.Meta.CellWidth || img.Height != clip.Meta.CellHeight)
                    Raylib.ImageResize(ref img, clip.Meta.CellWidth, clip.Meta.CellHeight);
                byte[] pixels = new byte[img.Width * img.Height * 4];
                Marshal.Copy((IntPtr)img.Data, pixels, 0, pixels.Length);
                Raylib.UnloadImage(img);
                clip.Decoded[index] = pixels;
            }
            catch
            {
                // 読めないコマは透明にしておく（何度も読みに行かないように）
                clip.Decoded[index] = new byte[clip.Meta.CellWidth * clip.Meta.CellHeight * 4];
            }
            finally
            {
                long took = Stopwatch.GetTimestamp() - started;
                Interlocked.Increment(ref statDecodes);
                Interlocked.Add(ref statDecodeTicks, took);
                long max;
                while (took > (max = Interlocked.Read(ref statDecodeMaxTicks))
                    && Interlocked.CompareExchange(ref statDecodeMaxTicks, took, max) != max) { }
            }
        }

        public void Dispose()
        {
            stop = true;
            wake.Set();
            worker.Join(2000);
            foreach (Clip clip in clips.Values)
                if (clip.Texture.Id != 0) Raylib.UnloadTexture(clip.Texture);
            clips.Clear();
            lastDrawn = null;
            wake.Dispose();
        }
    }
}

