using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.UI
{
    // モード選択・選曲画面で、スキンの画像を描く道具
    //   画像が無いときは false を返すので、画面側は今までどおりプログラムで描く
    //   (スキンに画像が無くても、SystemSkin(テスト用)のときも、同じ見た目で動く)
    //
    // 色が変わるもの(板・難易度の大きい板)は、白い「土台」に色をかけて描いてから、縁などの「上にのせる」画像を描く
    public static class MenuSkin
    {
        // フォルダ名は TaikøNauts と同じ番号(02.SongSelect・03.SongLoading・04.EnsoGame…)。モード選択は 01 にした(TaikøNauts の名前はまだ分からない)
        public const string ModeFolder = "Image/01.ModeSelect/";
        public const string SongFolder = "Image/02.SongSelect/";

        // 画面に入ったときに、先に読み込んでおく(初めて描くときに止まらないように)
        public static readonly string[] BarImages = { "Bar_Base.png", "Bar_Overlay.png", "Bar_Overlay_Plain.png", "Bar_Cursor.png", "Bar_Cursor_Decide.png" };
        public static readonly string[] SongImages = { "Bar_Label.png", "Panel_Base.png", "Panel_Overlay.png", "Marker_1P.png", "Hint.png",
            "Difficulty_Level_Number.png", "Difficulty_Select/Difficulty_Level_Number.png", "Difficulty_Select/Difficulty_Level_Star.png",
            "Maker_Balloon.png" };

        // TaikøNauts のスキンの選曲画面の画像(あるものだけ読まれる)
        public static readonly string[] TnSongImages = Enumerable.Range(0, 12).Select(i => $"Bar/{i}.png")
            .Concat(Enumerable.Range(0, 12).Select(i => $"Background/{i}.png"))
            .Concat(Enumerable.Range(0, 12).Select(i => $"Difficulty_Select/Background/{i}.png"))
            .Concat(Enumerable.Range(0, 14).Select(i => $"Box_Pop/{i}.png"))
            .Concat(new[] { "Bar/Back.png", "Bar/Overlay.png", "Bar/Decoration.png", "Bar/Selected.png", "Difficulty_Panel.png",
                "Difficulty_Select/Difficulty_Panel.png", "Difficulty_Select/Cursor_Effect.png", "Difficulty_Select/Cursor_Effect_Small.png",
                "Difficulty_Select/Player_Cursor.png" })
            .ToArray();

        public static void Preload(string folder, IEnumerable<string> names)
        {
            foreach (string name in names) SkinManager.GetTexture(folder + name);
        }

        // 全部の画像があるか(片方だけあって、もう片方が無いときに、変な見た目にならないように)
        public static bool Has(params string[] paths)
        {
            foreach (string path in paths)
                if (SkinManager.GetTexture(path) == null) return false;
            return true;
        }

        // 画像全体を dest に描く
        public static bool Whole(string path, Rectangle dest, Color tint)
        {
            if (SkinManager.GetTexture(path) is not Texture2D texture) return false;
            Raylib.DrawTexturePro(texture, new Rectangle(0, 0, texture.Width, texture.Height), dest, Vector2.Zero, 0f, tint);
            return true;
        }

        // 画像を cellCount 個に縦(または横)に区切ったうちの index 番目を描く
        public static bool Cell(string path, int index, int columns, int rows, Rectangle dest, Color tint)
        {
            if (SkinManager.GetTexture(path) is not Texture2D texture) return false;
            float w = (float)texture.Width / columns, h = (float)texture.Height / rows;
            Rectangle source = new Rectangle(index % columns * w, index / columns * h, w, h);
            Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, 0f, tint);
            return true;
        }

        // 数字の画像(0〜9 が横に10個並んだもの)で数字を書く。(centerX, centerY) が全体の真ん中
        //   advance：1文字ずつ右へ進める量(画像の1文字の幅より少し詰める)。scale：大きさ
        public static bool Digits(string path, string digits, float centerX, float centerY, float advance, float scale, Color tint)
        {
            if (digits.Length == 0 || SkinManager.GetTexture(path) is not Texture2D texture) return false;
            float w = texture.Width / 10f, h = texture.Height;
            float total = advance * (digits.Length - 1) * scale;
            float x = centerX - total / 2f;
            foreach (char ch in digits)
            {
                if (ch < '0' || ch > '9') continue;
                Rectangle source = new Rectangle((ch - '0') * w, 0, w, h);
                Rectangle dest = new Rectangle(x - w * scale / 2f, centerY - h * scale / 2f, w * scale, h * scale);
                Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, 0f, tint);
                x += advance * scale;
            }
            return true;
        }

        // 画像の一部(source)を dest に描く
        public static bool Part(string path, Rectangle source, Rectangle dest, Color tint)
        {
            if (SkinManager.GetTexture(path) is not Texture2D texture) return false;
            Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, 0f, tint);
            return true;
        }

        // 画像の一部(source)を、9つに切って伸ばして描く
        public static bool NPatchPart(string path, Rectangle source, Rectangle dest, int border, Color tint)
        {
            if (SkinManager.GetTexture(path) is not Texture2D texture) return false;
            int b = (int)Math.Min(border, Math.Min(source.Width, source.Height) / 2);
            NPatchInfo info = new NPatchInfo { Source = source, Left = b, Top = b, Right = b, Bottom = b, Layout = NPatchLayout.NinePatch };
            Raylib.DrawTextureNPatch(texture, info, dest, Vector2.Zero, 0f, tint);
            return true;
        }

        // 9つに切って伸ばして描く(角はそのまま、まん中だけ伸ばす。開いていく板や、大きさの違う枠に使う)
        public static bool NPatch(string path, Rectangle dest, int border, Color tint)
        {
            if (SkinManager.GetTexture(path) is not Texture2D texture) return false;
            int b = Math.Min(border, Math.Min(texture.Width, texture.Height) / 2);
            NPatchInfo info = new NPatchInfo
            {
                Source = new Rectangle(0, 0, texture.Width, texture.Height),
                Left = b, Top = b, Right = b, Bottom = b,
                Layout = NPatchLayout.NinePatch,
            };
            Raylib.DrawTextureNPatch(texture, info, dest, Vector2.Zero, 0f, tint);
            return true;
        }

        // 上下左右で残す幅を変えて、9つに切って伸ばす
        public static bool NPatch(string path, Rectangle dest, int left, int top, int right, int bottom, Color tint)
        {
            if (SkinManager.GetTexture(path) is not Texture2D texture) return false;
            NPatchInfo info = new NPatchInfo
            {
                Source = new Rectangle(0, 0, texture.Width, texture.Height),
                Left = left, Top = top, Right = right, Bottom = bottom,
                Layout = NPatchLayout.NinePatch,
            };
            Raylib.DrawTextureNPatch(texture, info, dest, Vector2.Zero, 0f, tint);
            return true;
        }

        // 透明度だけかけた白(画像をそのままの色で描くとき)
        public static Color White(float alpha) => MenuDraw.Fade(Color.White, alpha);
    }
}
