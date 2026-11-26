using Raylib_cs;
using TakeTaikoSimulator.Configs;

namespace TakeTaikoSimulator.UI
{
    // UI の色（ダーク／ピンク）
    //   11/26 たけらい「UI色2つえらべるようにしない？」「この二色にしない？」→ UI全体。
    //   「初回セットアップで選べるようにしよ。その設定画面行くまではダークで」「ダークなんだけどさっきのグラデーションも適応しよう」
    //
    //   ・ダーク：今までの色（ほぼ黒に赤みを少し。朱色は選んでいる物・決定だけ）
    //   ・ピンク：深いピンクの背景に白い文字。選んでいる物・決定は「白の上にピンクの字」に反転する（ピンクの上にピンクだと見えないので）
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
        public Color ToastFrom, ToastTo;  // トーストの斜めグラデーション
        public Color ToastText, ToastTextOutline, ToastBar, ToastBorder;
        public bool ToastBoldFont;        // トーストの字をスキンの太字（縁つき）にするか
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
            PanelFrom = new(22, 12, 17, 255), PanelTo = new(66, 22, 40, 255),
            ToastFrom = new(20, 11, 16, 255), ToastTo = new(130, 32, 66, 255),
            ToastText = new(252, 238, 242, 255), ToastTextOutline = new(0, 0, 0, 0), ToastBar = new(226, 58, 94, 255), ToastBorder = new(90, 55, 70, 255),
            ToastBoldFont = false,
        };

        public static readonly UiPalette Pink = new()
        {
            Text = new(255, 255, 255, 255), SubText = new(255, 208, 222, 255), MutedText = new(240, 168, 190, 255),
            Box = new(176, 44, 82, 255), BoxHover = new(194, 58, 96, 255),
            Fill = new(255, 255, 255, 255), Accent = new(255, 255, 255, 255), AccentDeep = new(255, 214, 226, 255), Sakura = new(255, 255, 255, 255),
            Background = new(110, 18, 44, 255), Panel = new(150, 30, 64, 255), Border = new(225, 120, 150, 255), Track = new(120, 22, 50, 255),
            PrimaryHover = new(255, 236, 242, 255), TrackHover = new(136, 30, 62, 255), BubbleText = new(150, 30, 64, 255),
            OnAccent = new(200, 30, 72, 255), KnobDot = new(200, 30, 72, 255),
            PanelFrom = new(150, 30, 64, 255), PanelTo = new(220, 80, 115, 255),
            ToastFrom = new(196, 26, 66, 255), ToastTo = new(252, 135, 162, 255),
            ToastText = new(255, 255, 255, 255), ToastTextOutline = new(120, 10, 35, 255), ToastBar = new(255, 255, 255, 255), ToastBorder = new(255, 255, 255, 255),
            ToastBoldFont = true,
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
        //   境目の線は、四角の真ん中を通って右上がり（横から AngleDeg 度）。線の左上が from、線をまたいで右下へ BlurPx で to になる
        public const float AngleDeg = 19f, BlurPx = 320f;

        private static readonly Dictionary<(int W, int H, int R, uint From, uint To), Texture2D> cache = new();
        private static readonly LinkedList<(int, int, int, uint, uint)> order = new();
        private const int CacheLimit = 24;   // トーストは字の長さで幅が変わるので、古いものから捨てる

        /// <summary>角の丸い四角を、斜めのグラデーションでぬる（ふちの外は透明。alpha で全体をうすくできる）</summary>
        public static void FillGradient(Rectangle area, float radius, Color from, Color to, float alpha = 1f) =>
            FillGradientPart(area, area, radius, from, to, alpha);

        /// <summary>大きい四角 whole のグラデーションのうち、part の所だけをぬる（パネルの上に重ねる帯などで、色の続きをそろえる）</summary>
        public static void FillPanelPart(Rectangle whole, Rectangle part, float alpha = 1f) =>
            FillGradientPart(whole, part, 0f, P.PanelFrom, P.PanelTo, alpha);

        private static void FillGradientPart(Rectangle area, Rectangle part, float radius, Color from, Color to, float alpha)
        {
            int w = Math.Max(1, (int)MathF.Round(area.Width)), h = Math.Max(1, (int)MathF.Round(area.Height));
            int r = Math.Clamp((int)MathF.Round(radius), 0, Math.Min(w, h) / 2);
            var key = (w, h, r, Pack(from), Pack(to));
            if (!cache.TryGetValue(key, out Texture2D texture))
            {
                texture = Make(w, h, r, from, to);
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

        /// <summary>今のテーマのパネル（斜めグラデーション）</summary>
        public static void FillPanel(Rectangle area, float radius, float alpha = 1f) => FillGradient(area, radius, P.PanelFrom, P.PanelTo, alpha);

        private static uint Pack(Color c) => ((uint)c.R << 24) | ((uint)c.G << 16) | ((uint)c.B << 8) | c.A;

        private static unsafe Texture2D Make(int w, int h, int r, Color from, Color to)
        {
            Image image = Raylib.GenImageColor(w, h, new Color(0, 0, 0, 0));
            if (image.Data == null) return default;
            Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
            byte* px = (byte*)image.Data;
            float a = AngleDeg * MathF.PI / 180f;
            float nx = MathF.Sin(a), ny = MathF.Cos(a);   // 線に垂直で、右下向き
            float cx = w / 2f, cy = h / 2f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float px0 = x + 0.5f, py0 = y + 0.5f;
                    float t = Math.Clamp(((px0 - cx) * nx + (py0 - cy) * ny) / BlurPx + 0.5f, 0f, 1f);
                    t = t * t * (3f - 2f * t);
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
