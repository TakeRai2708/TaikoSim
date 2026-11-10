using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator.Scenes;

// 選んでいる間はプレビューだけ。適用で保存し、段位の状態には触らない。
public sealed class DressingScene : Scene
{
    private sealed class Draft
    {
        public NamePlateConfig Plate;
        public PlayerAppearanceConfig Appearance;
        private string originalPlate = "";
        private (string, int, bool, string, int) originalAppearance;
        private (string, int, bool, string, int) Options => (Appearance.DonchanId, Appearance.DonchanType, Appearance.IsUsePuchiChara, Appearance.PuchiCharaId, Appearance.PuchiCharaType);
        public bool Dirty => Plate.NamePlateId != originalPlate || Options != originalAppearance;
        public void Saved() { originalPlate = Plate.NamePlateId; originalAppearance = Options; }
        public Draft(int slot) { Plate = PlayerDataStore.Copy(PlayerDataStore.NamePlate(slot)); Appearance = PlayerDataStore.Copy(PlayerDataStore.Appearance(slot)); Saved(); }
    }
    private readonly Dictionary<int, Draft> drafts = new();
    private int side, category, cursor, top;
    private string[] characters = Array.Empty<string>(), puchi = Array.Empty<string>(), plates = Array.Empty<string>();
    private string error = "";
    private bool asking, readFailed;
    private static readonly Rectangle List = new(80, 240, 710, 620);
    private static readonly string[] Categories = { "どんちゃん", "ぷちキャラ", "ネームプレート" };
    public override bool CanOpenSettings => false;
    public override string? MenuBgmFile => MenuBgm.EntryFile;
    private int Slot => LocalSession.Profiles[side];
    private Draft Current => drafts[Slot];
    public override void Enter()
    {
        side = LocalSession.Controller;
        if (LocalSession.Count == 0) LocalSession.Don(side);
        try
        {
            foreach (int player in LocalSession.Players) drafts[LocalSession.Profiles[player]] = new(LocalSession.Profiles[player]);
            string? root = SkinManager.CurrentPath;
            characters = CosmeticCatalog.Folders(root == null ? null : Path.Combine(root, "Image/99.Common/Chara"), "Config.json");
            puchi = PuchiChara.Choices();
            string? plateRoot = root == null ? null : Path.Combine(root, NamePlate.Root, "Plates");
            plates = CosmeticCatalog.Folders(plateRoot, "Base.png").Concat(CosmeticCatalog.Folders(plateRoot, "Anime.aup2"))
                .Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
            SyncCursor();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { error = "設定が読めません：" + ex.Message; readFailed = true; }
    }
    private int Count => category == 0 ? characters.Length : category == 1 ? puchi.Length + 1 : plates.Length;
    private string Label(int i) => category == 0 ? characters[i] : category == 1 ? i == 0 ? "なし" : Path.GetFileName(puchi[i - 1]) : plates[i];
    private void SyncCursor()
    {
        if (!drafts.ContainsKey(Slot)) return;
        var a = Current.Appearance;
        cursor = category == 0 ? Array.IndexOf(characters, CosmeticCatalog.Choose(characters, a.DonchanId, a.DonchanType))
            : category == 1 ? !a.IsUsePuchiChara ? 0 : Math.Max(0, Array.FindIndex(puchi, p => Path.GetFileName(p) == a.PuchiCharaId)) + 1
            : Array.IndexOf(plates, Current.Plate.NamePlateId);
        cursor = Math.Clamp(cursor, 0, Math.Max(0, Count - 1));
        top = Math.Clamp(cursor - 4, 0, Math.Max(0, Count - 9));
    }
    private void Select(int index)
    {
        if (Count == 0) return;
        cursor = Math.Clamp(index, 0, Count - 1);
        if (category == 0) { Current.Appearance.DonchanId = characters[cursor]; Current.Appearance.DonchanType = cursor; }
        else if (category == 1)
        {
            Current.Appearance.IsUsePuchiChara = cursor > 0;
            if (cursor > 0) { Current.Appearance.PuchiCharaId = Path.GetFileName(puchi[cursor - 1]); Current.Appearance.PuchiCharaType = cursor - 1; }
        }
        else Current.Plate.NamePlateId = plates[cursor];
        top = Math.Clamp(cursor - 4, 0, Math.Max(0, Count - 9));
        MenuSounds.Ka();
    }
    public override void Update(float dt)
    {
        if (readFailed || asking) return;
        if (Raylib.IsKeyPressed(KeyboardKey.Tab) && LocalSession.Count > 1)
        { side = 1 - side; SyncCursor(); }
        int tab = (Raylib.IsKeyPressed(KeyboardKey.Right) ? 1 : 0) - (Raylib.IsKeyPressed(KeyboardKey.Left) ? 1 : 0);
        if (tab != 0) { category = Math.Clamp(category + tab, 0, 2); SyncCursor(); }
        int move = (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressedRepeat(KeyboardKey.Down) || InputManager.IsDrumPressed(DrumPart.RightKa, side) ? 1 : 0)
            - (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressedRepeat(KeyboardKey.Up) || InputManager.IsDrumPressed(DrumPart.LeftKa, side) ? 1 : 0);
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0 && Raylib.CheckCollisionPointRec(Screen.GetMousePosition(), List)) move -= Math.Sign(wheel);
        if (move != 0 && Count > 0) Select(cursor + move);
        if (InputManager.IsDonPressed(side) || Raylib.IsKeyPressed(KeyboardKey.Enter)) Apply();
    }
    private bool Apply()
    {
        bool changeChara = false;
        try
        {
            foreach (var (slot, draft) in drafts)
            {
                if (!draft.Dirty) continue;
                // 名前と段位はそのままにして、着せ替えのプレートIDだけを変える。
                var previous = PlayerDataStore.Appearance(slot);
                changeChara |= previous.DonchanId != draft.Appearance.DonchanId || previous.DonchanType != draft.Appearance.DonchanType;
                var saved = PlayerDataStore.Copy(PlayerDataStore.NamePlate(slot));
                saved.NamePlateId = draft.Plate.NamePlateId;
                PlayerDataStore.Save(slot, saved);
                PlayerDataStore.Save(slot, draft.Appearance);
                draft.Saved();
            }
            NamePlate.Preload(); error = ""; MenuSounds.Don();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { error = "保存できませんでした（一部が保存済みの場合は、もう一度適用してください）：" + ex.Message; return false; }
        finally
        {
            if (changeChara) CharaBaker.Start(SkinManager.SystemSkin ? null : SkinManager.CurrentPath, SkinManager.CurrentName);
        }
    }
    public override void OnBack()
    {
        if (asking) { asking = false; return; }
        if (drafts.Values.Any(d => d.Dirty)) asking = true;
        else SceneManager.Simple(new ModeSelectScene());
    }
    public override void Draw()
    {
        Gui.BeginFrame();
        Raylib.ClearBackground(new Color(10, 17, 28, 255));
        FontManager.Draw("着せ替え", 80, 55, 48, Color.White);
        for (int player = 0; player < 2; player++)
        {
            if (!LocalSession.Joined[player]) continue;
            Rectangle box = new(1280 + 240 * player, 55, 220, 65);
            bool clicked = player == side ? Gui.PrimaryButton(box, $"{player + 1}P") : Gui.Button(box, $"{player + 1}P");
            if (clicked && !asking && !readFailed) { side = player; SyncCursor(); }
        }
        for (int i = 0; i < Categories.Length; i++)
        {
            Rectangle box = new(80 + i * 245, 145, 230, 60);
            if (Gui.Button(box, (category == i ? "［" : "") + Categories[i] + (category == i ? "］" : "")) && !asking && !readFailed)
            { category = i; SyncCursor(); }
        }
        Raylib.DrawRectangleRounded(List, .02f, 8, new Color(21, 32, 49, 255));
        if (drafts.ContainsKey(Slot))
        {
            for (int i = top; i < Math.Min(Count, top + 9); i++)
            {
                Rectangle row = new(100, 255 + (i - top) * 65, 660, 59);
                if (i == cursor) Raylib.DrawRectangleRec(row, new Color(44, 88, 132, 255));
                FontManager.Draw(Label(i), row.X + 22, row.Y + 12, 30, Color.White);
                if (!asking && !readFailed && Gui.Clicked(row)) Select(i);
            }
            if (Count == 0) FontManager.Draw("このスキンには素材がありません", 116, 280, 28, Color.LightGray);
            if (Count > 9)
            {
                Raylib.DrawRectangle(775, 255, 5, 585, new Color(45, 58, 75, 255));
                float h = 585f * 9 / Count;
                Raylib.DrawRectangle(775, 255 + (int)((585 - h) * top / (Count - 9)), 5, (int)h, Color.SkyBlue);
            }
            FontManager.Draw($"{side + 1}P  {Current.Plate.Name}", 890, 245, 36, Color.White);
            NamePlate.Draw(Slot, side, 980, 345, 1.8f, preview: Current.Plate);
            string? rel = category == 0 && characters.Length > 0 ? "Image/99.Common/Chara/" + characters[cursor] + "/songselect_loop/0.png" : null;
            if (rel != null && SkinManager.GetTexture(rel) is Texture2D tex)
            {
                float scale = Math.Min(720f / tex.Width, 370f / tex.Height);
                Raylib.DrawTextureEx(tex, new(920, 500), 0, scale, Color.White);
            }
            if (category == 1)
            {
                PuchiChara.Draw(Slot, side, "entry");
                FontManager.Draw(puchi.Length == 0 ? "ぷちキャラの素材を追加すると選べます" : "選択したぷちキャラは適用後に反映されます", 890, 570, 28, Color.LightGray);
            }
        }
        if (error.Length > 0) FontManager.Draw(error, 80, 885, 26, Color.Orange);
        FontManager.Draw("←→：種類　↑↓・カッ：選択　Tab：プレイヤー　ドン・Enter：適用", 80, 970, 26, Color.LightGray);
        if (Gui.Button(new(1280, 900, 240, 65), "戻る") && !asking) OnBack();
        if (Gui.PrimaryButton(new(1560, 900, 240, 65), "適用") && !asking && !readFailed) Apply();
        if (!asking) return;
        Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 190));
        Raylib.DrawRectangleRounded(new(450, 350, 1020, 350), .025f, 8, new Color(21, 32, 49, 255));
        if (Gui.Button(new(470, 365, 60, 50), "×")) asking = false;
        FontManager.Draw("保存していない設定があります", 580, 440, 40, Color.White);
        if (Gui.Button(new(530, 570, 370, 70), "保存しないで戻る")) SceneManager.Simple(new ModeSelectScene());
        if (Gui.PrimaryButton(new(1020, 570, 370, 70), "保存して戻る") && Apply()) SceneManager.Simple(new ModeSelectScene());
    }
}
