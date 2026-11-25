using System.Runtime.InteropServices;

namespace TakeTaikoSimulator.Updates;

// インストーラー・更新の画面で、角丸やチェックの線をなめらかに描くための GDI+（Windows に入っている）
//   11/26 たけらい「チェックボックスもAAつけてる？」→ GDI の RoundRect / LineTo はアンチエイリアスが無いので、ここで描く
//   使えないとき（GDI+ の起動に失敗）は Ready が false になり、UpdateWindow は前の GDI で描く
internal static class Gdip
{
    private static readonly Lazy<bool> started = new(Start);
    internal static bool Ready => started.Value;

    private static bool Start()
    {
        try
        {
            var input = new StartupInput { Version = 1 };
            return GdiplusStartup(out _, ref input, 0) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    // 塗りと1pxの縁の角丸の四角。diameter は角の円の直径
    internal static bool RoundedRect(nint canvas, float x, float y, float width, float height, float diameter, uint fill, uint edge)
    {
        if (GdipCreateFromHDC(canvas, out nint graphics) != 0)
            return false;
        nint path = 0, brush = 0, pen = 0;
        try
        {
            GdipSetSmoothingMode(graphics, 4);   // アンチエイリアス
            GdipSetPixelOffsetMode(graphics, 4); // ピクセルのまん中で合わせる（GDI と同じ位置になる）
            // 縁の線が四角の内側に収まるように 0.5px 内側を通す
            float left = x + 0.5f, top = y + 0.5f, w = width - 1, h = height - 1;
            float d = Math.Min(diameter, Math.Min(w, h));
            if (GdipCreatePath(0, out path) != 0)
                return false;
            if (d > 0)
            {
                GdipAddPathArc(path, left, top, d, d, 180, 90);
                GdipAddPathArc(path, left + w - d, top, d, d, 270, 90);
                GdipAddPathArc(path, left + w - d, top + h - d, d, d, 0, 90);
                GdipAddPathArc(path, left, top + h - d, d, d, 90, 90);
            }
            else
            {
                GdipAddPathRectangle(path, left, top, w, h);
            }
            GdipClosePathFigure(path);
            if (GdipCreateSolidFill(fill, out brush) != 0 || GdipFillPath(graphics, brush, path) != 0)
                return false;
            if (edge != fill)
            {
                if (GdipCreatePen1(edge, 1, 2, out pen) != 0) // 2 = ピクセル単位
                    return false;
                GdipDrawPath(graphics, pen, path);
            }
            return true;
        }
        finally
        {
            if (pen != 0) GdipDeletePen(pen);
            if (brush != 0) GdipDeleteBrush(brush);
            if (path != 0) GdipDeletePath(path);
            GdipDeleteGraphics(graphics);
        }
    }

    // 折れ線（チェックの印）。端と角は丸くする。points は x, y の順に並べる
    internal static bool Polyline(nint canvas, float[] points, float width, uint color)
    {
        if (GdipCreateFromHDC(canvas, out nint graphics) != 0)
            return false;
        nint pen = 0;
        try
        {
            GdipSetSmoothingMode(graphics, 4);
            GdipSetPixelOffsetMode(graphics, 4);
            if (GdipCreatePen1(color, width, 2, out pen) != 0)
                return false;
            GdipSetPenStartCap(pen, 2); // 丸
            GdipSetPenEndCap(pen, 2);
            GdipSetPenLineJoin(pen, 2);
            return GdipDrawLines(graphics, pen, points, points.Length / 2) == 0;
        }
        finally
        {
            if (pen != 0) GdipDeletePen(pen);
            GdipDeleteGraphics(graphics);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput
    {
        public uint Version;
        public nint Callback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [DllImport("gdiplus.dll")] private static extern int GdiplusStartup(out nuint token, ref StartupInput input, nint output);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFromHDC(nint canvas, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipSetSmoothingMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPixelOffsetMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePath(int fillMode, out nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePath(nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathArc(nint path, float x, float y, float width, float height, float start, float sweep);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathRectangle(nint path, float x, float y, float width, float height);
    [DllImport("gdiplus.dll")] private static extern int GdipClosePathFigure(nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateSolidFill(uint color, out nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteBrush(nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipFillPath(nint graphics, nint brush, nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePen1(uint color, float width, int unit, out nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePen(nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenStartCap(nint pen, int cap);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenEndCap(nint pen, int cap);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenLineJoin(nint pen, int join);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawPath(nint graphics, nint pen, nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawLines(nint graphics, nint pen, float[] points, int count);
}
