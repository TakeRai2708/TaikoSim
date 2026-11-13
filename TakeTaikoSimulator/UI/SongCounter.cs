using Raylib_cs;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Scenes;
using static TakeTaikoSimulator.UI.MenuDraw;

namespace TakeTaikoSimulator.UI
{
    // 「N曲目 / 3曲」の札と、演奏画面のジャンルの札
    //
    //   11/13 たけらい「演奏画面の曲数のカウント、ジャンル表示を実装してほしい。リザルトは曲数のカウントだけ。選曲も曲数のカウントだけ」
    //   TaikøNauts の録画（2026-11-11 23-42-45.mp4・23-46-22.mp4）で測った：
    //     ・選曲・リザルト：99.Common/SongCountSystem/Plate.png（284×77。「曲目」「曲」の字が入っている）
    //         左上 選曲 (1612,232)・リザルト (1631,2)。大きい数字 Number_Large.png（36×37 が10個）は 札の左上から (+48,+19)、
    //         小さい数字 Number_Small.png（26×27 が10個）は (+188,+29)。2けた以上は右はしをそろえて左へのばす
    //     ・演奏画面：曲名の下 (1584,99) に 04.EnsoGame/Genre/Song.png（「1曲目 | 3曲」）と Genre/Bar.png（ジャンル名）を
    //         約2秒ずつ交互に出す（曲数が先）。字は 曲数 = 黒に白いふち、ジャンル = 白に黒いふち
    //   「何曲目」は、エントリーしてから始めた曲の数（LocalSession.SongsPlayed）。1プレイは TaikøNauts と同じ 3曲 として、
    //   4曲目からはまた「1曲目」から数える（TakeTaikoSimulator はフリープレイなので、終わりにはしない）
    public static class SongCounter
    {
        public const int SongsPerPlay = 3;
        private const string CountFolder = "Image/99.Common/SongCountSystem/";
        private const string GenreFolder = "Image/04.EnsoGame/Genre/";
        private const float PillSeconds = 2f, PillFade = 0.2f;
        public const float SongSelectX = 1612f, SongSelectY = 232f, ResultX = 1631f, ResultY = 2f;
        public const float PlayX = 1584f, PlayY = 99f;

        /// <summary>今の「何曲目」（1〜3）。started = すでに始めた曲の数（選曲では次の曲 = +1 したもの）</summary>
        public static int Number(int started) => ((Math.Max(1, started) - 1) % SongsPerPlay) + 1;

        /// <summary>選曲・リザルトの札。number は 1〜3</summary>
        public static void DrawPlate(float x, float y, int number, float alpha = 1f)
        {
            Color white = Fade(Color.White, alpha);
            if (SkinManager.GetTexture(CountFolder + "Plate.png") is Texture2D plate)
            {
                Raylib.DrawTextureV(plate, new System.Numerics.Vector2(x, y), white);
                DrawDigits(CountFolder + "Number_Large.png", 36, 37, number.ToString(), x + 48f + 36f, y + 19f, white);
                DrawDigits(CountFolder + "Number_Small.png", 26, 27, SongsPerPlay.ToString(), x + 188f + 26f, y + 29f, white);
                return;
            }
            // 絵が無いスキン：同じ形をプログラムで描く（白い左・薄紫の右）
            Rectangle all = new(x + 12f, y + 10f, 258f, 52f);
            SmoothShapes.Rounded(new Rectangle(all.X, all.Y + 3f, all.Width, all.Height), 1f, 16, Fade(new Color(0, 0, 0, 60), alpha));
            SmoothShapes.Rounded(all, 1f, 16, Fade(new Color(186, 190, 226, 255), alpha));
            SmoothShapes.Rounded(new Rectangle(all.X, all.Y, all.Width * 0.6f, all.Height), 1f, 16, white);
            Outlined($"{number}曲目", new Rectangle(all.X, all.Y, all.Width * 0.6f, all.Height), 30f, Fade(Color.Black, alpha), white);
            Outlined($"{SongsPerPlay}曲", new Rectangle(all.X + all.Width * 0.6f, all.Y, all.Width * 0.4f, all.Height), 24f, Fade(Color.Black, alpha), white);
        }

        // 数字を、右はし right にそろえて描く
        private static void DrawDigits(string image, float w, float h, string text, float right, float y, Color tint)
        {
            if (SkinManager.GetTexture(image) is not Texture2D tex) return;
            for (int i = 0; i < text.Length; i++)
            {
                int d = text[i] - '0';
                float x = right - (text.Length - i) * w;
                Raylib.DrawTexturePro(tex, new Rectangle(d * w, 0, w, h), new Rectangle(x, y, w, h), System.Numerics.Vector2.Zero, 0f, tint);
            }
        }

        /// <summary>演奏画面：曲数とジャンルを交互に。clock は演奏画面に入ってからの秒</summary>
        //   11/14 たけらい：曲数とジャンルの字は細かい字のフォント（曲名のほうが太い字）。ジャンルの札は、そのジャンルの色
        //   録画（09-41-48 の1秒）：アニメの札はべたぬりのピンク (253,146,223) に、白い字と黒い縁
        //   スキンの Genre/Bar.png は1枚だけ（どのジャンルでも同じ）なので、白い札にジャンルの色をかけて描いている（推測）
        public static void DrawPlay(int number, string genre, Color genreColor, float clock)
        {
            float countAlpha = 1f;
            if (genre.Length > 0)
            {
                float t = clock % (PillSeconds * 2f);
                countAlpha = t < PillSeconds
                    ? Math.Clamp((PillSeconds - t) / PillFade, 0f, 1f)
                    : 1f - Math.Clamp((PillSeconds * 2f - t) / PillFade, 0f, 1f);
            }
            Rectangle area = new(PlayX, PlayY, 296f, 40f);
            if (countAlpha > 0f)
            {
                Color a = Fade(Color.White, countAlpha);
                if (!MenuSkin.Whole(GenreFolder + "Song.png", area, a))
                {
                    SmoothShapes.Rounded(area, 1f, 12, Fade(new Color(200, 228, 246, 255), countAlpha));
                    SmoothShapes.Rounded(new Rectangle(area.X, area.Y, 160f, area.Height), 1f, 12, a);
                }
                // 録画：「1曲目」は x 1643〜1704、「3曲」は x 1789〜1838、高さ y 107〜131（ふち込み）
                float thick = 2.5f;
                OutlinedDetail($"{number}曲目", new Rectangle(1623.5f, PlayY, 100f, 40f), 22f, Fade(Color.Black, countAlpha), a, thick);
                OutlinedDetail($"{SongsPerPlay}曲", new Rectangle(1763.5f, PlayY, 100f, 40f), 22f, Fade(Color.Black, countAlpha), a, thick);
            }
            if (genre.Length > 0 && countAlpha < 1f)
            {
                float g = 1f - countAlpha;
                if (!MenuSkin.Whole(GenreFolder + "Bar.png", area, Fade(genreColor, g)))
                    SmoothShapes.Rounded(area, 1f, 12, Fade(genreColor, g));
                // 11/14 TaikøNauts のスクショ：ジャンル名は白い字の高さ 17px・縁 4px（ナムコオリジナルの札はべたぬり (254,112,39)）
                OutlinedDetail(genre, new Rectangle(area.X + 10f, area.Y, area.Width - 20f, area.Height), 21f, Fade(Color.White, g), Fade(Color.Black, g), 4f);
            }
        }

        /// <summary>譜面のファイルから、ジャンル名（カテゴリの Genre、無ければカテゴリの名前）を探す</summary>
        public static string GenreOf(string tjaPath) => GenreInfo(tjaPath).Name;

        /// <summary>ジャンル名と、そのジャンルの色（選曲の板と同じ色）</summary>
        public static (string Name, Color Color) GenreInfo(string tjaPath)
        {
            foreach (SongCategory category in SongList.Categories)
                foreach (SongEntry song in category.Songs)
                    if (string.Equals(song.TjaPath, tjaPath, StringComparison.OrdinalIgnoreCase))
                        return category.IsLoose || category.Title == SongList.OthersTitle ? ("", Color.White)
                            : (string.IsNullOrWhiteSpace(category.Genre) ? category.Title : category.Genre, category.Color);
            return ("", Color.White);
        }
    }
}
