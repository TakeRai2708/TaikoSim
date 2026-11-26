using System.Diagnostics;
using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator.Scenes;

// タイミング調整（設定の「演奏」→［タイミングを測る］から）
//   11/26 たけらい：音オフセットを自分で何 ms にすればいいか分かりにくい → 叩いて測る
//   ・BPM120 のメトロノーム(4拍目ごとに高い音)に合わせて、ドンを叩く
//   ・メトロノームは曲と同じしくみ(Song)で鳴らすので、出力方式の遅れも全部込みで測れる
//   ・時計も演奏画面と同じ合わせ方(曲の再生位置に少しずつ合わせる)。叩いた時刻も演奏画面と同じく Windows のキーの時刻を使う
//   ・最初の4回は慣れ用で捨てて、残りのズレの中央値を「音オフセット」にする
//     (演奏画面の判定は「時計 − 音オフセット」なので、いつも +20ms 遅く叩くなら音オフセット +20ms でちょうどになる)
public sealed class TimingCalibrationScene : Scene
{
    private const double BeatMs = 500;          // BPM120
    private const double LeadMs = 2000;         // 最初の拍までの時間(音を準備する余裕)
    private const int Beats = 32;               // 鳴らす拍の数(16秒)
    private const int WarmUp = 4;               // 最初の何回を捨てるか
    private const int MinHits = 12;             // これより少ないと結果を出さない
    private const double MatchWindowMs = 220;   // 一番近い拍からこれ以上ずれた打鍵は数えない(拍の間の半分より少し小さく)
    private const double ResyncThresholdMs = 50, SyncRate = 0.1;   // 演奏画面と同じ

    private enum Phase { Ready, Running, Result }

    private Phase phase = Phase.Ready;
    private Song? click;
    private string error = "";
    private double nowMs;
    private bool started;
    private readonly List<double> errors = new();   // 叩いた時刻 − 拍の時刻(ms)
    private readonly HashSet<int> usedBeats = new();
    private readonly WindowsKeyCapture windowsInput = new();
    private double result;
    private float flash;                              // 拍で光る円の明るさ

    private static readonly Rectangle Panel = new(360, 150, 1200, 780);
    private static readonly Vector2 Center = new(960, 470);
    private static readonly Rectangle StartButton = new(1270, 840, 230, 56);
    private static readonly Rectangle SecondButton = new(1020, 840, 230, 56);
    private static readonly Rectangle BackButton = new(420, 840, 190, 56);

    public override bool CanOpenSettings => false;

    public override void Enter()
    {
        Gui.Reset();
        windowsInput.CaptureEnabled = windowsInput.Start();
    }

    public override void Exit()
    {
        click?.Dispose();
        click = null;
        windowsInput.Dispose();
    }

    public override void OnBack()
    {
        if (phase == Phase.Running) { StopRun(); phase = Phase.Ready; return; }
        SceneManager.Simple(new ModeSelectScene());
    }

    // ===== 測る =====

    private void StartRun()
    {
        error = "";
        click?.Dispose();
        string path = Path.Combine(DataPaths.Data("Cache"), "timing-click.wav");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, MakeClickWave());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"メトロノームの音を作れませんでした：{ex.Message}";
            return;
        }
        click = Song.Load(path, out string loadError);
        if (click == null) { error = loadError; return; }
        click.SetVolume((float)ConfigManager.Sound.GetFinalVolume(ConfigManager.Sound.MusicVolume));

        errors.Clear();
        usedBeats.Clear();
        windowsInput.Reset();
        nowMs = -300;   // 少し待ってから鳴らし始める
        started = false;
        phase = Phase.Running;
    }

    private void StopRun()
    {
        click?.Stop();
        started = false;
    }

    public override void Update(float dt)
    {
        flash = Math.Max(0, flash - dt * 4);
        if (phase != Phase.Running || click == null) return;

        // ----- 時計(演奏画面と同じ) -----
        nowMs += dt * 1000.0;
        if (!started)
        {
            if (nowMs >= 0) { click.Play(nowMs); started = true; }
        }
        else if (click.IsPlaying)
        {
            double diff = click.PositionMs - nowMs;
            if (Math.Abs(diff) > ResyncThresholdMs) nowMs += diff;
            else nowMs += diff * SyncRate;
        }

        // 拍で光らせる(見た目だけ。測る値には使わない)
        double beatPhase = (nowMs - LeadMs) / BeatMs;
        if (beatPhase >= 0 && beatPhase < Beats && beatPhase - Math.Floor(beatPhase) < dt * 1000.0 / BeatMs) flash = 1;

        // ----- 叩いた時刻 -----
        long frameClockAt = Stopwatch.GetTimestamp();
        windowsInput.Drain(null, 0, nowMs);
        int key;
        while ((key = Raylib.GetKeyPressed()) != 0)
        {
            KeyboardKey pressed = (KeyboardKey)key;
            if (!IsDon(pressed)) continue;
            long collectedAt = Stopwatch.GetTimestamp();
            WindowsKeyEvent? native = windowsInput.Match(pressed, collectedAt);
            long pressedAt = native?.CapturedAt ?? collectedAt;
            double elapsedMs = Stopwatch.GetElapsedTime(pressedAt, frameClockAt).TotalMilliseconds;
            Record(nowMs - Math.Max(0, elapsedMs));
        }

        // 最後の拍が過ぎたら終わり
        if (nowMs > LeadMs + (Beats - 1) * BeatMs + MatchWindowMs + 300) Finish();
    }

    private void Record(double hitMs)
    {
        int beat = (int)Math.Round((hitMs - LeadMs) / BeatMs);
        if (beat < 0 || beat >= Beats || usedBeats.Contains(beat)) return;
        double diff = hitMs - (LeadMs + beat * BeatMs);
        if (Math.Abs(diff) > MatchWindowMs) return;
        usedBeats.Add(beat);
        if (beat >= WarmUp) errors.Add(diff);
    }

    private void Finish()
    {
        StopRun();
        if (errors.Count < MinHits)
        {
            error = $"叩いた回数が少なかったので測れませんでした（{errors.Count}回。{MinHits}回以上必要）";
            phase = Phase.Ready;
            return;
        }
        List<double> sorted = errors.OrderBy(e => e).ToList();
        result = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
        phase = Phase.Result;
        Gui.Reset();
    }

    private static int Recommended(double median) =>
        Math.Clamp((int)Math.Round(median), GameConfig.OffsetMin, GameConfig.OffsetMax);

    private static bool IsDon(KeyboardKey key)
    {
        int offset = LocalSession.KeySets[LocalSession.MenuPlayer] * 4;
        foreach (DrumPart part in new[] { DrumPart.LeftDon, DrumPart.RightDon })
            if (ConfigManager.Key.DrumKeys.TryGetValue((DrumPart)((int)part + offset), out List<KeyboardKey>? keys) && keys.Contains(key))
                return true;
        return false;
    }

    // ===== 描く =====

    public override void Draw()
    {
        Raylib.ClearBackground(Gui.BackgroundColor);
        UiTheme.FillBackground(new Rectangle(0, 0, Screen.Width, Screen.Height));   // 11/26 背景も斜めのグラデーション
        Gui.BeginFrame();

        UiTheme.FillPanel(Panel, 24);
        SmoothShapes.Border(Panel, 24, 1.5f, Gui.BorderColor);
        FontManager.Draw("タイミング調整", 420, 196, 44, Gui.SakuraColor);
        FontManager.Draw("メトロノームの音に合わせてドンを叩いてください（画面は見なくてOK）", 422, 252, 24, Gui.SubTextColor);

        // まんなかの円：拍で光る
        Gui.GlowCircle(Center, 110, Gui.AccentColor, flash * 0.8f);
        SmoothShapes.Circle(Center, 110, Gui.Lerp(Gui.BoxColor, Gui.AccentColor, flash));
        SmoothShapes.CircleLines(Center, 110, Gui.SakuraColor);

        switch (phase)
        {
            case Phase.Ready: DrawReady(); break;
            case Phase.Running: DrawRunning(); break;
            case Phase.Result: DrawResult(); break;
        }

        if (error.Length > 0) FontManager.Draw(error, 420, 790, 24, UiTheme.P.Error);
        if (Gui.Button(BackButton, "もどる")) OnBack();
    }

    private void DrawReady()
    {
        Gui.DrawCentered("スタート", new Rectangle(Center.X - 110, Center.Y - 30, 220, 60), 34, Gui.TextColor);
        DrawLines(
            $"BPM120 で {Beats} 回鳴ります（4拍目ごとに高い音）。最初の {WarmUp} 回は慣れ用で、数えません。",
            $"今の音オフセット：{ConfigManager.Game.AudioOffsetMs:+0;-0;0} ms　／　出力：{AudioManager.GetTypeName(AudioManager.CurrentType)}");
        if (Gui.PrimaryButton(StartButton, "はじめる")) StartRun();
    }

    private void DrawRunning()
    {
        int remaining = Math.Max(0, Beats - Math.Max(0, (int)Math.Floor((nowMs - LeadMs) / BeatMs)));
        Gui.DrawCentered(remaining.ToString(), new Rectangle(Center.X - 110, Center.Y - 40, 220, 80), 64, Gui.TextColor);
        DrawLines($"叩いた回数：{usedBeats.Count}", "Esc：やめる");
        DrawDots();
    }

    private void DrawResult()
    {
        int value = Recommended(result);
        Gui.DrawCentered($"{value:+0;-0;0} ms", new Rectangle(Center.X - 110, Center.Y - 30, 220, 60), 40, Gui.TextColor);
        int current = ConfigManager.Game.AudioOffsetMs;
        string way = value == current ? "今の設定と同じでした" : value > current ? $"今の設定より {value - current} ms 遅く叩いていました" : $"今の設定より {current - value} ms 早く叩いていました";
        double spread = errors.Count > 1 ? Math.Sqrt(errors.Select(e => (e - result) * (e - result)).Average()) : 0;
        DrawLines($"音オフセットを {value:+0;-0;0} ms にしますか？（{way}）",
            $"{errors.Count}回の中央値。ばらつき ±{spread:0} ms" + (spread > 40 ? "（大きめなので、もう一回測るのがおすすめ）" : ""));
        DrawDots();
        if (Gui.PrimaryButton(StartButton, "反映する"))
        {
            ConfigManager.Game.AudioOffsetMs = value;
            ConfigManager.SaveAll();
            SceneManager.Simple(new ModeSelectScene());
        }
        if (Gui.Button(SecondButton, "もう一回")) StartRun();
    }

    private static void DrawLines(string first, string second)
    {
        Gui.DrawCentered(first, new Rectangle(Panel.X, 650, Panel.Width, 34), 26, Gui.TextColor);
        Gui.DrawCentered(second, new Rectangle(Panel.X, 692, Panel.Width, 30), 22, Gui.SubTextColor);
    }

    // ズレの点(左が早い、右が遅い。±100ms)
    private void DrawDots()
    {
        const float x0 = 560, x1 = 1360, y = 752;
        Raylib.DrawLineEx(new Vector2(x0, y), new Vector2(x1, y), 2, Gui.TrackColor);
        Raylib.DrawLineEx(new Vector2((x0 + x1) / 2, y - 16), new Vector2((x0 + x1) / 2, y + 16), 2, Gui.SubTextColor);
        FontManager.Draw("早い", x0 - 60, y - 14, 22, Gui.MutedTextColor);
        FontManager.Draw("遅い", x1 + 14, y - 14, 22, Gui.MutedTextColor);
        foreach (double e in errors)
        {
            float x = (x0 + x1) / 2 + (float)Math.Clamp(e / 100.0, -1, 1) * (x1 - x0) / 2;
            SmoothShapes.Circle(new Vector2(x, y), 6, Gui.WithAlpha(Gui.SakuraColor, 0.8f));
        }
    }

    // ===== メトロノームの音(WAV をその場で作る) =====

    private static byte[] MakeClickWave()
    {
        const int rate = 44100;
        int total = (int)((LeadMs + Beats * BeatMs + 500) / 1000.0 * rate);
        short[] samples = new short[total];
        for (int beat = 0; beat < Beats; beat++)
        {
            int start = (int)Math.Round((LeadMs + beat * BeatMs) / 1000.0 * rate);
            double freq = beat % 4 == 0 ? 2000 : 1500;
            int length = rate * 30 / 1000;   // 30ms
            for (int i = 0; i < length && start + i < total; i++)
            {
                double t = (double)i / rate;
                double envelope = Math.Exp(-t * 180);   // すぐ立ち上がって、すぐ消える(音の頭がはっきり分かるように)
                samples[start + i] = (short)(Math.Sin(2 * Math.PI * freq * t) * envelope * 26000);
            }
        }
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8); w.Write(36 + total * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(total * 2);
        foreach (short s in samples) w.Write(s);
        w.Flush();
        return stream.ToArray();
    }
}
