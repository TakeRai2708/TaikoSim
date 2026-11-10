using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;
using static TakeTaikoSimulator.UI.MenuDraw;

namespace TakeTaikoSimulator.Scenes;

// 11/9のエントリー動画25秒：背景を暗くし、中央の黄色い板でデータを選ぶ。
// 人数を決めた後、参加している側のデータを順番に選ぶ。
public sealed class EntryScene : Scene
{
    private const string Folder = "Image/01.Entry/";
    private readonly SongRecordSummary?[] totals = new SongRecordSummary?[3];
    private readonly string[] errors = new string[3];
    private int cursor;
    private bool choosing;
    private bool leaving;
    private float elapsed;
    private float opened;
    private float joiningTransition = -1;
    private int dataPlayerIndex;
    private readonly HashSet<int> chosenProfiles = new();
    private string error = "";
    private readonly PlayerNameInput nameInput = new();
    private bool naming;
    private bool selectionChanged;
    private readonly bool[] registered = new bool[3];
    private Aup2? controlGuide;
    private int guideSkin = -1;
    private static readonly NamePlateConfig NewDataPlate = new() { Name = "新しいデータ" };
    private static readonly Rectangle ConfirmArea = new(960, 810, 184, 94);
    public override bool IsWaitingKey => naming;
    public override bool CanOpenSettings => !naming;
    private static readonly Rectangle DataPanel = new(249, 295, 1423, 490);

    public override string? MenuBgmFile => MenuBgm.EntryFile;

    public EntryScene(bool selectData = false) => choosing = selectData;

    public override void Enter()
    {
        if (!choosing) LocalSession.Reset();
        if (choosing && LocalSession.Count == 0) LocalSession.Don(0);
        PlayerDataStore.Reload();
        NamePlate.Preload();
        cursor = SongHistory.ActiveProfile - 1;
        MenuSkin.Preload(Folder, new[] { "Background.png", "Header.png", "CreditBar.png",
            "DataLoad_Panel.png", "DataLoad_Text.png", "PlayerData_Panel.png", "Cursor_Left.png", "Cursor_Right.png", "Background_Fireworks.png" });
        PrepareGuide();
        MenuSkin.Preload(MenuSkin.SongFolder, SongRecordPanel.Images);
        FontManager.PreloadSkinMain(new[] { "使用するデータを選んでくださいプレイヤー123記録なしおに裏譜面クリアフルコンボドンダフル記録が読めません左右カッえらぶドン決定戻る終了太鼓をたたいてスタート" });
        for (int i = 0; i < 3; i++)
        {
            registered[i] = PlayerDataStore.HasName(i + 1);
            try { totals[i] = SongHistory.PreviewProfile(i + 1, ConfigManager.Game.RecordGroup); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { errors[i] = "記録が読めません"; Console.WriteLine($"[Entry] {i + 1}: {ex.Message}"); }
        }
        MenuVoice.Play("Sound/Entry/Start.ogg");
    }

    public override void Exit()
    {
        controlGuide?.Unload(); controlGuide = null;
        MenuVoice.StopLoop();
    }

    private void PrepareGuide()
    {
        if (guideSkin == SkinManager.Generation) return;
        controlGuide?.Unload(); controlGuide = null;
        guideSkin = SkinManager.Generation;
        if (SkinManager.CurrentPath == null || SkinManager.SystemSkin) return;
        controlGuide = Aup2.Load(Path.Combine(SkinManager.CurrentPath, "Image/99.Common/Control_Guide/Small/Anime.aup2"));
        if (controlGuide != null && !controlGuide.HasAllImages) { controlGuide.Unload(); controlGuide = null; }
    }

    public override void Update(float dt)
    {
        selectionChanged = false;
        elapsed += dt;
        PrepareGuide();
        if (choosing) opened += dt;
        if (leaving || Game.IsSettingsOpen) return;
        if (naming)
        {
            if (nameInput.Update())
            {
                try
                {
                    var plate = PlayerDataStore.Copy(PlayerDataStore.NamePlate(cursor + 1));
                    plate.Name = nameInput.Value;
                    PlayerDataStore.Save(cursor + 1, plate);
                    registered[cursor] = true;
                    naming = false;
                    Decide();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { nameInput.Error = "保存できませんでした：" + ex.Message; }
            }
            return;
        }
        if (joiningTransition >= 0)
        {
            joiningTransition += dt;
            if (joiningTransition >= .5f) { joiningTransition = -1; choosing = true; opened = 0; }
            return;
        }
        if (!choosing)
        {
            bool[] don = { InputManager.IsDonPressed(0) || Raylib.IsKeyPressed(KeyboardKey.Enter), InputManager.IsDonPressed(1) };
            bool advance = false;
            // 同じフレームで両側が初参加しても、2回押したことにはしない。
            for (int p = 0; p < 2; p++) if (don[p]) advance |= LocalSession.Don(p);
            if (don.Any(x => x)) MenuSounds.Don();
            if (advance) joiningTransition = 0;
            return;
        }
        int dataPlayer = LocalSession.Players[dataPlayerIndex];
        if (choosing)
        {
            int move = 0;
            if (Raylib.IsKeyPressed(KeyboardKey.Left) || InputManager.IsDrumPressed(DrumPart.LeftKa, dataPlayer)) move--;
            if (Raylib.IsKeyPressed(KeyboardKey.Right) || InputManager.IsDrumPressed(DrumPart.RightKa, dataPlayer)) move++;
            // 選択を動かした入力を、同じフレームの決定にも使わない。
            if (move != 0) { Move(move); return; }
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || InputManager.IsDonPressed(dataPlayer)) Decide();
    }

    private void Move(int step)
    {
        int next = Math.Clamp(cursor + step, 0, 2);
        if (next == cursor) return;
        cursor = next;
        selectionChanged = true;
        error = "";
        MenuSounds.Ka();
    }

    private void Decide()
    {
        if (leaving || selectionChanged) return;
        if (opened < .2f) return;
        if (!string.IsNullOrEmpty(errors[cursor])) { error = errors[cursor]; return; }
        if (chosenProfiles.Contains(cursor + 1)) { error = "別のプレイヤーが使用中です"; return; }
        if (!PlayerDataStore.HasName(cursor + 1)) { naming = true; nameInput.Open(); return; }
        try { PlayerDataStore.NamePlate(cursor + 1); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { error = "名前の設定が読めません：" + ex.Message; return; }
        int physicalPlayer = LocalSession.Players[dataPlayerIndex];
        try { SongHistory.SelectProfile(cursor + 1); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { error = "記録が読めません"; Console.WriteLine($"[Entry] {ex.Message}"); return; }
        LocalSession.Profiles[physicalPlayer] = cursor + 1;
        chosenProfiles.Add(cursor + 1);
        // 最後の確定後も、暗転中はこの画面を描く。参加者番号を人数の外へ進めない。
        if (dataPlayerIndex + 1 < LocalSession.Count)
        {
            dataPlayerIndex++;
            cursor = Enumerable.Range(1, 3).First(s => !chosenProfiles.Contains(s)) - 1;
            opened = 0;
            MenuSounds.Don();
            return;
        }
        SongHistory.SelectProfile(LocalSession.Profiles[LocalSession.Controller]);
        leaving = true;
        SongSelectScene.ResetForProfile();
        MenuSounds.Don();
        MenuVoice.Play("Sound/Entry/DataLoadSuccess.ogg");
        SceneManager.Simple(new ModeSelectScene());
    }

    public override void OnBack()
    {
        if (naming) { if (!WindowsIme.IsComposing) naming = false; return; }
        if (leaving) return;
        if (joiningTransition >= 0) return;
        if (choosing) { choosing = false; dataPlayerIndex = 0; chosenProfiles.Clear(); LocalSession.Reset(); error = ""; MenuSounds.Ka(); }
        else Game.ShouldExit = true;
    }

    public override void Draw()
    {
        Gui.BeginFrame();
        DrawBackdrop();
        bool interactive = !leaving && !Game.IsSettingsOpen && joiningTransition < 0;
        if (naming) nameInput.Draw();
        else if (!choosing) DrawStart(interactive);
        else DrawProfiles(interactive);
        MenuSkin.Whole(Folder + "Header.png", new(0, 0, 1920, 1080), Color.White);
        Outlined("フリープレイ", new(730, 1012, 460, 56), 48, Color.White, Color.Black);
        Outlined(naming ? "Esc：データ選択へ" : "F1：設定　Esc：戻る", new(24, 1030, 390, 40), 22, Color.White, Color.Black);
    }

    private void DrawBackdrop()
    {
        if (!MenuSkin.Whole(Folder + "Background.png", new(0, 0, 1920, 1080), Color.White))
            Raylib.DrawRectangleGradientV(0, 0, 1920, 1080, new(22, 32, 52, 255), new(8, 13, 24, 255));
        DrawFirework(380, 75, 0, .25f);
        DrawFirework(1360, 85, 1, 1.45f);
    }

    private void DrawFirework(float x, float y, int cell, float offset)
    {
        if (SkinManager.GetTexture(Folder + "Background_Fireworks.png") is not Texture2D texture) return;
        float age = (elapsed + offset) % 3.2f;
        if (age >= 1.4f) return;
        float size = 300 * (.7f + .3f * age / 1.4f);
        float alpha = MathF.Sin(age / 1.4f * MathF.PI) * .6f;
        Raylib.DrawTexturePro(texture, new(cell * 300, 0, 300, 300), new(x - size / 2, y - size / 2, size, size), Vector2.Zero, 0, Fade(Color.White, alpha));
    }

    private void DrawStart(bool interactive)
    {
        // 20秒の動画とCreditBarの静止部分を照合：原寸、左上(380,404)。
        float fade = joiningTransition < 0 ? 1 : 1 - Math.Clamp(joiningTransition / .5f, 0, 1);
        for (int i = 0; i < 2; i++)
        {
            Rectangle row = new(380, 404 + i * 190, 1172, 190);
            bool enabled = LocalSession.Count < 2 || i == 1;
            Color tint = MenuSkin.White(fade * (enabled ? 1 : .55f));
            if (!MenuSkin.Cell(Folder + "CreditBar.png", i, 1, 2, row, tint))
            {
                Raylib.DrawRectangleRounded(new(row.X + 13, row.Y + 14, 1146, 160), .2f, 12, Fade(new Color(255, 226, 61, 255), fade));
                Outlined(i == 0 ? "1人プレイ" : "2人プレイ", new(row.X + 70, row.Y + 30, 320, 110), 56, Fade(Color.Black, fade), Fade(Color.White, fade));
            }
            string message = LocalSession.Count == 0 ? "太鼓をたたいてスタート!"
                : i == 0 ? LocalSession.Count == 1 ? $"{LocalSession.Controller + 1}P参加中　もう一度ドンで決定" : "2人プレイを選択中"
                : LocalSession.Count == 1 ? $"{2 - LocalSession.Controller}Pのドンで2人プレイ" : "もう一度ドンで決定";
            Outlined(message, new(row.X + 470, row.Y + 36, 630, 100), 54, Fade(Color.White, fade), Fade(Color.Black, fade));
            if (interactive && enabled && Gui.Clicked(row)) { ClickStart(i + 1); interactive = false; }
        }
        if (joiningTransition >= 0) Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, (int)(150 * (1 - fade))));
    }

    private void ClickStart(int count)
    {
        if (count == 1)
        {
            if (LocalSession.Count == 0) LocalSession.Don(0);
            else if (LocalSession.Count == 1) joiningTransition = 0;
        }
        else if (LocalSession.Count == 2) joiningTransition = 0;
        else
        {
            for (int side = 0; side < 2; side++) if (!LocalSession.Joined[side]) LocalSession.Don(side);
        }
        MenuSounds.Don();
    }

    private void DrawProfiles(bool interactive)
    {
        Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 150));
        int player = LocalSession.Players[dataPlayerIndex];
        // 黄色/緑の2段。動画24秒の中央パネル・カードを原寸で使う。
        if (!MenuSkin.Cell(Folder + "DataLoad_Panel.png", player, 1, 2, DataPanel, Color.White))
            Raylib.DrawRectangleRounded(DataPanel, .08f, 12, player == 0 ? new Color(238, 192, 55, 255) : new Color(135, 190, 65, 255));
        if (!MenuSkin.Cell(Folder + "DataLoad_Text.png", 0, 1, 3, DataPanel, Color.White))
            Outlined("使用するデータを選んでください", new(360, 385, 1200, 75), 52, Color.White, Color.Black);
        Rectangle card = new(563, 461, 794, 288);
        if (!MenuSkin.Whole(Folder + "PlayerData_Panel.png", card, Color.White))
            Raylib.DrawRectangleRounded(card, .07f, 12, new Color(155, 50, 48, 255));
        Outlined($"データ {cursor + 1}", new(628, 515, 320, 52), 36, Color.White, Color.Black);
        Outlined(registered[cursor] ? "登録済みのデータ" : "決定すると名前入力", new(628, 568, 320, 48), 27, Color.White, Color.Black);
        NamePlate.Draw(cursor + 1, player, 620, 626, preview: registered[cursor] ? null : NewDataPlate);
        if (totals[cursor] != null)
        {
            Rlgl.PushMatrix();
            Rlgl.Translatef(946, 197, 0);
            SongRecordPanel.DrawSummary(ConfigManager.Game.RecordGroup, 1, SongHistory.GetSummary(ConfigManager.Game.RecordGroup, cursor + 1));
            Rlgl.PopMatrix();
        }
        DrawArrow(false, new(464, 550, 88, 100), cursor > 0, interactive);
        DrawArrow(true, new(1330, 550, 88, 100), cursor < 2, interactive);
        DrawGuide();
        if (interactive && !selectionChanged && Gui.Clicked(ConfirmArea)) Decide();
        if (error.Length > 0 || !string.IsNullOrEmpty(errors[cursor]))
            Outlined(error.Length > 0 ? error : errors[cursor], new(450, 758, 1020, 45), 27, Color.Red, Color.Black);
    }

    private void DrawGuide()
    {
        // 動画24秒の操作案内：Smallの500×124を0.75倍にして中央に置く。
        if (controlGuide != null && !SkinManager.SystemSkin)
        {
            Rlgl.PushMatrix();
            Rlgl.Translatef(772.5f, 806, 0); Rlgl.Scalef(.75f, .75f, 1);
            controlGuide.Draw(controlGuide.LoopedFrameAt(elapsed));
            Centered("えらぶ", new(174, 46, 92, 40), 28, Color.Black);
            Centered("決定", new(350, 46, 115, 40), 28, Color.Black);
            Rlgl.PopMatrix();
        }
        else
        {
            Raylib.DrawRectangleRounded(new(777, 812, 366, 90), .25f, 12, Color.White);
            Centered("← → 選択", new(790, 830, 150, 50), 27, Color.Black);
            Centered("決定", new(960, 830, 170, 50), 30, Color.Black);
        }
    }

    private void DrawArrow(bool right, Rectangle box, bool enabled, bool interactive)
    {
        if (!enabled) return;
        if (!MenuSkin.Whole(Folder + (right ? "Cursor_Right.png" : "Cursor_Left.png"), box, Color.White))
            Outlined(right ? "▶" : "◀", box, 66, Color.Yellow, Color.Black);
        if (interactive && Gui.Clicked(box)) Move(right ? 1 : -1);
    }
}
