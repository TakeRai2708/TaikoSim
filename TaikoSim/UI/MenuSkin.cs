using System.Numerics;
using Raylib_cs;
using TaikoSim.Graphics;

namespace TaikoSim.UI
{
    // モード選択・選曲画面で、スキンの画像を描く道具
    //   画像が無いときは false を返すので、画面側は今までどおりプログラムで描く
    //   (スキンに画像が無くても、SystemSkin(テスト用)のときも、同じ見た目で動く)
    //
    // 色が変わるもの(板・難易度の大きい板)は、白い「土台」に色をかけて描いてから、縁などの「上にのせる」画像を描く
    public static class MenuSkin
    {
        public const string ModeFolder = "Image/02.ModeSelect/";
        public const string SongFolder = "Image/03.SongSelect/";

        // 画面に入ったときに、先に読み込んでおく(初めて描くときに止まらないように)
        public static readonly string[] BarImages = { "Bar_Base.png", "Bar_Overlay.png", "Bar_Overlay_Plain.png", "Bar_Cursor.png", "Bar_Cursor_Decide.png" };
        public static readonly string[] SongImages = { "Bar_Label.png", "Chip.png", "Panel_Base.png", "Panel_Overlay.png", "Back.png", "Option.png", "Card.png", "Card_Cursor.png", "Marker_1P.png", "Hint.png" };

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

        // 透明度だけかけた白(画像をそのままの色で描くとき)
        public static Color White(float alpha) => MenuDraw.Fade(Color.White, alpha);
    }
}
