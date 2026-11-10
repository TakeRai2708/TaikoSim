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
// 2人同時プレイは未実装なので、この画面では1人分のデータを選ぶ。
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
    private string error = "";
    private static readonly Rectangle Start = new(374, 430, 1172, 190);
    private static readonly Rectangle DataPanel = new(248, 292, 1423, 490);

    public EntryScene(bool selectData = false) => choosing = selectData;

    public override void Enter()
    {
        cursor = SongHistory.ActiveProfile - 1;
        MenuSkin.Preload(Folder, new[] { "Background.png", "Header.png", "CreditBar.png",
            "DataLoad_Panel.png", "DataLoad_Text.png", "PlayerData_Panel.png", "Cursor_Left.png", "Cursor_Right.png" });
        MenuSkin.Preload(MenuSkin.SongFolder, SongRecordPanel.Images);
        FontManager.PreloadSkinMain(new[] { "使用するデータを選んでくださいプレイヤー123記録なしおに裏譜面クリアフルコンボドンダフル記録が読めません左右カッえらぶドン決定戻る終了太鼓をたたいてスタート" });
        for (int i = 0; i < 3; i++)
        {
            try { totals[i] = SongHistory.PreviewProfile(i + 1); }
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
        if (choosing)
        {
            int move = 0;
            if (Raylib.IsKeyPressed(KeyboardKey.Left) || InputManager.IsDrumPressed(DrumPart.LeftKa)) move--;
            if (Raylib.IsKeyPressed(KeyboardKey.Right) || InputManager.IsDrumPressed(DrumPart.RightKa)) move++;
            if (move != 0) Move(move);
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || InputManager.IsDonPressed()) Decide();
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
        if (!choosing)
        {
            choosing = true;
            opened = 0;
            MenuSounds.Don();
            return;
        }
        if (opened < .2f) return;
        if (!string.IsNullOrEmpty(errors[cursor])) { error = errors[cursor]; return; }
        try { SongHistory.SelectProfile(cursor + 1); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { error = "記録が読めません"; Console.WriteLine($"[Entry] {ex.Message}"); return; }
        leaving = true;
        SongSelectScene.ResetForProfile();
        MenuSounds.Don();
        MenuVoice.Play("Sound/Entry/DataLoadSuccess.ogg");
        SceneManager.Simple(new ModeSelectScene());
    }

    public override void OnBack()
    {
        if (leaving) return;
        if (choosing) { choosing = false; error = ""; MenuSounds.Ka(); }
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
            if (!MenuSkin.Cell(Folder + "CreditBar.png", 0, 1, 2, Start, Color.White))
            {
                Raylib.DrawRectangleRounded(Start, .12f, 12, Color.Gold);
                Outlined("1人プレイ", Start, 64, Color.White, Color.Black);
            }
            Outlined("太鼓をたたいてスタート", new(480, 700, 960, 70), 46,
                Fade(Color.White, .75f + .25f * MathF.Sin(elapsed * 4)), Color.Black);
            if (interactive && Gui.Clicked(Start)) Decide();
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
        Rectangle card = new(563, 461, 794, 288);
        if (!MenuSkin.Whole(Folder + "PlayerData_Panel.png", card, Color.White))
            Raylib.DrawRectangleRounded(card, .07f, 12, new Color(50, 72, 100, 255));
        Outlined(SongHistory.ProfileName(cursor + 1), new(620, 530, 340, 60), 38, Color.White, Color.Black);
        Outlined($"{cursor + 1} / 3", new(650, 625, 250, 55), 34, Color.White, Color.Black);
        if (totals[cursor] is SongRecordSummary summary)
        {
            // 選曲の集計パネルを、カード右側の同じ原寸で使う。
            Rlgl.PushMatrix();
            Rlgl.Translatef(946, 197, 0);
            SongRecordPanel.DrawSummary(CourseType.Oni, 1, summary);
            Rlgl.PopMatrix();
        }
        DrawArrow(false, new(464, 550, 88, 100), cursor > 0, interactive);
        DrawArrow(true, new(1330, 550, 88, 100), cursor < 2, interactive);
        Rectangle confirm = new(780, 812, 360, 90);
        Raylib.DrawRectangleRounded(confirm, .15f, 10, Color.White);
        Outlined("決定", confirm, 42, Color.Black, Color.White);
        if (interactive && Gui.Clicked(confirm)) Decide();
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
