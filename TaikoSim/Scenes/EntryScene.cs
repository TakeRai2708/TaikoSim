using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Charts;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.UI;
using static TaikoSim.UI.MenuDraw;

namespace TaikoSim.Scenes;

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
    private static readonly Rectangle DataPanel = new(248, 292, 1423, 490);

    public EntryScene(bool selectData = false) => choosing = selectData;

    public override void Enter()
    {
        if (!choosing) LocalSession.Reset();
        if (choosing && LocalSession.Count == 0) LocalSession.Don(0);
        cursor = SongHistory.ActiveProfile - 1;
        MenuSkin.Preload(Folder, new[] { "Background.png", "Header.png", "CreditBar.png",
            "DataLoad_Panel.png", "DataLoad_Text.png", "PlayerData_Panel.png", "Cursor_Left.png", "Cursor_Right.png" });
        MenuSkin.Preload(MenuSkin.SongFolder, SongRecordPanel.Images);
        FontManager.PreloadSkinMain(new[] { "使用するデータを選んでくださいプレイヤー123記録なしおに裏譜面クリアフルコンボドンダフル記録が読めません左右カッえらぶドン決定戻る終了太鼓をたたいてスタート" });
        for (int i = 0; i < 3; i++)
        {
            try { totals[i] = SongHistory.PreviewProfile(i + 1, ConfigManager.Game.RecordGroup); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { errors[i] = "記録が読めません"; Console.WriteLine($"[Entry] {i + 1}: {ex.Message}"); }
        }
        MenuVoice.Play("Sound/Entry/Start.ogg");
    }

    public override void Exit() => MenuVoice.StopLoop();

    public override void Update(float dt)
    {
        elapsed += dt;
        if (choosing) opened += dt;
        if (leaving || Game.IsSettingsOpen) return;
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
            if (move != 0) Move(move);
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || InputManager.IsDonPressed(dataPlayer)) Decide();
    }

    private void Move(int step)
    {
        int next = Math.Clamp(cursor + step, 0, 2);
        if (next == cursor) return;
        cursor = next;
        error = "";
        MenuSounds.Ka();
    }

    private void Decide()
    {
        if (leaving) return;
        if (opened < .2f) return;
        if (!string.IsNullOrEmpty(errors[cursor])) { error = errors[cursor]; return; }
        if (chosenProfiles.Contains(cursor + 1)) { error = "別のプレイヤーが使用中です"; return; }
        int physicalPlayer = LocalSession.Players[dataPlayerIndex];
        try { SongHistory.SelectProfile(cursor + 1); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { error = "記録が読めません"; Console.WriteLine($"[Entry] {ex.Message}"); return; }
        LocalSession.Profiles[physicalPlayer] = cursor + 1;
        chosenProfiles.Add(cursor + 1);
        if (++dataPlayerIndex < LocalSession.Count)
        {
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
        if (leaving) return;
        if (joiningTransition >= 0) return;
        if (choosing) { choosing = false; dataPlayerIndex = 0; chosenProfiles.Clear(); LocalSession.Reset(); error = ""; MenuSounds.Ka(); }
        else Game.ShouldExit = true;
    }

    public override void Draw()
    {
        Gui.BeginFrame();
        if (!MenuSkin.Whole(Folder + "Background.png", new(0, 0, 1920, 1080), Color.White))
            Raylib.DrawRectangleGradientV(0, 0, 1920, 1080, new(22, 32, 52, 255), new(8, 13, 24, 255));
        MenuSkin.Whole(Folder + "Header.png", new(0, 0, 1920, 1080), Color.White);
        bool interactive = !leaving && !Game.IsSettingsOpen;
        if (!choosing)
        {
            for (int p = 0; p < 2; p++)
            {
                Rectangle slot = new(180 + p * 840, 400, 720, 240);
                Raylib.DrawRectangleRounded(slot, .1f, 12, LocalSession.Joined[p] ? (p == 0 ? Color.Red : Color.SkyBlue) : new Color(24, 36, 55, 235));
                Outlined($"{p + 1}P", new(slot.X, 420, slot.Width, 65), 55, Color.White, Color.Black);
                Outlined(LocalSession.Joined[p] ? "参加中　もう一度ドンで次へ" : "ドンでエントリー", new(slot.X, 525, slot.Width, 65), 34, Color.White, Color.Black);
            }
            Outlined("2P：ドン V / N　カッ C / M　F1：キー設定", new(380, 750, 1160, 65), 36, Color.White, Color.Black);
            if (joiningTransition >= 0) Raylib.DrawRectangle(0, 0, 1920, 1080, Fade(Color.Black, Math.Clamp(joiningTransition / .5f, 0, 1)));
        }
        else DrawProfiles(interactive);
        Outlined(choosing ? "← →・カッ：えらぶ　ドン・Enter：決定　Esc：戻る" : "Esc：終了",
            new(200, 970, 1520, 55), 30, Color.White, Color.Black);
    }

    private void DrawProfiles(bool interactive)
    {
        Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 150));
        // 動画の原寸シート。黄色/緑の2段、文字は3段。
        if (!MenuSkin.Cell(Folder + "DataLoad_Panel.png", 0, 1, 2, DataPanel, Color.White))
            Raylib.DrawRectangleRounded(DataPanel, .08f, 12, new Color(24, 35, 53, 255));
        if (!MenuSkin.Cell(Folder + "DataLoad_Text.png", 0, 1, 3, DataPanel, Color.White))
            Outlined("使用するデータを選んでください", new(360, 385, 1200, 75), 52, Color.White, Color.Black);
        Outlined($"{LocalSession.Players[Math.Min(dataPlayerIndex, LocalSession.Count - 1)] + 1}P のデータ", new(600, 205, 720, 65), 44, Color.White, Color.Black);
        Rectangle card = new(563, 461, 794, 288);
        if (!MenuSkin.Whole(Folder + "PlayerData_Panel.png", card, Color.White))
            Raylib.DrawRectangleRounded(card, .07f, 12, new Color(50, 72, 100, 255));
        Outlined(SongHistory.ProfileName(cursor + 1), new(620, 530, 340, 60), 38, Color.White, Color.Black);
        Outlined($"{cursor + 1} / 3", new(650, 625, 250, 55), 34, Color.White, Color.Black);
        if (totals[cursor] != null)
        {
            // 選曲の集計パネルを、カード右側の同じ原寸で使う。
            Rlgl.PushMatrix();
            Rlgl.Translatef(946, 197, 0);
            SongRecordPanel.DrawSummary(ConfigManager.Game.RecordGroup, 1, SongHistory.GetSummary(ConfigManager.Game.RecordGroup, cursor + 1));
            Rlgl.PopMatrix();
        }
        DrawArrow(false, new(464, 550, 88, 100), cursor > 0, interactive);
        DrawArrow(true, new(1330, 550, 88, 100), cursor < 2, interactive);
        Rectangle confirm = new(780, 812, 360, 90);
        Raylib.DrawRectangleRounded(confirm, .15f, 10, Color.White);
        Outlined("決定", confirm, 42, Color.Black, Color.White);
        if (interactive && joiningTransition < 0 && Gui.Clicked(confirm)) Decide();
        if (error.Length > 0 || !string.IsNullOrEmpty(errors[cursor]))
            Outlined(error.Length > 0 ? error : errors[cursor], new(450, 760, 1020, 45), 30, Color.Red, Color.Black);
    }

    private void DrawArrow(bool right, Rectangle box, bool enabled, bool interactive)
    {
        if (!enabled) return;
        if (!MenuSkin.Whole(Folder + (right ? "Cursor_Right.png" : "Cursor_Left.png"), box, Color.White))
            Outlined(right ? "▶" : "◀", box, 66, Color.Yellow, Color.Black);
        if (interactive && Gui.Clicked(box)) Move(right ? 1 : -1);
    }
}
