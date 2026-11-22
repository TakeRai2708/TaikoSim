using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator.Scenes;

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
    private readonly TrainingScrollMotion trainingScroll = new();
    private Judge?[] trainingHistory = Array.Empty<Judge?>();
    private double?[] trainingDeltas = Array.Empty<double?>();
    private int trainingEarlyOk, trainingLateOk;
    private string trainingNotice = "";
    // 再開時に選択した小節より前の音符を描画・判定しないための開始時刻。
    // 通常の特訓開始時や一時停止中の小節移動では制限しない。
    private double trainingNoteStartMs = double.NegativeInfinity;
    private bool IsBeforeTrainingStart(Note note) => isTraining && note.TimeMs + 0.001 < trainingNoteStartMs;
    private double PlaybackRate => isTraining ? trainingTimeline?.Rate ?? 1.0 : playSpeed;

    private static bool IsTrainingKey(KeyboardKey key) => ConfigManager.Key.SystemKeys
        .Any(pair => pair.Key is SystemAction.TrainingPause or SystemAction.TrainingPreviousBar
            or SystemAction.TrainingNextBar or SystemAction.TrainingFaster or SystemAction.TrainingSlower
            && pair.Value == key);

    private static string TrainingKey(SystemAction action) => ConfigManager.Key.SystemKeys
        .GetValueOrDefault(action, KeyboardKey.Null).ToString();

    // 一時停止中の入力を再開後の打鍵にしない。小節移動と速度変更でも時刻の基準を切り直す。
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
        trainingScroll.Reset(nowMs);
        trainingCountdown = 0;
        donSound?.Stop();
        kaSound?.Stop();
        balloonSound?.Stop();
        // 止めたときの音（A-Style の Sound/EnsoGame/Pause.ogg。OpenTaiko も特訓で止めたときに Pause.ogg を鳴らす。
        //   TaikøNauts も一時停止メニューを開いたときに鳴っていた：11/9 の録画 2026-11-09 02-25-14.mp4 の 64.7秒）
        MenuVoice.PlaySe("Sound/EnsoGame/Pause.ogg");
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

        trainingScroll.Update(dt);
        bool space = InputManager.IsSystemPressed(SystemAction.TrainingPause);
        int direction = (TrainingHeld(SystemAction.TrainingNextBar) ? 1 : 0)
            - (TrainingHeld(SystemAction.TrainingPreviousBar) ? 1 : 0);
        int move = trainingPaused ? trainingScroll.Repeat(direction, dt) : 0;
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
                // 選択した小節の2小節前から助走。前の小節の音符は表示も判定もしない。
                int measure = trainingTimeline.At(nowMs - AudioOffsetMs);
                double targetMs = trainingTimeline[measure];
                if (measure == 0) Restart(); // 先頭は従来の開始前の余白を使う
                else
                {
                    // 判定・連打・オートは選択小節を開始点として準備し、時計だけ2小節前に戻す。
                    SeekTraining(targetMs, keepReview: false);
                    nowMs = trainingTimeline.LeadInStart(measure) + AudioOffsetMs;
                }
                trainingNoteStartMs = targetMs;
                trainingScroll.Reset(nowMs);
                trainingPaused = false;
                trainingNotice = measure == 0 ? "曲の頭から再生しています"
                    : $"{Math.Min(2, measure)}小節前から再生しています（選択小節より前の音符は流れません）";
                ClearTrainingInput();
            }
            else PauseTraining();
            return true;
        }

        if (trainingPaused)
        {
            if (move != 0)
            {
                int current = trainingTimeline.At(nowMs - AudioOffsetMs);
                int target = Math.Clamp(current + move, 0, trainingTimeline.Count - 1);
                if (target != current)
                {
                    double shown = trainingScroll.Position;
                    SeekTraining(trainingTimeline[target]);
                    trainingScroll.MoveFrom(shown, nowMs);
                }
            }
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

    private void SeekTraining(double chartMs, bool keepReview = true)
    {
        if (course == null) return;
        var history = trainingHistory;
        var deltas = trainingDeltas;
        int early = trainingEarlyOk, late = trainingLateOk;
        int good = goodCount, ok = okCount, bad = badCount, roll = totalRollHits;
        int previousCombo = combo, previousMaxCombo = maxCombo;
        double gauge = stats.Gauge;
        long score = stats.Score;
        // 履歴を見ているだけでクリア背景が最初に戻ったり、通常背景へ戻ったりしないようにする。
        Restart(keepBackground: keepReview, preserveTrainingVisuals: keepReview);
        if (keepReview)
        {
            trainingHistory = history;
            trainingDeltas = deltas;
            trainingEarlyOk = early; trainingLateOk = late;
            goodCount = good; okCount = ok; badCount = bad; totalRollHits = roll;
            combo = previousCombo; maxCombo = previousMaxCombo;
            stats.SetGauge(gauge);
            stats.SetScore(score);
        }
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
        trainingNotice = "一時停止中は判定履歴を確認できます。再開時に集計をリセットします";
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
        // 特訓モードも他の設定画面と同じ桜色テーマに統一する。
        Color bg = Gui.BackgroundColor, panel = Gui.BoxColor;
        Color border = Gui.BorderColor, accent = Gui.AccentColor, sakura = Gui.SakuraColor;
        double logicalTime = nowMs;
        if (trainingPaused) nowMs = trainingScroll.Position;
        try { DrawPlayScreen(); }
        finally { nowMs = logicalTime; }
        if (course == null || chart == null)
        {
            FontManager.Draw(error, 48, 120, 30, Color.Orange);
            FontManager.Draw("Esc：選曲へ戻る", 48, 190, 28, Gui.TextColor);
            return;
        }

        void Text(string text, float x, float y, float size, Color color, float maxWidth = 1800)
        {
            float width = FontManager.Measure(text, size).X;
            if (width > maxWidth) size *= maxWidth / width;
            FontManager.Draw(text, x, y, size, color);
        }
        // 上半分は通常演奏そのもの。特訓の情報だけ下半分に重ねる。
        Raylib.DrawRectangle(0, 540, 1920, 540, bg);
        Raylib.DrawLine(0, 540, 1920, 540, border);
        double gameMs = nowMs - AudioOffsetMs;
        Text("特訓モード", 48, 550, 28, sakura);
        Text(trainingPaused ? "一時停止中" : "練習中", 1510, 570, 36,
            trainingPaused ? accent : sakura, 360);
        Text($"再生速度  {PlaybackRate:0.0}×", 1510, 630, 32, Gui.TextColor, 360);
        int measure = (trainingTimeline?.At(gameMs) ?? 0) + 1;
        Text($"小節  {measure} / {trainingTimeline?.Count ?? 1}", 690, 600, 42, Gui.TextColor);
        Text($"曲の位置  {Math.Max(0, nowMs) / 1000:0.00} 秒", 690, 664, 28, Gui.SubTextColor);
        Text("今回の判定", 48, 595, 28, Gui.SubTextColor);
        string[] labels = { "良", "可", "不可", "連打数" };
        int[] counts = { goodCount, okCount, badCount, totalRollHits };
        Color[] colors = { new(255, 197, 78, 255), Gui.TextColor, Gui.SubTextColor, sakura };
        for (int i = 0; i < labels.Length; i++)
        {
            float y = 640 + 76 * i;
            Rectangle row = new(48, y, 550, 66);
            SmoothShapes.Rectangle(row, 8f, panel);
            SmoothShapes.Border(row, 8f, 1f, border);
            if (i == 1)
            {
                Text($"−可 {trainingEarlyOk}", 65, y + 16, 26, sakura, 155);
                Text($"可 {okCount}", 245, y + 16, 26, Gui.TextColor, 155);
                Text($"+可 {trainingLateOk}", 425, y + 16, 26, new Color(255, 168, 130, 255), 155);
                continue;
            }
            Text(labels[i], 72, y + 12, 34, colors[i]);
            string value = counts[i].ToString();
            Text(value, 565 - FontManager.Measure(value, 46).X, y + 5, 46, Gui.TextColor);
        }
        Text($"早可 −可 {trainingEarlyOk}    遅可 +可 {trainingLateOk}", 690, 730, 32, sakura);
        Text($"{TrainingKey(SystemAction.TrainingPause)}  一時停止／再開", 690, 970, 28, Gui.TextColor, 650);
        Text($"{TrainingKey(SystemAction.TrainingPreviousBar)} / {TrainingKey(SystemAction.TrainingNextBar)}  小節移動（一時停止中）", 1200, 970, 22, Gui.SubTextColor, 660);
        Text($"{TrainingKey(SystemAction.TrainingSlower)} / {TrainingKey(SystemAction.TrainingFaster)}  速度変更", 1200, 1006, 22, Gui.SubTextColor, 660);
        Raylib.DrawLine(48, 950, 1872, 950, border);
        Text("Esc  選曲へ戻る", 48, 978, 28, Gui.TextColor);
        Text(trainingNotice.Length > 0 ? trainingNotice : "練習の成績は保存されません。低速再生では音程も下がります。", 48, 1035, 20, Gui.SubTextColor, 1800);
        if (audioMessage.Length > 0) Text(audioMessage, 690, 918, 20, Color.Orange, 1160);
    }
    private static bool TrainingHeld(SystemAction action) =>
        ConfigManager.Key.SystemKeys.TryGetValue(action, out KeyboardKey key) && Raylib.IsKeyDown(key);

    private void ResetTrainingReview()
    {
        if (!isTraining) return;
        trainingHistory = new Judge?[course?.Notes.Count ?? 0];
        trainingDeltas = new double?[trainingHistory.Length];
        trainingEarlyOk = trainingLateOk = 0;
    }

    private void RecordTrainingJudge(int index, Judge judge, double? delta)
    {
        if (!isTraining || index < 0 || index >= trainingHistory.Length || delta == null) return;
        trainingHistory[index] = judge;
        trainingDeltas[index] = delta;
        if (judge == Judge.Ok)
        {
            if (delta < 0) trainingEarlyOk++;
            else trainingLateOk++;
        }
    }

    // ノーツの上端からすぐ上。判定枠の150px画像とは別に、小さい文字を置く。
    private void DrawTrainingReview(double viewMs)
    {
        if (!isTraining || !trainingPaused || course == null) return;
        for (int i = 0; i < trainingHistory.Length && i < course.Notes.Count; i++)
        {
            if (trainingHistory[i] is not Judge judge) continue;
            Note note = course.Notes[i];
            if (IsBeforeTrainingStart(note)) continue;
            float x = GetX(note.TimeMs, note.Bpm, note.Scroll, viewMs);
            if (x < LaneX + 30 || x > Screen.Width - 30) continue;
            string text = judge == Judge.Good ? "良" : judge == Judge.Bad ? "不可"
                : trainingDeltas[i] < 0 ? "−可" : "+可";
            Color color = judge == Judge.Good ? new Color(255, 197, 78, 255)
                : judge == Judge.Bad ? Gui.SubTextColor
                : trainingDeltas[i] < 0 ? Gui.SakuraColor : new Color(255, 168, 130, 255);
            const float size = 26;
            float width = FontManager.Measure(text, size).X;
            FontManager.DrawOutlined(text, x - width / 2, NoteY - NoteHalf - size - 3,
                size, color, Color.Black, 2);
        }
    }

}
