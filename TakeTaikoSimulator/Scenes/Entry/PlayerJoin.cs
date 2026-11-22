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
    public enum Result { None, Finished, Cancelled, Setup }

    /// <summary>
    /// 「はじめの設定」（SetupScene）から戻ってきたときに、続きをするための覚え書き。
    ///   名前を入れた → SetupScene へ → 元の画面に戻る → Pending を見て、そのデータで通信中から続ける
    /// </summary>
    public sealed record ResumeInfo(int Side, int KeySet, int Slot, bool Overlay, int[] Taken);
    public static ResumeInfo? Pending { get; set; }

    /// <summary>名前を入れたあと、SetupScene に行く前に呼ぶ（戻ってきたとき Resume できるように）</summary>
    public ResumeInfo MakeResume() => new(side, keySet, Slot, overlay, taken.ToArray());

    /// <summary>SetupScene から戻ってきたとき：データ選択を開いて、そのデータの通信中からすぐ続ける</summary>
    public void Resume(ResumeInfo info)
    {
        Pending = null;
        Open(info.Side, info.KeySet, info.Taken, info.Overlay);
        cursor = Math.Clamp(info.Slot - 1, 0, EntryCount - 1);
        if (PlayerDataStore.IsGuest(info.Slot)) cursor = GuestIndex;
        opened = 1f;
        Decide();
    }

    private const string Folder = "Image/01.Entry/";
    private const float DimAlpha = 150f;
    private const float DimFadeSeconds = 0.15f;
    private const float CommunicationEnd = 4.4f;

    // 並び：データ1・2・3・ゲスト（11/13 たけらい ①B：3枠のまま、空いている枠は「新規作成」、いちばん右に「ゲスト」）
    private const int GuestIndex = 3, EntryCount = 4;
    private readonly SongRecordSummary?[] totals = new SongRecordSummary?[3];
    private readonly string[] errors = new string[3];
    private readonly bool[] registered = new bool[3];
    private bool IsGuestCursor => cursor == GuestIndex;
    /// <summary>今えらんでいるデータの番号（0 = ゲスト）</summary>
    private int Slot => IsGuestCursor ? PlayerDataStore.GuestSlot : cursor + 1;
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
    public bool Busy => Active && communication >= 0;

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
        FontManager.PreloadSkinMain(new[] { "使用するデータを選んでくださいプレイヤー123記録なしおに裏譜面クリアフルコンボドンダフル記録が読めません左右カッえらぶドン決定戻る終了EDCBAS読み込み中読み込みが完了しました新しく遊ぶ別のプレイヤーが使用中です登録済みのデータゲストモードでプレイします。プレイヤーデータを新規作成します。どんちゃん" });
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
        int start = LocalSession.Joined[side] ? LocalSession.Profiles[side] : LocalSession.Count == 0 ? SongHistory.ActiveProfile : -1;
        if (PlayerDataStore.IsGuest(start)) cursor = GuestIndex;
        else
        {
            if (start < 1 || start > 3 || taken.Contains(start)) start = Enumerable.Range(1, 3).First(s => !taken.Contains(s));
            cursor = start - 1;
        }
    }

    public void Dispose()
    {
        controlGuide?.Unload(); controlGuide = null;
        guideSkin = -1;
        Active = false;
    }

    /// <summary>
    /// 下の操作の表示（えらぶ・決定）だけを描く。dy はデータ選択のときの場所から下へずらす量
    ///   「どちらの太鼓で」の画面用（11/14 たけらい：白い台だけ出ていて、中が空っぽだった）
    /// </summary>
    public void DrawGuideOnly(float dy, float alpha = 1)
    {
        PrepareGuide();
        Rlgl.PushMatrix();
        try
        {
            Rlgl.Translatef(Layout.GuideOffset.X, Layout.GuideOffset.Y + dy, 0);
            DrawGuide(alpha);
        }
        finally { Rlgl.PopMatrix(); }
    }

    // データ選択を終えた後も、操作ガイドのアニメーションを進める。
    public void UpdateGuide(float dt)
    {
        elapsed += Math.Max(0, dt);
        PrepareGuide();
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
        UpdateGuide(dt);
        opened += dt;
        if (Game.IsSettingsOpen) return Result.None;
        if (communication >= 0)
        {
            communication += dt;
            if (!successVoice && communication >= 2.4f)
            { successVoice = true; MenuVoice.Play("Sound/Entry/DataLoadSuccess.ogg"); }
            if (communication < CommunicationEnd) return Result.None;
            // ここで初めて参加したことにする
            LocalSession.Join(side, keySet, Slot);
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
                    PlayerDataStore.Save(Slot, plate);
                    registered[cursor] = true;
                    naming = false;
                    // 名前が入ったら「はじめの設定」へ（画面が切りかわる。戻ってきたら Resume）
                    Active = false;
                    return Result.Setup;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { nameInput.Error = "保存できませんでした：" + ex.Message; }
            }
            return Result.None;
        }

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
        Active = false;
        MenuSounds.Ka();
        return true;
    }

    private void Move(int step)
    {
        int next = Math.Clamp(cursor + step, 0, EntryCount - 1);
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
        if (!IsGuestCursor)
        {
            if (!string.IsNullOrEmpty(errors[cursor])) { error = errors[cursor]; return; }
            if (taken.Contains(cursor + 1)) { error = "別のプレイヤーが使用中です"; return; }
            if (!PlayerDataStore.HasName(cursor + 1)) { naming = true; nameInput.Open(); return; }
        }
        try { PlayerDataStore.NamePlate(Slot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { error = "名前の設定が読めません：" + ex.Message; return; }
        try
        {
            // メニューを動かす人（最初に入った人）のデータだけ「今のデータ」にする。
            // あとから入る人は読めるかを確かめるだけ（選曲の途中で、今のデータを入れ替えない）
            bool controller = LocalSession.Count == 0 || LocalSession.Joined[side] && LocalSession.Controller == side;
            if (controller) SongHistory.SelectProfile(Slot);
            else SongHistory.PreviewProfile(Slot, ConfigManager.Game.RecordGroup);
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
        else DrawProfiles(interactive && !Game.IsSettingsOpen, overlay ? Math.Clamp(opened / DimFadeSeconds, 0f, 1f) : 1f);
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
        if (IsGuestCursor || !registered[cursor])
        {
            // ゲスト／まだ名前が無いデータ（11/13 たけらいのスクショ・TaikøNauts と同じ）：
            //   赤いカードの上の方に白い字で一文、その下に白い長丸の名札（ゲストは「どんちゃん」、新規作成は空）
            //   場所はスクショ（1920×1080）で測った：字は中心 x 960・y 551〜575（高さ 24px）、
            //   名札は黒いふち (804,626)〜(1123,697)・白い中 (809,631)、名前の字は高さ 48px
            string message = IsGuestCursor ? "ゲストモードでプレイします。" : "プレイヤーデータを新規作成します。";
            Outlined(message, new Rectangle(card.X, 540, card.Width, 46), 31, Fade(Color.White, alpha), Fade(Color.Black, alpha));
            Rectangle pill = new(804, 626, 320, 72);
            SmoothShapes.Rounded(new Rectangle(pill.X, pill.Y + 3, pill.Width, pill.Height), 1f, 16, Fade(new Color(0, 0, 0, 70), alpha));   // 下の影
            SmoothShapes.Rounded(pill, 1f, 16, Fade(Color.Black, alpha));
            SmoothShapes.Rounded(new Rectangle(pill.X + 5, pill.Y + 5, pill.Width - 10, pill.Height - 10), 1f, 16, Fade(Color.White, alpha));
            if (IsGuestCursor)
                Outlined(PlayerDataStore.GuestName, pill, 40, Fade(Color.White, alpha), Fade(Color.Black, alpha));
        }
        else
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
        DrawArrow(false, Layout.Previous, cursor > 0, interactive, alpha);
        DrawArrow(true, Layout.Next, cursor < EntryCount - 1, interactive, alpha);
        Rlgl.PushMatrix();
        try
        {
            Rlgl.Translatef(Layout.GuideOffset.X, Layout.GuideOffset.Y, 0);
            DrawGuide(alpha);
        }
        finally { Rlgl.PopMatrix(); }
        if (interactive && !selectionChanged && Gui.Clicked(ConfirmArea)) Decide();
        string shownError = error.Length > 0 ? error : IsGuestCursor ? "" : errors[cursor];
        if (shownError.Length > 0) Outlined(shownError, new(450, 758, 1020, 45), 27, Color.Red, Color.Black);
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
