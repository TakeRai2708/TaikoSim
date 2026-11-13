using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using Layout = TakeTaikoSimulator.UI.LayoutAdjustments.Plate;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Scenes;

namespace TakeTaikoSimulator.Graphics;

// 共通の338×80プレート。画像と文字は反転させず、2Pは置き場所だけ変える。
public static class NamePlate
{
    public const string Root = "Image/99.Common/NamePlate/";
    private sealed record Art(Aup2? Anime, string? Base, float X, float Y);
    private static readonly Dictionary<string, Art> art = new();

    // NamePlate_Base.png の左上 338×80 から、黒いふち（暗いところ）だけを取り出した絵（スキンが変わったら作り直す）
    private static Texture2D? borderTexture;
    private static int borderGeneration = -1;
    private static Texture2D? BorderOnly(Texture2D sheet)
    {
        if (borderGeneration == SkinManager.Generation) return borderTexture;
        if (borderTexture is Texture2D old) Raylib.UnloadTexture(old);
        borderTexture = null;
        borderGeneration = SkinManager.Generation;
        Image full = Raylib.LoadImageFromTexture(sheet);
        if (full.Width < 338 || full.Height < 80) { Raylib.UnloadImage(full); return null; }
        Image img = Raylib.ImageFromImage(full, new Rectangle(0, 0, 338, 80));
        Raylib.UnloadImage(full);
        Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                Color p = Raylib.GetImageColor(img, x, y);
                float luma = 0.299f * p.R + 0.587f * p.G + 0.114f * p.B;
                float keep = Math.Clamp((160f - luma) / 160f, 0f, 1f);
                Raylib.ImageDrawPixel(ref img, x, y, new Color(p.R, p.G, p.B, (byte)(p.A * keep)));
            }
        Texture2D tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        if (tex.Id == 0) return null;
        Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(tex, TextureWrap.Clamp);
        return borderTexture = tex;
    }
    private static int skinGeneration = -1;
    public static void Clear()
    {
        foreach (var item in art.Values) item.Anime?.Unload();
        art.Clear(); skinGeneration = SkinManager.Generation;
    }
    private static Art Prepare(string id)
    {
        if (skinGeneration != SkinManager.Generation) Clear();
        if (art.TryGetValue(id, out var found)) return found;
        string rel = Root + "Plates/" + id + "/";
        string? folder = SkinManager.CurrentPath == null ? null : Path.Combine(SkinManager.CurrentPath, rel);
        float x = 0, y = 0;
        Aup2? anime = null;
        if (!SkinManager.SystemSkin && folder != null)
        {
            try
            {
                string config = Path.Combine(folder, "PlateConfig.json");
                if (File.Exists(config))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(config));
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("配置設定がオブジェクトではありません");
                    if (doc.RootElement.TryGetProperty("adjustX", out var dx) && dx.ValueKind == JsonValueKind.Number && dx.TryGetSingle(out float vx) && float.IsFinite(vx)) x = vx;
                    if (doc.RootElement.TryGetProperty("adjustY", out var dy) && dy.ValueKind == JsonValueKind.Number && dy.TryGetSingle(out float vy) && float.IsFinite(vy)) y = vy;
                }
                anime = Aup2.Load(Path.Combine(folder, "Anime.aup2"));
                anime?.Preload();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { Console.WriteLine($"[NamePlate] {id}：{ex.Message}"); }
        }
        string? image = SkinManager.GetTexture(rel + "Base.png") != null ? rel + "Base.png" : null;
        return art[id] = new(anime, image, x + Layout.TitleArtOffset.X, y + Layout.TitleArtOffset.Y);
    }
    public static void Preload()
    {
        SkinManager.GetTexture(Root + "NamePlate_Base.png");
        for (int slot = PlayerDataStore.GuestSlot; slot <= 3; slot++)
        {
            try
            {
                var data = PlayerDataStore.NamePlate(slot); Prepare(data.NamePlateId);
                FontManager.PreloadSkinMain(new[] { data.Name, data.Title, data.Rank });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { Console.WriteLine($"[NamePlate] データ{slot}：{ex.Message}"); }
        }
    }
    // 参考のモード画面では、どんちゃんの足元にプレートがある。左右の余白を揃える。
    public static Rectangle MenuArea(int side) => new(side == 0 ? Layout.Menu1PX : Layout.Menu2PX, Layout.MenuY, 338, 80);
    public static void DrawPlayers(float y = Layout.MenuY, float alpha = 1)
    {
        for (int side = 0; side < 2; side++)
            if (LocalSession.Joined[side]) Draw(LocalSession.Profiles[side], side, MenuArea(side).X, y, 1, alpha);
    }
    public static void Draw(int slot, int side, float x, float y, float scale = 1, float alpha = 1, NamePlateConfig? preview = null)
    {
        NamePlateConfig data;
        try { data = preview ?? PlayerDataStore.NamePlate(slot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { data = new() { Name = $"プレイヤー{slot}" }; }
        Art image = Prepare(CosmeticCatalog.SafeId(data.NamePlateId) ? data.NamePlateId : "00");
        Color white = Raylib.ColorAlpha(Color.White, alpha);
        Rlgl.PushMatrix(); Rlgl.Translatef(x, y, 0); Rlgl.Scalef(scale, scale, 1);
        try
        {
            if (SkinManager.GetTexture(Root + "NamePlate_Base.png") is Texture2D sheet)
            {
                Raylib.DrawTexturePro(sheet, new(0, 0, 338, 80), new(0, 0, 338, 80), Vector2.Zero, 0, white);
                if (image.Anime != null && !SkinManager.SystemSkin)
                {
                    float frame = (float)(Raylib.GetTime() * 60 % (image.Anime.LastFrame + 1));
                    image.Anime.Draw(frame, new(image.X, image.Y), alpha);
                }
                else if (image.Base != null && SkinManager.GetTexture(image.Base) is Texture2D plate)
                    Raylib.DrawTextureEx(plate, new(image.X, image.Y), 0, 1, white);
                else Raylib.DrawRectangle(60, 7, 264, 29, Raylib.ColorAlpha(new Color(55, 95, 160, 255), alpha));
                // 称号の背景のうえに、プレートの黒いふちだけをもう一度描く
                //   11/14 たけらい「タイトルの右側、下と上で縁の太さ違う」：称号の背景の絵が右の黒いふちに 2px くらいかぶっていた
                //   （かぶらないように左へずらすと、今度はすき間ができる）。ふちを上に重ねれば、どちらも起きない
                if (BorderOnly(sheet) is Texture2D border)
                    Raylib.DrawTexture(border, 0, 0, white);
            }
            else
            {
                SmoothShapes.Rounded(new(0, 0, 338, 80), .12f, 8, Raylib.ColorAlpha(new Color(21, 32, 50, 255), alpha));
                Raylib.DrawRectangle(0, 0, 6, 80, Raylib.ColorAlpha(side == 0 ? Color.Red : Color.SkyBlue, alpha));
                Text($"{side + 1}P", new(10, 19, 48, 44), 36, white, Raylib.ColorAlpha(Color.Black, alpha));
            }
            Text(data.Title, Layout.TitleText, Layout.TitleTextSize, Raylib.ColorAlpha(Color.Black, alpha), Raylib.ColorAlpha(Color.White, alpha));
            bool rank = data.Rank.Length > 0;
            if (rank)
            {
                // 09.zipの原本を確認。黒い段位欄と銀・金・虹の飾りは共通シートに入っている。
                if (SkinManager.GetTexture(Root + "NamePlate_Base.png") is Texture2D rankSheet
                    && rankSheet.Width >= 328 && rankSheet.Height >= 523)
                {
                    Raylib.DrawTexturePro(rankSheet, new(12, 241, 131, 34), Layout.RankBacking, Vector2.Zero, 0, white);
                    int type = Math.Clamp(data.RankType, 0, 2);
                    Raylib.DrawTexturePro(rankSheet, new(72, 323 + 82 * type, 70, 36), Layout.RankOrnament, Vector2.Zero, 0, white);
                }
                else Raylib.DrawRectangleRec(Layout.RankFallback, Raylib.ColorAlpha(Color.Black, alpha));
                // 金合格は段位文字の色。rankTypeは上の飾りの種類で、文字色とは別。
                Color rankColor = data.IsRankGold ? new Color(255, 218, 85, 255) : new Color(225, 233, 240, 255);
                Text(data.Rank, Layout.RankText, Layout.RankTextSize, Raylib.ColorAlpha(rankColor, alpha), Raylib.ColorAlpha(Color.Black, alpha));
            }
            // 段位欄の上にバッジを重ねる。各バッジは82px刻みで、80pxでは右端が切れる。
            if (SkinManager.GetTexture(Root + "NamePlate_Base.png") is Texture2D badgeSheet)
                Raylib.DrawTexturePro(badgeSheet, new(side == 0 ? 0 : 164, 80, 82, 80), Layout.PlayerBadge, Vector2.Zero, 0, white);
            NameText(data.Name, rank ? Layout.NameWithRank : Layout.NameWithoutRank, white, Raylib.ColorAlpha(Color.Black, alpha));
        }
        finally { Rlgl.PopMatrix(); }
    }
    // 名前の高さは固定。長い場合も縦を潰さず、横だけ縮めて中央に収める。
    private static void NameText(string text, Rectangle area, Color fill, Color outline)
    {
        if (text.Length == 0) return;
        const float size = Layout.NameSize, edge = Layout.NameOutline, padding = 2;
        Rectangle ink = FontManager.MeasureSkinMainInk(text, size);
        float scaleX = Math.Min(1, (area.Width - 2 * padding) / Math.Max(1, ink.Width + 2 * edge));
        Rlgl.PushMatrix();
        try
        {
            Rlgl.Translatef(area.X + area.Width / 2, area.Y + (area.Height - ink.Height) / 2 - ink.Y, 0);
            Rlgl.Scalef(scaleX, 1, 1);
            FontManager.DrawSkinMainOutlined(text, -ink.X - ink.Width / 2, 0, size, fill, outline, edge);
        }
        finally { Rlgl.PopMatrix(); }
    }

    private static void Text(string text, Rectangle area, float size, Color fill, Color outline)
    {
        if (text.Length == 0) return;
        const float edge = Layout.OtherTextOutline, padding = 2;
        Rectangle ink = FontManager.MeasureSkinMainInk(text, size);
        float factor = Math.Min(1, Math.Min((area.Width - 2 * (edge + padding)) / Math.Max(1, ink.Width),
            (area.Height - 2 * edge) / Math.Max(1, ink.Height)));
        float fitted = size * factor;
        ink = FontManager.MeasureSkinMainInk(text, fitted);
        FontManager.DrawSkinMainOutlined(text, area.X + (area.Width - ink.Width) / 2 - ink.X,
            area.Y + (area.Height - ink.Height) / 2 - ink.Y, fitted, fill, outline, edge);
    }
}
