using Raylib_cs;
using TaikoSim.Configs;
using TaikoSim.Graphics;

namespace TaikoSim.UI
{
    // 設定の説明文(初回セットアップと設定パネルで同じ文を使う)
    // 説明を直したいときは、ここだけ書き換えればOK
    public static class SettingTexts
    {
        // ===== 説明文 =====

        public const string Window = "ウィンドウで表示します。ほかの作業をしながら遊びたい人向け。";

        public const string Borderless = "画面いっぱいの枠なしウィンドウで表示します。";
        public const string BorderlessNote = "※ 解像度はモニターに合わせます";

        public const string Exclusive = "画面合成（DWM）を通さずに表示します。PCによっては遅れやカクつきが減ります。かわりに Alt＋Tab が遅くなります。";
        public const string ExclusiveNote = "※ 選んだ解像度にモニターを切り替えます";

        public const string VSync = "画面のちらつきを防ぎます。かわりに少し遅延が増えることがあります。";

        public const string TargetFps = "0 で無制限。垂直同期オンのときは、モニターのリフレッシュレート（Hz）に合わせます。";

        // フルスクリーン方式のプルダウンの選択肢(FullscreenType の順番)
        public static readonly string[] FullscreenTypeNames = { "ボーダーレス（おすすめ）", "排他フルスクリーン" };

        // フルスクリーン方式の説明(本文と※の2つ)
        public static string[] GetFullscreenTypeTexts(FullscreenType type)
        {
            return type == FullscreenType.Borderless
                ? new[] { Borderless, BorderlessNote }
                : new[] { Exclusive, ExclusiveNote };
        }

        // ===== 折り返して描く =====

        // 行の頭に来ないようにする文字(「。」が次の行の先頭に来ると読みにくいので)
        private const string NoLineStart = "。、，．）」』！？ー";

        // 一度折り返した結果を覚えておく(毎フレーム計算しなくていいように)
        private static readonly Dictionary<string, List<string>> wrapCache = new();

        // 幅に収まるように、1文字ずつ見て行を分ける
        public static List<string> Wrap(string text, float width, float size)
        {
            string cacheKey = $"{size}|{width}|{text}";
            if (wrapCache.TryGetValue(cacheKey, out List<string>? cached)) return cached;

            List<string> lines = new();
            string line = "";

            foreach (char c in text)
            {
                string next = line + c;
                bool canBreak = line.Length > 0 && !NoLineStart.Contains(c);

                if (canBreak && FontManager.Measure(next, size).X > width)
                {
                    lines.Add(line);
                    line = c == ' ' ? "" : c.ToString(); // 行の頭の空白は消す
                }
                else
                {
                    line = next;
                }
            }
            if (line.Length > 0) lines.Add(line);

            wrapCache[cacheKey] = lines;
            return lines;
        }

        // 折り返して描いて、使った高さを返す
        public static float DrawWrapped(string text, float x, float y, float width, float size, Color color, float lineHeight)
        {
            List<string> lines = Wrap(text, width, size);
            for (int i = 0; i < lines.Count; i++)
            {
                FontManager.Draw(lines[i], x, y + i * lineHeight, size, color);
            }
            return lines.Count * lineHeight;
        }
    }
}
