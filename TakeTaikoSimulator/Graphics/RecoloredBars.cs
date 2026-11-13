using Raylib_cs;

namespace TakeTaikoSimulator.Graphics
{
    // スキンの板の絵を、別の色にぬりかえて使う（絵が無いジャンルの代わり）
    //   11/14 たけらい「a-style で素材なかったらほかので代用できない？」「縁の感じが全然違う。グラデーションっていうかグローっていうか」：
    //   プログラムで形をまねて描くのはやめて、そのスキンにある板の絵（Bar/1.png）を、ジャンルの色にぬりかえて使う。
    //   絵はその人のPCにあるスキンから、起動中にその場で作るだけ（リポジトリには何も入れない）
    //
    //   ぬりかえ方：板の色（左はしの中の色）を基準に、明るさだけを見る
    //     ・基準より明るいところ（上のハイライト）→ ジャンルの色を白に近づける（どれだけ明るいかの割合で）
    //     ・基準より暗いところ（下の帯・黒いふち）→ ジャンルの色を同じ割合で暗くする（黒は黒のまま）
    //     ・透明さはそのまま
    public static class RecoloredBars
    {
        private static readonly Dictionary<(string, uint), Texture2D> cache = new();
        private static int generation = -1;

        public static Texture2D? Get(string templatePath, Color target)
        {
            if (generation != SkinManager.Generation) Clear();
            uint key = ((uint)target.R << 24) | ((uint)target.G << 16) | ((uint)target.B << 8) | target.A;
            if (cache.TryGetValue((templatePath, key), out Texture2D have)) return have;
            if (SkinManager.CurrentPath is not string skin || SkinManager.SystemSkin) return null;
            string full = Path.Combine(skin, templatePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) return null;

            Image image = Raylib.LoadImage(full);
            if (image.Width <= 0 || image.Height <= 0) return null;
            Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
            Color baseColor = Raylib.GetImageColor(image, Math.Min(30, image.Width / 2), image.Height / 2);
            float lb = Math.Clamp(Luma(baseColor), 1f, 254f);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    Color p = Raylib.GetImageColor(image, x, y);
                    if (p.A == 0) continue;
                    float lp = Luma(p);
                    Color c;
                    if (lp >= lb)
                    {
                        float w = Math.Clamp((lp - lb) / (255f - lb), 0f, 1f);
                        c = new Color((byte)(target.R + (255 - target.R) * w), (byte)(target.G + (255 - target.G) * w), (byte)(target.B + (255 - target.B) * w), p.A);
                    }
                    else
                    {
                        float k = lp / lb;
                        c = new Color((byte)(target.R * k), (byte)(target.G * k), (byte)(target.B * k), p.A);
                    }
                    Raylib.ImageDrawPixel(ref image, x, y, c);
                }
            Texture2D texture = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            if (texture.Id == 0) return null;
            Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(texture, TextureWrap.Clamp);
            cache[(templatePath, key)] = texture;
            return texture;
        }

        private static float Luma(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

        public static void Clear()
        {
            foreach (Texture2D t in cache.Values) Raylib.UnloadTexture(t);
            cache.Clear();
            generation = SkinManager.Generation;
        }
    }
}
