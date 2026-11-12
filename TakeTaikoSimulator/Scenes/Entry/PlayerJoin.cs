using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using Layout = TakeTaikoSimulator.UI.LayoutAdjustments.Entry;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;
using static TakeTaikoSimulator.UI.MenuDraw;

namespace TakeTaikoSimulator.Scenes;

// 1人分の「データ選択 → データ通信中 → 完了」
//
//   11/12 たけらい「エントリー画面の画面遷移の流れを変えよう」：
//     エントリー画面（EntryScene）だけでなく、モード選択・選曲の上にも重ねて出せるように、EntryScene から切り出した。
//     ・エントリー：黄色い板でドン → どちらの太鼓 → 【ここ】 → モード選択
//     ・途中参加：モード選択・選曲（曲の一覧）で、まだ使っていない方のキーのドン → 【ここ】 → 元の画面
//   ・だれが（side・keySet）は Open で受け取る。まだ側が決まっていない人なので、キーは「キーの組」を直接見る
//   ・LocalSession に参加させるのは「完了」が消えたとき。途中で Esc でやめても、参加したことにならない
//   ・11/12 たけらい「新しいスロットの表示は新しく遊ぶみたいな表示だけにする。スコアランク、王冠記録、名前は表示しない」
//     → 名前がまだ無いデータは、赤いカードの中に「新しく遊ぶ」だけを書く
//   ・11/12 たけらい「作った後に、初期セットアップ画面流れるようにして」（③A・④B）
//     → 新しいデータは、名前を入れたあとに「はじめの設定」（はやさ・音色・音量）を出す。途中参加のときも、その場で出す。
//       叩くキーと画面は全員共通なので、ここでは聞かない。決めた値は、そのデータの設定（Configs/PlayerSettings.cs）に保存する
//   ・時間は前のまま（参考動画 32.9〜37.3秒を測ったもの）：0.3秒で板が消える → 通信中 → 2.2秒で完了 → 4.4秒で終わり
public sealed class PlayerJoin
{
    public enum Result { None, Finished, Cancelled }

    private const string Folder = "Image/01.Entry/";
    private const float DimAlpha = 150f;
    private const float DimFadeSeconds = 0.15f;
    private const float CommunicationEnd = 4.4f;

    private readonly SongRecordSummary?[] totals = new SongRecordSummary?[3];
    private readonly string[] errors = new string[3];
    private readonly bool[] registered = new bool[3];
    private readonly HashSet<int> taken = new();
    private readonly PlayerNameInput nameInput = new();
    private int side, keySet;
    private bool overlay;            // ほかの画面の上に重ねているか（うしろの暗い幕を、出るとき・消えるときにぼかす）
    private int cursor;
    private float elapsed;
    private float opened;
    private float communication = -1;
    private bool successVoice;
    private bool naming;
    private bool selectionChanged;
    private string error = "";
    private Aup2? controlGuide;
    private int guideSkin = -1;
    private static Rectangle ConfirmArea => new(960 + Layout.GuideOffset.X, 810 + Layout.GuideOffset.Y, 184, 94);
    private static readonly Rectangle DataPanel = Layout.DataPanel;

    public bool Active { get; private set; }
    /// <summary>名前を入力中か（画面側は IsWaitingKey を true にする）</summary>
    public bool Naming => Active && naming;
    /// <summary>通信中か（戻れない・設定も開けない）</summary>
    public bool Busy => Active && (communication >= 0 || setup);

    // ===== 新しいデータの「はじめの設定」 =====
    private const int SetupRows = 4;                 // はやさ・音色・音量・「これではじめる」
    private static readonly Rectangle SetupPanel = new(440, 230, 1040, 620);
    private bool setup;
    private int setupRow;
    private PlayerSettingsFile setupFile = new();
    private string hitSoundBefore = "";              // 音色の試し聞きのために変えた設定を、終わったら戻す
    public int Side => side;

    /// <summary>まだ参加していない方のキーのドンが押されたか（1人で遊んでいるときだけ）</summary>
    public static bool NewPlayerPressed => LocalSession.Count == 1 && InputManager.IsKeySetDonPressed(LocalSession.FreeKeySet);

    /// <summary>2人目が、空いている側に入る（モード選択・選曲から）</summary>
    public void OpenForNewPlayer()
    {
        int other = 1 - LocalSession.FreeSide;
        Open(LocalSession.FreeSide, LocalSession.FreeKeySet, new[] { LocalSession.Profiles[other] }, overlay: true);
        MenuSounds.Don();
    }

    /// <param name="side">入る側（0 = ひだり・1P、1 = みぎ・2P）</param>
    /// <param name="keySet">その人が叩くキーの組</param>
    /// <param name="takenProfiles">ほかの人が使っているデータ（選べない）</param>
    public void Open(int side, int keySet, IEnumerable<int> takenProfiles, bool overlay)
    {
        this.side = side;
        this.keySet = keySet;
        this.overlay = overlay;
        taken.Clear();
        foreach (int slot in takenProfiles) taken.Add(slot);
        Active = true;
        opened = 0;
        communication = -1;
        successVoice = naming = selectionChanged = false;
        error = "";

        PlayerDataStore.Reload();
        NamePlate.Preload();
        MenuSkin.Preload(Folder, new[] { "DataLoad_Panel.png", "DataLoad_Text.png", "PlayerData_Panel.png", "Cursor_Left.png", "Cursor_Right.png" });
        MenuSkin.Preload(MenuSkin.SongFolder, SongRecordPanel.Images);
        FontManager.PreloadSkinMain(new[] { "使用するデータを選んでくださいプレイヤー123記録なしおに裏譜面クリアフルコンボドンダフル記録が読めません左右カッえらぶドン決定戻る終了EDCBAS読み込み中読み込みが完了しました新しく遊ぶ別のプレイヤーが使用中です登録済みのデータ" });
        PrepareGuide();
        for (int i = 0; i < 3; i++)
        {
            registered[i] = PlayerDataStore.HasName(i + 1);
            totals[i] = null;
            errors[i] = "";
            try { totals[i] = SongHistory.PreviewProfile(i + 1, ConfigManager.Game.RecordGroup); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { errors[i] = "記録が読めません"; Console.WriteLine($"[Entry] {i + 1}: {ex.Message}"); }
        }
        // 最初は、前に使っていたデータ。ほかの人が使っていたら、空いている最初のデータ
        int start = LocalSession.Joined[side] ? LocalSession.Profiles[side] : LocalSession.Count == 0 ? SongHistory.ActiveProfile : 0;
        if (start < 1 || start > 3 || taken.Contains(start)) start = Enumerable.Range(1, 3).First(s => !taken.Contains(s));
        cursor = start - 1;
    }

    public void Dispose()
    {
        controlGuide?.Unload(); controlGuide = null;
        guideSkin = -1;
        Active = false;
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

    public Result Update(float dt)
    {
        if (!Active) return Result.None;
        selectionChanged = false;
        elapsed += dt;
        opened += dt;
        PrepareGuide();
        if (Game.IsSettingsOpen) return Result.None;
        if (communication >= 0)
        {
            communication += dt;
            if (!successVoice && communication >= 2.4f)
            { successVoice = true; MenuVoice.Play("Sound/Entry/DataLoadSuccess.ogg"); }
            if (communication < CommunicationEnd) return Result.None;
            // ここで初めて参加したことにする
            LocalSession.Join(side, keySet, cursor + 1);
            Active = false;
            return Result.Finished;
        }
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
                    OpenSetup();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { nameInput.Error = "保存できませんでした：" + ex.Message; }
            }
            return Result.None;
        }

        if (setup) { UpdateSetup(dt); return Result.None; }

        int move = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.Left) || InputManager.IsKeySetPressed(DrumPart.LeftKa, keySet)) move--;
        if (Raylib.IsKeyPressed(KeyboardKey.Right) || InputManager.IsKeySetPressed(DrumPart.RightKa, keySet)) move++;
        // 選択を動かした入力を、同じフレームの決定にも使わない。
        if (move != 0) { Move(move); return Result.None; }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || InputManager.IsKeySetDonPressed(keySet)) Decide();
        return Result.None;
    }

    /// <summary>Esc。やめて閉じたら true（名前入力を閉じただけ・通信中で戻れないときは false）</summary>
    public bool Back()
    {
        if (!Active) return false;
        if (naming) { if (!WindowsIme.IsComposing) naming = false; return false; }
        if (communication >= 0) return false;
        // はじめの設定：1つ上の行に戻るだけ（名前はもう保存してあるので、やめても「新しく遊ぶ」には戻らない）
        if (setup) { if (setupRow > 0) { setupRow--; MenuSounds.Ka(); } return false; }
        Active = false;
        MenuSounds.Ka();
        return true;
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
        if (communication >= 0 || selectionChanged) return;
        if (opened < .2f) return;
        if (!string.IsNullOrEmpty(errors[cursor])) { error = errors[cursor]; return; }
        if (taken.Contains(cursor + 1)) { error = "別のプレイヤーが使用中です"; return; }
        if (!PlayerDataStore.HasName(cursor + 1)) { naming = true; nameInput.Open(); return; }
        try { PlayerDataStore.NamePlate(cursor + 1); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { error = "名前の設定が読めません：" + ex.Message; return; }
        try
        {
            // メニューを動かす人（最初に入った人）のデータだけ「今のデータ」にする。
            // あとから入る人は読めるかを確かめるだけ（選曲の途中で、今のデータを入れ替えない）
            bool controller = LocalSession.Count == 0 || LocalSession.Joined[side] && LocalSession.Controller == side;
            if (controller) SongHistory.SelectProfile(cursor + 1);
            else SongHistory.PreviewProfile(cursor + 1, ConfigManager.Game.RecordGroup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { error = "記録が読めません"; Console.WriteLine($"[Entry] {ex.Message}"); return; }
        communication = 0;
        successVoice = false;
        MenuSounds.Don();
    }

    // うしろの暗い幕。重ねているときだけ、出るとき・消えるときにぼかす
    //   エントリーでは、ずっと同じ濃さ（このあとモード選択が、同じ濃さの幕を消していくので）
    private float Dim
    {
        get
        {
            if (!overlay) return 1f;
            float fade = Math.Clamp(opened / DimFadeSeconds, 0f, 1f);
            if (communication >= 0) fade = Math.Min(fade, Math.Clamp((CommunicationEnd - communication) / .2f, 0f, 1f));
            return fade;
        }
    }

    public void Draw(bool interactive)
    {
        if (!Active) return;
        Gui.ResetClip();
        Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, (int)(DimAlpha * Dim)));
        if (communication >= 0) DrawCommunication();
        else if (naming) nameInput.Draw();
        else if (setup) DrawSetup(interactive && !Game.IsSettingsOpen);
        else DrawProfiles(interactive && !Game.IsSettingsOpen, overlay ? Math.Clamp(opened / DimFadeSeconds, 0f, 1f) : 1f);
    }

    private void OpenSetup()
    {
        setup = true;
        setupRow = 0;
        setupFile = PlayerSettings.ForNewData();
        hitSoundBefore = ConfigManager.Game.HitSound;
    }

    private void CloseSetup()
    {
        setup = false;
        ConfigManager.Game.HitSound = hitSoundBefore;
        PlayerSettings.Save(cursor + 1, setupFile);
        opened = 1f;   // すぐ決定できるように（Decide は、開いた直後の 0.2秒は受け付けない）
        Decide();
    }

    private void UpdateSetup(float dt)
    {
        HitSounds.Update(dt);   // 音色の試し聞きのカッ
        if (Raylib.IsKeyPressed(KeyboardKey.Up) && setupRow > 0) { setupRow--; MenuSounds.Ka(); return; }
        if (Raylib.IsKeyPressed(KeyboardKey.Down) && setupRow < SetupRows - 1) { setupRow++; MenuSounds.Ka(); return; }
        int change = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.Left) || InputManager.IsKeySetPressed(DrumPart.LeftKa, keySet)) change--;
        if (Raylib.IsKeyPressed(KeyboardKey.Right) || InputManager.IsKeySetPressed(DrumPart.RightKa, keySet)) change++;
        if (change != 0) { ChangeSetup(setupRow, change); return; }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || InputManager.IsKeySetDonPressed(keySet))
        {
            if (setupRow < SetupRows - 1) { setupRow++; MenuSounds.Don(); }
            else CloseSetup();
        }
    }

    private void ChangeSetup(int row, int direction)
    {
        switch (row)
        {
            case 0:
            {
                // 演奏オプションの「はやさ」と同じ段階（0 は「譜面どおり」＝1.0倍）
                float[] steps = PlayOptionsPanel.HiSpeedSteps;
                float now = setupFile.HiSpeed > 0f ? setupFile.HiSpeed : 1f;
                setupFile.HiSpeed = direction > 0
                    ? steps.FirstOrDefault(s => s > now + 0.0001f, steps[^1])
                    : steps.LastOrDefault(s => s < now - 0.0001f, steps[0]);
                MenuSounds.Ka();
                break;
            }
            case 1:
            {
                var list = HitSounds.List;
                if (list.Count == 0) break;
                int next = ((list.FindIndex(e => e.Key == setupFile.HitSound) + direction) % list.Count + list.Count) % list.Count;
                setupFile.HitSound = list[next].Key;
                // 試し聞き（ドン → カッ）。HitSounds は今の設定を見るので、その間だけ入れておく
                ConfigManager.Game.HitSound = setupFile.HitSound;
                HitSounds.PlayPreview();
                break;
            }
            case 2:
                setupFile.MasterVolume = Math.Clamp(setupFile.MasterVolume + direction * 5, 0, 100);
                MenuSounds.Ka();
                break;
        }
    }

    private string SetupValue(int row) => row switch
    {
        0 => (setupFile.HiSpeed > 0f ? setupFile.HiSpeed : 1f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " 倍",
        1 => HitSounds.List.FirstOrDefault(e => e.Key == setupFile.HitSound)?.Name ?? "スキン",
        _ => setupFile.MasterVolume.ToString(),
    };

    // はじめの設定（名前入力と同じく、どのスキンでも自前の見た目）
    private void DrawSetup(bool interactive)
    {
        Rectangle panel = SetupPanel;
        SmoothShapes.Rectangle(new Rectangle(panel.X, panel.Y + 8f, panel.Width, panel.Height), 24f, new Color(0, 0, 0, 90));
        SmoothShapes.Rectangle(panel, 24f, Gui.PanelColor);
        SmoothShapes.Border(panel, 24f, 1.5f, Gui.BorderColor);
        FontManager.Draw("はじめの設定", panel.X + 60, panel.Y + 44, 46, Gui.TextColor);
        FontManager.Draw($"{PlayerDataStore.DisplayName(cursor + 1)} さんの設定です。あとで、設定と演奏オプションから変えられます", panel.X + 60, panel.Y + 112, 24, Gui.SubTextColor);

        string[] labels = { "音符のはやさ", "音色（ドン・カッの音）", "音量（全体）" };
        for (int i = 0; i < 3; i++)
        {
            Rectangle row = new(panel.X + 50, panel.Y + 180 + i * 92, panel.Width - 100, 76);
            bool selected = i == setupRow;
            if (interactive && Gui.IsMouseOver(row) && Raylib.GetMouseDelta() != Vector2.Zero) setupRow = i;
            if (selected) Gui.Glow(row, 14f, Gui.AccentColor, .7f);
            SmoothShapes.Rectangle(row, 14f, selected ? Gui.BoxHoverColor : Gui.BoxColor);
            SmoothShapes.Border(row, 14f, selected ? 2f : 1f, selected ? Gui.AccentColor : Gui.BorderColor);
            FontManager.Draw(labels[i], row.X + 28, row.Y + 22, 30, Gui.TextColor);
            Rectangle left = new(row.X + row.Width - 420, row.Y + 14, 56, 48);
            Rectangle right = new(row.X + row.Width - 76, row.Y + 14, 56, 48);
            Gui.DrawCentered(SetupValue(i), new Rectangle(left.X + left.Width, row.Y, right.X - left.X - left.Width, row.Height), 30, Gui.TextColor);
            bool minus = Gui.Button(left, "◀"), plus = Gui.Button(right, "▶");
            if (interactive && (minus || plus)) { setupRow = i; ChangeSetup(i, plus ? 1 : -1); }
        }
        Rectangle start = new(panel.X + panel.Width / 2 - 220, panel.Y + 470, 440, 72);
        if (interactive && Gui.IsMouseOver(start) && Raylib.GetMouseDelta() != Vector2.Zero) setupRow = SetupRows - 1;
        bool go = setupRow == SetupRows - 1 ? Gui.PrimaryButton(start, "これではじめる") : Gui.Button(start, "これではじめる");
        if (interactive && go) { CloseSetup(); return; }
        FontManager.Draw("カッ・←→：変える　　ドン・Enter：次へ　　↑↓：えらぶ　　Esc：1つ戻る", panel.X + 60, panel.Y + panel.Height - 52, 22, Gui.SubTextColor);
    }

    private void DrawProfiles(bool interactive, float alpha = 1)
    {
        // 黄色/緑の2段。動画24秒の中央パネル・カードを原寸で使う。
        if (!MenuSkin.Cell(Folder + "DataLoad_Panel.png", side, 1, 2, DataPanel, Fade(Color.White, alpha)))
            SmoothShapes.Rounded(DataPanel, .08f, 12, Fade(side == 0 ? new Color(238, 192, 55, 255) : new Color(135, 190, 65, 255), alpha));
        if (!MenuSkin.Cell(Folder + "DataLoad_Text.png", 0, 1, 3, DataPanel, Fade(Color.White, alpha)))
            Outlined("使用するデータを選んでください", new(360, 385, 1200, 75), 52, Fade(Color.White, alpha), Fade(Color.Black, alpha));
        Rectangle card = Layout.DataCard;
        if (!MenuSkin.Whole(Folder + "PlayerData_Panel.png", card, Fade(Color.White, alpha)))
            SmoothShapes.Rounded(card, .07f, 12, Fade(new Color(155, 50, 48, 255), alpha));
        if (registered[cursor])
        {
            Outlined($"データ {cursor + 1}", Layout.DataTitle, 36, Fade(Color.White, alpha), Fade(Color.Black, alpha));
            Outlined("登録済みのデータ", Layout.DataSubtitle, 27, Fade(Color.White, alpha), Fade(Color.Black, alpha));
            NamePlate.Draw(cursor + 1, side, Layout.PlatePosition.X, Layout.PlatePosition.Y, alpha: alpha);
            if (totals[cursor] != null)
            {
                Rlgl.PushMatrix();
                Rlgl.Translatef(Layout.SummaryOffset.X, Layout.SummaryOffset.Y, 0);
                SongRecordPanel.DrawSummary(ConfigManager.Game.RecordGroup, alpha, SongHistory.GetSummary(ConfigManager.Game.RecordGroup, cursor + 1));
                Rlgl.PopMatrix();
            }
        }
        else
        {
            // まだ名前が無いデータ：「新しく遊ぶ」だけ（名前・スコアランク・王冠の集計は出さない）
            Outlined("新しく遊ぶ", card, 72, Fade(Color.White, alpha), Fade(Color.Black, alpha));
        }
        DrawArrow(false, Layout.Previous, cursor > 0, interactive, alpha);
        DrawArrow(true, Layout.Next, cursor < 2, interactive, alpha);
        Rlgl.PushMatrix();
        try
        {
            Rlgl.Translatef(Layout.GuideOffset.X, Layout.GuideOffset.Y, 0);
            DrawGuide(alpha);
        }
        finally { Rlgl.PopMatrix(); }
        if (interactive && !selectionChanged && Gui.Clicked(ConfirmArea)) Decide();
        if (error.Length > 0 || !string.IsNullOrEmpty(errors[cursor]))
            Outlined(error.Length > 0 ? error : errors[cursor], new(450, 758, 1020, 45), 27, Color.Red, Color.Black);
    }

    private void DrawCommunication()
    {
        float t = communication;
        if (t < .3f) { DrawProfiles(false, 1 - t / .3f); return; }
        bool success = t >= 2.2f;
        float start = success ? 2.2f : .3f, end = success ? CommunicationEnd : 2.2f;
        float height = Math.Clamp((t - start) / .2f, 0, 1);
        float alpha = Math.Clamp((end - t) / .2f, 0, 1);
        if (height <= 0 || alpha <= 0) return;
        Rectangle panel = new(DataPanel.X, DataPanel.Y + DataPanel.Height * (1 - height) / 2, DataPanel.Width, DataPanel.Height * height);
        if (!MenuSkin.Cell(Folder + "DataLoad_Panel.png", success ? 0 : 1, 1, 2, panel, Fade(Color.White, alpha)))
            SmoothShapes.Rounded(panel, .08f, 12, Fade(success ? Color.Gold : new Color(148, 211, 80, 255), alpha));
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
            controlGuide.Draw(controlGuide.LoopedFrameAt(elapsed), new Vector2(710, 796), alpha);
        }
        else
        {
            SmoothShapes.Rounded(new(777, 812, 366, 90), .25f, 12, Fade(Color.White, alpha));
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
