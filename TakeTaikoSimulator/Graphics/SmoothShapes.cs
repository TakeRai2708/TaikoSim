using System.Numerics;
using Raylib_cs;

namespace TakeTaikoSimulator.Graphics;

// 設定部品の輪郭だけを約1ピクセルの透明度でなじませる。
// ウィンドウのMSAAに頼らず、1920×1080の描画先でも同じ処理を使う。
public static class SmoothShapes
{
    private const string Fragment = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform vec4 colDiffuse;
uniform vec4 shape;
uniform vec2 quadSize;
uniform float triangleMode;
uniform vec2 pointA;
uniform vec2 pointB;
uniform vec2 pointC;
uniform float gradMode;
uniform vec4 gL0;
uniform vec4 gL1;
uniform vec4 gL2;
uniform vec4 gL3;
uniform vec4 gR0;
uniform vec4 gR1;
uniform vec3 gStops;
out vec4 finalColor;

float segmentDistance(vec2 p, vec2 a, vec2 b)
{
    vec2 ab = b - a;
    float t = clamp(dot(p - a, ab) / max(dot(ab, ab), 0.00001), 0.0, 1.0);
    return length(p - a - t * ab);
}
float side(vec2 p, vec2 a, vec2 b)
{
    vec2 e = b - a;
    vec2 v = p - a;
    return e.x * v.y - e.y * v.x;
}
void main()
{
    vec2 p = (fragTexCoord - vec2(0.5)) * quadSize;
    float d;
    if (triangleMode > 0.5)
    {
        d = min(segmentDistance(p, pointA, pointB), min(segmentDistance(p, pointB, pointC), segmentDistance(p, pointC, pointA)));
        float a = side(p, pointA, pointB), b = side(p, pointB, pointC), c = side(p, pointC, pointA);
        bool inside = (a >= 0.0 && b >= 0.0 && c >= 0.0) || (a <= 0.0 && b <= 0.0 && c <= 0.0);
        if (inside) d = -d;
    }
    else
    {
        vec2 q = abs(p) - shape.xy + vec2(shape.z);
        d = min(max(q.x, q.y), 0.0) + length(max(q, vec2(0.0))) - shape.z;
        if (shape.w > 0.0) d = abs(d + shape.w * 0.5) - shape.w * 0.5;
    }
    float edge = max(fwidth(d), 0.0001);
    float coverage = 1.0 - smoothstep(-edge * 0.5, edge * 0.5, d);
    vec4 color = fragColor * colDiffuse;
    if (gradMode > 0.5)
    {
        // 上から下へのグラデ。左は 4色（gStops の位置で切りかえ）、右は 2色。横は真ん中でなめらかに混ぜる
        float ty = clamp((p.y + shape.y) / (2.0 * shape.y), 0.0, 1.0);
        float tx = clamp((p.x + shape.x) / (2.0 * shape.x), 0.0, 1.0);
        vec4 left = ty < gStops.x ? mix(gL0, gL1, ty / max(gStops.x, 0.0001))
                  : ty < gStops.y ? mix(gL1, gL2, (ty - gStops.x) / max(gStops.y - gStops.x, 0.0001))
                  : ty < gStops.z ? mix(gL2, gL3, (ty - gStops.y) / max(gStops.z - gStops.y, 0.0001))
                  : gL3;
        vec4 right = mix(gR0, gR1, ty);
        vec3 rgb = mix(left.rgb, right.rgb, smoothstep(0.3, 0.7, tx));
        color = vec4(rgb, color.a);
    }
    finalColor = vec4(color.rgb, color.a * coverage);
}
";
    private static Shader shader;
    private static bool prepared, available;
    private static int shapeLocation, sizeLocation, triangleLocation, aLocation, bLocation, cLocation;
    private static int gradModeLocation, gStopsLocation, gR0Location, gR1Location;
    private static readonly int[] gLeftLocations = new int[4];

    private static bool Prepare()
    {
        if (prepared) return available;
        prepared = true;
        shader = Raylib.LoadShaderFromMemory(null!, GraphicsBackend.Glsl(Fragment));
        available = shader.Id != 0 && shader.Id != Rlgl.GetShaderIdDefault();
        if (!available) { Console.WriteLine("[UI] 輪郭のAAを準備できませんでした。従来の図形で表示します"); return false; }
        shapeLocation = Raylib.GetShaderLocation(shader, "shape");
        sizeLocation = Raylib.GetShaderLocation(shader, "quadSize");
        triangleLocation = Raylib.GetShaderLocation(shader, "triangleMode");
        aLocation = Raylib.GetShaderLocation(shader, "pointA");
        bLocation = Raylib.GetShaderLocation(shader, "pointB");
        cLocation = Raylib.GetShaderLocation(shader, "pointC");
        gradModeLocation = Raylib.GetShaderLocation(shader, "gradMode");
        gStopsLocation = Raylib.GetShaderLocation(shader, "gStops");
        gR0Location = Raylib.GetShaderLocation(shader, "gR0");
        gR1Location = Raylib.GetShaderLocation(shader, "gR1");
        for (int i = 0; i < 4; i++) gLeftLocations[i] = Raylib.GetShaderLocation(shader, "gL" + i);
        return true;
    }

    public static void Preload() => Prepare();

    public static void Rounded(Rectangle area, float roundness, int segments, Color color) =>
        Rectangle(area, Math.Clamp(roundness, 0, 1) * Math.Min(area.Width, area.Height) / 2, color);

    public static void Rectangle(Rectangle area, float radius, Color color) => Draw(area, radius, 0, color);

    public static void Border(Rectangle area, float radius, float thickness, Color color)
    {
        if (thickness > 0) Draw(area, radius, thickness, color);
    }

    // ===== Raylib の関数と同じ並びの引数で呼べる版 =====
    //   11/12 たけらい「シンプルスキンが全部アンチエイリアシングが効いてなくてカクカク。でも効きすぎないように、ほどほどに」
    //   画像の無いスキン(シンプル)は、板・丸・三角・線をプログラムで描いている。そこを Raylib.DrawXxx から
    //   SmoothShapes.Xxx に置きかえた(輪郭の約1px だけをなじませる。ぼかしはしない)。
    //   置きかえやすいように、引数の並びを Raylib と同じにしてある

    /// <summary>Raylib.DrawRectangleRoundedLinesEx と同じ(線は四角の外側に thickness の太さ)</summary>
    public static void RoundedLines(Rectangle area, float roundness, int segments, float thickness, Color color)
    {
        float radius = Math.Clamp(roundness, 0, 1) * Math.Min(area.Width, area.Height) / 2;
        Border(new Rectangle(area.X - thickness, area.Y - thickness, area.Width + thickness * 2, area.Height + thickness * 2),
            radius + thickness, thickness, color);
    }

    /// <summary>Raylib.DrawCircle と同じ</summary>
    public static void Circle(int centerX, int centerY, float radius, Color color) => Circle(new Vector2(centerX, centerY), radius, color);

    /// <summary>Raylib.DrawCircleLinesV と同じ(細い輪)</summary>
    public static void CircleLines(Vector2 center, float radius, Color color) => Ring(center, radius - 0.75f, radius + 0.75f, color);

    /// <summary>Raylib.DrawRing と同じ。ぐるっと1周の輪だけなめらかにする(途中で切れた輪は Raylib のまま)</summary>
    public static void Ring(Vector2 center, float inner, float outer, float startAngle, float endAngle, int segments, Color color)
    {
        if (MathF.Abs(endAngle - startAngle) >= 360f) Ring(center, inner, outer, color);
        else Raylib.DrawRing(center, inner, outer, startAngle, endAngle, segments, color);
    }

    public static void Circle(Vector2 center, float radius, Color color) =>
        Draw(new(center.X - radius, center.Y - radius, radius * 2, radius * 2), radius, 0, color);

    public static void Ring(Vector2 center, float inner, float outer, Color color)
    {
        if (outer <= 0 || outer <= inner) return;
        if (inner <= 0) { Circle(center, outer, color); return; }
        Draw(new(center.X - outer, center.Y - outer, outer * 2, outer * 2), outer, outer - inner, color);
    }

    private static void Draw(Rectangle area, float radius, float stroke, Color color)
    {
        if (color.A == 0 || area.Width <= 0 || area.Height <= 0) return;
        radius = Math.Clamp(radius, 0, Math.Min(area.Width, area.Height) / 2);
        if (!Prepare())
        {
            if (stroke > 0 && area.Width == area.Height && radius == area.Width / 2)
                Raylib.DrawRing(new(area.X + area.Width / 2, area.Y + area.Height / 2), radius - stroke, radius, 0, 360, 64, color);
            else if (stroke > 0) Raylib.DrawRectangleRoundedLinesEx(area, 2 * radius / Math.Min(area.Width, area.Height), 16, stroke, color);
            else Raylib.DrawRectangleRounded(area, 2 * radius / Math.Min(area.Width, area.Height), 16, color);
            return;
        }
        Rectangle quad = new(area.X - 2, area.Y - 2, area.Width + 4, area.Height + 4);
        Raylib.BeginShaderMode(shader);
        Raylib.SetShaderValue(shader, shapeLocation, new Vector4(area.Width / 2, area.Height / 2, radius, stroke), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(shader, sizeLocation, new Vector2(quad.Width, quad.Height), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(shader, triangleLocation, 0f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, gradModeLocation, 0f, ShaderUniformDataType.Float);
        Quad(quad, color);
        Raylib.EndShaderMode();
    }

    // 角丸の四角を、上から下へのグラデでぬる（モード選択の素材の無い板のふち用）
    //   left は左がわの4色（上→下）、stops はその切りかえ位置(0〜1)3つ。right は右がわの上と下の色
    //   シェーダーが使えないときは、left の真ん中の色でぬるだけ
    public static void RoundedGradient(Rectangle area, float radius, Color[] left, Vector3 stops, Color rightTop, Color rightBottom, float alpha)
    {
        if (alpha <= 0f || area.Width <= 0 || area.Height <= 0) return;
        if (!Prepare()) { Rectangle(area, radius, Fade(left[2], alpha)); return; }
        radius = Math.Clamp(radius, 0, Math.Min(area.Width, area.Height) / 2);
        Rectangle quad = new(area.X - 2, area.Y - 2, area.Width + 4, area.Height + 4);
        Raylib.BeginShaderMode(shader);
        Raylib.SetShaderValue(shader, shapeLocation, new Vector4(area.Width / 2, area.Height / 2, radius, 0), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(shader, sizeLocation, new Vector2(quad.Width, quad.Height), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(shader, triangleLocation, 0f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, gradModeLocation, 1f, ShaderUniformDataType.Float);
        for (int i = 0; i < 4; i++) Raylib.SetShaderValue(shader, gLeftLocations[i], Vec(left[i]), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(shader, gR0Location, Vec(rightTop), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(shader, gR1Location, Vec(rightBottom), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(shader, gStopsLocation, stops, ShaderUniformDataType.Vec3);
        Quad(quad, new Color((byte)255, (byte)255, (byte)255, (byte)Math.Clamp(alpha * 255f, 0f, 255f)));
        Raylib.SetShaderValue(shader, gradModeLocation, 0f, ShaderUniformDataType.Float);
        Raylib.EndShaderMode();
    }

    private static Vector4 Vec(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
    private static Color Fade(Color c, float alpha) => new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * alpha, 0f, 255f));

    public static void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        if (color.A == 0) return;
        if (!Prepare()) { Raylib.DrawTriangle(a, b, c, color); return; }
        float left = Math.Min(a.X, Math.Min(b.X, c.X)), top = Math.Min(a.Y, Math.Min(b.Y, c.Y));
        Rectangle quad = new(left - 2, top - 2, Math.Max(a.X, Math.Max(b.X, c.X)) - left + 4, Math.Max(a.Y, Math.Max(b.Y, c.Y)) - top + 4);
        Vector2 center = new(quad.X + quad.Width / 2, quad.Y + quad.Height / 2);
        Raylib.BeginShaderMode(shader);
        Raylib.SetShaderValue(shader, sizeLocation, new Vector2(quad.Width, quad.Height), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(shader, triangleLocation, 1f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, gradModeLocation, 0f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(shader, aLocation, a - center, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(shader, bLocation, b - center, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(shader, cLocation, c - center, ShaderUniformDataType.Vec2);
        Quad(quad, color);
        Raylib.EndShaderMode();
    }

    public static void Line(Vector2 start, Vector2 end, float width, Color color)
    {
        if (width <= 0) return;
        Vector2 direction = end - start;
        Rlgl.PushMatrix();
        Rlgl.Translatef(start.X, start.Y, 0);
        Rlgl.Rotatef(MathF.Atan2(direction.Y, direction.X) * 180 / MathF.PI, 0, 0, 1);
        Rectangle(new(-width / 2, -width / 2, direction.Length() + width, width), width / 2, color);
        Rlgl.PopMatrix();
    }

    private static void Quad(Rectangle area, Color color)
    {
        // 既定の白テクスチャと0〜1のUVを使う。移動中もUIの座標変換とクリップをそのまま使える。
        Rlgl.SetTexture(Rlgl.GetTextureIdDefault());
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Normal3f(0, 0, 1);
        Rlgl.TexCoord2f(0, 0); Rlgl.Vertex2f(area.X, area.Y);
        Rlgl.TexCoord2f(0, 1); Rlgl.Vertex2f(area.X, area.Y + area.Height);
        Rlgl.TexCoord2f(1, 1); Rlgl.Vertex2f(area.X + area.Width, area.Y + area.Height);
        Rlgl.TexCoord2f(1, 0); Rlgl.Vertex2f(area.X + area.Width, area.Y);
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    public static void Unload()
    {
        if (available) Raylib.UnloadShader(shader);
        shader = default; prepared = available = false;
    }
}
