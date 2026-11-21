using Raylib_cs;

namespace TakeTaikoSimulator.Graphics
{
    // 透明な画素の「色」を、となりの絵の色で埋める係（Unity の「Alpha Is Transparency」と同じ考え方）
    //
    //   11/22 たけらい「最初の踊り子が出るときに、切り取りが甘くて黒線が出る」：
    //   スキンの PNG は、透明な所の色が黒 (0,0,0,0) になっている。なめらか(Bilinear)に描くと、
    //   絵のふちでは「絵の色」と「透明な黒」がまざるので、ふちが黒っぽくにじんで線に見える。
    //   透明な所の色を、いちばん近い絵の色にしておけば、まざっても黒くならない（透明さはそのまま）。
    //   ふちから Passes 画素ぶんだけ埋める（なめらかに描くときに混ざるのは、ふちから1〜2画素）
    public static class AlphaBleed
    {
        private const int Passes = 2;

        public static unsafe void Apply(ref Image image)
        {
            if (image.Width <= 0 || image.Height <= 0) return;
            if (image.Format != PixelFormat.UncompressedR8G8B8A8) Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
            int w = image.Width, h = image.Height;
            byte* px = (byte*)image.Data;

            // 0 = 透明でまだ色が無い、1 = 色がある（絵か、前の回で埋めた所）
            byte[] filled = new byte[w * h];
            for (int i = 0; i < w * h; i++) filled[i] = px[i * 4 + 3] > 0 ? (byte)1 : (byte)0;

            var newly = new List<int>();
            for (int pass = 0; pass < Passes; pass++)
            {
                newly.Clear();
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (filled[i] != 0) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w || (dx == 0 && dy == 0)) continue;
                            int j = yy * w + xx;
                            if (filled[j] == 0) continue;
                            r += px[j * 4]; g += px[j * 4 + 1]; b += px[j * 4 + 2]; n++;
                        }
                    }
                    if (n == 0) continue;
                    px[i * 4] = (byte)(r / n); px[i * 4 + 1] = (byte)(g / n); px[i * 4 + 2] = (byte)(b / n);
                    newly.Add(i);
                }
                if (newly.Count == 0) break;
                foreach (int i in newly) filled[i] = 1;   // この回で埋めた所は、次の回から「色がある」
            }
        }
    }
}
