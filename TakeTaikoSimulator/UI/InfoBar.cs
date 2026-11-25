using Raylib_cs;
using TakeTaikoSimulator.Graphics;
using static TakeTaikoSimulator.UI.MenuDraw;

namespace TakeTaikoSimulator.UI
{
    // お知らせの帯（「今回のプレイは記録されません」）
    //
    //   11/12 たけらい「オート演奏したときの記録は保存されません見たいな表示出したい」
    //   TaikøNauts の録画（2026-10-29_09-28-06.mp4、オートで演奏。87.48〜94.13秒）で測った：
    //     ・絵は A-Style の 04.EnsoGame/Information_Bar.png（940×110。黒いふち・白い線・灰色の帯）
    //     ・左上は (998, 119)。右の端は画面の少し内側（x 1913）なので、横を 19px 縮めて描く（角がつぶれないように3つに切る）
    //     ・字は白に黒いふち、帯のまんなか。大きさは約23px（33文字で幅 約710px）
    //     ・パッと出て 6.6秒ほど出たあと、0.05秒で消える
    //     ・文は「特定の演奏オプションが使用されたため、今回のプレイは記録されません。」
    //   出す所：演奏画面（オートのとき、始まってから 6.6秒）・リザルト（オートのとき、ずっと）・
    //           演奏オプションの板（オートプレイを「する」にしたとき、4秒）
    //   絵が無いスキンでは、同じ形をプログラムで描く
    public static class InfoBar
    {
        private const string Image = "Image/04.EnsoGame/Information_Bar.png";
        public const string NotSavedText = "特定の演奏オプションが使用されたため、今回のプレイは記録されません。";
        public const string AutoOnText = "オートプレイ中は、プレイが記録されません。";

        // 演奏画面・リザルトでの置き場所（絵の左上）と、出しておく秒
        public const float PlayX = 998f, PlayY = 119f;
        public const float PlaySeconds = 6.6f, FadeSeconds = 0.05f;

        private const float ImageWidth = 940f, ImageHeight = 110f;
        private const float DrawWidth = 921f;                       // 絵の幅（余白こみ）。940 から 19px 縮めた
        private const float PadLeft = 18f, PadRight = 6f, PadTop = 17f, VisibleHeight = 79f;   // 絵の中の、帯が見えている所
        private const float TextSize = 23f, TextRaise = 4f;

        /// <summary>帯を、絵の左上 (x, y) に描く。alpha 0〜1</summary>
        public static void Draw(float x, float y, string text, float alpha = 1f)
        {
            if (alpha <= 0f) return;
            Rectangle dest = new(x, y, DrawWidth, ImageHeight);
            Rectangle visible = new(x + PadLeft, y + PadTop, DrawWidth - PadLeft - PadRight, VisibleHeight);
            if (!MenuSkin.NPatch(Image, dest, 70, Fade(Color.White, alpha)))
            {
                SmoothShapes.Rounded(visible, 1f, 16, Fade(Color.Black, alpha));
                Rectangle line = new(visible.X + 5, visible.Y + 5, visible.Width - 10, visible.Height - 10);
                SmoothShapes.Rounded(line, 1f, 16, Fade(Color.White, alpha));
                Rectangle inner = new(line.X + 4, line.Y + 4, line.Width - 8, line.Height - 8);
                SmoothShapes.Rounded(inner, 1f, 16, Fade(new Color(62, 62, 62, 255), alpha));
            }
            // 11/25 たけらいのスクショ：文字の真ん中が帯の中の黒い所の真ん中より 4.5px 下だった → 4px 上げる
            Rectangle area = new(visible.X + 40, visible.Y - TextRaise, visible.Width - 80, visible.Height);
            Outlined(text, area, TextSize, Fade(Color.White, alpha), Fade(Color.Black, alpha));
        }

        /// <summary>出してからの秒 → 濃さ（seconds 秒出して、FadeSeconds で消える）</summary>
        public static float AlphaAt(float age, float seconds) =>
            age < 0f ? 0f : Math.Clamp((seconds - age) / FadeSeconds, 0f, 1f);
    }
}
