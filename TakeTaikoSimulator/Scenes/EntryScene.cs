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
    private float communication = -1;
    private bool communicationDone, successVoice;
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
    public override bool CanOpenSettings => !naming && communication < 0;
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
        FontManager.PreloadSkinMain(new[] { "使用するデータを選んでくださいプレイヤー123記録なしおに裏譜面クリアフルコンボドンダフル記録が読めません左右カッえらぶドン決定戻る終了太鼓をたたいてスタートEDCBAS読み込み中読み込みが完了しました" });
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
        if (Game.IsSettingsOpen) return;
        if (communication >= 0) { AdvanceCommunication(dt); return; }
        if (leaving) return;
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
        if (leaving || communication >= 0 || selectionChanged) return;
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
        communication = 0;
        communicationDone = successVoice = false;
        leaving = dataPlayerIndex + 1 >= LocalSession.Count;
        MenuSounds.Don();
    }

    // 参考動画32.9〜37.3秒を0.1秒刻みで計測。実通信ではなくローカルデータの読み込み演出。
    private void AdvanceCommunication(float dt)
    {
        if (communicationDone) return;
        communication += dt;
        if (!successVoice && communication >= 2.4f)
        { successVoice = true; MenuVoice.Play("Sound/Entry/DataLoadSuccess.ogg"); }
        if (communication < 4.4f) return;
        communicationDone = true;
        FinishDataLoad();
    }

    private void FinishDataLoad()
    {
        // 最後の確定後も、暗転中はこの画面を描く。参加者番号を人数の外へ進めない。
        if (dataPlayerIndex + 1 < LocalSession.Count)
        {
            communication = -1;
            dataPlayerIndex++;
            cursor = Enumerable.Range(1, 3).First(s => !chosenProfiles.Contains(s)) - 1;
            opened = 0;
            return;
        }
        SongHistory.SelectProfile(LocalSession.Profiles[LocalSession.Controller]);
        leaving = true;
        SongSelectScene.ResetForProfile();
        // 黒くしないで、そのままモード選択へ。板やどんちゃんが順番に出てくる（ModeSelectScene の「エントリーから入ってきたときの出かた」）
        //   11/11 の録画：エントリーとモード選択は同じ背景のままつながっていた。前は 0.3秒ずつ黒くしていた
        SceneManager.ChangeScene(new ModeSelectScene(intro: true));
    }

    public override void OnBack()
    {
        if (naming) { if (!WindowsIme.IsComposing) naming = false; return; }
        if (leaving || communication >= 0) return;
        if (joiningTransition >= 0) return;
        if (choosing) { choosing = false; dataPlayerIndex = 0; chosenProfiles.Clear(); LocalSession.Reset(); error = ""; MenuSounds.Ka(); }
        else Game.ShouldExit = true;
    }

    public override void Draw()
    {
        Gui.BeginFrame();
        DrawBackdrop();
        bool interactive = !leaving && !Game.IsSettingsOpen && joiningTransition < 0;
        if (communication >= 0) DrawCommunication();
        else if (naming) nameInput.Draw();
        else if (!choosing) DrawStart(interactive);
        else DrawProfiles(interactive);
        MenuSkin.Whole(Folder + "Header.png", new(0, 0, 1920, 1080), Color.White);
        FreePlay();
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
            // CreditBarの焼き込み文字は、2行目だけセル内で14px上にある。
            Rectangle area = new(row.X + 470, row.Y + 61 - i * 14, 630, 77);
            Rectangle ink = FontManager.MeasureSkinMainInk(message, 54);
            float size = 54 * Math.Min(1, (area.Width - 12) / Math.Max(1, ink.Width));
            ink = FontManager.MeasureSkinMainInk(message, size);
            FontManager.DrawSkinMainOutlined(message, area.X + (area.Width - ink.Width) / 2 - ink.X,
                area.Y + (area.Height - ink.Height) / 2 - ink.Y, size, Fade(Color.White, fade), Fade(Color.Black, fade), 5);
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

    private void DrawProfiles(bool interactive, float alpha = 1)
    {
        Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 150));
        int player = LocalSession.Players[dataPlayerIndex];
        // 黄色/緑の2段。動画24秒の中央パネル・カードを原寸で使う。
        if (!MenuSkin.Cell(Folder + "DataLoad_Panel.png", player, 1, 2, DataPanel, Fade(Color.White, alpha)))
            Raylib.DrawRectangleRounded(DataPanel, .08f, 12, Fade(player == 0 ? new Color(238, 192, 55, 255) : new Color(135, 190, 65, 255), alpha));
        if (!MenuSkin.Cell(Folder + "DataLoad_Text.png", 0, 1, 3, DataPanel, Fade(Color.White, alpha)))
            Outlined("使用するデータを選んでください", new(360, 385, 1200, 75), 52, Fade(Color.White, alpha), Fade(Color.Black, alpha));
        Rectangle card = new(563, 461, 794, 288);
        if (!MenuSkin.Whole(Folder + "PlayerData_Panel.png", card, Fade(Color.White, alpha)))
            Raylib.DrawRectangleRounded(card, .07f, 12, Fade(new Color(155, 50, 48, 255), alpha));
        Outlined($"データ {cursor + 1}", new(628, 515, 320, 52), 36, Fade(Color.White, alpha), Fade(Color.Black, alpha));
        Outlined(registered[cursor] ? "登録済みのデータ" : "決定すると名前入力", new(628, 568, 320, 48), 27, Fade(Color.White, alpha), Fade(Color.Black, alpha));
        NamePlate.Draw(cursor + 1, player, 620, 626, alpha: alpha, preview: registered[cursor] ? null : NewDataPlate);
        if (totals[cursor] != null)
        {
            Rlgl.PushMatrix();
            Rlgl.Translatef(946, 197, 0);
            SongRecordPanel.DrawSummary(ConfigManager.Game.RecordGroup, alpha, SongHistory.GetSummary(ConfigManager.Game.RecordGroup, cursor + 1));
            Rlgl.PopMatrix();
        }
        DrawArrow(false, new(464, 550, 88, 100), cursor > 0, interactive, alpha);
        DrawArrow(true, new(1330, 550, 88, 100), cursor < 2, interactive, alpha);
        DrawGuide(alpha);
        if (interactive && !selectionChanged && Gui.Clicked(ConfirmArea)) Decide();
        if (error.Length > 0 || !string.IsNullOrEmpty(errors[cursor]))
            Outlined(error.Length > 0 ? error : errors[cursor], new(450, 758, 1020, 45), 27, Color.Red, Color.Black);
    }

    private void DrawCommunication()
    {
        float t = communication;
        if (t < .3f) { DrawProfiles(false, 1 - t / .3f); return; }
        Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 150));
        bool success = t >= 2.2f;
        float start = success ? 2.2f : .3f, end = success ? 4.4f : 2.2f;
        float height = Math.Clamp((t - start) / .2f, 0, 1);
        float alpha = Math.Clamp((end - t) / .2f, 0, 1);
        if (height <= 0 || alpha <= 0) return;
        Rectangle panel = new(DataPanel.X, DataPanel.Y + DataPanel.Height * (1 - height) / 2, DataPanel.Width, DataPanel.Height * height);
        if (!MenuSkin.Cell(Folder + "DataLoad_Panel.png", success ? 0 : 1, 1, 2, panel, Fade(Color.White, alpha)))
            Raylib.DrawRectangleRounded(panel, .08f, 12, Fade(success ? Color.Gold : new Color(148, 211, 80, 255), alpha));
        if (!MenuSkin.Cell(Folder + "DataLoad_Text.png", success ? 2 : 1, 1, 3, panel, Fade(Color.White, alpha)))
            Outlined(success ? "読み込みが完了しました" : "読み込み中", panel, 48 * height, Fade(Color.White, alpha), Fade(Color.Black, alpha));
    }

    private void DrawGuide(float alpha = 1)
    {
        // 操作の案内：Small（500×124）を原寸で、左上を (710, 796) に置く。
        //   11/12 たけらい「下の操作の表示が重なってる」：Base.png に「えらぶ」「決定」の字がもう描いてあるのに、
        //   プログラムでも同じ字を書いていた。字を書くのをやめた。
        //   場所と大きさは 11/11 の録画（2026-11-11 23-40-44.mp4 の 30秒）で測った：白い板が 778〜1141・814〜901
        //   ＝ Base.png の白い所（68〜431・18〜105）と同じ大きさ。前は 0.75倍で (772.5, 806) に置いていた
        if (controlGuide != null && !SkinManager.SystemSkin)
        {
            controlGuide.Draw(controlGuide.LoopedFrameAt(elapsed), new System.Numerics.Vector2(710, 796), alpha);
        }
        else
        {
            Raylib.DrawRectangleRounded(new(777, 812, 366, 90), .25f, 12, Fade(Color.White, alpha));
            Centered("← → 選択", new(790, 830, 150, 50), 27, Fade(Color.Black, alpha));
            Centered("決定", new(960, 830, 170, 50), 30, Fade(Color.Black, alpha));
        }
    }

    private void DrawArrow(bool right, Rectangle box, bool enabled, bool interactive, float alpha = 1)
    {
        if (!enabled) return;
        if (!MenuSkin.Whole(Folder + (right ? "Cursor_Right.png" : "Cursor_Left.png"), box, Fade(Color.White, alpha)))
            Outlined(right ? "▶" : "◀", box, 66, Fade(Color.Yellow, alpha), Fade(Color.Black, alpha));
        if (interactive && Gui.Clicked(box)) Move(right ? 1 : -1);
    }
}
