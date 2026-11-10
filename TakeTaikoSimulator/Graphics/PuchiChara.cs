using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Scenes;

namespace TakeTaikoSimulator.Graphics;

public static class PuchiChara
{
    public static string[] Roots => new[] { "Image/99.Common/PuchiChara", "Image/99.Common/Cosmetic/PuchiChara" };
    private sealed record Art(string Image, JsonElement Config);
    private static readonly Dictionary<string, Art?> assets = new();
    private static int generation = -1;
    public static string[] Choices()
    {
        if (SkinManager.CurrentPath == null) return Array.Empty<string>();
        return Roots.SelectMany(root => CosmeticCatalog.Folders(Path.Combine(SkinManager.CurrentPath, root), "Chara.png")
            .Select(id => root + "/" + id)).ToArray();
    }
    public static void DrawPlayers(string scene)
    {
        for (int side = 0; side < 2; side++)
            if (LocalSession.Joined[side]) Draw(LocalSession.Profiles[side], side, scene);
    }
    public static void Draw(int slot, int side, string scene)
    {
        if (SkinManager.SystemSkin || SkinManager.CurrentPath == null) return;
        if (generation != SkinManager.Generation) { assets.Clear(); generation = SkinManager.Generation; }
        PlayerAppearanceConfig cfg;
        try { cfg = PlayerDataStore.Appearance(slot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return; }
        if (!cfg.IsUsePuchiChara) return;
        string id = cfg.PuchiCharaId;
        string key = id + ":" + cfg.PuchiCharaType;
        // 相対パスそのものは設定に受け入れず、見つかった素材一覧と照合する。
        if (!assets.TryGetValue(key, out var art))
        {
            string[] choices = Choices();
            string? path = choices.FirstOrDefault(p => Path.GetFileName(p) == id);
            if (path == null && cfg.PuchiCharaType >= 0 && cfg.PuchiCharaType < choices.Length) path = choices[cfg.PuchiCharaType];
            try
            {
                string file = path == null ? "" : Path.Combine(SkinManager.CurrentPath, path, "Config.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("ぷちキャラ設定がオブジェクトではありません");
                art = path == null ? null : new(path + "/Chara.png", doc.RootElement.Clone());
                if (art != null) SkinManager.GetTexture(art.Image);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { art = null; }
            assets[key] = art;
        }
        if (art == null || SkinManager.GetTexture(art.Image) is not Texture2D texture) return;
        float Read(string name, float fallback)
        {
            if (!art.Config.TryGetProperty(name, out var value)) return fallback;
            if (value.ValueKind == JsonValueKind.Array)
            {
                if (value.GetArrayLength() == 0) return fallback;
                value = value.GetArrayLength() > side ? value[side] : value[0];
            }
            if (value.ValueKind != JsonValueKind.Number) return fallback;
            return value.TryGetSingle(out float n) && float.IsFinite(n) ? n : fallback;
        }
        int count = Math.Clamp((int)Read("charaImageCount", 1), 1, texture.Width);
        float width = Math.Clamp(Read("charaSizeWidth", texture.Width / count), 1, texture.Width);
        float height = Math.Clamp(Read("charaSizeHeight", texture.Height), 1, texture.Height);
        count = Math.Min(count, (int)(texture.Width / width));
        float scale = Math.Clamp(Read(scene + "Scale", 1), .01f, 10);
        float x = Read(scene + "X", side == 0 ? 350 : 1450), y = Read(scene + "Y", 850);
        int frame = (int)(Raylib.GetTime() * 4) % count;
        Raylib.DrawTexturePro(texture, new(frame * width, 0, width, height), new(x, y, width * scale, height * scale), Vector2.Zero, 0, Color.White);
    }
}
