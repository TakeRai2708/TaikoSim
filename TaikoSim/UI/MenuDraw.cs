using System.Numerics;
using Raylib_cs;
using TaikoSim.Graphics;

namespace TaikoSim.UI
{
    // モード選択・選曲画面で使う、描く道具
    //   文字はスキンの太字フォント(Simple なら Font/default.otf の Noto Sans CJK JP Bold)で描く
    //   スキンにフォントが無いときは、システム用のフォントで描く
    public static class MenuDraw
    {
        // ===== 文字 =====

        public static void Text(string text, float x, float y, float size, Color color) =>
            FontManager.DrawSkinMain(text, x, y, size, color);

        public static Vector2 Measure(string text, float size) => FontManager.MeasureSkinMain(text, size);

        // 左から、はみ出さない大きさで書く
        public static void TextFit(string text, float x, float y, float maxWidth, float size, Color color)
        {
            if (text.Length == 0) return;
            float width = Measure(text, size).X;
            if (width > maxWidth && width > 0) size *= maxWidth / width;
            Text(text, x, y, size, color);
        }

        // 四角の真ん中に、はみ出さない大きさで書く
        public static void Centered(string text, Rectangle area, float size, Color color)
        {
            if (text.Length == 0) return;
            Vector2 measured = Measure(text, size);
            if (measured.X > area.Width && measured.X > 0)
            {
                size *= area.Width / measured.X;
                measured = Measure(text, size);
            }
            Text(text, area.X + (area.Width - measured.X) / 2f, area.Y + (area.Height - measured.Y) / 2f, size, color);
        }

        // 縁の太さ = 文字の大きさ × この割合(どの大きさでも同じ見た目の太さになるように。28px なら約4.5px、60px なら約9.6px)
        //   10/30 に 0.11 → 0.16：TaikøNauts の動画(10/29 A.mp4 の難易度の板)で、サブタイトル「かめりあ」の縁が TaikoSim の約1.5倍あった
        //   (縁が細いと、白い字がうすく・灰色っぽく見える)
        public const float OutlineRatio = 0.16f;

        // 縁の太さ(文字の大きさから決める。小さい字でも 2px はつける。フォントに持っている距離より太くはしない)
        public static float OutlineWidth(float size) => Math.Min(Math.Max(2f, size * OutlineRatio), DynamicFont.MaxOutline(size));

        // 縁取りした文字を、四角の真ん中にはみ出さない大きさで書く
        //   縁は、まわりにぐるっと(円の上に)ずらした文字を描いて作る。8方向だとななめだけ飛び出してトゲトゲになるので、細かく回す
        public static void Outlined(string text, Rectangle area, float size, Color fill, Color outline)
        {
            if (text.Length == 0) return;
            Vector2 measured = Measure(text, size);
            float limit = area.Width - OutlineWidth(size) * 2;
            if (measured.X > limit && measured.X > 0)
            {
                size *= limit / measured.X;
                measured = Measure(text, size);
            }
            float x = area.X + (area.Width - measured.X) / 2f, y = area.Y + (area.Height - measured.Y) / 2f;
            OutlinedAt(text, x, y, size, fill, outline);
        }

        // 縁取りした文字を (x, y) から書く
        //   SDF フォント(DynamicFont)で、文字を太らせた縁 → 文字 の2回で描く(角は丸くなる)
        //   前は、まわりにずらして 25回描いていた(重いうえに、にじんだ文字を重ねるのでボヤけた)
        public static void OutlinedAt(string text, float x, float y, float size, Color fill, Color outline)
        {
            if (text.Length == 0) return;
            FontManager.DrawSkinMainOutlined(text, x, y, size, fill, outline, OutlineWidth(size));
        }

        // ===== 色・形 =====

        // 角の丸さ(半径 px)を、DrawRectangleRounded の roundness(短い辺に対する割合)に直す
        public static float Roundness(Rectangle area, float radius)
        {
            float shorter = Math.Min(area.Width, area.Height);
            return shorter <= 0 ? 0f : Math.Clamp(radius * 2f / shorter, 0f, 1f);
        }

        // 白に近づけた色(amount 0 そのまま → 1 白)
        public static Color Tint(Color color, float amount) => new Color(
            (byte)(color.R + (255 - color.R) * amount), (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount), color.A);

        // 透明度をかける
        public static Color Fade(Color color, float alpha) => new Color(color.R, color.G, color.B, (byte)(color.A * Math.Clamp(alpha, 0f, 1f)));

        public static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
    }
}
