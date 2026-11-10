using Raylib_cs;
using TakeTaikoSimulator.Charts;

namespace TakeTaikoSimulator.Graphics
{
    // 段位プレート（縦長の看板）を読む係
    //
    //   11/5 たけらい「プレートはスキンに入れたい」：段位フォルダの danPlatePath だと、スキンを変えても同じ絵が出て違和感がある。
    //   読む順番：
    //     ① スキンの Image/06.DanSelect/DanPlate/{danIndex}.png（五級＝0 … 初段＝5 … 六段＝10 … 十段＝14 … 外伝＝19）
    //     ② 無ければ、段位フォルダの danPlatePath（自分で作った段位にも出るように残す）
    //   A-Style の Dan_Plate.png は 190×371 と小さく、画面では 230×485 に引き延ばすので、たけらいの PC では AI で2倍（380×742）にしたものをスキンに置いた。
    //   大きい画像を縮めて描くので、ミップマップ＋なめらか（Trilinear）にする。
    //   （前は読んだまま＝最近傍で描いていて、引き延ばすとギザギザだった）
    public static class DanPlates
    {
        public const string SkinFolder = "Image/06.DanSelect/DanPlate/";

        /// <summary>その段位のプレートの画像の場所。無ければ null</summary>
        public static string? Find(DanCourse course)
        {
            string? skin = SkinManager.CurrentPath;
            if (skin != null && !SkinManager.SystemSkin && course.DanIndex >= 0)
            {
                string inSkin = Path.Combine(skin, SkinFolder, $"{course.DanIndex}.png");
                if (File.Exists(inSkin)) return inSkin;
            }
            if (course.PlatePath.Length > 0)
            {
                string inFolder = Path.Combine(course.FolderPath, course.PlatePath);
                if (File.Exists(inFolder)) return inFolder;
            }
            return null;
        }

        /// <summary>プレートを読む（使い終わったら Raylib.UnloadTexture）。読めなければ Id が 0</summary>
        public static Texture2D Load(DanCourse course)
        {
            string? path = Find(course);
            return path == null ? default : LoadSmooth(path);
        }

        /// <summary>縮めても拡大してもなめらかに描けるように読む（ミップマップ＋Trilinear）</summary>
        public static Texture2D LoadSmooth(string path)
        {
            try
            {
                Texture2D texture = Raylib.LoadTexture(path);
                if (texture.Id == 0) return default;
                Raylib.GenTextureMipmaps(ref texture);
                Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
                return texture;
            }
            catch
            {
                return default;   // 読めなければプレートなし
            }
        }
    }
}
