using Raylib_cs;
using TaikoSim.Charts;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.UI;

namespace TaikoSim.Scenes;

// 選曲は共通、画面は特訓専用。音符と判定は通常演奏と同じ処理を使う。
public sealed class TrainingScene : PlayScene
{
    public TrainingScene(string path, CourseType? course = null)
        : base(path, course, returnToSelect: true, training: true) { }
}

public partial class PlayScene
{
    private readonly bool isTraining;
    private TrainingTimeline? trainingTimeline;
    private bool trainingPaused;
    private float trainingCountdown;
    private string trainingNotice = "";
    private double PlaybackRate => isTraining ? trainingTimeline?.Rate ?? 1.0 : 1.0;

    private static bool IsTrainingKey(KeyboardKey key) => ConfigManager.Key.SystemKeys
        .Any(pair => pair.Key is SystemAction.TrainingPause or SystemAction.TrainingPreviousBar
            or SystemAction.TrainingNextBar or SystemAction.TrainingFaster or SystemAction.TrainingSlower
            && pair.Value == key);

    private static string TrainingKey(SystemAction action) => ConfigManager.Key.SystemKeys
        .GetValueOrDefault(action, KeyboardKey.Null).ToString();

    // ポーズ中の入力を再開後の打鍵にしない。小節移動と速度変更でも時刻の基準を切り直す。
    private void ClearTrainingInput()
    {
        while (Raylib.GetKeyPressed() != 0) { }
        windowsInput.Reset();
        inputScheduler.Reset();
    }

    private void PauseTraining()
    {
        song?.Stop();
        songStarted = false;
        trainingPaused = true;
        trainingCountdown = 0;
        donSound?.Stop();
        kaSound?.Stop();
        balloonSound?.Stop();
        foreach (var voice in comboVoices.Values) voice.Stop();
        ClearTrainingInput();
    }

    private bool UpdateTrainingControls(float dt)
    {
        if (course == null || trainingTimeline == null) return false;
        if (!Raylib.IsWindowFocused())
        {
            if (!trainingPaused) PauseTraining();
            ClearTrainingInput();
            return true;
        }

        bool space = InputManager.IsSystemPressed(SystemAction.TrainingPause);
        int move = (InputManager.IsSystemPressed(SystemAction.TrainingNextBar) ? 1 : 0)
            - (InputManager.IsSystemPressed(SystemAction.TrainingPreviousBar) ? 1 : 0);
        int speed = (InputManager.IsSystemPressed(SystemAction.TrainingFaster) ? 1 : 0)
            - (InputManager.IsSystemPressed(SystemAction.TrainingSlower) ? 1 : 0);

        if (speed != 0)
        {
            int wanted = Math.Clamp(trainingTimeline.SpeedTenths + speed, 5, 10);
            if (wanted != trainingTimeline.SpeedTenths)
            {
                // 曲の時刻はそのままにして、次のフレームから新しい速度で鳴らし直す。
                song?.Stop();
                songStarted = false;
                if (song == null || song.SetPlaybackRate(wanted / 10.0))
                {
                    trainingTimeline.SetSpeed(wanted);
                    trainingNotice = "";
                }
                else trainingNotice = "音声の速度を変更できませんでした。元の速度を保ちます。";
                ClearTrainingInput();
                return true;
            }
        }

        if (space)
        {
            if (trainingPaused)
            {
                // 小節の先頭で突然叩かせず、ひとつ前の小節から流し直す。
                int measure = trainingTimeline.At(nowMs - AudioOffsetMs);
                if (measure == 0) Restart(); // OFFSETと通常の開始前の空白をそのまま使う
                else SeekTraining(trainingTimeline[measure - 1]);
                trainingPaused = false;
                trainingNotice = "1小節前から再生しています";
                ClearTrainingInput();
            }
            else PauseTraining();
            return true;
        }

        if (trainingPaused)
        {
            if (move != 0) SeekTraining(trainingTimeline[trainingTimeline.Move(nowMs - AudioOffsetMs, move)]);
            ClearTrainingInput();
            return true;
        }
        if (trainingCountdown > 0)
        {
            trainingCountdown = Math.Max(0, trainingCountdown - dt);
            ClearTrainingInput();
            return true;
        }
        return false;
    }

    private void SeekTraining(double chartMs)
    {
        if (course == null) return;
        Restart();
        nowMs = chartMs + AudioOffsetMs;
        trainingPaused = true;
        trainingCountdown = 0;

        // 飛ばした音符は採点しない。戻ったときは判定・連打・風船・演出を作り直す。
        for (int i = 0; i < course.Notes.Count; i++)
        {
            Note note = course.Notes[i];
            if (IsHittable(note) && note.TimeMs < chartMs) judges[i] = Judge.Bad;
            if ((note.IsRoll || note.IsBalloon) && note.EndTimeMs <= chartMs) balloonPopped[i] = true;
        }
        while (nextHittable < hittable.Length && course.Notes[hittable[nextHittable]].TimeMs < chartMs) nextHittable++;
        // 過去の分岐は成績0で再評価。先の分岐は、ここからの練習の成績で決める。
        branches.Advance(chartMs);
        while (autoHits.TryPeek(out _, out double time) && time < chartMs) autoHits.Dequeue();
        if (autoPlay && autoRollPerSecond > 0)
        {
            foreach (int i in rolls)
            {
                Note note = course.Notes[i];
                if (note.TimeMs >= chartMs || note.EndTimeMs <= chartMs) continue;
                int tick = (int)Math.Ceiling((chartMs - note.TimeMs) * autoRollPerSecond / 1000.0);
                double at = note.TimeMs + tick * 1000.0 / autoRollPerSecond;
                if (at < note.EndTimeMs) autoHits.Enqueue((i, tick), at);
            }
        }
        trainingNotice = "移動先からの判定を数え直します";
        ClearTrainingInput();
    }

    private void UpdateTrainingEnd()
    {
        if (course == null) return;
        double last = course.Notes.Count == 0 ? course.EndTimeMs
            : Math.Max(course.EndTimeMs, course.Notes.Max(n => n.EndTimeMs));
        if (nowMs - AudioOffsetMs < last + 1000) return;
        PauseTraining();
        trainingNotice = "曲の終わりです。←で戻って練習を続けられます";
    }

    private void DrawTraining()
    {
        Color bg = new(13, 19, 30, 255), panel = new(22, 31, 46, 255);
        Color border = new(49, 64, 84, 255), blue = new(91, 172, 255, 255);
        Raylib.ClearBackground(bg);
        FontManager.Draw("特訓モード", 48, 32, 32, blue);
        if (course == null || chart == null)
        {
            FontManager.Draw(error, 48, 120, 30, Color.Orange);
            FontManager.Draw("Esc：選曲へ戻る", 48, 190, 28, Color.White);
            return;
        }

        void Text(string text, float x, float y, float size, Color color, float maxWidth = 1800)
        {
            float width = FontManager.Measure(text, size).X;
            if (width > maxWidth) size *= maxWidth / width;
            FontManager.Draw(text, x, y, size, color);
        }
        Text(chart.Title, 48, 88, 48, Color.White, 1400);
        Raylib.DrawRectangle(48, 150, 650, 76, panel);
        Text($"{Chart.GetCourseName(course.Type)}  ★{course.Level}", 68, 162, 40,
            course.Type == CourseType.Edit ? new Color(196, 139, 255, 255) : blue, 455);
        Text(autoPlay ? "AUTO" : "手動", 550, 176, 24, Color.LightGray);
        string state = trainingPaused ? "ポーズ中" : trainingCountdown > 0 ? "再開まで 1" : "練習中";
        Text(state, 1510, 55, 40, trainingPaused ? Color.Orange : blue, 360);
        Text($"再生速度  {PlaybackRate:0.0}×", 1510, 120, 32, Color.White, 360);

        double gameMs = nowMs - AudioOffsetMs;
        double viewMs = gameMs - TaikoSim.Configs.ConfigManager.Game.ChartOffsetMs;
        DrawLane();
        branchLane.Draw(LaneX, LaneY);
        branchLane.DrawText();
        effects.DrawLaneLayer();
        DrawJudgeFrame();
        effects.DrawBehindNotes();
        DrawBarLines(course, viewMs);
        for (int i = course.Notes.Count - 1; i >= 0; i--)
            if (!IsHidden(i)) { DrawNote(course.Notes[i], viewMs); DrawSeText(course.Notes[i], viewMs); }
        DrawBalloonAnime(gameMs);
        // レーン以外は専用の落ち着いた画面。スキンのパネルや背景でUIが隠れないようにする。
        Raylib.DrawRectangle(0, (int)LaneY, (int)LaneX + 5, (int)LaneHeight, panel);
        effects.DrawHitEffects();
        effects.DrawPanel();
        DrawJudgeText();
        // 扇子と数字は通常演奏のまま。タイトルと重ならないように下段へ移す。
        Rlgl.PushMatrix();
        Rlgl.Translatef(700, 570, 0);
        DrawRollPopup();
        Rlgl.PopMatrix();
        // 風船は専用の位置へ。扇子用の移動量を使わない。
        Rlgl.PushMatrix();
        Rlgl.Translatef(690 - BalloonFrameX, 735 - BalloonFrameY, 0);
        DrawBalloonPopup(gameMs);
        Rlgl.PopMatrix();
        Text($"連打数  {totalRollHits}", 1510, 745, 28, Color.LightGray);

        int measure = (trainingTimeline?.At(gameMs) ?? 0) + 1;
        Text($"小節  {measure} / {trainingTimeline?.Count ?? 1}", 690, 600, 42, Color.White);
        Text($"曲の位置  {Math.Max(0, nowMs) / 1000:0.00} 秒", 690, 664, 28, Color.LightGray);
        Text("今回の判定", 48, 595, 28, Color.LightGray);
        string[] labels = { "良", "可", "不可" };
        int[] counts = { goodCount, okCount, badCount };
        Color[] colors = { new(255, 197, 78, 255), Color.White, new(142, 177, 255, 255) };
        for (int i = 0; i < 3; i++)
        {
            float y = 648 + 90 * i;
            Raylib.DrawRectangle(48, (int)y, 550, 78, panel);
            Text(labels[i], 72, y + 12, 42, colors[i]);
            string value = counts[i].ToString();
            Text(value, 565 - FontManager.Measure(value, 52).X, y + 5, 52, Color.White);
        }
        Text(lastDeltaMs is double delta ? $"直近のズレ  {delta / PlaybackRate:+0.0;-0.0;0.0} ms" : "直近のズレ  —", 690, 730, 32, blue);
        Text($"{TrainingKey(SystemAction.TrainingPause)}  ポーズ／再開", 690, 970, 28, Color.White, 650);
        Text($"{TrainingKey(SystemAction.TrainingPreviousBar)} / {TrainingKey(SystemAction.TrainingNextBar)}  小節移動（ポーズ中）", 1200, 970, 22, Color.LightGray, 660);
        Text($"{TrainingKey(SystemAction.TrainingSlower)} / {TrainingKey(SystemAction.TrainingFaster)}  速度変更", 1200, 1006, 22, Color.LightGray, 660);
        Raylib.DrawLine(48, 950, 1872, 950, border);
        Text("Esc  選曲へ戻る", 48, 978, 28, Color.White);
        Text(trainingNotice.Length > 0 ? trainingNotice : "練習の成績は保存されません。低速再生では音程も下がります。", 48, 1035, 20, Color.LightGray, 1800);
        if (audioMessage.Length > 0) Text(audioMessage, 690, 918, 20, Color.Orange, 1160);
    }
}
