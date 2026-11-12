using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using Layout = TakeTaikoSimulator.UI.LayoutAdjustments.Dressing;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator.Scenes;

// 選んでいる間はプレビューだけ。段位は合格して獲得した状態から選ぶ。
public sealed class DressingScene : Scene
{
    private sealed class Draft
    {
        public NamePlateConfig Plate;
        public PlayerAppearanceConfig Appearance;
        private (string, string, string, bool, int) originalPlate;
        private (string, int, bool, string, int) originalAppearance;
        private (string, int, bool, string, int) Options => (Appearance.DonchanId, Appearance.DonchanType, Appearance.IsUsePuchiChara, Appearance.PuchiCharaId, Appearance.PuchiCharaType);
        private (string, string, string, bool, int) Identity => (Plate.NamePlateId, Plate.Name, Plate.Rank, Plate.IsRankGold, Plate.RankType);
        public bool Dirty => Identity != originalPlate || Options != originalAppearance;
        public void Saved() { originalPlate = Identity; originalAppearance = Options; }
        public Draft(int slot) { Plate = PlayerDataStore.Copy(PlayerDataStore.NamePlate(slot)); Appearance = PlayerDataStore.Copy(PlayerDataStore.Appearance(slot)); Saved(); }
    }
    private readonly Dictionary<int, Draft> drafts = new();
    private int side, category, cursor, top;
    private string[] characters = Array.Empty<string>(), puchi = Array.Empty<string>(), plates = Array.Empty<string>();
    private string error = "";
    private bool asking, readFailed;
    private readonly PlayerNameInput nameInput = new();
    private bool naming;
    private float appliedAge = -1;
    private readonly Dictionary<int, RankChoice[]> rankChoices = new();
    private sealed record RankChoice(string Name, bool Gold, int Type)
    {
        public string Label => Name.Length == 0 ? "段位を表示しない" : $"{Name}　{(Gold ? "金合格" : "赤合格")}　{new[] { "銀", "金", "虹" }[Type]}";
    }
    private static readonly Rectangle List = Layout.List;
    private static readonly string[] Categories = { "どんちゃん", "ぷちキャラ", "ネームプレート", "名前・段位" };
    public override bool CanOpenSettings => false;
    public override bool IsWaitingKey => naming;
    public override string? MenuBgmFile => MenuBgm.EntryFile;
    private int Slot => LocalSession.Profiles[side];
    private Draft Current => drafts[Slot];
    public override void Enter()
    {
        side = LocalSession.Controller;
        if (LocalSession.Count == 0) LocalSession.Don(side);
        try
        {
            foreach (int player in LocalSession.Players)
            {
                int slot = LocalSession.Profiles[player];
                drafts[slot] = new(slot);
                var choices = new List<RankChoice> { new("", false, 0) };
                foreach (var rank in SongHistory.GetEarnedDanRanks(slot))
                    for (int gold = 0; gold <= (rank.Gold ? 1 : 0); gold++)
                        for (int type = 0; type <= rank.RankType; type++)
                            choices.Add(new(PlayerDataStore.RankName(rank.Index), gold == 1, type));
                rankChoices[slot] = choices.ToArray();
            }
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
    private int Count => category == 0 ? characters.Length : category == 1 ? puchi.Length + 1 : category == 2 ? plates.Length : rankChoices[Slot].Length + 1;
    private string Label(int i) => category == 0 ? characters[i] : category == 1 ? i == 0 ? "なし" : Path.GetFileName(puchi[i - 1])
        : category == 2 ? plates[i] : i == 0 ? "名前を変更" : rankChoices[Slot][i - 1].Label;
    private void SyncCursor()
    {
        if (!drafts.ContainsKey(Slot)) return;
        var a = Current.Appearance;
        cursor = category == 0 ? Array.IndexOf(characters, CosmeticCatalog.Choose(characters, a.DonchanId, a.DonchanType))
            : category == 1 ? !a.IsUsePuchiChara ? 0 : Math.Max(0, Array.FindIndex(puchi, p => Path.GetFileName(p) == a.PuchiCharaId)) + 1
            : category == 2 ? Array.IndexOf(plates, Current.Plate.NamePlateId)
            : Math.Max(0, Array.FindIndex(rankChoices[Slot], c => c.Name == Current.Plate.Rank && c.Gold == Current.Plate.IsRankGold && c.Type == Current.Plate.RankType) + 1);
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
        else if (category == 2) Current.Plate.NamePlateId = plates[cursor];
        else if (cursor > 0)
        {
            var rank = rankChoices[Slot][cursor - 1];
            Current.Plate.Rank = rank.Name; Current.Plate.IsRankGold = rank.Gold; Current.Plate.RankType = rank.Type;
        }
        appliedAge = -1;
        top = Math.Clamp(cursor - 4, 0, Math.Max(0, Count - 9));
        MenuSounds.Ka();
    }
    public override void Update(float dt)
    {
        if (appliedAge >= 0) { appliedAge += dt; if (appliedAge > 2) appliedAge = -1; }
        if (readFailed || asking) return;
        if (naming)
        {
            if (nameInput.Update()) { Current.Plate.Name = nameInput.Value.Trim(); naming = false; appliedAge = -1; }
            return;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Tab) && LocalSession.Count > 1)
        { side = 1 - side; SyncCursor(); }
        int tab = (Raylib.IsKeyPressed(KeyboardKey.Right) ? 1 : 0) - (Raylib.IsKeyPressed(KeyboardKey.Left) ? 1 : 0);
        if (tab != 0) { category = Math.Clamp(category + tab, 0, Categories.Length - 1); SyncCursor(); }
        int move = (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressedRepeat(KeyboardKey.Down) || InputManager.IsDrumPressed(DrumPart.RightKa, side) ? 1 : 0)
            - (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressedRepeat(KeyboardKey.Up) || InputManager.IsDrumPressed(DrumPart.LeftKa, side) ? 1 : 0);
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0 && Raylib.CheckCollisionPointRec(Screen.GetMousePosition(), List)) move -= Math.Sign(wheel);
        if (move != 0 && Count > 0) Select(cursor + move);
        if (InputManager.IsDonPressed(side) || Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            if (category == 3 && cursor == 0) BeginNameEdit();
            else Apply();
        }
    }
    private void BeginNameEdit() { nameInput.Open(Current.Plate.Name); naming = true; appliedAge = -1; }
    private bool Apply()
    {
        bool changeChara = false;
        try
        {
            foreach (var (slot, draft) in drafts)
            {
                if (!draft.Dirty) continue;
                // 称号と未知の項目は維持し、画面で変更した値だけを保存する。
                var previous = PlayerDataStore.Appearance(slot);
                changeChara |= previous.DonchanId != draft.Appearance.DonchanId || previous.DonchanType != draft.Appearance.DonchanType;
                var saved = PlayerDataStore.Copy(PlayerDataStore.NamePlate(slot));
                saved.NamePlateId = draft.Plate.NamePlateId;
                saved.Name = draft.Plate.Name;
                saved.Rank = draft.Plate.Rank;
                saved.IsRankGold = draft.Plate.IsRankGold;
                saved.RankType = draft.Plate.RankType;
                PlayerDataStore.Save(slot, saved);
                PlayerDataStore.Save(slot, draft.Appearance);
                draft.Saved();
            }
            NamePlate.Preload(); error = ""; appliedAge = 0; MenuSounds.Don();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { appliedAge = -1; error = "保存できませんでした（一部が保存済みの場合は、もう一度適用してください）：" + ex.Message; return false; }
        finally
        {
            if (changeChara) CharaBaker.Start(SkinManager.SystemSkin ? null : SkinManager.CurrentPath, SkinManager.CurrentName);
        }
    }
    public override void OnBack()
    {
        if (naming) { if (!WindowsIme.IsComposing) naming = false; return; }
        if (asking) { asking = false; return; }
        if (drafts.Values.Any(d => d.Dirty)) asking = true;
        else SceneManager.Simple(new ModeSelectScene());
    }
    public override void Draw()
    {
        Gui.BeginFrame();
        Gui.ResetClip(); // 前の画面のクリック範囲を引き継がない
        Raylib.ClearBackground(Gui.BackgroundColor);   // 11/12：設定と同じ色にそろえた
        FontManager.Draw("着せ替え", 80, 55, 48, Gui.TextColor);
        for (int player = 0; player < 2; player++)
        {
            if (!LocalSession.Joined[player]) continue;
            Rectangle box = new(1280 + 240 * player, 55, 220, 65);
            bool clicked = player == side ? Gui.PrimaryButton(box, $"{player + 1}P") : Gui.Button(box, $"{player + 1}P");
            if (clicked && !asking && !readFailed && !naming) { side = player; SyncCursor(); }
        }
        for (int i = 0; i < Categories.Length; i++)
        {
            Rectangle box = new(80 + i * 245, 145, 230, 60);
            bool clicked = category == i ? Gui.PrimaryButton(box, Categories[i]) : Gui.Button(box, Categories[i]);
            if (clicked && !asking && !readFailed && !naming)
            { category = i; SyncCursor(); }
        }
        SmoothShapes.Rectangle(List, 18, Gui.PanelColor);
        SmoothShapes.Border(List, 18, 1.5f, Gui.BorderColor);
        if (drafts.ContainsKey(Slot))
        {
            // 描画途中で選択・スクロールを変えると、同じクリックが別の行にも当たる。
            int visibleTop = top, clickedRow = -1;
            for (int i = visibleTop; i < Math.Min(Count, visibleTop + 9); i++)
            {
                Rectangle row = new(List.X + 20, List.Y + 15 + (i - visibleTop) * 65, List.Width - 50, 59);
                // 選んでいる行：少し明るい箱＋左に朱色の印(設定の左のメニューと同じ形)
                if (i == cursor)
                {
                    SmoothShapes.Rectangle(row, 10, Gui.BoxHoverColor);
                    SmoothShapes.Rounded(new Rectangle(row.X + 6, row.Y + 14, 5, row.Height - 28), 1f, 4, Gui.AccentColor);
                }
                else if (!asking && !readFailed && !naming && Gui.IsMouseOver(row)) SmoothShapes.Rectangle(row, 10, Gui.BoxColor);
                string label = Label(i);
                float size = 30 * Math.Min(1, (row.Width - 44) / Math.Max(1, FontManager.Measure(label, 30).X));
                FontManager.Draw(label, row.X + 22, row.Y + (row.Height - size) / 2, size, i == cursor ? Gui.TextColor : Gui.SubTextColor);
                if (!asking && !readFailed && !naming && Gui.Clicked(row))
                clickedRow = i;
            }
            if (Count == 0) FontManager.Draw("このスキンには素材がありません", List.X + 36, List.Y + 40, 28, Gui.SubTextColor);
            if (Count > 9)
            {
                SmoothShapes.Rounded(new Rectangle(List.X + List.Width - 15, List.Y + 15, 5, 585), 1f, 4, Gui.TrackColor);
                float h = 585f * 9 / Count;
                SmoothShapes.Rounded(new Rectangle(List.X + List.Width - 15, List.Y + 15 + (585 - h) * top / (Count - 9), 5, h), 1f, 4, Gui.AccentColor);
            }
            // 一覧を描き終えてから、一つの行だけ選択する。
            if (clickedRow >= 0)
            { Select(clickedRow); if (category == 3 && clickedRow == 0) BeginNameEdit(); }
            string title = $"{side + 1}P  {Current.Plate.Name}";
            FontManager.Draw(title, Layout.PreviewTitle.X, Layout.PreviewTitle.Y, 36 * Math.Min(1, 910 / Math.Max(1, FontManager.Measure(title, 36).X)), Color.White);
            NamePlate.Draw(Slot, side, Layout.PlatePosition.X, Layout.PlatePosition.Y, preview: Current.Plate);
            string? rel = category == 0 && characters.Length > 0 ? "Image/99.Common/Chara/" + characters[cursor] + "/songselect_loop/0.png" : null;
            if (rel != null && SkinManager.GetTexture(rel) is Texture2D tex)
            {
                float scale = Math.Min(Layout.CharacterMaxSize.X / tex.Width, Layout.CharacterMaxSize.Y / tex.Height);
                // 添付のオレンジ枠へ。A-Styleの透明余白を含む820×599画像が基準。
                Raylib.DrawTextureEx(tex, Layout.Character, 0, scale, Color.White);
            }
            if (category == 1)
            {
                PuchiChara.Draw(Slot, side, "entry");
                FontManager.Draw(puchi.Length == 0 ? "ぷちキャラの素材を追加すると選べます" : "選択したぷちキャラは適用後に反映されます", 890, 570, 28, Gui.SubTextColor);
            }
            if (category == 3)
            {
                FontManager.Draw("段位は、このデータで合格したものから選べます", 890, 570, 28, Gui.SubTextColor);
                FontManager.Draw("金合格・色も、獲得済みの範囲で変更できます", 890, 620, 28, Gui.SubTextColor);
                if (rankChoices[Slot].Length == 1) FontManager.Draw("まだ合格した段位がありません", 890, 690, 28, Gui.SakuraColor);
            }
        }
        if (error.Length > 0) FontManager.Draw(error, 80, 885, 26, Color.Orange);
        FontManager.Draw("←→：種類　↑↓・カッ：選択　Tab：プレイヤー　ドン・Enter：適用", 80, 970, 26, Gui.SubTextColor);
        if (Gui.Button(new(1280, 900, 240, 65), "戻る") && !asking && !naming) OnBack();
        if (Gui.PrimaryButton(new(1560, 900, 240, 65), "適用") && !asking && !readFailed && !naming) Apply();
        if (appliedAge >= 0 && !asking && !naming)
        {
            // アイコンを付けず、操作を止めない短い通知。0.15秒で6px上がり、最後の0.2秒で消える。
            float enter = Math.Clamp(appliedAge / .15f, 0, 1);
            float alpha = enter * Math.Clamp((2 - appliedAge) / .2f, 0, 1);
            Rectangle toast = new(1510, 810 + 6 * MathF.Pow(1 - enter, 3), 290, 62);
            SmoothShapes.Rectangle(toast, 12, Raylib.ColorAlpha(Gui.BoxHoverColor, alpha));
            SmoothShapes.Border(toast, 12, 1.5f, Raylib.ColorAlpha(Gui.AccentColor, alpha));
            Gui.DrawCentered("適用しました", toast, 28, Raylib.ColorAlpha(Color.White, alpha));
        }
        if (naming) { nameInput.Draw(); return; }
        if (!asking) return;
        Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 190));
        SmoothShapes.Rectangle(new(450, 350, 1020, 350), 20, Gui.PanelColor);
        SmoothShapes.Border(new(450, 350, 1020, 350), 20, 1.5f, Gui.BorderColor);
        if (Gui.Button(new(470, 365, 60, 50), "×")) asking = false;
        FontManager.Draw("保存していない設定があります", 580, 440, 40, Gui.TextColor);
        if (Gui.Button(new(530, 570, 370, 70), "保存しないで戻る")) SceneManager.Simple(new ModeSelectScene());
        if (Gui.PrimaryButton(new(1020, 570, 370, 70), "保存して戻る") && Apply()) SceneManager.Simple(new ModeSelectScene());
    }
}
