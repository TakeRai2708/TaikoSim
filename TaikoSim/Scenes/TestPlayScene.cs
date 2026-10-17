using System.Numerics;
using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Charts;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;

namespace TaikoSim.Scenes
{
    // テスト用：.tja の譜面を読み込んで、曲に合わせて音符を流す画面
    // ドン・カッ(大音符も片手でOK)を叩いて、良・可・不可の判定が出る。連打・風船はまだ数えない
    public class TestPlayScene : Scene
    {
        // ===== 配置(1920×1080) ※動画とスキン画像の大きさから測った値 =====
        private const float LaneX = 494;          // レーンの左端
        private const float LaneY = 276;          // レーン(と左パネル)の上端
        private const float LaneHeight = 264;
        private const float JudgeX = 615;         // 判定枠の中心x
        private const float NoteY = 386;          // 音符の中心y
        private const float BarTop = 288;         // 小節線の上端
        private const float BarBottom = 488;      // 小節線の下端
        private const float SeTextY = 490;        // 音符の下の文字の上端
        // 1拍で進む距離(はやさ1倍)
        // OpenTaiko の標準：1小節(4拍)で 960px(720pの画面) → 1080p だと 1440px → 1拍 360px
        private const float PixelsPerBeat = 360f;

        private const float NoteSize = 195f;      // 音符の画像の大きさ
        private const float NoteHalf = NoteSize / 2f;

        // ===== スキンの画像の場所 =====
        private const string NotesDir = "Image/04.EnsoGame/Notes/Normal/0/";
        private const string JudgeFrameImage = "Image/04.EnsoGame/Notes/JudgementFrame.png";
        private const string LaneImage = "Image/04.EnsoGame/Lane/Background_1P.png";
        private const string PanelImage = "Image/04.EnsoGame/MiniTaiko/Background_1P.png";
        private const string SeNotesImage = "Image/04.EnsoGame/SENotes.png";
        private const string SeRollingImage = "Image/04.EnsoGame/SENotes_Rolling.png";

        // 音符の下の文字(SENotes.png の何段目か。1段 130×40)
        private const int SeDon = 0, SeKa = 4, SeBigDon = 5, SeBigKa = 6, SeRoll = 7, SeRollEnd = 8, SeBalloon = 9, SeKusudama = 10;
        private const float SeWidth = 130f, SeHeight = 40f;

        // 音符の下の文字の大きさ(1 で画像のまま。0.94 = 6％小さく)
        private const float SeScale = 0.94f;
        private const float SeDrawWidth = SeWidth * SeScale;
        private const float SeDrawHeight = SeHeight * SeScale;
        private const float SeDrawY = SeTextY + (SeHeight - SeDrawHeight) / 2f; // 小さくしても上下の真ん中はそのまま
        private static readonly string[] SeFallbackText =
            { "ドン", "ド", "コ", "カ", "カッ", "ドン(大)", "カッ(大)", "連打", "っ!!", "ふうせん", "くすだま" };

        // スキンが無いときの色
        private static readonly Color DonColor = new Color(235, 90, 60, 255);
        private static readonly Color KaColor = new Color(90, 190, 230, 255);
        private static readonly Color RollColor = new Color(240, 180, 40, 255);

        private readonly string path;

        // 遊ぶ難易度(null なら おに → 裏 → … の順で自動で選ぶ)
        private readonly CourseType? courseType;

        // Esc で選曲画面に戻るか(false ならホームに戻る)
        private readonly bool returnToSelect;

        private Chart? chart;
        private Course? course;
        private string error = "";

        // 今の時間(曲の頭が0。マイナスは曲が始まる前)
        private double nowMs;

        // ===== 曲 =====

        // 曲(読み込めなかったら null。そのときは音なしで流す)
        private Song? song;

        // 曲を鳴らし始めたか(時間が0になった瞬間に鳴らす)
        private bool songStarted = false;

        // 曲が読み込めなかった理由など(画面に出す)
        private string audioMessage = "";

        // 曲の位置と大きくズレたら、一気に合わせる境目(ミリ秒)
        private const double ResyncThresholdMs = 50;

        // 少しのズレを1フレームでどれだけ縮めるか(0.1 = 1割ずつ)
        private const double SyncRate = 0.1;

        // ===== 判定 =====

        // 判定の幅(ミリ秒)。OpenTaiko の初期値と同じ
        private const double GoodRangeMs = 25;  // 良
        private const double OkRangeMs = 75;    // 可
        private const double BadRangeMs = 108;  // 不可(これより早く叩いても何も起きない)

        private enum Judge { Good, Ok, Bad } // 良・可・不可(Judge.png の上から順)

        // 音符ごとの判定(null ならまだ。course.Notes と同じ番号)
        private Judge?[] judges = Array.Empty<Judge?>();

        // 叩ける音符(ドン・カッ・大ドン・大カッ)の番号を、時間の順に並べたもの
        private int[] hittable = Array.Empty<int>();

        // hittable の中で、まだ判定していない一番前の場所
        private int nextHittable = 0;

        // 数
        private int goodCount, okCount, badCount, combo, maxCombo;

        // 最後に叩いたときのズレ(ミリ秒。プラスなら遅い)。音オフセットを合わせるときの目安
        private double? lastDeltaMs = null;

        // 平均のズレを出すための合計と回数(色が合っていた叩きだけ数える)
        private double deltaSumMs = 0;
        private int deltaCount = 0;

        // ===== 入力ログ(原因を調べる用) =====

        // 画面の右下に、直近の「叩いた・見逃した」を出す。要らなくなったら false にする
        public static bool ShowInputLog = true;
        private const int InputLogCount = 12;

        // ログ1行分
        //   NoteNumber … 何個目の音符か(1から。空振りなら 0)
        //   Delta      … ズレ(ミリ秒。プラスなら遅い)
        //   Result     … 判定(空振りなら null)
        //   Frame      … 何フレーム目に処理したか(同じフレームの叩きを見分けるため)
        private readonly record struct InputLogEntry(
            double TimeMs, string KeyName, string PartName, bool Don, int NoteNumber, double Delta, Judge? Result, bool Missed, long Frame);

        private readonly List<InputLogEntry> inputLog = new();
        private long frameCount = 0;

        // 音符の番号 → 叩ける音符の中で何個目か(1から。ログの表示用)
        private int[] noteNumbers = Array.Empty<int>();

        // 判定の文字(良・可・不可)の表示
        private Judge? shownJudge = null;
        private float judgeTimer = 0f;
        private const float JudgeShowSeconds = 0.4f;

        // ドン・カッの音
        private SoundEffect? donSound;
        private SoundEffect? kaSound;
        private const string DonSoundFile = "Sound/Taiko/0.Taiko/don.ogg";
        private const string KaSoundFile = "Sound/Taiko/0.Taiko/ka.ogg";

        // 判定の文字の画像(150×150 が縦に3つ：良・可・不可)
        private const string JudgeImage = "Image/04.EnsoGame/Judge.png";
        private const float JudgeImageSize = 150f;

        // 演奏中は設定パネルを開かない
        public override bool CanOpenSettings => false;

        public TestPlayScene(string path, CourseType? courseType = null, bool returnToSelect = false)
        {
            this.path = path;
            this.courseType = courseType;
            this.returnToSelect = returnToSelect;
        }

        // .tja ファイルか
        public static bool IsTja(string path)
        {
            return path.EndsWith(".tja", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }

        // ===== 入る・更新 =====

        public override void Enter()
        {
            try
            {
                chart = TjaParser.Load(path);

                // 選曲画面で選んだ難易度。無ければいつもの順で自動で選ぶ
                course = courseType is CourseType type
                    ? chart.Courses.FirstOrDefault(c => c.Type == type) ?? chart.GetPreferredCourse()
                    : chart.GetPreferredCourse();
                if (course == null) error = "譜面（#START〜#END）が見つかりませんでした";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = $"読み込めませんでした：{ex.Message}";
            }

            LoadSong();
            LoadHitSounds();
            PrepareJudge();
            Restart();
        }

        // 画面から出るとき：曲と効果音を片付ける
        public override void Exit()
        {
            StopSong();
        }

        // スキンの Sound フォルダから、ドン・カッの音を読み込む(無ければ音なし)
        private void LoadHitSounds()
        {
            string? skin = SkinManager.CurrentPath;
            if (skin == null) return;

            donSound = SoundEffect.Load(Path.Combine(skin, DonSoundFile), out _);
            kaSound = SoundEffect.Load(Path.Combine(skin, KaSoundFile), out _);
        }

        // 叩ける音符の一覧を作る
        private void PrepareJudge()
        {
            if (course == null) return;

            hittable = Enumerable.Range(0, course.Notes.Count)
                .Where(i => IsHittable(course.Notes[i]))
                .OrderBy(i => course.Notes[i].TimeMs)
                .ToArray();

            noteNumbers = new int[course.Notes.Count];
            for (int i = 0; i < hittable.Length; i++)
            {
                noteNumbers[hittable[i]] = i + 1;
            }
        }

        // 叩いて判定する音符か(連打・風船はまだ)
        private static bool IsHittable(Note note)
        {
            return note.Type is NoteType.Don or NoteType.Ka or NoteType.BigDon or NoteType.BigKa;
        }

        // ドン(面で叩く音符)か
        private static bool IsDonNote(Note note)
        {
            return note.Type is NoteType.Don or NoteType.BigDon;
        }

        // .tja と同じフォルダにある WAVE: の曲を読み込む
        private void LoadSong()
        {
            if (chart == null) return;

            if (string.IsNullOrWhiteSpace(chart.Wave))
            {
                audioMessage = "WAVE: が書かれていないので、音なしで流します";
                return;
            }

            string folder = Path.GetDirectoryName(path) ?? "";
            song = Song.Load(Path.Combine(folder, chart.Wave), out string loadError);

            if (song == null)
            {
                audioMessage = $"{loadError}（音なしで流します）";
                return;
            }

            // 音量 = Master × Music
            SoundConfig sound = ConfigManager.Sound;
            song.SetVolume((float)sound.GetFinalVolume(sound.MusicVolume));
        }

        // 曲と効果音を止めて片付ける
        private void StopSong()
        {
            song?.Dispose();
            song = null;
            donSound?.Dispose();
            donSound = null;
            kaSound?.Dispose();
            kaSound = null;
        }

        // 最初から(最初の小節の3秒前から始める)
        private void Restart()
        {
            double first = (course != null && course.BarLines.Count > 0) ? course.BarLines[0].TimeMs : 0;
            nowMs = Math.Min(0, first) - 3000;

            // 曲は止めておいて、時間が0になったら鳴らし始める
            song?.Stop();
            songStarted = false;

            // 判定も最初から
            if (course != null) judges = new Judge?[course.Notes.Count];
            nextHittable = 0;
            goodCount = okCount = badCount = combo = maxCombo = 0;
            lastDeltaMs = null;
            deltaSumMs = 0;
            deltaCount = 0;
            shownJudge = null;
            judgeTimer = 0f;
            inputLog.Clear();
        }

        public override void Update(float dt)
        {
            // クイックリスタート(F5)で最初から
            if (InputManager.IsSystemPressed(SystemAction.QuickRestart))
            {
                Restart();
                return;
            }

            frameCount++;

            // まずは前のフレームからの経過時間で進める(なめらかに動かすため)
            nowMs += dt * 1000.0;

            UpdateSongSync();
            UpdateJudge(dt);
        }

        // 曲の再生位置に合わせて時間を直す
        private void UpdateSongSync()
        {
            if (song == null) return;

            // 時間が0(曲の頭)になったら鳴らし始める
            if (!songStarted)
            {
                if (nowMs >= 0)
                {
                    song.Play(nowMs);
                    songStarted = true;
                }
                return;
            }

            // 鳴っている間は、曲の再生位置に合わせる(ズレないようにする一番大事なところ)
            // ・大きくズレたら(処理落ちなど)一気に合わせる
            // ・少しのズレは、カクッとならないように少しずつ合わせる
            if (song.IsPlaying)
            {
                double diff = song.PositionMs - nowMs;
                if (Math.Abs(diff) > ResyncThresholdMs) nowMs += diff;
                else nowMs += diff * SyncRate;
            }
        }

        // ===== 叩く・判定 =====

        private void UpdateJudge(float dt)
        {
            if (judgeTimer > 0f) judgeTimer -= dt;
            if (course == null) return;

            // 判定に使う時間(音オフセットでずらす。マイナスで早く来る)
            double gameMs = nowMs - ConfigManager.Game.AudioOffsetMs;

            // 1. 叩かれないまま判定枠を通りすぎた音符は「不可」(見逃し)
            ProcessMisses(gameMs);

            // 2. 前のフレームから今までに押されたキーを、押された順に全部取り出して判定
            //    IsKeyPressed(1フレームに1回「今押されているか」を見る)だと、
            //    1フレームの間に「押して離す」と見逃してしまう(ラピッドトリガーのキーボードで起きやすい)
            //    GetKeyPressed は押された瞬間を全部ためておいてくれるので、見逃さない
            //    ※同じキーを1フレームに2回押しても、2回分出てくる
            int key = Raylib.GetKeyPressed();
            while (key != 0)
            {
                if (TryGetDrumPart((KeyboardKey)key, out DrumPart part)) Hit(part, (KeyboardKey)key, gameMs);
                key = Raylib.GetKeyPressed();
            }
        }

        // キー → 叩く場所。叩くキーに登録されていなければ false
        // 同じキーが2か所に登録されていたら、DrumPart の並びが上の方を使う(KeyConfig の決まりと同じ)
        private static bool TryGetDrumPart(KeyboardKey key, out DrumPart part)
        {
            foreach (DrumPart candidate in Enum.GetValues<DrumPart>())
            {
                if (ConfigManager.Key.DrumKeys.TryGetValue(candidate, out List<KeyboardKey>? keys) && keys.Contains(key))
                {
                    part = candidate;
                    return true;
                }
            }
            part = default;
            return false;
        }

        // 不可の幅を過ぎた音符を見逃し(不可)にする
        private void ProcessMisses(double gameMs)
        {
            while (nextHittable < hittable.Length)
            {
                int index = hittable[nextHittable];
                if (judges[index] == null)
                {
                    if (gameMs - course!.Notes[index].TimeMs <= BadRangeMs) break; // まだ叩ける
                    SetJudge(index, Judge.Bad, null);
                    AddInputLog(new InputLogEntry(gameMs, "", "", IsDonNote(course.Notes[index]), noteNumbers[index],
                        gameMs - course.Notes[index].TimeMs, Judge.Bad, true, frameCount));
                }
                nextHittable++;
            }
        }

        // 叩いた
        private void Hit(DrumPart part, KeyboardKey key, double gameMs)
        {
            bool don = KeyConfig.IsDon(part); // true なら面(ドン)、false なら縁(カッ)

            // 音は叩いたら必ず鳴らす(音量 = Master × SE)
            SoundConfig sound = ConfigManager.Sound;
            (don ? donSound : kaSound)?.Play((float)sound.GetFinalVolume(sound.SeVolume));

            // どの音符に当てるか決める(当てる音符が無ければ空振り)
            int target = FindTarget(don, gameMs);
            if (target < 0)
            {
                AddInputLog(new InputLogEntry(gameMs, key.ToString(), KeyConfig.GetDrumPartName(part), don, 0, 0, null, false, frameCount));
                return;
            }

            double delta = gameMs - course!.Notes[target].TimeMs; // プラスなら遅い
            deltaSumMs += delta;
            deltaCount++;

            Judge judge;
            if (Math.Abs(delta) <= GoodRangeMs) judge = Judge.Good;
            else if (Math.Abs(delta) <= OkRangeMs) judge = Judge.Ok;
            else judge = Judge.Bad;

            SetJudge(target, judge, delta);
            AddInputLog(new InputLogEntry(gameMs, key.ToString(), KeyConfig.GetDrumPartName(part), don, noteNumbers[target], delta, judge, false, frameCount));
        }

        // 叩いたときに、どの音符に当てるか決める(OpenTaiko の GetChipToJudgeIgnoringRollBody と同じ考え方)
        //   ・色が違う音符は相手にしない(飛ばして、同じ色の音符を探す。見つからなければ空振り)
        //   ・候補は2つ
        //       前の候補：叩いた時刻より前(か同じ)の、まだ判定していない音符。一番古いもの
        //                 ただし、それが不可になるなら、不可にならない音符の中で一番古いものを使う
        //       後の候補：叩いた時刻より後の、まだ判定していない音符。一番近いもの
        //   ・両方あるときは、片方だけが不可になるなら、不可にならない方。それ以外は前の候補
        // 当てる音符の番号を返す(無ければ -1)
        private int FindTarget(bool don, double gameMs)
        {
            int past = -1;        // 前の候補(一番古い)
            int pastNotBad = -1;  // 前の候補のうち、不可にならない一番古いもの
            int future = -1;      // 後の候補

            for (int i = nextHittable; i < hittable.Length; i++)
            {
                int index = hittable[i];
                Note note = course!.Notes[index];
                double delta = gameMs - note.TimeMs; // プラスなら音符の時刻を過ぎている

                if (delta < -BadRangeMs) break;                         // ここから先は早すぎる(判定の幅に入っていない)
                if (judges[index] != null) continue;                    // もう判定した
                if (IsDonNote(note) != don) continue;                   // 色が違う
                if (delta > BadRangeMs) continue;                       // 過ぎすぎ(見逃しになる音符)

                if (delta >= 0)
                {
                    if (past < 0) past = index;
                    if (pastNotBad < 0 && delta <= OkRangeMs) pastNotBad = index;
                }
                else
                {
                    future = index;
                    break; // 後の候補は一番近い1つだけ
                }
            }

            if (pastNotBad >= 0) past = pastNotBad;

            if (past < 0) return future;
            if (future < 0) return past;

            // 両方あるとき：片方だけ不可になるなら、不可にならない方
            bool pastBad = gameMs - course!.Notes[past].TimeMs > OkRangeMs;
            bool futureBad = course.Notes[future].TimeMs - gameMs > OkRangeMs;
            if (pastBad && !futureBad) return future;
            return past;
        }

        // 入力ログに1行足す(古いものから消える)
        private void AddInputLog(InputLogEntry entry)
        {
            inputLog.Add(entry);
            if (inputLog.Count > InputLogCount) inputLog.RemoveAt(0);
        }

        // 判定を決めて、数とコンボを数える
        private void SetJudge(int index, Judge judge, double? delta)
        {
            judges[index] = judge;

            switch (judge)
            {
                case Judge.Good: goodCount++; combo++; break;
                case Judge.Ok: okCount++; combo++; break;
                case Judge.Bad: badCount++; combo = 0; break;
            }
            maxCombo = Math.Max(maxCombo, combo);

            if (delta != null) lastDeltaMs = delta;
            shownJudge = judge;
            judgeTimer = JudgeShowSeconds;
        }

        // 良・可で叩けた音符は消す(不可の音符はそのまま流れていく)
        private bool IsHidden(int index)
        {
            return index < judges.Length && judges[index] is Judge.Good or Judge.Ok;
        }

        // Esc で選曲画面(ドロップで来たときはホーム)に戻る
        public override void OnBack()
        {
            StopSong(); // 念のため、すぐ止める
            if (returnToSelect) SceneManager.ChangeScene(new SongSelectScene());
            else SceneManager.ChangeScene(new HomeScene());
        }

        // 別の .tja がドロップされたら、そっちに切り替える
        public override void OnFilesDropped(string[] paths)
        {
            string? tja = paths.FirstOrDefault(IsTja);
            if (tja == null) return;

            StopSong(); // 念のため、すぐ止める
            SceneManager.ChangeScene(new TestPlayScene(tja));
        }

        // ===== 描画 =====

        public override void Draw()
        {
            Raylib.ClearBackground(new Color(24, 26, 36, 255));

            if (chart == null || course == null)
            {
                FontManager.Draw(error, 80, 120, 36, new Color(255, 110, 100, 255));
                FontManager.Draw("Esc：もどる", 80, 180, 28, Color.LightGray);
                return;
            }

            // 音オフセット(判定ごとずらす)と譜面オフセット(見た目だけずらす)。どちらもマイナスで早く来る
            double gameMs = nowMs - ConfigManager.Game.AudioOffsetMs;
            double viewMs = gameMs - ConfigManager.Game.ChartOffsetMs;

            DrawLane();
            DrawBarLines(course, viewMs);

            // 後ろ(右)の音符から描いて、手前(左)の音符が上に重なるようにする
            for (int i = course.Notes.Count - 1; i >= 0; i--)
            {
                if (!IsHidden(i)) DrawNote(course.Notes[i], viewMs);
            }
            for (int i = course.Notes.Count - 1; i >= 0; i--)
            {
                if (!IsHidden(i)) DrawSeText(course.Notes[i], viewMs);
            }

            // 左のパネル(判定枠を通りすぎた音符はこの下に隠れる)
            if (!SkinManager.Draw(PanelImage, 0, LaneY))
            {
                Raylib.DrawRectangle(0, (int)LaneY, (int)LaneX + 5, (int)LaneHeight, new Color(170, 50, 40, 255));
            }

            DrawJudgeText();
            DrawInfo(chart, course);
            if (ShowInputLog) DrawInputLog();
        }

        // 入力ログ(右下)。新しいものが下
        //   例：「 23.715 秒  D 左面 ドン → 45個目 ドン  -12.3 ms 良」
        //   同じフレームで処理した叩きには「同」が付く(同じ時刻として判定している)
        private void DrawInputLog()
        {
            const float x = 1060;
            const float top = 560;
            const float lineHeight = 32;
            const float size = 22;

            Raylib.DrawRectangle((int)x - 16, (int)top - 12, Screen.Width - (int)x + 16, (int)(lineHeight * (InputLogCount + 1) + 20), new Color(0, 0, 0, 150));
            FontManager.Draw("入力ログ（新しいものが下）", x, top, size, Color.LightGray);

            for (int i = 0; i < inputLog.Count; i++)
            {
                InputLogEntry e = inputLog[i];
                bool sameFrame = i > 0 && inputLog[i - 1].Frame == e.Frame && !inputLog[i - 1].Missed && !e.Missed;
                string color = e.Don ? "ドン" : "カッ";
                string time = $"{e.TimeMs / 1000.0:F3} 秒";

                string text;
                Color textColor;
                if (e.Missed)
                {
                    text = $"{time}  見逃し　　　→ {e.NoteNumber}個目 {color}  {e.Delta:+0.0;-0.0;0.0} ms 不可";
                    textColor = new Color(140, 150, 255, 255);
                }
                else if (e.NoteNumber == 0)
                {
                    text = $"{time}  {e.KeyName} {e.PartName} {color} → 空振り（近くに同じ色の音符なし）";
                    textColor = Color.Gray;
                }
                else
                {
                    string judge = e.Result switch { Judge.Good => "良", Judge.Ok => "可", _ => "不可" };
                    text = $"{time}  {e.KeyName} {e.PartName} {color} → {e.NoteNumber}個目  {e.Delta:+0.0;-0.0;0.0} ms {judge}";
                    textColor = e.Result switch
                    {
                        Judge.Good => new Color(255, 200, 60, 255),
                        Judge.Ok => Color.White,
                        _ => new Color(140, 150, 255, 255),
                    };
                }

                if (sameFrame) text += "　同";
                FontManager.Draw(text, x, top + lineHeight * (i + 1), size, textColor);
            }
        }

        // 判定の文字(良・可・不可)。判定枠の上に出て、少し上にふわっと動きながら消える
        private void DrawJudgeText()
        {
            if (shownJudge is not Judge judge || judgeTimer <= 0f) return;

            float t = 1f - judgeTimer / JudgeShowSeconds;   // 0 → 1 に進む
            float y = NoteY - 105 - t * 20f;                // 少し上に動く
            float alpha = t < 0.7f ? 1f : (1f - t) / 0.3f;  // 最後の3割で消える
            Color tint = Raylib.ColorAlpha(Color.White, alpha);

            if (SkinManager.GetTexture(JudgeImage) is Texture2D image)
            {
                Rectangle source = new Rectangle(0, (int)judge * JudgeImageSize, JudgeImageSize, JudgeImageSize);
                Rectangle dest = new Rectangle(JudgeX - JudgeImageSize / 2f, y - JudgeImageSize / 2f, JudgeImageSize, JudgeImageSize);
                Raylib.DrawTexturePro(image, source, dest, Vector2.Zero, 0f, tint);
                return;
            }

            // スキンが無いときは普通の文字で
            (string text, Color color) = judge switch
            {
                Judge.Good => ("良", new Color(255, 200, 60, 255)),
                Judge.Ok => ("可", Color.White),
                _ => ("不可", new Color(120, 140, 255, 255)),
            };
            float width = FontManager.Measure(text, 44).X;
            FontManager.Draw(text, JudgeX - width / 2f, y - 22, 44, Raylib.ColorAlpha(color, alpha));
        }

        // レーンと判定枠
        private static void DrawLane()
        {
            if (!SkinManager.Draw(LaneImage, LaneX, LaneY))
            {
                Raylib.DrawRectangle((int)LaneX, (int)LaneY, Screen.Width - (int)LaneX, (int)LaneHeight, new Color(50, 50, 50, 255));
            }

            if (!SkinManager.Draw(JudgeFrameImage, JudgeX - NoteHalf, NoteY - NoteHalf))
            {
                Raylib.DrawRing(new Vector2(JudgeX, NoteY), 44, 50, 0, 360, 48, Color.LightGray);
            }
        }

        // 小節線
        private static void DrawBarLines(Course course, double viewMs)
        {
            foreach (BarLine bar in course.BarLines)
            {
                if (!bar.Visible) continue;

                float x = GetX(bar.TimeMs, bar.Bpm, bar.Scroll, viewMs);
                if (x < LaneX || x > Screen.Width) continue;

                Raylib.DrawLineEx(new Vector2(x, BarTop), new Vector2(x, BarBottom), 3, new Color(255, 255, 255, 170));
            }
        }

        // 音符1つ
        private static void DrawNote(Note note, double viewMs)
        {
            float x = GetX(note.TimeMs, note.Bpm, note.Scroll, viewMs);

            // ----- 風船・くすだま -----
            if (note.IsBalloon)
            {
                if (viewMs >= note.EndTimeMs) return;   // 終わったら消える
                if (viewMs >= note.TimeMs) x = JudgeX; // 始まったら判定枠で止まる
                if (x - NoteHalf > Screen.Width) return;

                // 風船の画像は横長(390×195)。左の丸の中心が音符の位置
                if (!SkinManager.Draw(NotesDir + "10.png", x - NoteHalf, NoteY - NoteHalf))
                {
                    DrawFallbackCircle(x, 50, new Color(240, 130, 40, 255));
                }
                return;
            }

            // ----- 連打 -----
            if (note.IsRoll)
            {
                float endX = GetX(note.EndTimeMs, note.Bpm, note.Scroll, viewMs);
                if (x - NoteHalf > Screen.Width || endX + NoteHalf < 0) return;

                bool big = note.IsBig;
                Texture2D? head = SkinManager.GetTexture(NotesDir + (big ? "7" : "4") + ".png");
                Texture2D? body = SkinManager.GetTexture(NotesDir + (big ? "8" : "5") + ".png");
                Texture2D? tail = SkinManager.GetTexture(NotesDir + (big ? "9" : "6") + ".png");

                if (head is Texture2D h && body is Texture2D b && tail is Texture2D t)
                {
                    // 胴：頭の中心〜おわりの位置まで横に伸ばす
                    Raylib.DrawTexturePro(b, new Rectangle(0, 0, b.Width, b.Height),
                        new Rectangle(x, NoteY - NoteHalf, Math.Max(0, endX - x), NoteSize), Vector2.Zero, 0f, Color.White);
                    // おしり：画像の左端をおわりの位置に合わせる
                    Raylib.DrawTexture(t, (int)endX, (int)(NoteY - NoteHalf), Color.White);
                    // 頭
                    Raylib.DrawTexture(h, (int)(x - NoteHalf), (int)(NoteY - NoteHalf), Color.White);
                }
                else
                {
                    float radius = big ? 72 : 48;
                    Raylib.DrawRectangleRec(new Rectangle(x, NoteY - radius, Math.Max(0, endX - x), radius * 2), RollColor);
                    DrawFallbackCircle(endX, radius, RollColor);
                    DrawFallbackCircle(x, radius, RollColor);
                }
                return;
            }

            // ----- ドン・カッ・大ドン・大カッ -----
            if (x - NoteHalf > Screen.Width || x + NoteHalf < 0) return;

            string image = note.Type switch
            {
                NoteType.Don => "0",
                NoteType.Ka => "1",
                NoteType.BigDon => "2",
                NoteType.BigKa => "3",
                _ => "0",
            };

            if (!SkinManager.Draw(NotesDir + image + ".png", x - NoteHalf, NoteY - NoteHalf))
            {
                bool don = note.Type == NoteType.Don || note.Type == NoteType.BigDon;
                DrawFallbackCircle(x, note.IsBig ? 72 : 48, don ? DonColor : KaColor);
            }
        }

        // 音符の下の文字(ドン・カッ・連打…)
        private static void DrawSeText(Note note, double viewMs)
        {
            float x = GetX(note.TimeMs, note.Bpm, note.Scroll, viewMs);

            if (note.IsBalloon)
            {
                if (viewMs >= note.EndTimeMs) return;
                if (viewMs >= note.TimeMs) x = JudgeX;
            }

            float endX = note.IsRoll ? GetX(note.EndTimeMs, note.Bpm, note.Scroll, viewMs) : x;
            if (x - SeWidth > Screen.Width || endX + SeWidth < 0) return;

            int row = note.Type switch
            {
                NoteType.Don => SeDon,
                NoteType.Ka => SeKa,
                NoteType.BigDon => SeBigDon,
                NoteType.BigKa => SeBigKa,
                NoteType.Roll or NoteType.BigRoll => SeRoll,
                NoteType.Balloon => SeBalloon,
                NoteType.Kusudama => SeKusudama,
                _ => SeDon,
            };

            // スキンが無いときは普通の文字で
            if (SkinManager.GetTexture(SeNotesImage) is not Texture2D se)
            {
                FontManager.Draw(SeFallbackText[row], x - 30, SeTextY + 4, 26 * SeScale, Color.White);
                return;
            }

            if (note.IsRoll)
            {
                // 「連打」と「っ!!」の間を線でつなぐ
                if (SkinManager.GetTexture(SeRollingImage) is Texture2D line)
                {
                    float lineStart = x + SeDrawWidth / 2f;
                    Raylib.DrawTexturePro(line, new Rectangle(0, 0, line.Width, line.Height),
                        new Rectangle(lineStart, SeDrawY, Math.Max(0, endX - lineStart), SeDrawHeight), Vector2.Zero, 0f, Color.White);
                }
                DrawSeRow(se, SeRollEnd, endX);
            }
            DrawSeRow(se, row, x);
        }

        // SENotes.png の row 段目を、centerX を中心にして描く(SeScale の大きさで)
        private static void DrawSeRow(Texture2D se, int row, float centerX)
        {
            Raylib.DrawTexturePro(se, new Rectangle(0, row * SeHeight, SeWidth, SeHeight),
                new Rectangle(centerX - SeDrawWidth / 2f, SeDrawY, SeDrawWidth, SeDrawHeight), Vector2.Zero, 0f, Color.White);
        }

        // 上と下の情報
        private void DrawInfo(Chart chart, Course course)
        {
            FontManager.Draw(chart.Title, 40, 20, 48, Color.White);
            if (chart.Subtitle.Length > 0) FontManager.Draw(chart.Subtitle, 40, 80, 28, Color.LightGray);

            FontManager.Draw($"{Chart.GetCourseName(course.Type)} ★{course.Level}　音符 {course.Notes.Count}個　小節 {course.BarLines.Count}",
                40, 125, 28, Color.White);
            FontManager.Draw($"時間 {nowMs / 1000.0:F2} 秒　BPM {GetCurrentBpm(chart, course):0.##}", 40, 165, 28, Color.White);

            // 判定の数とコンボ(本家っぽい見た目は後で)
            FontManager.Draw($"良 {goodCount}　可 {okCount}　不可 {badCount}　コンボ {combo}（最大 {maxCombo}）",
                40, 690, 28, Color.Gold);

            // ズレ(プラスなら遅い)。平均がいつも同じ向きにズレていたら、音オフセットで直せる
            string last = lastDeltaMs is double d ? DeltaText(d) : "-";
            string average = deltaCount > 0 ? $"{DeltaText(deltaSumMs / deltaCount)}（{deltaCount}回）" : "-";
            FontManager.Draw($"最後のズレ {last}　平均のズレ {average}", 40, 730, 28, Color.Gold);

            // 曲の状態
            if (song != null)
            {
                // ズレ = 曲の再生位置 − 画面の時間(鳴っている間だけ。0 に近いほどいい)
                string sync = song.IsPlaying ? $"　（ズレ {song.PositionMs - nowMs:+0.0;-0.0;0.0} ms）" : "";
                FontManager.Draw($"曲：{chart.Wave}　{song.PositionMs / 1000.0:F2} / {song.LengthMs / 1000.0:F2} 秒{sync}",
                    40, 560, 24, Color.White);
            }
            else
            {
                FontManager.Draw(audioMessage, 40, 560, 24, new Color(255, 170, 80, 255));
            }

            KeyboardKey restartKey = ConfigManager.Key.SystemKeys.GetValueOrDefault(SystemAction.QuickRestart, KeyboardKey.F5);
            FontManager.Draw($"{restartKey}：最初から　Esc：もどる　（テスト表示：連打・風船はまだ数えません）", 40, 600, 28, Color.SkyBlue);
            FontManager.Draw($"ファイル：{path}", 40, 645, 22, Color.Gray);
        }

        // ズレの表示(例：「-12.3 ms（早い）」)
        private static string DeltaText(double ms)
        {
            string direction = ms < 0 ? "早い" : ms > 0 ? "遅い" : "ぴったり";
            return $"{ms:+0.0;-0.0;0.0} ms（{direction}）";
        }

        // 今のBPM(最後に通りすぎた小節線のBPM)
        private double GetCurrentBpm(Chart chart, Course course)
        {
            double bpm = chart.Bpm;
            foreach (BarLine bar in course.BarLines)
            {
                if (bar.TimeMs > nowMs) break;
                bpm = bar.Bpm;
            }
            return bpm;
        }

        // ===== 計算 =====

        // 音符の x 座標 = 判定枠 + (音符の時間 − 今) × BPM × SCROLL ÷ 60000 × 1拍の距離
        private static float GetX(double timeMs, double bpm, double scroll, double viewMs)
        {
            return JudgeX + (float)((timeMs - viewMs) * bpm * scroll / 60000.0 * PixelsPerBeat);
        }

        // スキンが無いときの丸い音符
        private static void DrawFallbackCircle(float x, float radius, Color color)
        {
            Raylib.DrawCircleV(new Vector2(x, NoteY), radius + 6, Color.Black);
            Raylib.DrawCircleV(new Vector2(x, NoteY), radius + 3, Color.RayWhite);
            Raylib.DrawCircleV(new Vector2(x, NoteY), radius, color);
        }
    }
}