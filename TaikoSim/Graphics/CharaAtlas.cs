using System.Text.Json;
using Raylib_cs;
using TaikoSim.Configs;

namespace TaikoSim.Graphics
{
    // どんちゃんのアニメを「アトラス(コマを並べた1枚絵)」にして持つ
    //
    //   A-Style のどんちゃんは 1コマ 820×599 で、1アニメが最大478コマある。
    //   そのまま読むと `normal` だけで 883MB、演奏画面ぶん全部で 4.7GB になって載らない。
    //   なので初回だけ時間をかけて、下の3つをやった画像を exe の横の Cache フォルダに焼いておく。
    //     ① 余白カット  …… 中身は 820×599 のうち 346×284 くらいしかない(アニメごとに実測する)
    //     ② 縮小        …… 画面に出す倍率(演奏なら 0.81)で焼いてしまう
    //     ③ コマ間引き  …… 2コマに1コマ(30fps 相当)。マスコットなので見て分からない
    //   これで `normal` が 883MB → 71MB になる。2回目からはアトラスPNGを読むだけなので速い。
    //
    //   アトラス1枚は 4096×4096 まで。GPU の上限は今どき 16384 だが、古いものだと 4096 なので安全側にする。
    //   入りきらないぶんは2枚目・3枚目に分ける。
    //
    //   Raylib の Image 系(LoadImage / ImageCrop / ImageResize / ImageDraw)は CPU だけで動くので、
    //   焼く作業は別スレッドでやれる(画面が固まらない)。テクスチャにするのはメインスレッドだけ。
    public static class CharaAtlas
    {
        public const int MaxPageSize = 4096;
        private const int MetaVersion = 1;

        // 余白を切るときのアルファのしきい値と、まわりに足す余裕(縮小のにじみ対策)
        private const float AlphaThreshold = 0.02f;
        private const int TrimMargin = 2;

        public static string CacheRoot => Path.Combine(AppContext.BaseDirectory, "Cache", "Chara");

        // そのアニメのキャッシュの置き場所(スキンごとに分ける。スキンを変えたら作り直しになる)
        public static string FolderFor(string skinName, string charaFolder) =>
            Path.Combine(CacheRoot, Safe(skinName), Safe(charaFolder));

        private static string Safe(string name)
        {
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            return name.Length == 0 ? "_" : name;
        }

        // ===== 焼く(別スレッドでよい。GL を触らない) =====

        /// <summary>
        /// コマPNGの入ったフォルダからアトラスを焼く。すでに新しいキャッシュがあれば何もしない。
        /// progress には 0〜1 が入る。やめたいときは cancel を true にする。
        /// </summary>
        public static bool BuildIfNeeded(string sourceFolder, string cacheFolder, string animName,
                                         float scale, int frameStep, Action<float>? progress = null,
                                         Func<bool>? cancel = null)
        {
            if (!Directory.Exists(sourceFolder)) return false;
            frameStep = Math.Max(1, frameStep);
            scale = Math.Clamp(scale, 0.05f, 4f);

            string metaPath = Path.Combine(cacheFolder, animName + ".json");
            int sourceCount = CountFrames(sourceFolder);
            if (sourceCount == 0) return false;
            long stamp = SourceStamp(sourceFolder, sourceCount);

            // すでにある？(元が差し替わっていたら作り直す)
            if (ReadMeta(metaPath) is { } have
                && have.Version == MetaVersion && have.SourceFrameCount == sourceCount
                && have.SourceStamp == stamp && Math.Abs(have.Scale - scale) < 0.0001f
                && have.FrameStep == frameStep && AllPagesExist(cacheFolder, animName, have.Pages))
                return true;

            Directory.CreateDirectory(cacheFolder);

            // ① 全コマを見て「中身の入っている範囲」を求める
            //    一部だけ見て決めると、見ていないコマがはみ出して切れるので、ここは手を抜かない
            Rectangle box = default;
            bool any = false;
            for (int i = 0; i < sourceCount; i++)
            {
                if (cancel?.Invoke() == true) return false;
                string path = Path.Combine(sourceFolder, i + ".png");
                Image img = Raylib.LoadImage(path);
                if (img.Width > 0)
                {
                    Rectangle b = Raylib.GetImageAlphaBorder(img, AlphaThreshold);
                    if (b.Width > 0 && b.Height > 0) { box = any ? Union(box, b) : b; any = true; }
                    Raylib.UnloadImage(img);
                }
                progress?.Invoke(i / (float)sourceCount * 0.35f);
            }
            if (!any) return false;

            // 元画像の大きさ(位置合わせに要る)を1コマ目から取る
            Image first = Raylib.LoadImage(Path.Combine(sourceFolder, "0.png"));
            int srcW = first.Width, srcH = first.Height;
            Raylib.UnloadImage(first);
            if (srcW <= 0 || srcH <= 0) return false;

            // 余裕を足して、元画像からはみ出さないように丸める
            int trimX = Math.Max(0, (int)box.X - TrimMargin);
            int trimY = Math.Max(0, (int)box.Y - TrimMargin);
            int trimW = Math.Min(srcW - trimX, (int)box.Width + TrimMargin * 2);
            int trimH = Math.Min(srcH - trimY, (int)box.Height + TrimMargin * 2);
            if (trimW <= 0 || trimH <= 0) return false;

            int cellW = Math.Max(1, (int)MathF.Round(trimW * scale));
            int cellH = Math.Max(1, (int)MathF.Round(trimH * scale));
            if (cellW > MaxPageSize || cellH > MaxPageSize) return false; // 1コマが1枚に入らない

            int keep = (sourceCount + frameStep - 1) / frameStep;
            int cols = Math.Max(1, MaxPageSize / cellW);
            int rowsPerPage = Math.Max(1, MaxPageSize / cellH);
            int perPage = cols * rowsPerPage;
            int pages = (keep + perPage - 1) / perPage;

            // ② コマを切って縮めて、アトラスに並べる
            Rectangle crop = new(trimX, trimY, trimW, trimH);
            for (int page = 0; page < pages; page++)
            {
                int inThisPage = Math.Min(perPage, keep - page * perPage);
                int rows = (inThisPage + cols - 1) / cols;
                Image sheet = Raylib.GenImageColor(cols * cellW, rows * cellH, new Color(0, 0, 0, 0));

                for (int slot = 0; slot < inThisPage; slot++)
                {
                    if (cancel?.Invoke() == true) { Raylib.UnloadImage(sheet); return false; }

                    int keepIndex = page * perPage + slot;
                    Image img = Raylib.LoadImage(Path.Combine(sourceFolder, keepIndex * frameStep + ".png"));
                    if (img.Width > 0)
                    {
                        Raylib.ImageCrop(ref img, crop);
                        Raylib.ImageResize(ref img, cellW, cellH);
                        Rectangle dest = new(slot % cols * cellW, slot / cols * cellH, cellW, cellH);
                        Raylib.ImageDraw(ref sheet, img, new Rectangle(0, 0, cellW, cellH), dest, Color.White);
                        Raylib.UnloadImage(img);
                    }
                    progress?.Invoke(0.35f + (keepIndex + 1) / (float)keep * 0.6f);
                }

                Raylib.ExportImage(sheet, PagePath(cacheFolder, animName, page));
                Raylib.UnloadImage(sheet);
            }

            WriteMeta(metaPath, new CharaAtlasMeta
            {
                Version = MetaVersion,
                FrameCount = keep,
                SourceFrameCount = sourceCount,
                FrameStep = frameStep,
                CellWidth = cellW,
                CellHeight = cellH,
                Columns = cols,
                RowsPerPage = rowsPerPage,
                Pages = pages,
                Scale = scale,
                TrimX = trimX,
                TrimY = trimY,
                SourceWidth = srcW,
                SourceHeight = srcH,
                SourceStamp = stamp,
            });
            progress?.Invoke(1f);
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

        private static string PagePath(string cacheFolder, string animName, int page) =>
            Path.Combine(cacheFolder, $"{animName}_{page}.png");

        private static bool AllPagesExist(string cacheFolder, string animName, int pages)
        {
            if (pages <= 0) return false;
            for (int i = 0; i < pages; i++) if (!File.Exists(PagePath(cacheFolder, animName, i))) return false;
            return true;
        }

        // ===== 読む(メインスレッドだけ。テクスチャを作るので) =====

        /// <summary>焼いてあるアトラスを読む。無ければ null。</summary>
        public static CharaClip? Load(string cacheFolder, string animName)
        {
            CharaAtlasMeta? meta = ReadMeta(Path.Combine(cacheFolder, animName + ".json"));
            if (meta == null || meta.Version != MetaVersion || meta.FrameCount <= 0 || meta.Pages <= 0) return null;

            Texture2D[] pages = new Texture2D[meta.Pages];
            for (int i = 0; i < meta.Pages; i++)
            {
                Texture2D t = Raylib.LoadTexture(PagePath(cacheFolder, animName, i));
                if (t.Id == 0)
                {
                    for (int j = 0; j < i; j++) Raylib.UnloadTexture(pages[j]);
                    return null;
                }
                pages[i] = t;
            }
            return new CharaClip(meta, pages);
        }

        private static CharaAtlasMeta? ReadMeta(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.CharaAtlasMeta);
            }
            catch { return null; }
        }

        private static void WriteMeta(string path, CharaAtlasMeta meta)
        {
            try { File.WriteAllText(path, JsonSerializer.Serialize(meta, AppJsonContext.Default.CharaAtlasMeta)); }
            catch { /* 書けなくても次回また焼くだけ */ }
        }
    }

    // 焼いたアニメ1本。コマ番号から「どのページのどこか」を返す
    public sealed class CharaClip
    {
        private readonly CharaAtlasMeta meta;
        private readonly Texture2D[] pages;

        public CharaClip(CharaAtlasMeta meta, Texture2D[] pages) { this.meta = meta; this.pages = pages; }

        public int FrameCount => meta.FrameCount;
        /// <summary>焼いたときの倍率。画面に出す倍率がこれと同じなら等倍で描ける</summary>
        public float BakedScale => meta.Scale;
        /// <summary>元画像(820×599 など)の中での切り出し位置。位置合わせに使う</summary>
        public int TrimX => meta.TrimX;
        public int TrimY => meta.TrimY;
        public int SourceWidth => meta.SourceWidth;
        public int SourceHeight => meta.SourceHeight;

        /// <summary>コマ番号 → 描くテクスチャと、その中の場所</summary>
        public (Texture2D Texture, Rectangle Source) Frame(int index)
        {
            int i = meta.FrameCount <= 0 ? 0 : ((index % meta.FrameCount) + meta.FrameCount) % meta.FrameCount;
            int perPage = Math.Max(1, meta.Columns * meta.RowsPerPage);
            int page = Math.Clamp(i / perPage, 0, pages.Length - 1);
            int slot = i - page * perPage;
            Rectangle src = new(slot % meta.Columns * meta.CellWidth, slot / meta.Columns * meta.CellHeight,
                                meta.CellWidth, meta.CellHeight);
            return (pages[page], src);
        }

        public void Unload()
        {
            foreach (Texture2D t in pages) if (t.Id != 0) Raylib.UnloadTexture(t);
        }
    }

    // アトラスのメタ情報(キャッシュの <アニメ名>.json)
    public sealed class CharaAtlasMeta
    {
        public int Version { get; set; }
        public int FrameCount { get; set; }        // キャッシュに入っているコマ数(間引き後)
        public int SourceFrameCount { get; set; }  // 元のコマ数
        public int FrameStep { get; set; } = 1;    // 何コマに1コマ残したか
        public int CellWidth { get; set; }
        public int CellHeight { get; set; }
        public int Columns { get; set; }
        public int RowsPerPage { get; set; }
        public int Pages { get; set; }
        public float Scale { get; set; } = 1f;
        public int TrimX { get; set; }
        public int TrimY { get; set; }
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
        public long SourceStamp { get; set; }
    }
}
