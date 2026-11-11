using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Scenes;

namespace TakeTaikoSimulator.Graphics;

// 共通の338×80プレート。画像と文字は反転させず、2Pは置き場所だけ変える。
public static class NamePlate
{
    public const string Root = "Image/99.Common/NamePlate/";
    private sealed record Art(Aup2? Anime, string? Base, float X, float Y);
    private static readonly Dictionary<string, Art> art = new();
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
        return art[id] = new(anime, image, x, y);
    }
    public static void Preload()
    {
        SkinManager.GetTexture(Root + "NamePlate_Base.png");
        SkinManager.GetTexture(Root + "NamePlate_Rank_Gold.png");
        for (int slot = 1; slot <= 3; slot++)
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
    public static Rectangle MenuArea(int side) => new(side == 0 ? 60 : 1522, 920, 338, 80);
    public static void DrawPlayers(float y = 920, float alpha = 1)
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
                // 1P赤・2P青。シートの中央の青1Pは使用しない。
                Raylib.DrawTexturePro(sheet, new(side == 0 ? 0 : 160, 80, 80, 80), new(-8, -1, 80, 80), Vector2.Zero, 0, white);
            }
            else
            {
                Raylib.DrawRectangleRounded(new(0, 0, 338, 80), .12f, 8, Raylib.ColorAlpha(new Color(21, 32, 50, 255), alpha));
                Raylib.DrawRectangle(0, 0, 6, 80, Raylib.ColorAlpha(side == 0 ? Color.Red : Color.SkyBlue, alpha));
                Text($"{side + 1}P", new(10, 19, 48, 44), 36, white, Raylib.ColorAlpha(Color.Black, alpha));
            }
            Text(data.Title, new(75, 6, 248, 28), 26, Raylib.ColorAlpha(Color.Black, alpha), Raylib.ColorAlpha(Color.White, alpha));
            bool rank = data.Rank.Length > 0;
            if (rank)
            {
                if (data.IsRankGold && SkinManager.GetTexture(Root + "NamePlate_Rank_Gold.png") is Texture2D gold)
                    Raylib.DrawTexturePro(gold, new(0, 0, gold.Width, gold.Height), new(73, 45, 72, 25), Vector2.Zero, 0, white);
                Color rankColor = data.RankType == 1 ? new Color(255, 218, 85, 255) : data.RankType == 2
                    ? Raylib.ColorFromHSV((float)(Raylib.GetTime() * 70 % 360), .55f, 1) : new Color(225, 233, 240, 255);
                Text(data.Rank, new(76, 44, 69, 28), 24, Raylib.ColorAlpha(rankColor, alpha), Raylib.ColorAlpha(Color.Black, alpha));
            }
            Text(data.Name, new(rank ? 151 : 76, 37, rank ? 174 : 249, 29), 35, white, Raylib.ColorAlpha(Color.Black, alpha));
        }
        finally { Rlgl.PopMatrix(); }
    }
    private static void Text(string text, Rectangle area, float size, Color fill, Color outline)
    {
        if (text.Length == 0) return;
        const float edge = 2, padding = 2;
        Rectangle ink = FontManager.MeasureSkinMainInk(text, size);
        float factor = Math.Min(1, Math.Min((area.Width - 2 * (edge + padding)) / Math.Max(1, ink.Width),
            (area.Height - 2 * edge) / Math.Max(1, ink.Height)));
        float fitted = size * factor;
        ink = FontManager.MeasureSkinMainInk(text, fitted);
        FontManager.DrawSkinMainOutlined(text, area.X + (area.Width - ink.Width) / 2 - ink.X,
            area.Y + (area.Height - ink.Height) / 2 - ink.Y, fitted, fill, outline, edge);
    }
}
