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

// エントリー画面
//
//   11/12 たけらい「エントリー画面の画面遷移の流れを変えよう」、11/13「やっぱり」で順番を入れかえた。流れ：
//     ① 黄色い板（太鼓をたたいてスタート）：どちらかのキーの組のドンを叩く
//     ② データ選択 → データ通信中 → 完了（PlayerJoin。いったん、叩いたキーの組と同じ側に入る）
//     ③ 「どちらの太鼓でつかいますか？」：ひだり ／ つかわない ／ みぎ（TaikøNauts と同じ。最初は「つかわない」）
//          ・ひだり = 1P（赤）、みぎ = 2P（青）。叩くキーは、① で叩いた方のまま（LocalSession.MoveSide）
//          ・②でデータを選んだあとなので、どんちゃんと名札をここに出せる（前は②より先に出していたので出せなかった）
//          ・つかわない・Esc：① に戻る（選んだデータも取り消し。11/13 たけらい ③A）
//     ④ モード選択へ
//   ・ここで入れるのは1人だけ。2人目は、モード選択か選曲（曲の一覧）で、まだ使っていない方のキーのドンを叩いて入る
//   ・2人プレイをやめるには、この画面まで戻る（入り直すと全員リセット）
//   ・前は：1P・2P のドンで人数を決める → もう一度ドン → 参加している順にデータ選択 だった
//
//   selectData: true（モード選択で F5・名札をクリック）：今いる人のデータを、順番に選び直す（①② は出さない）
public sealed class EntryScene : Scene
{
    private const string Folder = "Image/01.Entry/";
    private enum Step { Start, Side, Data }

    // 「どちらの太鼓で」の板。11/11 の録画（2026-11-11 23-40-44.mp4 の 104.5秒）と重ねて測った
    //   Playside_Panel.png・Playside_Panel_Overlay.png：1920×1080 を (0,0) に
    //   Playside_Bar.png：400×130 が縦に3つ（ひだり・つかわない・みぎ）。左上が (455 / 760 / 1065, 735)
    //   Playside_Effect.png：400×130 が縦に6つ。上3つ = つかわない用（枠・光・白）、下3つ = ひだり・みぎ用（枠・光・白）
    private static readonly float[] SideX = { 455, 760, 1065 };
    private const float SideY = 735, SideWidth = 400, SideHeight = 130;
    private const float SideOpenSeconds = 0.2f;     // 板が出てくる時間（黄色い板と入れ替わる）
    private const float SideDecideSeconds = 0.3f;   // 決めたあと、白く光る時間
    private static readonly string[] SideNames = { "ひだり", "つかわない", "みぎ" };
    private static Rectangle SideBar(int index) => new(SideX[index], SideY, SideWidth, SideHeight);
    // 押せる所・画像が無いときの箱（画像は、まわりに光のための余白がある）
    private static Rectangle SideBox(int index) => index == 1
        ? new(SideX[1] + 25, SideY + 20, SideWidth - 50, SideHeight - 40)
        : new(SideX[index] + 60, SideY + 20, SideWidth - 120, SideHeight - 40);

    private readonly PlayerJoin join = new();
    private readonly MenuChara chara = new();   // ③ のどんちゃん（選曲と同じくり返しの動き）
    private readonly bool selectData;
    private readonly HashSet<int> chosenProfiles = new();   // 選び直しのとき、もう選んだデータ
    private Step step = Step.Start;
    private int dataPlayerIndex;      // 選び直しのとき、何人目か
    private int sideKeySet;           // ① で叩いたキーの組
    private int sideCursor = 1;       // 0 ひだり・1 つかわない・2 みぎ
    private float sideAge;
    private float sideDecide = -1f;
    private bool leaving;
    private float elapsed;

    public override bool IsWaitingKey => join.Naming;
    public override bool CanOpenSettings => !join.Naming && !join.Busy;

    public override string? MenuBgmFile => MenuBgm.EntryFile;
    // 起動直後は、画面が明るくなりきるまで BGM を待つ（明るくなったら頭から流れる）
    public override bool MenuBgmMuted => SceneManager.IntroPlaying;
    private bool startVoicePending;   // 「太鼓をたたいてスタート」ボイスは、操作できるようになった最初のフレームで鳴らす

    public EntryScene(bool selectData = false) => this.selectData = selectData;

    // 「はじめの設定」から戻ってきた：そのデータの通信中から続ける（SetupScene → ここ）
    private void ResumeFromSetup(PlayerJoin.ResumeInfo info)
    {
        step = Step.Data;
        sideKeySet = info.KeySet;
        join.Resume(info);
    }

    // 名前を入れたあと：この画面を覚えておいて「はじめの設定」へ
    private void GoToSetup()
    {
        PlayerJoin.ResumeInfo info = join.MakeResume();
        PlayerJoin.Pending = info;
        bool reselect = selectData;
        int index = dataPlayerIndex;
        int[] chosen = chosenProfiles.ToArray();
        SceneManager.Simple(new SetupScene(info.Slot, () =>
        {
            EntryScene scene = new(reselect);
            scene.dataPlayerIndex = index;
            foreach (int c in chosen) scene.chosenProfiles.Add(c);
            return scene;
        }));
    }

    public override void Enter()
    {
        if (!selectData && PlayerJoin.Pending == null) LocalSession.Reset();
        MenuSkin.Preload(Folder, new[] { "Background.png", "Header.png", "CreditBar.png", "Background_Fireworks.png", "Background_Effect.png", "Background_Light_Effect.png",
            "Playside_Panel.png", "Playside_Panel_Overlay.png", "Playside_Bar.png", "Playside_Effect.png" });
        FontManager.PreloadSkinMain(new[] { "太鼓をたたいてスタート!どちらの太鼓であそびますか？ひだりつかわないみぎ" });
        if (PlayerJoin.Pending is PlayerJoin.ResumeInfo pending)
        {
            ResumeFromSetup(pending);
            return;
        }
        if (selectData)
        {
            if (LocalSession.Count == 0) LocalSession.Join(0, 0);
            OpenReselect();
        }
        startVoicePending = true;
    }

    // 画面切りかえ中（起動直後のフェードインを含む）：操作は受けないけど、背景の花火は動かしておく
    public override void UpdateBlocked(float dt) => elapsed += dt;

    public override void Exit()
    {
        join.Dispose();
        chara.Dispose();
        MenuVoice.StopLoop();
    }

    // 選び直し：dataPlayerIndex 番目の人のデータ選択を開く
    private void OpenReselect()
    {
        int side = LocalSession.Players[dataPlayerIndex];
        step = Step.Data;
        join.Open(side, LocalSession.KeySets[side], chosenProfiles, overlay: false);
    }

    private void BackToStart()
    {
        step = Step.Start;
        dataPlayerIndex = 0;
        chosenProfiles.Clear();
        sideDecide = -1f;
        LocalSession.Reset();
    }

    public override void Update(float dt)
    {
        elapsed += dt;
        if (startVoicePending) { startVoicePending = false; MenuVoice.Play("Sound/Entry/Start.ogg"); }
        if (leaving) return;
        if (step == Step.Data)
        {
            PlayerJoin.Result result = join.Update(dt);
            if (result == PlayerJoin.Result.Finished) FinishDataLoad();
            else if (result == PlayerJoin.Result.Setup) GoToSetup();
            return;
        }
        if (Game.IsSettingsOpen) return;
        if (step == Step.Side) { chara.Update(dt); UpdateSide(dt); return; }

        // ① 黄色い板：どちらのキーの組でも始められる（Enter は1Pのキーの組）
        if (InputManager.IsKeySetDonPressed(0) || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)) OpenData(0);
        else if (InputManager.IsKeySetDonPressed(1)) OpenData(1);
    }

    // ② データ選択。側はまだ決めないので、叩いたキーの組と同じ側（1Pのキー → ひだり）にいったん入る
    private void OpenData(int keySet)
    {
        step = Step.Data;
        sideKeySet = keySet;
        join.Open(keySet, keySet, Array.Empty<int>(), overlay: false);
        MenuSounds.Don();
    }

    // ③ どちらの太鼓で
    private void OpenSide()
    {
        step = Step.Side;
        sideCursor = 1;
        sideAge = 0f;
        sideDecide = -1f;
        chara.Load();
        chara.SetIdle("songselect_loop", c => c.SongSelectLoopTime);
    }

    // ② どちらの太鼓で
    private void UpdateSide(float dt)
    {
        sideAge += dt;
        if (sideDecide >= 0f)
        {
            sideDecide += dt;
            if (sideDecide < SideDecideSeconds) return;
            sideDecide = -1f;
            if (sideCursor == 1) { BackToStart(); return; }
            // 選んだ側に移って、モード選択へ
            LocalSession.MoveSide(sideKeySet, sideCursor == 0 ? 0 : 1);
            GoToModeSelect();
            return;
        }
        if (sideAge < SideOpenSeconds) return;   // 出てくる途中は押せない（開いたドンで、そのまま決まらないように）
        int move = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.Left) || InputManager.IsKeySetPressed(DrumPart.LeftKa, sideKeySet)) move--;
        if (Raylib.IsKeyPressed(KeyboardKey.Right) || InputManager.IsKeySetPressed(DrumPart.RightKa, sideKeySet)) move++;
        if (move != 0)
        {
            int next = Math.Clamp(sideCursor + move, 0, 2);
            if (next != sideCursor) { sideCursor = next; MenuSounds.Ka(); }
            return;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || InputManager.IsKeySetDonPressed(sideKeySet))
            DecideSide(sideCursor);
    }

    private void DecideSide(int index)
    {
        if (sideDecide >= 0f || sideAge < SideOpenSeconds) return;
        sideCursor = index;
        sideDecide = 0f;
        if (index == 1) MenuSounds.Cancel();
        else MenuSounds.Don();
    }

    private void FinishDataLoad()
    {
        if (selectData)
        {
            chosenProfiles.Add(LocalSession.Profiles[join.Side]);
            if (dataPlayerIndex + 1 < LocalSession.Count)
            {
                dataPlayerIndex++;
                OpenReselect();
                return;
            }
            GoToModeSelect();
            return;
        }
        OpenSide();   // ③ どちらの太鼓で
    }

    private void GoToModeSelect()
    {
        leaving = true;
        try { SongHistory.SelectProfile(LocalSession.Profiles[LocalSession.Controller]); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Console.WriteLine($"[Entry] {ex.Message}"); }
        SongSelectScene.ResetForProfile();
        // 黒くしないで、そのままモード選択へ。板やどんちゃんが順番に出てくる（ModeSelectScene の「エントリーから入ってきたときの出かた」）
        //   11/11 の録画：エントリーとモード選択は同じ背景のままつながっていた。前は 0.3秒ずつ黒くしていた
        SceneManager.ChangeScene(new ModeSelectScene(intro: true));
    }

    public override void OnBack()
    {
        if (leaving) return;
        if (step == Step.Data)
        {
            // データ選択でやめたら、黄色い板まで戻る（全員リセット）。選び直しのときはモード選択へ
            if (!join.Back()) return;
            if (selectData) GoToModeSelect();
            else BackToStart();
            return;
        }
        if (step == Step.Side)
        {
            if (sideDecide >= 0f) return;
            MenuSounds.Ka();
            BackToStart();
            return;
        }
        Game.ShouldExit = true;
    }

    public override void Draw()
    {
        Gui.BeginFrame();
        Gui.ResetClip();
        DrawBackdrop();
        bool interactive = !leaving && !Game.IsSettingsOpen;
        if (step == Step.Start) DrawStart(interactive, 1f);
        else if (step == Step.Side) DrawSide(interactive);
        else if (join.Active) join.Draw(interactive);
        else if (step == Step.Data) DrawStart(interactive, 1f);   // 選び直しで、データ選択をやめた直後の1コマ
        else Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 150));   // 完了のあと、モード選択に替わるまでの1コマ
        MenuSkin.Whole(Folder + "Header.png", new(0, 0, 1920, 1080), Color.White);
        FreePlay();
    }

    // 11/22 TaikøNauts(A-Style)の動画と見比べて合わせた：
    //   ① Background.png → ② 花火 → ③ Background_Effect.png(空だけくり抜いた、ぼかした絵)を全部の上に → ④ 大きい電球の光
    //   ③のおかげで花火は空にだけ見える(屋台の屋根を突き抜けない)。画面全体がふんわりして見えるのも③
    private static readonly (float X, float Y, float Offset)[] Fireworks =
    {
        (1360, 60, 0f), (390, 70, 1.65f), (720, 180, 2.3f), (1150, 260, 4.0f),   // 6.4秒ごとに、この順で上がる
    };
    private const float FireworkCycle = 6.4f, FireworkLife = 1.4f;
    // 大きい電球の光(Background_Light_Effect.png を足し算で重ねる)。中心と大きさ。2秒でふわっと明るくなって戻る
    private static readonly (float X, float Y, float Scale)[] Lights =
    {
        (40, 330, .8f), (1450, 410, .55f), (1640, 410, .68f), (1830, 380, .72f),
    };
    private const float LightPeriod = 2f;

    private void DrawBackdrop()
    {
        if (!MenuSkin.Whole(Folder + "Background.png", new(0, 0, 1920, 1080), Color.White))
            Raylib.DrawRectangleGradientV(0, 0, 1920, 1080, new(22, 32, 52, 255), new(8, 13, 24, 255));
        foreach (var f in Fireworks) DrawFirework(f.X, f.Y, f.Offset);
        MenuSkin.Whole(Folder + "Background_Effect.png", new(0, 0, 1920, 1080), Color.White);
        DrawLights();
    }

    private void DrawFirework(float x, float y, float offset)
    {
        if (SkinManager.GetTexture(Folder + "Background_Fireworks.png") is not Texture2D texture) return;
        float age = elapsed - offset;
        if (age < 0) return;
        age %= FireworkCycle;
        if (age >= FireworkLife) return;
        float t = age / FireworkLife;
        float size = 300 * (.65f + .35f * (1f - (1f - t) * (1f - t)));   // ぱっと開いて、だんだんゆっくり広がる
        float alpha = MathF.Sin(t * MathF.PI) * .7f;
        // 動画では4発とも水色(右のコマ)
        Raylib.DrawTexturePro(texture, new(300, 0, 300, 300), new(x - size / 2, y - size / 2, size, size), Vector2.Zero, 0, Fade(Color.White, alpha));
    }

    private void DrawLights()
    {
        if (SkinManager.GetTexture(Folder + "Background_Light_Effect.png") is not Texture2D texture) return;
        float pulse = .5f - .5f * MathF.Cos(elapsed / LightPeriod * MathF.Tau);
        float strength = .2f + .8f * pulse;
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var l in Lights)
        {
            float w = texture.Width * l.Scale, h = texture.Height * l.Scale;
            Raylib.DrawTexturePro(texture, new(0, 0, texture.Width, texture.Height), new(l.X - w / 2, l.Y - h / 2, w, h), Vector2.Zero, 0, Fade(Color.White, strength));
        }
        Raylib.EndBlendMode();
    }

    // ① 黄色い板。どちらの板にも「太鼓をたたいてスタート!」
    private void DrawStart(bool interactive, float fade)
    {
        if (fade <= 0f) return;
        // 20秒の動画とCreditBarの静止部分を照合：原寸、左上(380,404)。
        for (int i = 0; i < 2; i++)
        {
            Rectangle row = new(Layout.StartRow.X, Layout.StartRow.Y + i * Layout.StartRowSpacing, Layout.StartRow.Width, Layout.StartRow.Height);
            if (!MenuSkin.Cell(Folder + "CreditBar.png", i, 1, 2, row, MenuSkin.White(fade)))
            {
                SmoothShapes.Rounded(new(row.X + 13, row.Y + 14, 1146, 160), .2f, 12, Fade(new Color(255, 226, 61, 255), fade));
                Outlined(i == 0 ? "1P" : "2P", new(row.X + 70, row.Y + 30, 320, 110), 56, Fade(Color.Black, fade), Fade(Color.White, fade));
            }
            const string message = "太鼓をたたいてスタート!";
            // CreditBarの焼き込み文字は、2行目だけセル内で14px上にある。
            Rectangle messageBox = i == 0 ? Layout.StartMessage1 : Layout.StartMessage2;
            Rectangle area = new(row.X + messageBox.X, row.Y + messageBox.Y, messageBox.Width, messageBox.Height);
            Rectangle ink = FontManager.MeasureSkinMainInk(message, Layout.StartMessageSize);
            float size = Layout.StartMessageSize * Math.Min(1, (area.Width - 12) / Math.Max(1, ink.Width));
            ink = FontManager.MeasureSkinMainInk(message, size);
            FontManager.DrawSkinMainOutlined(message, area.X + (area.Width - ink.Width) / 2 - ink.X,
                area.Y + (area.Height - ink.Height) / 2 - ink.Y, size, Fade(Color.White, fade), Fade(Color.Black, fade), Layout.StartMessageOutline);
            // マウス：上の板 = 1Pのキーの組、下の板 = 2Pのキーの組で始める
            if (interactive && Gui.Clicked(row)) { OpenData(i); interactive = false; }
        }
    }

    // ③ どちらの太鼓で
    private const float SideGuideDrop = 44f;
    private void DrawSide(bool interactive)
    {
        float open = Math.Clamp(sideAge / SideOpenSeconds, 0f, 1f);
        Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 150));   // データ選択の幕を、そのまま引きつぐ
        Color tint = Fade(Color.White, open);
        int profile = LocalSession.Profiles[sideKeySet];
        // 決めたあとの白い光：パッと出て、消えていく
        float flash = sideDecide < 0f ? 0f : 1f - Math.Clamp(sideDecide / SideDecideSeconds, 0f, 1f);
        // 11/19 TaikøNauts の録画：「どちらの太鼓」のカーソルの光は点滅しない(止まったまま)
        float pulse = 1f;

        if (MenuSkin.Has(Folder + "Playside_Panel.png", Folder + "Playside_Bar.png"))
        {
            MenuSkin.Whole(Folder + "Playside_Panel.png", new(0, 0, 1920, 1080), tint);
            MenuSkin.Whole(Folder + "Playside_Panel_Overlay.png", new(0, 0, 1920, 1080), tint);
            for (int i = 0; i < 3; i++) MenuSkin.Cell(Folder + "Playside_Bar.png", i, 1, 3, SideBar(i), tint);
            int row = sideCursor == 1 ? 0 : 3;
            Rectangle bar = SideBar(sideCursor);
            MenuSkin.Cell(Folder + "Playside_Effect.png", row + 1, 1, 6, bar, Fade(Color.White, open * pulse));
            MenuSkin.Cell(Folder + "Playside_Effect.png", row, 1, 6, bar, tint);
            if (flash > 0f) MenuSkin.Cell(Folder + "Playside_Effect.png", row + 2, 1, 6, bar, Fade(Color.White, flash));
            // どんちゃんと名札（11/11 の録画 104.5秒で測った：名札の黒いふちが x 800〜1120・y 579〜650 ＝ 0.95倍、
            //   どんちゃんの絵が x 754〜1180・y 253〜516。どんちゃんの倍率と左上は、この大きさから逆算した推測）
            if (open >= 1f)
            {
                // 11/13 たけらい「どんちゃん小っちゃ」：2026-11-13_09-37-49.mp4（16秒）で、止まっているときの黒いふちが
                //   x 780〜1126 だった。0.55倍 → 1.02倍、左上 (735,225) → (554,76)
                chara.Draw(554f, 76f, 1.02f);
                NamePlate.Draw(profile, sideKeySet, 800f, 578f, 0.95f);
            }
            // 下の操作の表示（えらぶ・決定）。本家のスクショで、白い台が x 778〜1142・y 865〜950（データ選択より 44px 下）
            join.DrawGuideOnly(SideGuideDrop, open);
        }
        else
        {
            // スキンに絵が無いとき（Simple など）：Gui.cs の色で描く
            Rectangle panel = new(410, 330, 1100, 580);
            SmoothShapes.Rectangle(new Rectangle(panel.X, panel.Y + 8f, panel.Width, panel.Height), 28f, new Color(0, 0, 0, (int)(90 * open)));
            SmoothShapes.Rectangle(panel, 28f, Gui.WithAlpha(Gui.PanelColor, open));
            SmoothShapes.Border(panel, 28f, 1.5f, Gui.WithAlpha(Gui.BorderColor, open));
            Gui.DrawCentered("どちらの太鼓であそびますか？", new Rectangle(panel.X, panel.Y + 70, panel.Width, 70), 52, Gui.WithAlpha(Gui.TextColor, open));
            Gui.DrawCentered("ひだり = 1P　　みぎ = 2P　　（叩くキーは、今叩いた方のまま）", new Rectangle(panel.X, panel.Y + 170, panel.Width, 40), 26, Gui.WithAlpha(Gui.SubTextColor, open));
            if (open >= 1f) NamePlate.Draw(profile, sideKeySet, 791f, 240f, 1f);   // 名札（絵が無いスキンでも、だれのか分かるように）
            Color[] colors = { new(226, 58, 58, 255), Gui.TrackColor, new(58, 120, 226, 255) };
            for (int i = 0; i < 3; i++)
            {
                Rectangle box = SideBox(i);
                bool selected = i == sideCursor;
                if (selected) Gui.Glow(box, 18f, Color.White, open * pulse);
                SmoothShapes.Rectangle(box, 18f, Gui.WithAlpha(selected ? colors[i] : Gui.Lerp(colors[i], Gui.PanelColor, .55f), open));
                if (selected) SmoothShapes.Border(box, 18f, 3f, Gui.WithAlpha(Color.White, open));
                if (selected && flash > 0f) SmoothShapes.Rectangle(box, 18f, Gui.WithAlpha(Color.White, flash * .8f));
                Gui.DrawCentered(SideNames[i], box, 40, Gui.WithAlpha(selected ? Color.White : Gui.SubTextColor, open));
            }
        }

        if (!interactive || open < 1f || sideDecide >= 0f) return;
        for (int i = 0; i < 3; i++)
            if (Gui.Clicked(SideBox(i))) { DecideSide(i); break; }
    }
}
