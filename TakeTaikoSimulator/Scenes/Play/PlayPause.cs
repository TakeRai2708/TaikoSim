using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator.Scenes;

// 演奏中の一時停止（ポーズ）
//
//   11/12 たけらい「演奏画面のポーズ作ろうか」。決めたこと：
//     ① 見た目：スキンにポーズの絵があればそれ、無ければ自前の UI（Gui.cs の朱色の見た目）
//        → A-Style のポーズの絵の名前と置き場所がまだ分からないので、今はどのスキンでも自前の UI で出している。
//          絵が分かったら DrawPauseMenu の中で差しかえる
//     ② 中身：つづける ／ 最初からやりなおす ／ 曲選択にもどる
//     ③ つづけるとき：3・2・1 と数えてから再開（「俺は優しいから」。急に音符が来ないように）
//     ④ 段位道場：つづける ／ やめる の2つだけ
//
//   ・Esc で開く。開いている間にもう一度 Esc で「つづける」
//   ・カッ・↑↓ でえらぶ、ドン・Enter で決める。マウスでも押せる
//   ・止まっている間は、曲も時計も判定も止まる。背景やどんちゃんは動いたまま（特訓の一時停止と同じ作り）
//   ・特訓モード（自分の一時停止がある）・2人プレイ・演奏が終わったあとは、今までどおり Esc ですぐ戻る
//   ・前は、ふつうの演奏にポーズが無くて、Esc を押すとすぐ選曲に戻っていた
public partial class PlayScene
{
    private const float PauseCountdownSeconds = 3f;   // つづけるときに数える秒
    private const float PauseOpenSeconds = 0.2f;      // メニューが出る時間（下から 8px 上がりながら）

    private bool pauseOpen;
    private int pauseCursor;
    private float pauseAge;          // メニューを開いてからの秒
    private float pauseCountdown;    // 再開までの残り秒（0 なら数えていない）

    private string[] PauseItems => dan != null
        ? new[] { "つづける", "やめる" }
        : new[] { "つづける", "最初からやりなおす", "曲選択にもどる" };

    /// <summary>今、ポーズで止まっているか（メニューが出ている・再開のカウント中）</summary>
    private bool PauseActive => pauseOpen || pauseCountdown > 0f;

    // ポーズできる場面か
    private bool CanPause => !isTraining && !localPlay && error.Length == 0 && course != null
        && !end.Started && !danEnd.Started && endFadeElapsed < 0f;

    private void OpenPause()
    {
        pauseOpen = true;
        pauseCursor = 0;
        pauseAge = 0f;
        pauseCountdown = 0f;
        song?.Stop();
        songStarted = false;
        donSound?.Stop();
        kaSound?.Stop();
        balloonSound?.Stop();
        foreach (var voice in comboVoices.Values) voice.Stop();
        MenuVoice.PlaySe("Sound/EnsoGame/Pause.ogg");   // 特訓の一時停止と同じ音（スキンに無ければ鳴らない）
        ClearTrainingInput();
    }

    // 「つづける」：メニューを閉じて、3・2・1 と数える
    private void ResumePause()
    {
        pauseOpen = false;
        pauseCountdown = PauseCountdownSeconds;
        ClearTrainingInput();
    }

    private void ChoosePause(int index)
    {
        MenuSounds.Don();
        string item = PauseItems[index];
        if (item == "つづける") ResumePause();
        else if (item == "最初からやりなおす")
        {
            pauseOpen = false;
            pauseCountdown = 0f;
            Restart();
        }
        else QuitPlay();
    }

    // 止まっている間の更新。止まっていたら true（Update はそこで終わる）
    private bool UpdatePause(float dt)
    {
        if (!PauseActive) return false;

        // 見た目だけ動かす（時計と判定は進めない）
        hud.Update(dt, stats.Score);
        effects.Update(dt);
        branchLane.Update(dt);
        UpdatePlayAmbience(dt);
        judgeTimer = Math.Max(0, judgeTimer - dt);

        if (pauseOpen)
        {
            pauseAge += dt;
            int count = PauseItems.Length;
            bool up = Raylib.IsKeyPressed(KeyboardKey.Up) || InputManager.IsDrumPressed(DrumPart.LeftKa);
            bool down = Raylib.IsKeyPressed(KeyboardKey.Down) || InputManager.IsDrumPressed(DrumPart.RightKa);
            if (up) { pauseCursor = (pauseCursor + count - 1) % count; MenuSounds.Ka(); }
            else if (down) { pauseCursor = (pauseCursor + 1) % count; MenuSounds.Ka(); }
            else if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || InputManager.IsDonPressed())
                ChoosePause(pauseCursor);
        }
        else
        {
            // 数え終わったら、止めた所から続き（曲は Update の UpdateSongSync が鳴らし直す）
            pauseCountdown = Math.Max(0f, pauseCountdown - dt);
        }
        ClearTrainingInput();   // 止まっている間に叩いた分が、再開した瞬間に判定されないように
        return true;
    }

    // Esc：ポーズを開く・閉じる。ポーズで受け取ったら true
    private bool HandlePauseBack()
    {
        if (pauseOpen) { MenuSounds.Ka(); ResumePause(); return true; }
        if (pauseCountdown > 0f) { OpenPause(); return true; }   // 数えている間にもう一度 Esc：メニューに戻る
        if (!CanPause) return false;
        OpenPause();
        return true;
    }

    private void DrawPause()
    {
        if (pauseCountdown > 0f && !pauseOpen)
        {
            DrawPauseCountdown();
            return;
        }
        if (!pauseOpen) return;
        DrawPauseMenu();
    }

    // スキンのポーズの絵（A-Style：04.EnsoGame/Pause/）
    //   11/14 たけらいがくれた TaikøNauts のスクショ（1920×1080）に絵を重ねて測った：
    //     Panel.png（1920×1080）… 画面全体にそのまま (0,0)。「ポーズメニュー」の字と黄色い板が入っている
    //     Bar.png（1200×450）… えらんでいない項目。150px ずつ3段（つづける・やりなおす・曲選択）。i 段目を (360, 265 + 200×i)
    //     Select_Bar.png（1300×450）… えらんでいる項目（オレンジ）。i 段目を (310, 265 + 200×i)
    //     うしろの演奏画面は 0.7 倍の明るさ（黒を 30% 重ねる）
    //   段位（つづける・やめる の2つ）は字が合わないので、今までの自前の UI のまま
    private const string PauseFolder = "Image/04.EnsoGame/Pause/";
    private bool DrawPauseImages(float ease, bool open)
    {
        if (dan != null || SkinManager.GetTexture(PauseFolder + "Panel.png") is not Texture2D panel
            || SkinManager.GetTexture(PauseFolder + "Bar.png") is not Texture2D bar
            || SkinManager.GetTexture(PauseFolder + "Select_Bar.png") is not Texture2D select) return false;
        Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, (int)(77 * ease)));
        float rise = 8f * (1f - ease);
        Color tint = new Color(255, 255, 255, (int)(255 * ease));
        Raylib.DrawTexturePro(panel, new Rectangle(0, 0, panel.Width, panel.Height), new Rectangle(0, rise, Screen.Width, Screen.Height), Vector2.Zero, 0f, tint);
        string[] items = PauseItems;
        for (int i = 0; i < items.Length; i++)
        {
            bool on = i == pauseCursor;
            Texture2D tex = on ? select : bar;
            float x = on ? 310f : 360f, y = 265f + 200f * i + rise;
            Raylib.DrawTexturePro(tex, new Rectangle(0, 150 * i, tex.Width, 150), new Rectangle(x, y, tex.Width, 150), Vector2.Zero, 0f, tint);
            if (!open) continue;
            Rectangle hit = new Rectangle(588f, y + 25f, 744f, 100f);   // 押せる場所（オレンジの板の大きさ）
            if (Gui.IsMouseOver(hit) && Raylib.GetMouseDelta() != Vector2.Zero && pauseCursor != i) pauseCursor = i;
            if (Gui.IsMouseOver(hit) && Raylib.IsMouseButtonPressed(MouseButton.Left)) { ChoosePause(i); return true; }
        }
        return true;
    }

    // 自前の UI のメニュー（まんなかのパネル）
    private void DrawPauseMenu()
    {
        float open = Math.Clamp(pauseAge / PauseOpenSeconds, 0f, 1f);
        float ease = 1f - MathF.Pow(1f - open, 3f);   // easeOutCubic
        if (DrawPauseImages(ease, open >= 1f)) return;
        Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, (int)(165 * ease)));

        string[] items = PauseItems;
        const float width = 620f, itemHeight = 76f, gap = 16f, pad = 44f, titleHeight = 92f;
        float height = pad + titleHeight + items.Length * itemHeight + (items.Length - 1) * gap + pad;
        Rectangle panel = new Rectangle((Screen.Width - width) / 2f, (Screen.Height - height) / 2f + 8f * (1f - ease), width, height);

        Gui.BeginFrame();
        Gui.ResetClip();
        SmoothShapes.Rectangle(new Rectangle(panel.X, panel.Y + 8f, panel.Width, panel.Height), 24f, new Color(0, 0, 0, (int)(90 * ease)));   // 影
        UiTheme.FillPanel(panel, 24f, ease);
        SmoothShapes.Border(panel, 24f, 1.5f, Gui.WithAlpha(Gui.BorderColor, ease));

        Gui.DrawCentered("一時停止", new Rectangle(panel.X, panel.Y + pad - 6f, panel.Width, 52f), 40, Gui.WithAlpha(Gui.TextColor, ease));
        string sub = chart != null && chart.Title.Length > 0 ? chart.Title : "";
        if (sub.Length > 0)
            Gui.DrawCentered(sub, new Rectangle(panel.X + pad, panel.Y + pad + 46f, panel.Width - pad * 2f, 30f), 22, Gui.WithAlpha(Gui.SubTextColor, ease));

        if (open < 1f) return;   // 出てくる途中は、押せない・項目もまだ出さない（パッと出るより落ち着いて見える）
        for (int i = 0; i < items.Length; i++)
        {
            Rectangle box = new Rectangle(panel.X + pad, panel.Y + pad + titleHeight + i * (itemHeight + gap), panel.Width - pad * 2f, itemHeight);
            if (Gui.IsMouseOver(box) && Raylib.GetMouseDelta() != Vector2.Zero && pauseCursor != i) pauseCursor = i;
            bool clicked = i == pauseCursor ? Gui.PrimaryButton(box, items[i]) : Gui.Button(box, items[i]);
            if (clicked) { ChoosePause(i); return; }
        }
        // 11/26 たけらい「ここの説明書きいらない」：下の操作の説明は出さない
    }

    // 再開の前の 3・2・1（1秒ごとに、数字が少し大きい所から縮んで出る）
    private void DrawPauseCountdown()
    {
        int number = (int)MathF.Ceiling(pauseCountdown);
        float inSecond = number - pauseCountdown;   // この数字が出てからの秒（0→1）
        float pop = 1f - MathF.Pow(1f - Math.Clamp(inSecond / 0.25f, 0f, 1f), 3f);
        float size = 220f * (1.25f - 0.25f * pop);
        float alpha = Math.Clamp(inSecond / 0.1f, 0f, 1f) * Math.Clamp((1f - inSecond) / 0.2f, 0f, 1f);

        Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 70));
        Vector2 center = new Vector2(Screen.Width / 2f, Screen.Height / 2f);
        Gui.GlowCircle(center, 150f, Gui.AccentColor, 0.8f * alpha);
        SmoothShapes.Circle(center, 150f, Gui.WithAlpha(Gui.PanelColor, 0.92f * alpha));
        // まわりの輪が、1秒かけて減っていく
        SmoothShapes.Ring(center, 142f, 150f, Gui.WithAlpha(Gui.BorderColor, alpha));
        Raylib.DrawRing(center, 142f, 150f, -90f, -90f + 360f * (1f - inSecond), 96, Gui.WithAlpha(Gui.AccentColor, alpha));
        Gui.DrawCentered(number.ToString(), new Rectangle(center.X - 150f, center.Y - 150f, 300f, 300f), size, Gui.WithAlpha(Gui.TextColor, alpha));
    }
}
