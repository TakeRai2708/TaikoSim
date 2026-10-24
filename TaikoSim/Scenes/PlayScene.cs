using System.Diagnostics;
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
    // ドン・カッ(大音符も片手でOK)、連打・風船に対応。選曲オプションからオート演奏も選べる。
    public class PlayScene : Scene
    {
        // ===== 配置(1920×1080) ※動画とスキン画像の大きさから測った値 =====
        private const float LaneX = 494;          // レーンの左端
        private const float LaneY = 276;          // レーン(と左パネル)の上端
        private const float LaneHeight = 264;
        private const float JudgeX = 620;         // 判定枠の中心x
        private const float NoteY = 389;          // 音符の中心y
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
        private const string FaceNotesDir = "Image/04.EnsoGame/Notes/Normal/1/";
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
        private readonly PlayStats stats = new();
        private readonly PlayHUD hud = new();
        private readonly PlayBackground background = new();

        // 譜面分岐(決まり)と、分岐したときのレーンの色
        private readonly BranchTracker branches = new();
        private readonly BranchLane branchLane = new();

        // ===== 音合わせのテスト(F10) =====
        //   0：いつもの見た目  1：いつもの見た目＋ガイド  2：システムのスキン(スキンの画像を使わない)＋ガイド
        //   ガイド：判定枠の中心の線(黄)、±25ms(良・緑)・±75ms(可・橙)・±108ms(不可・赤)の目盛り、音符の中心の線(水色)
        private int testView;
        private static readonly Color GuideCenterColor = new Color(255, 230, 0, 255);
        private static readonly Color GuideGoodColor = new Color(80, 230, 120, 255);
        private static readonly Color GuideOkColor = new Color(255, 160, 40, 255);
        private static readonly Color GuideBadColor = new Color(255, 70, 70, 255);
        private static readonly Color GuideNoteColor = new Color(80, 220, 255, 255);

        // 良・可・不可・連打数の板(左下)
        private readonly PlayScoreboard scoreboard = new();

        // 測る用：音が聞こえるまでの遅れの目安(ms)の平均(デバッグ表示に出す)
        private double latencyAverageMs = 0;
        private bool latencyMeasured = false;
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

        // オートはフレーム数ではなく譜面時刻で進める。
        //   連打：設定の速さ(1秒に何打。0 なら叩かない)
        //   風船：連打と同じ速さ。その速さで終わりまでに割れないときだけ、残りの回数を残りの時間で均等に叩く(OpenTaiko と同じ)
        private bool autoPlay;
        private float hiSpeed = 1f;
        private bool autoRightHand;
        private int autoRollPerSecond = 20;
        private readonly PriorityQueue<(int Index, int Tick), double> autoHits = new();

        // オートの良・可・不可(音符ごと。course.Notes と同じ番号)と、叩くときのズレ(ms。プラスなら遅い)
        //   可・不可は、本当にその分ずれた時刻に叩く(判定の幅：良 ±25、可 ±75、不可 ±108)
        private Judge[] autoJudges = Array.Empty<Judge>();
        private double[] autoDeltas = Array.Empty<double>();

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
        // F3で演奏情報とログの表示を切り替える。リスタートしても選択を保つ。
        // 診断の収集・判定・打音は、表示のON/OFFに関係なく従来どおり動かす。
        private bool showDebugInfo;

        // 判定と音符に使う「音オフセット」。設定の値に、BASS の効果音の遅れの分(AudioManager.HitSoundLagMs)を足したもの
        //   マイナスほど早くなる(設定の音オフセットと同じ向き)
        private static double AudioOffsetMs => ConfigManager.Game.AudioOffsetMs - AudioManager.HitSoundLagMs;
        private bool showDiagnostics; // F8：入力診断を出すか(記録は「CSV自動保存」が ON のときも行う)
        private const int InputLogCount = 8;

        // ログ1行分
        //   NoteNumber … 何個目の音符か(1から。空振りなら 0)
        //   Delta      … ズレ(ミリ秒。プラスなら遅い)
        //   Result     … 判定(空振りなら null)
        //   Frame      … 何フレーム目に処理したか(同じフレームの叩きを見分けるため)
        //   RollText   … 連打・風船に当たったときの説明(「連打 12打」とか。普通の音符なら null)
        private readonly record struct InputLogEntry(
            double TimeMs, string KeyName, string PartName, bool Don, int NoteNumber, double Delta, Judge? Result, bool Missed, long Frame,
            string? RollText = null);

        private readonly List<InputLogEntry> inputLog = new();
        private long frameCount = 0;

        // ===== 入力診断（F8で表示切替。未割り当て時だけ。CSVは選曲画面のオプションで自動保存） =====
        // この段階では入力判定の時計やキー取得方式は一切変更しない。
        private readonly InputDiagnostics diagnostics = new();
        // Windowsの入力時刻も記録する。ゲームの判定・打音は今までどおりRaylib側で行う。
        private readonly WindowsKeyCapture windowsInput = new();
        // 生の入力時刻を受け取り、必要な場合だけ60Hzで判定する。
        private readonly DrumInputScheduler inputScheduler = new();
        private string diagnosticMessage = "";
        private float diagnosticMessageTimer = 0f;

        // 音符の番号 → 叩ける音符の中で何個目か(1から。ログの表示用)
        private int[] noteNumbers = Array.Empty<int>();

        // ===== 連打・風船 =====
        // 決まりは OpenTaiko と同じ(CStage演奏画面共通.cs の tRollProcess・tBalloonProcess)
        //   連打(大連打)：始まり〜終わりの間、ドン・カッどっちで叩いても1打
        //   風船(くす玉)：始まりの17ms前〜終わりの間、ドンだけ。決められた回数叩くと割れる
        //   どちらも良・可・不可やコンボには関係しない
        // ※ くす玉は、今は風船と同じあつかい(スキンにくす玉の画像が無いので)

        private const double BalloonEarlyMs = 17; // 風船は始まりのこれだけ前から叩ける

        // 連打・風船の音符の番号(course.Notes の番号。譜面の順)
        private int[] rolls = Array.Empty<int>();

        // 音符ごとの、叩いた数(連打・風船だけ使う。course.Notes と同じ番号)
        private int[] rollHits = Array.Empty<int>();

        // 割れた風船(course.Notes と同じ番号)
        private bool[] balloonPopped = Array.Empty<bool>();

        // 全部の連打数(風船も含む。OpenTaiko の成績と同じ数え方)
        private int totalRollHits = 0;

        // 連打数の表示(最後に叩いた連打の音符と、あと何秒出すか)
        private int shownRoll = -1;
        private float rollShowTimer = 0f;
        private const float RollShowSeconds = 2.0f; // 最後に叩いてから出しておく時間
        private const float RollFadeSeconds = 0.3f; // 最後のこれだけの時間で消える

        // 連打の扇(Roll_Base)が開いてからの時間・最後に叩いてからの時間(数字がぴょこっと大きくなる用)
        private float rollOpenElapsed = 0f;
        private float rollBumpElapsed = 99f;

        // 割れた瞬間の風船の絵を少しだけ出す
        private int poppedBalloon = -1;
        private float popTimer = 0f;
        private const float PopShowSeconds = 0.15f;

        // ----- 連打・風船の画像(スキンの Image/04.EnsoGame/Popup/) -----
        // ポップアップの表示位置は 1920×1080 基準。A-Style に合わせて個別調整する。
        private const string PopupDir = "Image/04.EnsoGame/Popup/";
        private const string RollBaseImage = PopupDir + "Roll_Base.png";        // 扇 380×240 が縦に5コマ(閉じる→開く)
        private const string RollNumberImage = PopupDir + "Roll_Number.png";    // 0〜9 が横に並ぶ(1文字 90×108)
        private const string RollTextImage = PopupDir + "Roll_Text.png";        // 「連打!!」
        private const string BalloonBaseImage = PopupDir + "Balloon_Base.png";  // 吹き出し
        private const string BalloonNumberImage = PopupDir + "Balloon_Number.png"; // 0〜9 が横に並ぶ(1文字 72×86)
        private const string BalloonAnimeImage = PopupDir + "Balloon_Anime.png";   // ふくらむ風船 390×360 が縦に8コマ(最後は割れた絵)

        // ===== ポップアップの座標（1920×1080 基準） =====
        // X を増やすと右、Y を増やすと下へ移動する。
        // 扇子・吹き出しは「画像の左上」、数字は「全体の中心X・下端Y」、
        // 「連打!!」は「画像の中心X・上端Y」を指定する。
        private const float RollFrameX = 428f, RollFrameY = 26f;         // 連打の扇子
        private const float RollNumberX = 613f, RollNumberY = 166f;      // 連打の数字(中央・下端)
        private const float RollTextX = 616f, RollTextY = 170f;          // 「連打!!」
        private const float BalloonFrameX = 589f, BalloonFrameY = 37f;    // 風船の吹き出し(数字は動かさない)
        private const float BalloonNumberX = 728f, BalloonNumberY = 180f; // 風船の数字

        // 数字の画像は、連打が1文字90px、風船が1文字72px。間隔を別々に調整する。
        private const float RollNumberInterval = 90f;
        private const float BalloonNumberInterval = 72f;
        private const float BalloonNumberScale = 0.879f;             // 残りの数の大きさ(OpenTaiko と同じ)
        private const int RollBaseFrames = 5;
        private const int BalloonAnimeFrames = 8;
        private const float RollFrameSeconds = 0.04f;                // 扇が1コマ進む時間(OpenTaiko 40ms)

        // 叩いた直後に数字を縦に伸ばす量(OpenTaiko の RollScale と同じ。14msごとに次へ)
        private static readonly float[] RollBump = { 0.000f, 0.123f, 0.164f, 0.164f, 0.164f, 0.137f, 0.110f, 0.082f, 0.055f, 0.000f };
        private const float RollBumpStep = 0.014f;

        // 風船が割れる音
        private SoundEffect? balloonSound;

        // 演出(レーンの光・判定枠の光・ミニ太鼓・コンボ数・ゴーゴー)
        private readonly PlayEffects effects = new PlayEffects(JudgeX, NoteY);
        private const string BalloonSoundFile = "Sound/Taiko/Balloon.ogg";

        // 判定の文字(良・可・不可)の表示
        private Judge? shownJudge = null;
        private float judgeTimer = 0f;
        private const float JudgeShowSeconds = 0.4f;

        // ===== 演奏の終わり =====
        // 終わりの決め方は OpenTaiko の「譜面の終わり」と同じ考え方：
        //   最後の音符(連打・風船は終わり)の 1.5秒後 と #END の 1秒後 の早い方で、演奏後の演出を始める
        //   ※ OpenTaiko は最後の音符の2秒後。TaikøNauts の動画では 1.5秒後だったので、そっちに合わせた
        //   演出が始まって 6.9秒(#END の 1秒後も過ぎていたら)で、2秒かけて暗くして、真っ暗で 0.5秒待って次の画面へ
        //   (TaikøNauts の動画：クリア失敗・クリア成功とも、6.9秒後から約2秒で真っ暗 → 約0.5秒後にリザルト)
        private const double EndAfterLastNoteMs = 1500;
        private const double EndAfterChartEndMs = 1000;
        private const float EndFadeSeconds = 2.0f;
        private const float EndBlackSeconds = 0.5f;
        private readonly PlayEnd end = new();
        private float endFadeElapsed = -1f; // 暗くなり始めてからの時間(-1 ならまだ)

        // 演奏後の音(クリア失敗・クリア成功・フルコンボ・ドンダフルコンボ。PlayResult の順)
        private readonly SoundEffect?[] endSounds = new SoundEffect?[4];
        private static readonly string[] EndSoundFiles =
        {
            "Sound/EnsoGame/End/Failed.ogg",
            "Sound/EnsoGame/End/Clear.ogg",
            "Sound/EnsoGame/End/FullCombo.ogg",
            "Sound/EnsoGame/End/DondaFullCombo.ogg",
        };

        // スコアランク(演奏中に 粋・雅・極 を出す)
        private readonly PlayScoreRank scoreRank = new();

        // コンボの声(フォルダにある「50.ogg」「100.ogg」…。その数のコンボになった瞬間に鳴らす)
        private const string ComboVoiceDir = "Sound/EnsoGame/Combo/1P/";
        private readonly Dictionary<int, SoundEffect> comboVoices = new();

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

        public PlayScene(string path, CourseType? courseType = null, bool returnToSelect = false)
        {
            this.path = path;
            this.courseType = courseType;
            this.returnToSelect = returnToSelect;
            stats.ScoreAdded += hud.OnScoreAdded;
            effects.SoulArrived += hud.OnSoulArrival;
            branches.Changed += branchLane.OnChanged;
        }

        // .tja ファイルか
        public static bool IsTja(string path)
        {
            return path.EndsWith(".tja", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }

        // ===== 入る・更新 =====

        public override void Enter()
        {
            autoPlay = ConfigManager.Game.AutoPlay; // 演奏開始時の選択を固定する。
            autoRollPerSecond = ConfigManager.Game.AutoRollPerSecond;
            hiSpeed = ConfigManager.Game.HiSpeed > 0f ? ConfigManager.Game.HiSpeed : 1f;
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
            // ウィンドウを作ったスレッドだけを監視する。開始できなくても従来の演奏は継続。
            // F8がOFFでもWindows側の時刻を拾う。使えなければRaylib取得時刻で代用。
            windowsInput.CaptureEnabled = windowsInput.Start();
        }

        // 画面から出るとき：曲と効果音を片付ける
        public override void Exit()
        {
            // 演奏画面から離れたら必ずWindowsフックを解除する。
            windowsInput.Dispose();
            StopSong();
            SkinManager.SystemSkin = false; // ほかの画面は、いつものスキンで
        }

        // スキンの Sound フォルダから、ドン・カッの音を読み込む(無ければ音なし)
        private void LoadHitSounds()
        {
            string? skin = SkinManager.CurrentPath;
            if (skin == null) return;

            // ドン・カッは1つずつだけ持って、叩くたびに前の音を切って頭から鳴らし直す(OpenTaiko の CSound と同じ)
            //   重ねて鳴らすと、連打で前の音の余韻が全部残って、音が厚く濁ってしまうため
            donSound = SoundEffect.Load(Path.Combine(skin, DonSoundFile), out _, 1);
            kaSound = SoundEffect.Load(Path.Combine(skin, KaSoundFile), out _, 1);
            balloonSound = SoundEffect.Load(Path.Combine(skin, BalloonSoundFile), out _);

            // 演奏後の音・コンボの声は重ならないので、重ねる数は少しでよい
            for (int i = 0; i < EndSoundFiles.Length; i++)
                endSounds[i] = SoundEffect.Load(Path.Combine(skin, EndSoundFiles[i]), out _, 1);
            LoadComboVoices(skin);
        }

        // コンボの声を読む。ファイル名が数字のもの(50.ogg など)だけ使う
        private void LoadComboVoices(string skin)
        {
            string folder = Path.Combine(skin, ComboVoiceDir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(folder)) return;
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".ogg" or ".wav" or ".mp3")) continue;
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int count) || count <= 0) continue;
                if (comboVoices.ContainsKey(count)) continue;
                if (SoundEffect.Load(file, out _, 2) is SoundEffect voice) comboVoices[count] = voice;
            }
        }

        // 叩ける音符の一覧を作る
        private void PrepareJudge()
        {
            if (course == null) return;

            hittable = Enumerable.Range(0, course.Notes.Count)
                .Where(i => IsHittable(course.Notes[i]))
                .OrderBy(i => course.Notes[i].TimeMs)
                .ToArray();

            rolls = Enumerable.Range(0, course.Notes.Count)
                .Where(i => IsRollNote(course.Notes[i]) || IsBalloonNote(course.Notes[i]))
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
            balloonSound?.Dispose();
            balloonSound = null;
            for (int i = 0; i < endSounds.Length; i++)
            {
                endSounds[i]?.Dispose();
                endSounds[i] = null;
            }
            foreach (SoundEffect voice in comboVoices.Values) voice.Dispose();
            comboVoices.Clear();
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
            if (course != null)
            {
                stats.Reset(course);
                hud.Reset(course, chart?.Title ?? "");
            }
            branches.Reset(course, () => stats.Score);
            branchLane.Reset(course?.HasBranch ?? false);
            scoreboard.Set(0, 0, 0, 0);
            latencyAverageMs = 0;
            latencyMeasured = false;
            lastDeltaMs = null;
            deltaSumMs = 0;
            deltaCount = 0;
            shownJudge = null;
            judgeTimer = 0f;
            inputLog.Clear();
            diagnostics.Reset();
            UpdateDiagnosticsRecording();
            windowsInput.Reset();
            inputScheduler.Reset();
            diagnosticMessage = "";
            diagnosticMessageTimer = 0f;

            // 連打・風船も最初から
            if (course != null)
            {
                rollHits = new int[course.Notes.Count];
                balloonPopped = new bool[course.Notes.Count];
            }
            totalRollHits = 0;
            shownRoll = -1;
            rollShowTimer = 0f;
            rollOpenElapsed = 0f;
            rollBumpElapsed = 99f;
            poppedBalloon = -1;
            popTimer = 0f;

            effects.Reset();
            background.Reset();
            ResetAutoPlay();

            // 演奏の終わり・スコアランクも最初から
            end.Reset();
            endFadeElapsed = -1f;
            foreach (SoundEffect? sound in endSounds) sound?.Stop();
            scoreRank.Reset();
            ApplyMusicVolume(1f);
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
            if (diagnosticMessageTimer > 0f) diagnosticMessageTimer -= dt;

            // 元の時計と補正はそのまま。診断中だけ補正量とフレーム時間を調べる。
            nowMs += dt * 1000.0;
            double beforeSync = nowMs;
            UpdateSongSync();
            if (diagnostics.Enabled && diagnostics.TrackFrame(dt * 1000.0, nowMs - beforeSync))
            {
                double? syncError = song is { IsPlaying: true } ? song.PositionMs - nowMs : null;
                // ミキサー以外の音声遅延はここでは測れないので、値を出さない。
                double? outputEstimate = AudioManager.Mixer != 0 ? AudioManager.OutputLatencyMs : null;
                diagnostics.RecordFrame(frameCount, nowMs, syncError, outputEstimate);
            }
            hud.Update(dt, stats.Score);
            effects.Update(dt); // 新しい打撃の演出を、そのフレームのdtで先に進めない。
            branchLane.Update(dt);
            UpdateJudge(dt);
            UpdateLatencyAverage();

            // ゴーゴーの演出は見た目なので、音符と同じ時間(譜面オフセット込み)で見る
            // 分岐のある譜面は、今の分岐のゴーゴーだけを見る
            if (course != null) effects.SetGoGo(course.IsGoGoAt(nowMs - AudioOffsetMs - ConfigManager.Game.ChartOffsetMs,
                branches.IsActive));
            background.Update(dt, stats.IsClear);
            scoreRank.Update(dt, stats);
            UpdateEnd(dt);
        }

        // ===== 演奏の終わり =====

        private void UpdateEnd(float dt)
        {
            if (course == null) return;
            double gameMs = nowMs - AudioOffsetMs;

            if (!end.Started)
            {
                if (gameMs >= GetChartEndMs()) StartEnd();
                return;
            }

            end.Update(dt);
            if (endFadeElapsed < 0f)
            {
                // 演出が終わって、#END の1秒後も過ぎたら暗くし始める
                if (end.IsFinished && gameMs >= course.EndTimeMs + EndAfterChartEndMs) endFadeElapsed = 0f;
                return;
            }

            endFadeElapsed += dt;
            ApplyMusicVolume(1f - Math.Clamp(endFadeElapsed / EndFadeSeconds, 0f, 1f)); // 曲もだんだん小さく
            if (endFadeElapsed >= EndFadeSeconds + EndBlackSeconds) FinishPlay();
        }

        // 演奏後の演出を始める時間(譜面の時間)
        //   今の分岐で出てくる音符の、最後の音符(連打・風船は終わり)の 1.5秒後 と #END の 1秒後 の早い方
        private double GetChartEndMs()
        {
            double chartEnd = course!.EndTimeMs + EndAfterChartEndMs;
            double last = double.NegativeInfinity;
            foreach (Note note in course.Notes)
            {
                if (!branches.IsActive(note)) continue;
                double time = note.IsRoll || note.IsBalloon ? Math.Max(note.TimeMs, note.EndTimeMs) : note.TimeMs;
                if (time > last) last = time;
            }
            return double.IsNegativeInfinity(last) ? chartEnd : Math.Min(last + EndAfterLastNoteMs, chartEnd);
        }

        // 結果を決めて、演出と音を始める
        //   クリア：魂ゲージがノルマ以上(OpenTaiko と同じく、フルコンボもクリアしていることが条件)
        //   フルコンボ：不可(見逃しも)が0　ドンダフルコンボ：さらに可も0
        private void StartEnd()
        {
            PlayResult result = !stats.IsClear ? PlayResult.Failed
                : badCount > 0 ? PlayResult.Clear
                : okCount > 0 ? PlayResult.FullCombo
                : PlayResult.DondaFullCombo;
            end.Start(result);

            SoundConfig sound = ConfigManager.Sound;
            endSounds[(int)result]?.Play((float)sound.GetFinalVolume(sound.SeVolume));
        }

        // 演奏を終えてリザルト画面へ
        private void FinishPlay()
        {
            StopSong();
            if (ConfigManager.Game.AutoSaveInputCsv) SaveDiagnosticCsv();
            PlayResultData data = new()
            {
                Title = chart?.Title ?? "",
                Course = course?.Type ?? CourseType.Oni,
                Result = end.Result,
                Stats = stats,
                Good = goodCount,
                Ok = okCount,
                Bad = badCount,
                RollHits = totalRollHits,
                MaxCombo = maxCombo,
            };
            SceneManager.ChangeScene(new ResultScene(data, returnToSelect));
        }

        // 曲の音量(rate：1 で設定どおり、0 で無音)
        private void ApplyMusicVolume(float rate)
        {
            if (song == null) return;
            SoundConfig sound = ConfigManager.Sound;
            song.SetVolume((float)sound.GetFinalVolume(sound.MusicVolume) * rate);
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
            if (rollShowTimer > 0f)
            {
                rollShowTimer -= dt;
                rollOpenElapsed += dt;
            }
            rollBumpElapsed += dt;
            if (popTimer > 0f) popTimer -= dt;
            if (course == null) return;

            double gameMs = nowMs - AudioOffsetMs;
            // ここで基準時刻をとる。Windows側のコールバック発生からの経過を引いて、
            // 「押下した時点の譜面時刻」を推定する（物理スイッチの瞬間ではない）。
            long frameClockAt = Stopwatch.GetTimestamp();

            // 診断OFFでも一致候補をキープする。Windows入力の監視に失敗した場合は
            // Raylibから取得した瞬間を入力時刻として使う。
            windowsInput.Drain(diagnostics.Enabled ? diagnostics : null, frameCount, gameMs);

            // そのフレームまでに押されたキーをすべて取得。判定は下のキューで行う。
            int key = Raylib.GetKeyPressed();
            while (key != 0)
            {
                KeyboardKey pressed = (KeyboardKey)key;
                if (pressed == KeyboardKey.F3 && IsFreeInPlay(KeyboardKey.F3))
                {
                    showDebugInfo = !showDebugInfo;
                    SetDiagnosticMessage(showDebugInfo ? "デバッグ表示：ON（F3で切替）" : "デバッグ表示：OFF（F3で切替）");
                }
                else if (pressed == KeyboardKey.F10 && IsFreeInPlay(KeyboardKey.F10))
                {
                    testView = (testView + 1) % 3;
                    SkinManager.SystemSkin = testView == 2;
                    SetDiagnosticMessage(testView switch
                    {
                        0 => "見た目：いつものスキン（F10で切替）",
                        1 => "見た目：いつものスキン＋ガイド（F10で切替）",
                        _ => "見た目：システムのスキン＋ガイド（F10で切替）",
                    });
                }
                else if (pressed == KeyboardKey.F8 && IsFreeInPlay(KeyboardKey.F8))
                {
                    showDiagnostics = !showDiagnostics;
                    UpdateDiagnosticsRecording();
                    SetDiagnosticMessage(showDiagnostics ? "入力診断：ON（F3 の表示も ON のときに出ます）" : "入力診断：OFF");
                }
                else if (pressed == KeyboardKey.F6 && IsFreeInPlay(KeyboardKey.F6))
                {
                    GameConfig settings = ConfigManager.Game;
                    settings.InputTiming = settings.InputTiming == InputTimingMode.HighPrecision
                        ? InputTimingMode.Compatible60Hz : InputTimingMode.HighPrecision;
                    SaveInputTimingSettings($"入力判定：{DrumInputScheduler.ModeName(settings.InputTiming)}");
                }
                else if (pressed == KeyboardKey.F7 && IsFreeInPlay(KeyboardKey.F7))
                {
                    GameConfig settings = ConfigManager.Game;
                    settings.LimitDrumHitsTo60PerSecond = !settings.LimitDrumHitsTo60PerSecond;
                    SaveInputTimingSettings(settings.LimitDrumHitsTo60PerSecond
                        ? "60打/秒制限：ON" : "60打/秒制限：OFF");
                }
                else if (!autoPlay && TryGetDrumPart(pressed, out DrumPart part))
                {
                    long collectedAt = Stopwatch.GetTimestamp();
                    WindowsKeyEvent? native = windowsInput.Match(pressed, collectedAt);
                    long pressedAt = native?.CapturedAt ?? collectedAt;
                    double elapsedMs = Stopwatch.GetElapsedTime(pressedAt, frameClockAt).TotalMilliseconds;
                    // 同じフレームのうち少し後に採れた時刻は未来にしない。
                    double eventGameMs = gameMs - Math.Max(0, elapsedMs);
                    // 音符に対する入力時刻を計算してから、必要なら60Hzへ量子化する。
                    inputScheduler.Enqueue(new DrumInputEvent(part, pressed, eventGameMs,
                        collectedAt, native), ConfigManager.Game.InputTiming,
                        ConfigManager.Game.LimitDrumHitsTo60PerSecond);
                }
                key = Raylib.GetKeyPressed();
            }

            if (autoPlay)
            {
                ProcessAutoPlay(gameMs);
                return; // 手動入力のキューと見逃し処理はオートに混ぜない。
            }

            // 60Hzの場合は次の16.67ms境界まで待つ。制限がONなら1枠につき最大1打。
            // モード切替直前の未処理入力もキューに残り、消さずに最後まで処理する。
            inputScheduler.ProcessReady(gameMs, HandleScheduledHit);
            // 将来の枠を待つ入力があっても、今の譜面時刻まで見逃しと分岐を進める。
            AdvanceTo(gameMs);
        }

        // 演奏画面で F3・F6〜F8・F10 を使ってよいか(叩くキーやほかの役割と重なっていないか)
        //   「検索」は選曲画面でしか使わないので、F3 が検索に割り当てられていても演奏画面では使う
        private static bool IsFreeInPlay(KeyboardKey key)
        {
            string? owner = ConfigManager.Key.FindOwner(key);
            return owner == null || owner == KeyConfig.GetSystemActionName(SystemAction.Search);
        }

        // ms の時刻まで、見逃しと分岐の出来事(分岐を決める・#SECTION など)を時刻の順に進める
        //   分岐を決める前に、その時刻までの見逃しを数えておく(数がずれないように)
        private void AdvanceTo(double ms)
        {
            while (branches.NextEventTime is double eventMs && eventMs <= ms)
            {
                ProcessMisses(eventMs);
                branches.Advance(eventMs);
            }
            ProcessMisses(ms);
        }

        // 測る用：音が聞こえるまでの遅れの目安を、ならして覚えておく(曲が鳴っている間だけ)
        private void UpdateLatencyAverage()
        {
            if (song is not { IsPlaying: true }) return;
            double latency = AudioManager.MeasuredLatencyMs;
            if (!latencyMeasured)
            {
                latencyAverageMs = latency;
                latencyMeasured = true;
            }
            else
            {
                latencyAverageMs += (latency - latencyAverageMs) * 0.02; // 少しずつ近づける(ガタガタしないように)
            }
        }

        private void ResetAutoPlay()
        {
            autoHits.Clear();
            autoRightHand = false;
            if (!autoPlay || course == null) return;
            PrepareAutoJudges();
            for (int i = 0; i < course.Notes.Count; i++)
            {
                Note note = course.Notes[i];
                if (IsHittable(note)) autoHits.Enqueue((i, 0), note.TimeMs + autoDeltas[i]);
                else if ((IsRollNote(note) || IsBalloonNote(note)) && note.EndTimeMs > note.TimeMs)
                    autoHits.Enqueue((i, 0), note.TimeMs);
            }
        }

        // オートで、音符ごとに良・可・不可とズレを決める(設定の割合で。合計が100でなくても割合として使う)
        private void PrepareAutoJudges()
        {
            GameConfig config = ConfigManager.Game;
            int good = config.AutoGoodPercent, ok = config.AutoOkPercent, bad = config.AutoBadPercent;
            if (good + ok + bad <= 0) good = 100;
            int total = good + ok + bad;

            // 毎回同じ：曲のファイル・難易度・割合から決まる数で始める。ランダム：毎回ちがう数
            int seed = config.AutoFixedPattern
                ? StableHash($"{path}|{course!.Type}|{good}|{ok}|{bad}")
                : Environment.TickCount ^ Guid.NewGuid().GetHashCode();
            Random random = new Random(seed);

            int count = course!.Notes.Count;
            autoJudges = new Judge[count];
            autoDeltas = new double[count];
            for (int i = 0; i < count; i++)
            {
                if (!IsHittable(course.Notes[i])) continue;
                int roll = random.Next(total);
                Judge judge = roll < good ? Judge.Good : roll < good + ok ? Judge.Ok : Judge.Bad;
                double sign = random.Next(2) == 0 ? -1 : 1; // 早いか遅いか
                autoDeltas[i] = judge switch
                {
                    Judge.Good => 0,
                    Judge.Ok => sign * (GoodRangeMs + 1 + random.NextDouble() * (OkRangeMs - GoodRangeMs - 2)),
                    _ => sign * (OkRangeMs + 1 + random.NextDouble() * (BadRangeMs - OkRangeMs - 2)),
                };
                autoJudges[i] = judge;
            }
        }

        // 文字列から、いつも同じになる数を作る(string.GetHashCode は起動するたびに変わるので使わない)
        private static int StableHash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (int)hash;
        }

        private DrumPart NextAutoPart(bool don)
        {
            DrumPart part = don
                ? (autoRightHand ? DrumPart.RightDon : DrumPart.LeftDon)
                : (autoRightHand ? DrumPart.RightKa : DrumPart.LeftKa);
            autoRightHand = !autoRightHand;
            return part;
        }

        private void ProcessAutoPlay(double gameMs)
        {
            if (course == null) return;
            SoundConfig sound = ConfigManager.Sound;
            float volume = (float)sound.GetFinalVolume(sound.SeVolume);

            // 低FPSや一時的な遅延で複数の音符を越えても、各音符の本来の時刻で処理する。
            while (autoHits.TryPeek(out _, out double hitMs) && hitMs <= gameMs)
            {
                // その時刻までに分岐を決めておく(決めた分岐の音符だけ叩く)
                branches.Advance(hitMs);

                var hit = autoHits.Dequeue();
                Note note = course.Notes[hit.Index];
                if (!branches.IsActive(note)) continue; // ほかの分岐の音符は叩かない
                if (IsHittable(note))
                {
                    if (judges[hit.Index] != null) continue;
                    bool don = IsDonNote(note);
                    DrumPart part = NextAutoPart(don);
                    effects.OnDrumHit(part);
                    (don ? donSound : kaSound)?.Play(volume);
                    Judge judge = autoJudges[hit.Index];
                    double delta = autoDeltas[hit.Index];
                    deltaSumMs += delta;
                    deltaCount++;
                    SetJudge(hit.Index, judge, delta);
                    // 良・可なら判定枠が光って、音符が飛んでいく(不可は光らない。手で叩いたときと同じ)
                    if (judge != Judge.Bad)
                    {
                        effects.OnNoteHit(judge == Judge.Good, note.IsBig);
                        StartFlyingNote(don, note.IsBig, hitMs);
                    }
                    AddInputLog(new InputLogEntry(hitMs, "AUTO", KeyConfig.GetDrumPartName(part),
                        don, noteNumbers[hit.Index], delta, judge, false, frameCount));
                }
                else
                {
                    if (balloonPopped[hit.Index]) continue;
                    bool balloon = IsBalloonNote(note);
                    if (autoRollPerSecond <= 0) continue; // 連打・風船は叩かない設定

                    DrumPart part = NextAutoPart(true);
                    effects.OnDrumHit(part);
                    HitRoll(hit.Index, true, KeyboardKey.Null, part, hitMs, volume, "AUTO");

                    // 連打・風船とも、始まりから 1000 ÷ 速さ ms ごと(オプションの「オート連打」の速さ)
                    double interval = 1000.0 / autoRollPerSecond;
                    double nextMs = note.TimeMs + (hit.Tick + 1) * interval;
                    if (balloon)
                    {
                        // 風船：この速さだと終わりまでに割れないときは、OpenTaiko と同じ叩き方にする(終わるギリギリで必ず割れる)
                        //   OpenTaiko は「今 ≧ 前に叩いた時刻 + (終わり − 今) ÷ 残り回数」で叩く。
                        //   これを「今」について解くと、次に叩く時刻 = (残り回数 × 前に叩いた時刻 + 終わり) ÷ (残り回数 + 1)
                        int remaining = note.BalloonCount - rollHits[hit.Index];
                        if (remaining <= 0) nextMs = double.MaxValue;
                        else
                        {
                            // この速さで、終わりまでにあと何回叩けるか
                            int slots = nextMs < note.EndTimeMs ? (int)Math.Ceiling((note.EndTimeMs - nextMs) / interval) : 0;
                            if (slots < remaining)
                                nextMs = (remaining * hitMs + note.EndTimeMs) / (remaining + 1);
                        }
                    }
                    if (!balloonPopped[hit.Index] && nextMs < note.EndTimeMs)
                        autoHits.Enqueue((hit.Index, hit.Tick + 1), nextMs);
                }
            }
            branches.Advance(gameMs);
        }

        // 判定・打音・演出を1回の入力として実行する。判定時刻はSchedulerが決める。
        private void HandleScheduledHit(ScheduledDrumHit item)
        {
            // 取り出し時刻ではなく、元の入力時刻（または60Hzに調整した時刻）で判定。
            AdvanceTo(item.JudgeGameMs);
            long startedAt = Stopwatch.GetTimestamp();
            Hit(item.Input.Part, item.Input.Key, item.JudgeGameMs);
            if (diagnostics.Enabled && inputLog.Count > 0)
            {
                long processedAt = Stopwatch.GetTimestamp();
                InputLogEntry entry = inputLog[^1];
                string result = entry.RollText ?? (entry.NoteNumber == 0 ? "空振り" : entry.Result switch
                {
                    Judge.Good => "良",
                    Judge.Ok => "可",
                    Judge.Bad => "不可",
                    _ => "未判定"
                });
                double? delta = entry.NoteNumber > 0 ? entry.Delta : null;
                diagnostics.RecordInput(frameCount, item.JudgeGameMs, item.Input.Key.ToString(),
                    KeyConfig.GetDrumPartName(item.Input.Part), entry.NoteNumber, result, delta,
                    item.Input.CollectedAt, processedAt, item.Input.NativeEvent,
                    startedAt, item.Input.EventGameMs, item.JudgeGameMs,
                    item.JudgeGameMs - item.Input.EventGameMs, item.Mode, item.Limited);
            }
        }

        private void SaveInputTimingSettings(string message)
        {
            try
            {
                ConfigManager.SaveAll();
                SetDiagnosticMessage(message + "（設定を保存しました）");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetDiagnosticMessage(message + $"（保存失敗：{ex.Message}）");
            }
        }

        // 診断の保存結果を数秒だけ表示する
        private void SetDiagnosticMessage(string message)
        {
            diagnosticMessage = message;
            diagnosticMessageTimer = 4f;
        }

        // 記録するのは「F8 で表示中」か「CSV自動保存が ON」のとき
        private void UpdateDiagnosticsRecording()
        {
            diagnostics.Enabled = showDiagnostics || ConfigManager.Game.AutoSaveInputCsv;
        }

        // ファイル名：曲名_日付時間.csv(ファイル名に使えない文字は _ にする)
        private string DiagnosticCsvName()
        {
            string title = (chart?.Title ?? "").Trim();
            if (title.Length == 0) title = "InputDiag";
            char[] invalid = Path.GetInvalidFileNameChars();
            title = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
            if (title.Length > 80) title = title[..80];
            return $"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        }

        private void SaveDiagnosticCsv()
        {
            if (diagnostics.RowCount == 0)
            {
                Console.WriteLine("[入力診断] 保存する記録がないので、CSV は作りませんでした");
                return;
            }
            try
            {
                string folder = Path.Combine(AppContext.BaseDirectory, "Logs");
                Directory.CreateDirectory(folder);
                string name = DiagnosticCsvName();
                string saved = diagnostics.SaveCsv(Path.Combine(folder, name));
                SetDiagnosticMessage($"入力診断CSV保存：Logs/{Path.GetFileName(saved)}");
                Console.WriteLine($"[入力診断] CSV保存：{saved}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetDiagnosticMessage($"CSV保存失敗：{ex.Message}");
                Console.WriteLine($"[入力診断] CSV保存失敗：{ex}");
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

                    // ほかの分岐の音符は、見逃しにしないで飛ばす(無かったことにする)
                    if (branches.IsActive(course.Notes[index]))
                    {
                        SetJudge(index, Judge.Bad, null);
                        AddInputLog(new InputLogEntry(gameMs, "", "", IsDonNote(course.Notes[index]), noteNumbers[index],
                            gameMs - course.Notes[index].TimeMs, Judge.Bad, true, frameCount));
                    }
                }
                nextHittable++;
            }
        }

        // 叩いた
        private void Hit(DrumPart part, KeyboardKey key, double gameMs)
        {
            bool don = KeyConfig.IsDon(part); // true なら面(ドン)、false なら縁(カッ)
            effects.OnDrumHit(part);          // ミニ太鼓とレーンの光(当たっても空振りでも)
            SoundConfig sound = ConfigManager.Sound;
            float volume = (float)sound.GetFinalVolume(sound.SeVolume);

            // どの音符に当てるか決める
            int target = FindTarget(don, gameMs);  // 普通の音符(無ければ -1)
            int roll = FindActiveRoll(don, gameMs); // 今叩ける連打・風船(無ければ -1)

            // 連打・風船に当てるか(OpenTaiko の GetChipToJudge と同じ考え方)
            //   ・普通の音符が無い
            //   ・普通の音符が「過ぎていて不可になる」
            //   ・連打の方が譜面で先に書いてある(＝普通の音符は連打より後ろ)
            // のどれかなら連打・風船。それ以外は普通の音符(連打より前の音符を叩き残しているとき)
            if (roll >= 0 && (target < 0 || IsPastBad(target, gameMs) || roll < target))
            {
                HitRoll(roll, don, key, part, gameMs, volume);
                return;
            }

            // 音は叩いたら必ず鳴らす(音量 = Master × SE)
            (don ? donSound : kaSound)?.Play(volume);

            // 当てる音符が無ければ空振り
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

            // 良・可なら判定枠が光る(不可は光らない。OpenTaiko と同じ)
            if (judge != Judge.Bad)
            {
                bool big = course!.Notes[target].Type is NoteType.BigDon or NoteType.BigKa;
                effects.OnNoteHit(judge == Judge.Good, big);
                StartFlyingNote(don, big, gameMs);
            }
        }

        // その音符に当てると「過ぎていて不可」になるか
        private bool IsPastBad(int index, double gameMs)
        {
            return gameMs - course!.Notes[index].TimeMs > OkRangeMs;
        }

        // ===== 連打・風船 =====

        private static bool IsRollNote(Note note) => note.Type is NoteType.Roll or NoteType.BigRoll;
        private static bool IsBalloonNote(Note note) => note.Type is NoteType.Balloon or NoteType.Kusudama;

        // 今叩ける連打・風船(譜面で先に書いてあるもの)。無ければ -1
        //   連打：始まり ≦ 今 ＜ 終わり。ドン・カッどっちでも
        //   風船：始まりの17ms前 ≦ 今 ＜ 終わり。ドンだけ。割れたものは除く
        private int FindActiveRoll(bool don, double gameMs)
        {
            foreach (int index in rolls)
            {
                Note note = course!.Notes[index];
                if (!branches.IsActive(note)) continue; // ほかの分岐の連打・風船
                if (IsRollNote(note))
                {
                    if (gameMs >= note.TimeMs && gameMs < note.EndTimeMs) return index;
                }
                else if (don && !balloonPopped[index])
                {
                    if (gameMs >= note.TimeMs - BalloonEarlyMs && gameMs < note.EndTimeMs) return index;
                }
            }
            return -1;
        }

        // 連打・風船を叩いた
        private void HitRoll(int index, bool don, KeyboardKey key, DrumPart part, double gameMs, float volume, string? keyName = null)
        {
            Note note = course!.Notes[index];
            rollHits[index]++;
            totalRollHits++;

            // 表示：扇が閉じていたら開き直す
            if (rollShowTimer <= 0f || shownRoll != index) rollOpenElapsed = 0f;
            shownRoll = index;
            rollShowTimer = RollShowSeconds;
            rollBumpElapsed = 0f;

            string text;
            if (IsBalloonNote(note))
            {
                int left = Math.Max(0, note.BalloonCount - rollHits[index]);
                if (left == 0)
                {
                    // 割れた！(割れる音が無いスキンなら、いつものドンの音)
                    balloonPopped[index] = true;
                    poppedBalloon = index;
                    popTimer = PopShowSeconds;
                    if (balloonSound != null) balloonSound.Play(volume);
                    else donSound?.Play(volume);
                    text = "風船 割れた！";
                }
                else
                {
                    donSound?.Play(volume);
                    text = $"風船 のこり {left}";
                }
            }
            else
            {
                (don ? donSound : kaSound)?.Play(volume);
                text = $"連打 {rollHits[index]}打";
            }

            stats.ApplyRollHit(note);
            branches.OnRollHit(note);
            effects.OnRollHit(note);
            if (note.IsRoll || balloonPopped[index]) StartFlyingNote(don, note.IsBig || note.IsBalloon, gameMs);
            AddInputLog(new InputLogEntry(gameMs, keyName ?? key.ToString(), KeyConfig.GetDrumPartName(part), don, 0, 0, null, false, frameCount, text));
        }

        private void StartFlyingNote(bool don, bool big, double hitMs)
        {
            // 入力やオートをまとめて処理したフレームでも、本来の打鍵時刻から飛ばす。
            float age = (float)Math.Max(0, (nowMs - AudioOffsetMs - hitMs) / 1000.0);
            effects.StartFlyingNote(don, big, age);
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
                if (!branches.IsActive(note)) continue;                 // ほかの分岐の音符
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
            Note note = course!.Notes[index];
            PlayJudgement result = judge switch
            {
                Judge.Good => PlayJudgement.Good,
                Judge.Ok => PlayJudgement.Ok,
                _ => PlayJudgement.Bad,
            };
            // スコア・ゲージを先に足してから、分岐の数を数える(条件 s が新しいスコアを見るように)
            stats.ApplyJudge(note, result, branches.GaugeBranch(note));
            branches.OnJudge(note, result);
            effects.SetCombo(combo);

            // コンボの声(50・100…コンボになった瞬間。OpenTaiko と同じく、切れてからまた届けばまた鳴る)
            if (judge != Judge.Bad && comboVoices.TryGetValue(combo, out SoundEffect? voice))
            {
                SoundConfig sound = ConfigManager.Sound;
                voice.Play((float)sound.GetFinalVolume(sound.VoiceVolume));
            }

            if (delta != null) lastDeltaMs = delta;
            shownJudge = judge;
            judgeTimer = JudgeShowSeconds;
        }

        // 良・可で叩けた音符は消す(不可の音符はそのまま流れていく)
        // 割れた風船も消す
        private bool IsHidden(int index)
        {
            if (course != null && index < course.Notes.Count && !branches.IsActive(course.Notes[index])) return true; // ほかの分岐
            if (index < balloonPopped.Length && balloonPopped[index]) return true;
            if (IsInflating(index)) return true; // ふくらむ絵の方を出す
            return index < judges.Length && judges[index] is Judge.Good or Judge.Ok;
        }

        // 叩いている途中の風船か(ふくらむ絵があるときだけ、音符の絵を隠す)
        private bool IsInflating(int index)
        {
            if (course == null || index >= rollHits.Length || rollHits[index] == 0) return false;
            Note note = course.Notes[index];
            if (!IsBalloonNote(note) || balloonPopped[index]) return false;
            if (SkinManager.GetTexture(BalloonAnimeImage) == null) return false;

            double gameMs = nowMs - AudioOffsetMs;
            return gameMs < note.EndTimeMs;
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
            SceneManager.ChangeScene(new PlayScene(tja));
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
            double gameMs = nowMs - AudioOffsetMs;
            double viewMs = gameMs - ConfigManager.Game.ChartOffsetMs;

            // システムのスキンのときは背景を描かない(背景のアニメは画像を先に持っているので)
            if (!SkinManager.SystemSkin) background.Draw();
            DrawLane();
            branchLane.Draw(LaneX, LaneY); // 分岐のレーンの色(普通譜面なら何も描かない)
            branchLane.DrawText();         // 「普通譜面・玄人譜面・達人譜面」(分岐のある譜面だけ)
            hud.DrawGauge(stats);
            effects.DrawLaneLayer();   // ゴーゴーのレーン・レーンの光
            DrawJudgeFrame();
            effects.DrawBehindNotes(); // 判定枠の顔
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

            // 叩いている途中の風船(ふくらむ絵。音符の絵の上に描く)
            DrawBalloonAnime(gameMs);

            // 音合わせのガイド(音符より手前、左のパネルより後ろ)
            if (testView > 0) DrawGuides(chart, course, viewMs);

            // 左のパネル(判定枠を通りすぎた音符はこの下に隠れる)
            if (!SkinManager.Draw(PanelImage, 0, LaneY))
            {
                Raylib.DrawRectangle(0, (int)LaneY, (int)LaneX + 5, (int)LaneHeight, new Color(170, 50, 40, 255));
            }

            hud.DrawPanel(course);

            // 花火全体を、音符・パネルより手前へ一度だけ描く。
            effects.DrawHitEffects();
            effects.DrawPanel(); // ミニ太鼓とコンボ数
            effects.DrawFlyingNotes();

            DrawJudgeText();
            DrawRollPopup();
            DrawBalloonPopup(gameMs);
            effects.DrawFront(); // ゴーゴー開始の火花
            hud.DrawSongTitle();
            branchLane.DrawPopup();  // 「レベルアップ／レベルダウン」
            scoreboard.Set(goodCount, okCount, badCount, totalRollHits);
            if (ConfigManager.Game.ShowScoreboard) scoreboard.Draw(); // 良・可・不可・連打数(左下。選曲画面のオプションで消せる)
            scoreRank.Draw();    // 粋・雅・極
            end.Draw();          // 演奏後の演出(クリア成功・フルコンボなど)
            if (autoPlay)
            {
                Raylib.DrawRectangle(24, 211, 132, 46, new Color(20, 60, 45, 240));
                FontManager.Draw("AUTO", 44, 217, 30, Color.Lime);
            }
            if (showDebugInfo)
            {
                DrawInfo(chart, course);
                if (ShowInputLog) DrawInputLog();
                if (showDiagnostics) DrawInputDiagnostics();
            }
            if (diagnosticMessageTimer > 0f)
                FontManager.Draw(diagnosticMessage, InfoX, InfoTop - 34, 22, Color.SkyBlue);

            // 演奏が終わって次の画面へ行く前に、だんだん暗くする
            if (endFadeElapsed > 0f)
            {
                float alpha = Math.Clamp(endFadeElapsed / EndFadeSeconds, 0f, 1f);
                Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, Raylib.ColorAlpha(Color.Black, alpha));
            }
        }

        // 入力処理と音声同期の診断（判定や打音には影響しない）
        // F3 の板(左下)の真上に積む。レーンの下(y 540)から、F3 の板の上のメッセージ(y 836)の間に収める
        private const float DiagLine = 22f, DiagSize = 18f;
        private const float DiagTop = 548f;

        private void DrawInputDiagnostics()
        {
            float x = InfoX;
            float y = DiagTop;
            Raylib.DrawRectangle((int)InfoX - 10, (int)DiagTop - 8, 1000, (int)(DiagLine * 12 + 12), InfoPanelColor);
            void Line(string text, Color color)
            {
                FontManager.Draw(text, x, y, DiagSize, color);
                y += DiagLine;
            }

            string autoSave = ConfigManager.Game.AutoSaveInputCsv ? "曲が終わったら CSV を自動保存" : "CSV 自動保存：OFF（選曲画面のオプション）";
            Line($"入力診断（F8 で切替）　{autoSave}", Color.SkyBlue);
            Line($"記録：入力 {diagnostics.InputCount}回 / データ {diagnostics.RowCount}行", Color.White);
            Line($"直近0.25秒の最大フレーム時間：{diagnostics.RecentMaxFrameMs:0.###} ms", Color.White);
            Line($"直近の音声・画面ズレ：{InputDiagnostics.FormatMs(diagnostics.LatestSyncErrorMs)}", Color.White);
            Line($"直近0.25秒の最大時計補正：{diagnostics.RecentMaxCorrectionMs:+0.###;-0.###;0} ms", Color.White);
            Line($"出力遅延の推定（ミキサー時）：{InputDiagnostics.FormatMs(diagnostics.LatestOutputEstimateMs)}", Color.White);
            Line($"直近の入力処理：{InputDiagnostics.FormatMs(diagnostics.LastInputHandlingMs)}", Color.LightGray);
            Line($"Windows：押下 {diagnostics.WindowsDownCount} / 解放 {diagnostics.WindowsUpCount} / リピート {diagnostics.WindowsRepeatCount}", Color.White);
            Line($"Raylibとの照合：一致 {diagnostics.NativeMatchedCount} / 未照合 {diagnostics.NativeUnmatchedCount} / バッファ超過 {windowsInput.DroppedEvents}", Color.White);
            Line($"Windows→Raylib：{InputDiagnostics.FormatMs(diagnostics.LastNativeToRaylibMs)}（OS時刻の概算） / {windowsInput.Status}", Color.LightGray);
            Line($"判定：{DrumInputScheduler.ModeName(ConfigManager.Game.InputTiming)}  /  60打/秒制限：{(ConfigManager.Game.LimitDrumHitsTo60PerSecond ? "ON" : "OFF")}", Color.White);
            Line($"処理待ち：{inputScheduler.PendingCount}打　最終入力：{InputDiagnostics.FormatMs(diagnostics.LastScheduledWaitMs)} 待機", Color.LightGray);
        }

        // ===== 連打・風船の表示 =====

        // 今叩いている途中の風船(1回以上叩いて、まだ割れていなくて、時間内)。無ければ -1
        private int FindHittingBalloon(double gameMs)
        {
            foreach (int index in rolls)
            {
                Note note = course!.Notes[index];
                if (!IsBalloonNote(note) || balloonPopped[index] || rollHits[index] == 0) continue;
                if (gameMs >= note.TimeMs - BalloonEarlyMs && gameMs < note.EndTimeMs) return index;
            }
            return -1;
        }

        // ふくらむ風船(叩いた回数に合わせて大きくなる)。割れた瞬間は割れた絵を少しだけ出す
        private void DrawBalloonAnime(double gameMs)
        {
            if (SkinManager.GetTexture(BalloonAnimeImage) is not Texture2D anime) return;

            int frame;
            int index = FindHittingBalloon(gameMs);
            if (index >= 0)
            {
                // 0〜6コマ目：残りが減るほど大きく
                Note note = course!.Notes[index];
                float progress = note.BalloonCount > 0 ? (float)rollHits[index] / note.BalloonCount : 1f;
                frame = Math.Clamp((int)(progress * (BalloonAnimeFrames - 1)), 0, BalloonAnimeFrames - 2);
            }
            else if (popTimer > 0f && poppedBalloon >= 0)
            {
                frame = BalloonAnimeFrames - 1; // 割れた絵
            }
            else
            {
                return;
            }

            float frameHeight = anime.Height / (float)BalloonAnimeFrames;
            Rectangle source = new Rectangle(0, frame * frameHeight, anime.Width, frameHeight);
            // 風船の口(左はし)を音符と同じ位置に、上下は判定枠の真ん中にそろえる
            Rectangle dest = new Rectangle(JudgeX - NoteHalf, NoteY - frameHeight / 2f, anime.Width, frameHeight);
            Raylib.DrawTexturePro(anime, source, dest, Vector2.Zero, 0f, Color.White);
        }

        // 連打数(扇＋数字＋「連打!!」)。最後に叩いてから2秒くらい出て、閉じて消える
        private void DrawRollPopup()
        {
            if (rollShowTimer <= 0f || shownRoll < 0) return;
            int count = rollHits[shownRoll];
            if (IsBalloonNote(course!.Notes[shownRoll])) return; // 風船は吹き出しの方に出す

            // 扇のコマ：開くとき 0→4、閉じるとき 4→0(OpenTaiko と同じく1コマ40ms)
            int opening = (int)(rollOpenElapsed / RollFrameSeconds);
            int closing = (int)(rollShowTimer / RollFrameSeconds);
            int frame = Math.Clamp(Math.Min(opening, closing), 0, RollBaseFrames - 1);
            byte numberAlpha = frame switch { 0 => 64, 1 => 128, 2 => 192, _ => 255 };

            if (SkinManager.GetTexture(RollBaseImage) is Texture2D baseImage)
            {
                float frameHeight = baseImage.Height / (float)RollBaseFrames;
                Rectangle source = new Rectangle(0, frame * frameHeight, baseImage.Width, frameHeight);
                Raylib.DrawTexturePro(baseImage, source, new Rectangle(RollFrameX, RollFrameY, baseImage.Width, frameHeight), Vector2.Zero, 0f, Color.White);
            }

            // 叩いた直後は数字が縦に少し伸びる
            int bumpIndex = (int)(rollBumpElapsed / RollBumpStep);
            float bump = bumpIndex < RollBump.Length ? RollBump[bumpIndex] : 0f;
            Color tint = new Color((byte)255, (byte)255, (byte)255, numberAlpha);

            if (DrawDigits(RollNumberImage, count, RollNumberX, RollNumberY,
                1f, 1f + bump, RollNumberInterval, tint) < 0)
            {
                // スキンに数字の画像が無いときは普通の文字で
                string text = $"{count}打";
                FontManager.Draw(text, RollNumberX - FontManager.Measure(text, 60).X / 2f, RollNumberY - 70, 60, tint);
                return;
            }

            // 「連打!!」は数字の桁数に影響されない位置に置く（Xは文字の中心）。
            if (SkinManager.GetTexture(RollTextImage) is Texture2D textImage)
            {
                Raylib.DrawTexture(textImage,
                    (int)(RollTextX - textImage.Width / 2f), (int)RollTextY, tint);
            }
        }

        // 風船の残りの数(吹き出し＋数字)。叩いている途中だけ出す
        private void DrawBalloonPopup(double gameMs)
        {
            int index = FindHittingBalloon(gameMs);
            if (index < 0) return;

            int left = Math.Max(0, course!.Notes[index].BalloonCount - rollHits[index]);

            SkinManager.Draw(BalloonBaseImage, BalloonFrameX, BalloonFrameY);

            int bumpIndex = (int)(rollBumpElapsed / RollBumpStep);
            float bump = bumpIndex < RollBump.Length ? RollBump[bumpIndex] : 0f;
            float scale = BalloonNumberScale;

            if (DrawDigits(BalloonNumberImage, left, BalloonNumberX, BalloonNumberY,
                scale, scale + bump, BalloonNumberInterval, Color.White) < 0)
            {
                string text = left.ToString();
                FontManager.Draw(text, BalloonNumberX - FontManager.Measure(text, 60).X / 2f, BalloonNumberY - 70, 60, Color.Black);
            }
        }

        // 数字の画像(0〜9 が横に10個並んだもの)で value を描く
        //   centerX … 数字全体の真ん中、bottomY … 数字の下
        //   scaleX・scaleY … 大きさ(縦に伸ばすと、下はそのままで上に伸びる)
        // 描いた一番右の x を返す(画像が無ければ -1)
        private static float DrawDigits(string image, int value, float centerX, float bottomY,
            float scaleX, float scaleY, float numberInterval, Color tint)
        {
            if (SkinManager.GetTexture(image) is not Texture2D digits) return -1;

            string text = Math.Max(0, value).ToString();
            float digitWidth = digits.Width / 10f;
            float digitHeight = digits.Height;
            float step = numberInterval * scaleX;           // ポップアップ別の数字の間隔
            float drawWidth = digitWidth * scaleX;
            float drawHeight = digitHeight * scaleY;

            // 全体の幅 = 間隔×(文字数−1) + 1文字の幅
            float total = step * (text.Length - 1) + drawWidth;
            float x = centerX - total / 2f;

            foreach (char c in text)
            {
                int d = c - '0';
                Rectangle source = new Rectangle(d * digitWidth, 0, digitWidth, digitHeight);
                Rectangle dest = new Rectangle(x, bottomY - drawHeight, drawWidth, drawHeight);
                Raylib.DrawTexturePro(digits, source, dest, Vector2.Zero, 0f, tint);
                x += step;
            }
            return x - step + drawWidth;
        }

        // 入力ログ(右下)。新しいものが下
        //   例：「 23.715 秒  D 左面 ドン → 45個目 ドン  -12.3 ms 良」
        //   同じフレームで処理した叩きには「同」が付く(同じ時刻として判定している)
        // 入力ログ(右下。新しいものが下)
        private void DrawInputLog()
        {
            const float x = 1270;
            const float lineHeight = 24;
            const float size = 18;
            const float top = 1080 - 20 - lineHeight * (InputLogCount + 1) - 8;

            Raylib.DrawRectangle((int)x - 10, (int)top - 8, Screen.Width - (int)x + 10, (int)(lineHeight * (InputLogCount + 1) + 14), InfoPanelColor);
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
                else if (e.RollText != null)
                {
                    text = $"{time}  {e.KeyName} {e.PartName} {color} → {e.RollText}";
                    textColor = new Color(255, 170, 80, 255);
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

        // 判定の文字。打撃直後に上へ跳ね、約100msで定位置へ戻る。
        private void DrawJudgeText()
        {
            if (shownJudge is not Judge judge || judgeTimer <= 0f) return;

            float t = 1f - judgeTimer / JudgeShowSeconds;   // 0 → 1 に進む
            float age = Math.Max(0f, JudgeShowSeconds - judgeTimer);
            float bounce = age < 0.1f ? 9f * MathF.Sin(MathF.PI * age / 0.1f) : 0f;
            float y = NoteY - 119 - bounce;
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

        // レーン
        private static void DrawLane()
        {
            if (!SkinManager.Draw(LaneImage, LaneX, LaneY))
            {
                Raylib.DrawRectangle((int)LaneX, (int)LaneY, Screen.Width - (int)LaneX, (int)LaneHeight, new Color(50, 50, 50, 255));
            }
        }

        // 判定枠(レーンの光より上に描く。光っていても判定枠が見えるように)
        private static void DrawJudgeFrame()
        {
            // 枠の画像だけ右へ1px。音符の流れ・判定時刻・花火の原点は変えない。
            const float frameX = JudgeX + 1f;
            if (!SkinManager.Draw(JudgeFrameImage, frameX - NoteHalf, NoteY - NoteHalf))
            {
                // 画像が無いとき(システムのスキン)は、本当の判定の場所(JudgeX)に描く
                Raylib.DrawRing(new Vector2(JudgeX, NoteY), 44, 50, 0, 360, 48, Color.LightGray);
            }
        }

        // 音合わせのガイド
        //   ・判定枠の中心(この時刻ちょうどに叩くと ±0ms)に黄色の線
        //   ・判定の幅の目盛り(今のBPM・SCROLL 1・HS倍率で、何ms が何px になるか)
        //   ・流れてくる音符の中心に水色の線(判定枠の画像の中心と、音符の画像の中心が合っているか見る)
        private void DrawGuides(Chart chart, Course course, double viewMs)
        {
            float top = LaneY, bottom = LaneY + LaneHeight;
            float pxPerMs = (float)(GetCurrentBpm(chart, course) * hiSpeed / 60000.0 * PixelsPerBeat);

            void Tick(double ms, Color color, float length)
            {
                foreach (int sign in new[] { -1, 1 })
                {
                    float x = JudgeX + sign * (float)ms * pxPerMs;
                    Raylib.DrawLineEx(new Vector2(x, top), new Vector2(x, top + length), 2, color);
                    Raylib.DrawLineEx(new Vector2(x, bottom - length), new Vector2(x, bottom), 2, color);
                }
            }
            Tick(BadRangeMs, GuideBadColor, 26);
            Tick(OkRangeMs, GuideOkColor, 34);
            Tick(GoodRangeMs, GuideGoodColor, 44);
            Raylib.DrawLineEx(new Vector2(JudgeX, top), new Vector2(JudgeX, bottom), 2, GuideCenterColor);

            // 流れてくる音符の中心
            for (int i = 0; i < course.Notes.Count; i++)
            {
                if (IsHidden(i)) continue;
                Note note = course.Notes[i];
                float x = GetX(note.TimeMs, note.Bpm, note.Scroll, viewMs);
                if (x < LaneX - 100 || x > Screen.Width + 100) continue;
                float half = note.IsBig ? 80f : 56f;
                Raylib.DrawLineEx(new Vector2(x, NoteY - half), new Vector2(x, NoteY + half), 2, GuideNoteColor);
                Raylib.DrawCircleV(new Vector2(x, NoteY), 4, GuideNoteColor);
            }

            FontManager.Draw("ガイド：判定の中心(黄)　±25ms 良(緑)　±75ms 可(橙)　±108ms 不可(赤)　音符の中心(水色)　F10 で切替",
                LaneX + 12, bottom + 4, 18, Color.White);
        }

        // 小節線
        private void DrawBarLines(Course course, double viewMs)
        {
            foreach (BarLine bar in course.BarLines)
            {
                if (!bar.Visible) continue;
                if (!branches.IsActive(bar)) continue; // ほかの分岐の小節線

                float x = GetX(bar.TimeMs, bar.Bpm, bar.Scroll, viewMs);
                if (x < LaneX || x > Screen.Width) continue;

                Raylib.DrawLineEx(new Vector2(x, BarTop), new Vector2(x, BarBottom), 3, new Color(255, 255, 255, 170));
            }
        }

        // 音符1つ
        private static Texture2D? GetNoteTexture(string file)
        {
            // 1番の画像に通常の顔が含まれる。無いスキンは従来の0番へ戻す。
            return SkinManager.GetTexture(FaceNotesDir + file) ?? SkinManager.GetTexture(NotesDir + file);
        }

        private void DrawNote(Note note, double viewMs)
        {
            float x = GetX(note.TimeMs, note.Bpm, note.Scroll, viewMs);

            // ----- 風船・くすだま -----
            if (note.IsBalloon)
            {
                if (viewMs >= note.EndTimeMs) return;   // 終わったら消える
                if (viewMs >= note.TimeMs) x = JudgeX; // 始まったら判定枠で止まる
                if (x - NoteHalf > Screen.Width) return;

                // 風船の画像は横長(390×195)。左の丸の中心が音符の位置
                if (GetNoteTexture("10.png") is Texture2D balloon)
                    Raylib.DrawTextureEx(balloon, new Vector2(x - NoteHalf, NoteY - NoteHalf), 0f, 1f, Color.White);
                else
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
                Texture2D? head = GetNoteTexture((big ? "7" : "4") + ".png");
                Texture2D? body = GetNoteTexture((big ? "8" : "5") + ".png");
                Texture2D? tail = GetNoteTexture((big ? "9" : "6") + ".png");

                if (head is Texture2D h && body is Texture2D b && tail is Texture2D t)
                {
                    bool heated = SkinManager.BeginRollHeat(effects.GetRollHeat(note));
                    // 胴：頭の中心〜おわりの位置まで横に伸ばす
                    Raylib.DrawTexturePro(b, new Rectangle(0, 0, b.Width, b.Height),
                        new Rectangle(x, NoteY - NoteHalf, Math.Max(0, endX - x), NoteSize), Vector2.Zero, 0f, Color.White);
                    // おしり：画像の左端をおわりの位置に合わせる
                    Raylib.DrawTexture(t, (int)endX, (int)(NoteY - NoteHalf), Color.White);
                    // 頭
                    Raylib.DrawTexture(h, (int)(x - NoteHalf), (int)(NoteY - NoteHalf), Color.White);
                    if (heated) Raylib.EndShaderMode();
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

            if (GetNoteTexture(image + ".png") is Texture2D texture)
                Raylib.DrawTextureEx(texture, new Vector2(x - NoteHalf, NoteY - NoteHalf), 0f, 1f, Color.White);
            else
            {
                bool don = note.Type == NoteType.Don || note.Type == NoteType.BigDon;
                DrawFallbackCircle(x, note.IsBig ? 72 : 48, don ? DonColor : KaColor);
            }
        }

        // 音符の下の文字(ドン・カッ・連打…)
        private void DrawSeText(Note note, double viewMs)
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
        // F3 のデバッグ表示(左下：判定の数の板の右どなりに、小さめの文字でまとめる)
        private const float InfoX = 262f, InfoTop = 870f, InfoLine = 25f, InfoSize = 20f;
        private static readonly Color InfoPanelColor = new Color(0, 0, 0, 170);

        private void DrawInfo(Chart chart, Course course)
        {
            Raylib.DrawRectangle((int)InfoX - 10, (int)InfoTop - 8, 1000, (int)(InfoLine * 7 + 14), InfoPanelColor);
            float y = InfoTop;
            void Line(string text, Color color)
            {
                FontManager.Draw(text, InfoX, y, InfoSize, color);
                y += InfoLine;
            }

            Line($"{Chart.GetCourseName(course.Type)} ★{course.Level}　音符 {course.Notes.Count}個　小節 {course.BarLines.Count}"
                + $"　　時間 {nowMs / 1000.0:F2} 秒　BPM {GetCurrentBpm(chart, course):0.##}", Color.White);

            // ズレ(プラスなら遅い)。平均がいつも同じ向きにズレていたら、音オフセットで直せる
            string last = lastDeltaMs is double d ? DeltaText(d) : "-";
            string average = deltaCount > 0 ? $"{DeltaText(deltaSumMs / deltaCount)}（{deltaCount}回）" : "-";
            Line($"コンボ {combo}（最大 {maxCombo}）　　ズレ 最後 {last} / 平均 {average}", Color.Gold);

            // 曲の状態(同期ズレ = 曲の再生位置 − 画面の時間。0 に近いほどいい)
            if (song != null)
            {
                string sync = song.IsPlaying ? $"（同期ズレ {song.PositionMs - nowMs:+0.0;-0.0;0.0} ms）" : "";
                Line($"曲 {chart.Wave}　{song.PositionMs / 1000.0:F2} / {song.LengthMs / 1000.0:F2} 秒{sync}", Color.White);
            }
            else Line(audioMessage, new Color(255, 170, 80, 255));

            // 測る用：出力方式と、音が聞こえるまでの遅れの目安
            string latencyText = !latencyMeasured ? "（曲が鳴ると出ます）"
                : AudioManager.Mixer != 0
                    ? $"まだ鳴っていない量 平均 {latencyAverageMs:0.0} ms（曲の位置から引いている）"
                    : $"機器の遅れ {latencyAverageMs:0.0} ms・バッファ {AudioManager.BassBufferMs} ms（曲の位置からは引いていない）";
            string lagText = AudioManager.HitSoundLagMs > 0 ? $"　効果音の遅れ補正 -{AudioManager.HitSoundLagMs:0.0} ms" : "";
            Line($"出力 {AudioManager.GetTypeName(AudioManager.CurrentType)}　{latencyText}{lagText}", Color.White);
            Line($"入力 {DrumInputScheduler.ModeName(ConfigManager.Game.InputTiming)} / 60打制限 {(ConfigManager.Game.LimitDrumHitsTo60PerSecond ? "ON" : "OFF")}",
                Color.SkyBlue);

            // 譜面分岐の状態
            Line(branches.GetDebugText(), new Color(255, 170, 230, 255));

            KeyboardKey restartKey = ConfigManager.Key.SystemKeys.GetValueOrDefault(SystemAction.QuickRestart, KeyboardKey.F5);
            Line($"{restartKey} 最初から　Esc もどる　F3 表示　F6 モード　F7 60打制限　F8 診断　F10 見た目（F6〜F10 は未割り当て時のみ）",
                Color.SkyBlue);
        }

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
        private float GetX(double timeMs, double bpm, double scroll, double viewMs)
        {
            return JudgeX + (float)((timeMs - viewMs) * bpm * scroll * hiSpeed / 60000.0 * PixelsPerBeat);
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