using Raylib_cs;
using TakeTaikoSimulator.Configs;

namespace TakeTaikoSimulator.UI
{
    // UI の色（ダーク／ピンク）
    //   11/26 たけらい「UI色2つえらべるようにしない？」「この二色にしない？」→ UI全体。
    //   「初回セットアップで選べるようにしよ。その設定画面行くまではダークで」「ダークなんだけどさっきのグラデーションも適応しよう」
    //
    //   ・ダーク：今までの色（ほぼ黒に赤みを少し。朱色は選んでいる物・決定だけ）
    //   ・ピンク：落ち着いたさくら色の明るい背景に、こげ茶の字。選んでいる物・決定はくすんだ紅（11/26 作り直し）
    //   ・パネルとトーストは、どちらも斜めのグラデーション（11/26 のトーストの案：境目の線は横から 19°、ぼかし 320px）
    //
    //   色を使う所は Gui.TextColor などを今までどおり読むだけ（中身がテーマで変わる）
    public enum UiThemeKind { Dark, Pink }

    public sealed class UiPalette
    {
        public Color Text, SubText, MutedText;
        public Color Box, BoxHover, Fill, Accent, AccentDeep, Sakura;
        public Color Background, Panel, Border, Track;
        public Color PrimaryHover, TrackHover, BubbleText;
        public Color OnAccent;            // 朱色（アクセント）の上に乗せる字・つまみの色
        public Color KnobDot;             // スライダーの白いつまみの真ん中の点
        public Color PanelFrom, PanelTo;  // パネルの斜めグラデーション（左上 → 右下）
        public Color BackFrom, BackTo;    // 背景（パネルのまわり・左の帯・上下の帯）の斜めグラデーション
        public Color ToastFrom, ToastTo;  // トーストの斜めグラデーション
        public Color ToastText, ToastTextOutline, ToastBar, ToastBorder;
        public bool ToastBoldFont;        // トーストの字をスキンの太字（縁つき）にするか
        public Color Card, CardLine;      // 設定のカード（行をまとめた箱）と、行と行の間の線
        public Color Success, Error, Neutral, Pending;   // お知らせの字の色（うまくいった・失敗・ふつう・まだ）
        public Color KnobEdge;            // 白いつまみのふち（明るい背景で、つまみが消えないように）
    }

    public static class UiTheme
    {
        public static readonly UiPalette Dark = new()
        {
            Text = new(252, 238, 242, 255), SubText = new(191, 165, 174, 255), MutedText = new(125, 103, 112, 255),
            Box = new(38, 23, 29, 255), BoxHover = new(51, 32, 42, 255),
            Fill = new(226, 58, 94, 255), Accent = new(226, 58, 94, 255), AccentDeep = new(163, 4, 27, 255), Sakura = new(249, 188, 206, 255),
            Background = new(18, 10, 13, 255), Panel = new(27, 16, 20, 255), Border = new(67, 42, 53, 255), Track = new(74, 50, 60, 255),
            PrimaryHover = new(240, 96, 127, 255), TrackHover = new(92, 62, 74, 255), BubbleText = new(27, 16, 20, 255),
            OnAccent = new(255, 255, 255, 255), KnobDot = new(226, 58, 94, 255),
            // 11/26 たけらい「ダークのグラデーションわかりずらい」「トーストみたいな感じにして」→ パネルはトーストと同じ色。背景は少し暗く
            PanelFrom = new(20, 11, 16, 255), PanelTo = new(130, 32, 66, 255),
            BackFrom = new(12, 7, 10, 255), BackTo = new(98, 24, 50, 255),
            ToastFrom = new(20, 11, 16, 255), ToastTo = new(130, 32, 66, 255),
            ToastText = new(252, 238, 242, 255), ToastTextOutline = new(0, 0, 0, 0), ToastBar = new(226, 58, 94, 255), ToastBorder = new(90, 55, 70, 255),
            ToastBoldFont = false,
            Card = new(33, 20, 25, 255), CardLine = new(58, 37, 48, 255),
            Success = new(110, 220, 130, 255), Error = new(255, 110, 100, 255), Neutral = new(205, 190, 196, 255), Pending = new(125, 103, 112, 255),
            KnobEdge = new(0, 0, 0, 0),
        };

        // 11/26 たけらい「新しい色見ずらい」「俺のアイコンの色に合うように」→ A（明るいさくら）→「もう少し落ち着いたさくら色」
        //   アイコン（狐のお面の子）の顔のうすいピンクを、くすませた色。字はこげ茶、決定・選んでいる物は、くすんだ紅
        //   明るい背景なので、白い字は使わない（決定ボタンの上の字だけ白）
        public static readonly UiPalette Pink = new()
        {
            Text = new(66, 42, 49, 255), SubText = new(128, 98, 107, 255), MutedText = new(160, 132, 140, 255),
            Box = new(247, 238, 241, 255), BoxHover = new(238, 223, 228, 255),
            Fill = new(194, 88, 112, 255), Accent = new(194, 88, 112, 255), AccentDeep = new(160, 62, 86, 255), Sakura = new(194, 88, 112, 255),
            Background = new(243, 233, 236, 255), Panel = new(249, 242, 244, 255), Border = new(214, 186, 195, 255), Track = new(228, 210, 216, 255),
            PrimaryHover = new(208, 108, 130, 255), TrackHover = new(218, 196, 204, 255), BubbleText = new(66, 42, 49, 255),
            OnAccent = new(255, 255, 255, 255), KnobDot = new(194, 88, 112, 255),
            PanelFrom = new(249, 242, 244, 255), PanelTo = new(236, 218, 224, 255),
            BackFrom = new(243, 233, 236, 255), BackTo = new(229, 207, 214, 255),
            ToastFrom = new(250, 243, 245, 255), ToastTo = new(234, 214, 221, 255),
            ToastText = new(66, 42, 49, 255), ToastTextOutline = new(0, 0, 0, 0), ToastBar = new(194, 88, 112, 255), ToastBorder = new(214, 186, 195, 255),
            ToastBoldFont = false,
            Card = new(253, 249, 250, 255), CardLine = new(234, 219, 224, 255),
            Success = new(46, 140, 80, 255), Error = new(196, 52, 52, 255), Neutral = new(128, 98, 107, 255), Pending = new(160, 132, 140, 255),
            KnobEdge = new(205, 178, 187, 255),
        };

        public static UiThemeKind Current { get; private set; } = UiThemeKind.Dark;
        public static UiPalette P { get; private set; } = Dark;

        // 設定ファイルの名前 → テーマ（知らない名前はダーク）
        public static UiThemeKind Parse(string? name) =>
            string.Equals(name, nameof(UiThemeKind.Pink), StringComparison.OrdinalIgnoreCase) ? UiThemeKind.Pink : UiThemeKind.Dark;

        public static void Set(UiThemeKind kind)
        {
            Current = kind;
            P = kind == UiThemeKind.Pink ? Pink : Dark;
        }

        // 設定ファイル（GameConfig.UiTheme）に合わせる。設定を読んだあと・変えたあとに呼ぶ
        public static void Apply(GameConfig config) => Set(Parse(config.UiTheme));

        // ===== 斜めのグラデーション =====
        //   境目の線は、四角の真ん中を通って右上がり（横から AngleDeg 度）。線の左上が from、線をまたいで右下へ blur px で to になる
        //   トーストは BlurPx（320px）。パネルは四角の全体（左上の角 → 右下の角）でじわっと変える
        //   （11/26 たけらい「グラデーションは全体的にでいいよ。ちょっと違和感ある」：大きいパネルに 320px だと真ん中に帯が見えた）
        public const float AngleDeg = 19f, BlurPx = 320f;
        private const float WholeBlur = 0f;   // 0：四角の全体を使う

        private static readonly Dictionary<(int W, int H, int R, uint From, uint To, int Blur), Texture2D> cache = new();
        private static readonly LinkedList<(int, int, int, uint, uint, int)> order = new();
        private const int CacheLimit = 24;   // トーストは字の長さで幅が変わるので、古いものから捨てる

        /// <summary>角の丸い四角を、斜めのグラデーションでぬる（ふちの外は透明。alpha で全体をうすくできる）</summary>
        public static void FillGradient(Rectangle area, float radius, Color from, Color to, float alpha = 1f, float blur = BlurPx) =>
            FillGradientPart(area, area, radius, from, to, alpha, blur);

        /// <summary>大きい四角 whole のグラデーションのうち、part の所だけをぬる（パネルの上に重ねる帯などで、色の続きをそろえる）</summary>
        public static void FillPanelPart(Rectangle whole, Rectangle part, float alpha = 1f) =>
            FillGradientPart(whole, part, 0f, P.PanelFrom, P.PanelTo, alpha, WholeBlur);

        private static void FillGradientPart(Rectangle area, Rectangle part, float radius, Color from, Color to, float alpha, float blur)
        {
            int w = Math.Max(1, (int)MathF.Round(area.Width)), h = Math.Max(1, (int)MathF.Round(area.Height));
            int r = Math.Clamp((int)MathF.Round(radius), 0, Math.Min(w, h) / 2);
            int b = (int)MathF.Round(blur);
            var key = (w, h, r, Pack(from), Pack(to), b);
            if (!cache.TryGetValue(key, out Texture2D texture))
            {
                texture = Make(w, h, r, from, to, b);
                if (texture.Id == 0) { Raylib.DrawRectangleRec(part, Gui.WithAlpha(from, alpha)); return; }
                cache[key] = texture;
                order.AddLast(key);
                while (order.Count > CacheLimit)
                {
                    var old = order.First!.Value;
                    order.RemoveFirst();
                    if (cache.Remove(old, out Texture2D gone)) Raylib.UnloadTexture(gone);
                }
            }
            float sx = w / Math.Max(1f, area.Width), sy = h / Math.Max(1f, area.Height);
            Rectangle source = new((part.X - area.X) * sx, (part.Y - area.Y) * sy, part.Width * sx, part.Height * sy);
            Raylib.DrawTexturePro(texture, source, part, System.Numerics.Vector2.Zero, 0f, Gui.WithAlpha(Color.White, alpha));
        }

        /// <summary>今のテーマの背景（画面の全体で、左上の角 → 右下の角）。11/26 たけらい：パネルのまわりもグラデーションに</summary>
        public static void FillBackground(Rectangle area) => FillGradientPart(area, area, 0f, P.BackFrom, P.BackTo, 1f, WholeBlur);

        /// <summary>背景 whole のうち part の所だけ（左の帯・上下の帯を、背景の続きの色でぬる）</summary>
        public static void FillBackgroundPart(Rectangle whole, Rectangle part) => FillGradientPart(whole, part, 0f, P.BackFrom, P.BackTo, 1f, WholeBlur);

        /// <summary>今のテーマのパネル（斜めグラデーション）</summary>
        public static void FillPanel(Rectangle area, float radius, float alpha = 1f) => FillGradient(area, radius, P.PanelFrom, P.PanelTo, alpha, WholeBlur);

        private static uint Pack(Color c) => ((uint)c.R << 24) | ((uint)c.G << 16) | ((uint)c.B << 8) | c.A;

        private static unsafe Texture2D Make(int w, int h, int r, Color from, Color to, int blur)
        {
            Image image = Raylib.GenImageColor(w, h, new Color(0, 0, 0, 0));
            if (image.Data == null) return default;
            Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
            byte* px = (byte*)image.Data;
            float a = AngleDeg * MathF.PI / 180f;
            float nx = MathF.Sin(a), ny = MathF.Cos(a);   // 線に垂直で、右下向き
            float cx = w / 2f, cy = h / 2f;
            // 全体を使うとき：左上の角で 0、右下の角で 1 になる幅（四角を線に垂直な向きに測った長さ）。なめらかさ優先で曲線はかけない
            bool whole = blur <= 0;
            float span = whole ? Math.Max(1f, w * nx + h * ny) : blur;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float px0 = x + 0.5f, py0 = y + 0.5f;
                    float t = Math.Clamp(((px0 - cx) * nx + (py0 - cy) * ny) / span + 0.5f, 0f, 1f);
                    if (!whole) t = t * t * (3f - 2f * t);
                    float cover = Coverage(px0, py0, w, h, r);
                    int i = (y * w + x) * 4;
                    px[i] = (byte)(from.R + (to.R - from.R) * t);
                    px[i + 1] = (byte)(from.G + (to.G - from.G) * t);
                    px[i + 2] = (byte)(from.B + (to.B - from.B) * t);
                    px[i + 3] = (byte)(255f * cover * ((from.A + (to.A - from.A) * t) / 255f));
                }
            }
            Texture2D texture = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            if (texture.Id != 0)
            {
                Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
                Raylib.SetTextureWrap(texture, TextureWrap.Clamp);
            }
            return texture;
        }

        // 角の丸い四角の中にどれだけ入っているか（ふちは 1px でなめらかに）
        private static float Coverage(float x, float y, int w, int h, int r)
        {
            if (r <= 0) return 1f;
            float qx = Math.Max(Math.Max(r - x, x - (w - r)), 0f);
            float qy = Math.Max(Math.Max(r - y, y - (h - r)), 0f);
            if (qx <= 0f || qy <= 0f) return 1f;
            float d = MathF.Sqrt(qx * qx + qy * qy) - r;   // 丸の外側までの距離
            return Math.Clamp(0.5f - d, 0f, 1f);
        }

        /// <summary>スキンを変えたとき・終了するときに作った絵を捨てる</summary>
        public static void Clear()
        {
            foreach (Texture2D t in cache.Values) Raylib.UnloadTexture(t);
            cache.Clear();
            order.Clear();
        }
    }
}
