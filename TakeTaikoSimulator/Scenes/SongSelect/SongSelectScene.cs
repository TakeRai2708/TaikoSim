using System.Numerics;
using System.Text;
using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;
using static TakeTaikoSimulator.UI.MenuDraw;

namespace TakeTaikoSimulator.Scenes
{
    // 選曲画面(演奏ゲーム)。TaikøNauts / OpenTaiko の流れに合わせた
    //   1. ジャンルをえらぶ(最近遊んだ曲・最近追加された曲・お気に入り・カテゴリ)
    //   2. 曲をえらぶ(7曲ごとに「とじる」、最後に「ランダム」。OpenTaiko と同じ並び)
    //      TaikøNauts と同じく、開いたジャンルの場所に曲が入る(上下にはほかのジャンルが見えたまま)
    //   3. 難易度をえらぶ(左から 戻る・オプション・かんたん・ふつう・むずかしい・おに)
    //      おにを選んでいるときに、右(カッ・→)を10回たたくと裏おにに切り替え(本物の新筐体と同じ。OpenTaiko は1回)。Tab ならすぐ切り替え(TaikøNauts と同じ)
    // 見た目と動きは TaikøNauts の動画を測って合わせた
    //   ・板は斜めに並ぶ(1つ下がるごとに右へ40px、下へ135px)
    //   ・カーソルは0.15秒で1つ動く。動いている間は板は小さいまま
    //   ・止まってから0.43秒たつと、選んでいる板が開いて、同時に曲が流れ始める(フェードなし)
    //   ・決定すると、枠が点滅 → 一覧が消える → 少し間をあけて難易度の板が出る
    //   ・ジャンル・「とじる」も、決定すると枠が点滅してから切り替わる
    //   (斜めの板の動きは UI/BarList。モード選択と同じもの)
    // 操作：カッ(左縁・右縁)・↑↓←→・ホイールで動かす、ドン・Enter・クリックで決定、Esc で1つ戻る
    //       F3：検索、F4：お気に入りに入れる/外す、F2：オプション(難易度をえらんでいるとき)
    // 選んでいた場所は static に覚えておくので、演奏から戻ってきても同じ曲を選んだまま
    public class SongSelectScene : Scene
    {
        public override Platform.DiscordPresence.Info? Presence => training
            ? new("特訓モードで曲を選択中")
            : new("曲を選んでいます");

        // プレイヤーを変えたら、その人のお気に入り・最近の曲で一覧を作り直す。
        public static void ResetForProfile()
        {
            builtFor = null;
            inSongs = false;
            genrePos = songPos = 0;
            openSpecial = Special.None;
            openCat = -1;
            lastSongKey = searchText = "";
            searchCourse = null; searchStars = null; searchGenre = null;
        }

        private readonly bool training;
        public SongSelectScene(bool training = false, bool returningFromDifficulty = false)
        {
            this.training = training;
            this.returningFromDifficulty = returningFromDifficulty;
        }
        private readonly bool returningFromDifficulty;

        // ===== 一覧の項目 =====

        private enum Special { None, Recent, Added, Favorites, Search, DanSongs }
        private enum Kind { Genre, Label, Back, Song, Random }

        private sealed class Item
        {
            public Kind Kind;
            public string Title = "";
            public Color Color;
            public int Cat = -1;               // ジャンル：カテゴリの番号
            public Special Special;            // ジャンル：特別な項目のとき
            public int SongCount;              // ジャンル：曲の数
            public SongEntry? Song;            // 曲
            public SongCategory? Category;     // カテゴリ(ジャンルの板・曲のときだけ。スキンの板の番号を決めるのに使う)
            public string CategoryTitle = "";  // 曲：入っているカテゴリの名前
            public DanCourse? Dan;             // 曲：「段位道場の課題曲」の中の曲のとき、どの段位の課題曲か
        }

        // 「最近追加された曲」に出す数
        private const int AddedMax = 30;
        // 「とじる」を入れる間隔(OpenTaiko と同じ7曲ごと)
        private const int BackInterval = 7;

        // ===== 覚えておく場所(static：演奏から戻ってきても同じ) =====
        private static bool inSongs;                   // false：ジャンルをえらぶ、true：曲をえらぶ
        private static int genrePos;                   // ジャンルのカーソル(一覧の数で割った余りが項目の番号。ぐるっと回れる)
        private static int songPos;                    // 曲のカーソル
        private static Special openSpecial = Special.None;
        private static int openCat = -1;
        private static string lastSongKey = "";        // 最後に選んでいた曲(一覧を作り直しても同じ曲に戻すため)
        private static CourseType lastCourse = CourseType.Oni;
        private static bool courseChosenThisRun;   // 起動してから一度でも難易度を決めて遊んだか
        private static string searchText = "";
        private static List<SongCategory>? builtFor;   // 今の一覧を作ったときのカテゴリ(読み直したら作り直す)

        private readonly List<Item> genres = new();
        private readonly List<Item> songs = new();     // 曲をえらんでいるときの一覧(ジャンルの一覧の、開いたジャンルの場所に曲を入れたもの)
        private int songStart;                         // songs の中で、開いたジャンルの曲が始まる場所(最初の「とじる」)
        private int ownCount;                          // 開いたジャンルの項目の数(とじる・曲・ランダム)
        private readonly BarList list = new(MenuSkin.SongFolder);         // 斜めの板の動き
        private Action? afterBlink;                    // 決定の点滅が終わったらすること(ジャンルを開く・とじる)

        // ===== ジャンルを決めたあとの「めくれる」 =====
        //   決めた瞬間を 0 として：0〜0.53秒 枠が点滅 → ここから下の時間(FlipStart からの秒)
        //   11/22 TaikøNauts の録画(J-POP を開く・閉じる)を 1/60秒ずつ測り直した。TakeTaiko の録画と並べて、ずれていた所を直した
        //   (前の 10/30 の測り方とくらべて：ポンッと大きくなるのが無い・細い線のまま止まる時間がほぼ無い・広がるのは加速しながら)
        //   0〜0.20    ジャンル名・キャラ・吹き出しが消えていく(中身の無い板だけが残る)
        //   0.04〜0.34 上の板は上へ、下の板は下へ、加速しながら 400px すべって画面の外へ(11/25 測り直し)
        //   0.337〜0.553 板が横につぶれていく(幅 793 → 736 → 622 → 525 → 430 → … → 32 → 12 → 0px)
        //   0.553〜0.603 見えない(幅 0)
        //   0.603〜0.837 開いた板の高さ(下そろえで上に 94px 高い)になって、加速しながら元の幅まで広がる(8 → 24 → 45 → 77 → 116 → … → 793px)
        //   0.837〜0.904 中身の無い開いた板のまま
        //   0.904      中身が最初の曲の板(開いた形)になる(11/25 に測り直し)
        //   0.904〜1.184 上下の板が 340px 外からすべり込む。曲の板の中身は 0.15秒でじわっと出る
        private const float FlipStart = 0.53f;
        private const float FlipPopEnd = 0f, FlipPopScaleX = 1f, FlipPopScaleY = 1f;
        private const float FlipFadeEnd = 0.20f;
        // 11/25 録画(15-05-30)：まわりの板は消えずに、だんだん速くなりながら画面の外まで 400px すべっていく(0.04〜0.34)
        private const float FlipOthersStart = 0.04f, FlipOthersEnd = 0.34f, FlipOthersShift = 400f;
        private const float FlipSquashStart = 0.337f, FlipSquashEnd = 0.553f, FlipThinEnd = 0.603f, FlipGrowEnd = 0.837f, FlipSwitch = 0.904f, FlipEnd = 1.184f;
        // 11/25 TaikøNauts の録画(15-05-30)：開ききって 0.067秒後から、上の板は上から・下の板は下から 340px すべり込む(0.28秒・最後ゆっくり)。
        //   すべっている間も板は濃いまま。曲の板の中身は最初の 0.15秒でじわっと出る
        private const float FlipInShift = 340f, FlipContentFade = 0.15f;
        private const float FlipThinScale = 0f;
        private const float FlipTallExtra = 94f;   // 広がるときの板は、開いた曲の板の高さ(中の水色 175 → 269px。下はそのまま。TaikøNauts の録画は縦が 0.979 倍に縮んでいたので戻した)
        // つぶれていく幅(793px を 1 として、1/60秒ごと。TaikøNauts の 11/22 の録画)
        private static readonly float[] FlipSquashWidths = { 1f, 0.928f, 0.784f, 0.662f, 0.542f, 0.433f, 0.340f, 0.257f, 0.189f, 0.126f, 0.075f, 0.040f, 0.015f, 0f };
        private float flipTime = -1f;      // めくれ始めてからの時間(-1 ならめくっていない)
        private bool flipTall;             // めくれて広がっている途中：開いた曲の板の高さで描く
        private Action? flipOpen;          // 0.70秒で開くジャンル
        private bool flipOpened;
        private bool flipFromBack;         // 「とじる」でめくっている(細い線から広がるときは、戻り先のジャンルの色の板にする)
        private float uraClock;                        // 板が開いてからの時間(おにと裏おにの札を入れ替えるのに使う)
        private Color backColor = new Color(30, 40, 70, 255);

        // ===== 難易度をえらぶ =====
        private static readonly CourseType[] PanelCourses = { CourseType.Easy, CourseType.Normal, CourseType.Hard, CourseType.Oni };
        private bool diffOpen;
        // 戻る専用。秒数は実測前の仮調整で、開くアニメの逆再生ではない。
        private const float ReturnOutSeconds = 0.18f, ReturnInSeconds = 0.18f;
        private float returnTime = -1f;
        private Action? returnSwap;
        private float ReturnAlpha => returnTime < 0f ? 1f : returnTime < ReturnOutSeconds
            ? 1f - returnTime / ReturnOutSeconds
            : Math.Clamp((returnTime - ReturnOutSeconds) / ReturnInSeconds, 0f, 1f);

        private void BeginReturn(Action swap)
        {
            if (returnTime >= 0f) return;
            returnTime = 0f;
            returnSwap = swap;
        }
        private int diffCursor;                        // 0 戻る、1 オプション、2〜5 かんたん〜おに
        private bool ura;
        private SongEntry? diffSong;
        private Color diffColor;                       // 難易度の板の色(選んだ曲のカテゴリの色)
        private int diffType;
        private Item? diffItem;                          // 難易度の板の番号(TaikøNauts のスキンの Difficulty_Select/Background/番号.png)
        private float diffTime;                        // 決定してからの時間(切り替えのアニメに使う)
        private Aup2? smallControlGuide;
        // 左上の太鼓の案内(Control_Guide/Large/Anime.aup2。346×312・653コマ＝10.9秒でくり返す)
        //   11/11 の録画：画面の左上 (0, 0) に原寸で出る。たたいても変わらず、この画面に入った時からずっとくり返す。
        //   曲を決めて 0.77秒で消え、難易度をえらんでいる間は出ない
        private Aup2? largeControlGuide;
        // 置く場所は (0.5, 0.5)。この絵の白い板(Base.png)は、アニメの中で半ピクセルずれた所(-0.5)に置いてある。
        //   そのまま (0, 0) に出すと、絵の右はしに、左はしの色がにじんで縦の線が出た(11/12 たけらい「横に縦の黒い線がある」)。
        //   半ピクセル足して、ぴったり画面の点に合わせる
        internal static readonly Vector2 GuideOffset = new Vector2(0.5f, 0.5f);
        private readonly PlayOptionsPanel options = new();

        // ===== 検索 =====
        private bool searchFocused;
        private string searchBefore = "";              // 検索の箱を開く前の文字(Esc でやめたら戻す)
        private float caretTimer;
        private bool searchSubmitQueued;
        private readonly TextInputBuffer searchInput = new();
        private float searchScrollX;
        private static readonly Rectangle SearchBox = new Rectangle(410, 330, 1100, 60);
        private const float SearchTextX = 430, SearchTextY = 345, SearchFontSize = 28;
        private const int SearchMaxLength = 40;
        public override bool IsWaitingKey => searchFocused || join.Naming;
        public override bool CanOpenSettings => !join.Naming && !join.Busy;
        // 2人目の途中参加（データ選択 → 通信中 → 完了）。曲の一覧・ジャンルの一覧にいるときだけ、まだ使っていない方のキーのドンで開く
        //   難易度選択では入れない（11/12 たけらい ④A）。特訓モードは1人用なので入れない
        private readonly PlayerJoin join = new();
        private static CourseType? searchCourse;
        private static int? searchStars;
        private static string? searchGenre;
        private (CourseType? Course, int? Stars, string? Genre) searchFilterBefore;
        private string[] searchGenres = { "すべてのジャンル" };
        private int[] searchLevels = Array.Empty<int>();
        private string[] searchLevelLabels = { "すべて" };
        private List<SongCategory>? searchCacheSource;
        private (string, CourseType?, int?, string?) searchCacheKey;
        private List<(SongEntry, SongCategory)> searchCache = new();
        private static readonly string[] SearchCourses = { "すべての難易度", "かんたん", "ふつう", "むずかしい", "おに", "裏おに" };

        private static readonly Color CompositionColor = new Color(255, 230, 150, 255);

        // ===== そのほか =====
        private readonly SongPreview preview = new();
        public override string? MenuBgmFile => MenuBgm.SongSelectFile;
        public override double MenuBgmLoopMs => MenuBgm.SongSelectLoopMs;
        private bool leavingForDifficulty;
        public override bool MenuBgmMuted => ((inSongs || diffOpen) && preview.HasPlaybackRequest) ||
            leavingForDifficulty || SongLoading.IsActive;
        private string toast = "";
        private float toastTimer;
        private bool wasScanning;
        private bool active = true;
        private static readonly Rectangle NoClickArea = new Rectangle(0, 0, 0, 0);
        private readonly Random random = new();

        // ===== 決定 → 難易度の板(決定した時を0秒として。TaikøNauts を測った値) =====
        //   (10/29 の A.mp4 を 1/60秒ずつ：69.15秒に決定 → 枠の点滅 → 0.53秒から白 → 0.65秒で枠が消える → 0.70〜0.93秒で一覧が消える)
        private const float FrameHideAt = 0.65f;                          // 白くなった枠が消える
        //   11/11 の録画(2026-11-11 23-44-20.mp4 の 70.97秒に決定)を 1/60秒ずつ測り直した：
        //     0〜0.55秒 枠の点滅(4回) → 0.77秒 左上の太鼓の案内がパッと消える・一覧が消え始める → 1.00秒 一覧が消えきる
        //     → 1.25〜1.77秒 難易度の板がじわっと出る(まっすぐ 0.52秒。前は 0.33秒にしていた) → 1.97秒 1P の印(0.05秒で出る)
        private const float ListFadeStart = 0.77f, ListFadeEnd = 1.00f;   // 一覧が消える
        private const float PanelInStart = 1.25f, PanelInEnd = 1.77f;     // 難易度の板が出る
        private const float MarkerAt = 1.97f, MarkerFade = 0.05f;         // 1P の印が出る
        private const float GuideHideAt = 0.77f;                          // 左上の太鼓の案内が消える

        // ===== 難易度のカーソルの動き(11/11 の同じ録画) =====
        //   動かしたとき(90.62秒・98.63秒)：枠と 1P の印は、すぐ次の箱へ移る(すべらない)。
        //     1P の印だけ、0.117秒かけて下へ 8.6px しずんで戻る
        //   決めたとき(102.25秒)：枠が 黄色 ⇔ 白 を4回くり返す(0.567秒。曲を決めたときの点滅と同じ速さ)
        //     → 0.65秒まで黄色 → 0.767秒までに消える。板やカードはそのまま。幕は 2.7秒あと
        private const float MarkerBounceSeconds = 0.117f, MarkerBounceDrop = 8.6f;
        private const float DiffBlinkSeconds = 0.567f, DiffBlinkCount = 4f, DiffCursorHoldEnd = 0.65f, DiffCursorGoneAt = 0.767f;
        private float markerBounce = 1f;      // カーソルを動かしてからの秒
        private float diffDecideTime = -1f;   // 難易度を決めてからの秒(-1 なら決めていない)
        private float guideClock;             // 左上の太鼓の案内の時計(この画面に入ってからの秒)

        // ===== 難易度の板の配置(TaikøNauts を測った値) =====
        private static readonly Rectangle DiffPanel = new Rectangle(352, 62, 1210, 746);
        private static readonly Rectangle DiffTitleBox = new Rectangle(388, 97, 1140, 210);
        // 戻る・オプション・カードは、画像の原寸(戻る・オプション 114×112、カード 196×354)。カードは 215px ずつ右へ
        //   (前は少しつぶして描いていたので、★と数字が上にずれていた)
        private static readonly Rectangle DiffBackBox = new Rectangle(383, 406, 114, 112);
        private static readonly Rectangle DiffOptionBox = new Rectangle(514, 406, 114, 112);
        private static readonly float[] DiffCardX = { 647, 862, 1077, 1292 };
        private const float DiffCardY = 406, DiffCardWidth = 196, DiffCardHeight = 354;
        private static readonly Rectangle DiffHintBox = new Rectangle(780, 945, 360, 85);

        private static readonly Color BackBoxColor = new Color(81, 48, 9, 255);    // 「とじる」の縁(OpenTaiko の #513009)
        private static readonly Color BackFillColor = new Color(170, 122, 52, 255);
        private static readonly Color RandomColor = new Color(110, 80, 160, 255);
        private static readonly Color LabelColor = new Color(20, 24, 36, 200);
        private static readonly Color InkColor = BarList.InkColor;
        private static readonly Color CursorColor = new Color(255, 240, 40, 255);  // 難易度のカードの枠(A-Style の Cursor_Effect.png と同じ黄色)
        private static readonly Color ErrorColor = new Color(255, 110, 100, 255);
        private static readonly Color MarkerColor = new Color(230, 60, 50, 255);

        // ===== 入る・出る =====

        private readonly MenuChara chara = new();   // 左下のどんちゃん（A-Style だけ。MenuChara.cs）

        // ===== どんちゃんの声（11/9 の TaikøNauts の録画 2026-11-09 02-23-14.mp4・02-25-14.mp4 などで確かめた） =====
        //   ・画面に入る → 「曲を選ぼう」系の声（SelectStart）を、何か操作するまでくり返す（3.9秒ごと）
        //   ・ジャンルの板にカーソルが乗る → 0.95秒あとに、そのジャンルの声（Genre/板の番号.ogg）。
        //     板の番号は BarType（J-POP=1・アニメ=3・ボーカロイド=4。どうよう・バラエティーは 0 で声なし。本家も鳴らなかった）
        //     「とじる」でジャンルの一覧に戻ったときも鳴る
        //   ・曲を決めて難易度の板が出る → 0.5秒あとに DecideSong
        //   ・難易度を決める → すぐ DecideDifficulty（どんちゃんのジャンプと同時）
        //   ・「とじる」を決める → Cancel.ogg（効果音）
        private const string SelectStartVoice = "Sound/SongSelect/SelectStart.ogg";
        private const string DecideSongVoice = "Sound/SongSelect/DecideSong.ogg";
        private const string DecideDifficultyVoice = "Sound/SongSelect/DecideDifficulty.ogg";
        private const double GenreVoiceDelay = 0.95, DecideSongVoiceDelay = 0.5;
        private bool selectPrompt;   // SelectStart をくり返している途中

        // カーソルが動いた：最初の声のくり返しをやめて、ジャンルの板なら少しあとにジャンルの声
        private void OnCursorMoved()
        {
            if (selectPrompt) { MenuVoice.StopLoop(); selectPrompt = false; }
            ScheduleGenreVoice();
        }

        private void ScheduleGenreVoice()
        {
            if (!inSongs && CurrentItem is { Kind: Kind.Genre } item && BarType(item) is int type and >= 1)
                MenuVoice.PlayAfter($"Sound/SongSelect/Genre/{type}.ogg", GenreVoiceDelay);
            else
                MenuVoice.CancelPending();
        }

        public override void Enter()
        {
            chara.Load();
            chara.SetIdle("songselect_loop", c => c.SongSelectLoopTime);
            if (!returningFromDifficulty)
            {
                MenuVoice.Loop(SelectStartVoice);
                selectPrompt = true;
            }
            guideClock = 0f;
            diffDecideTime = -1f;
            returnTime = -1f;
            returnSwap = null;
            if (!SongList.IsScanned && !SongList.IsScanning) SongList.StartScan();
            // 「はじめの設定」から戻ってきた：途中参加の通信中から続ける
            if (PlayerJoin.Pending is PlayerJoin.ResumeInfo pending) join.Resume(pending);
            wasScanning = SongList.IsScanning;
            searchFocused = false;
            if (!SongList.IsScanning) Rebuild(keepPlace: true);
            // 最初の Update は暗転解除後になる。曲名のフォントは黒幕中の Enter で準備する。
            PreloadListText();
            list.Reset(CurrentPos);
            afterBlink = null;
            MenuSkin.Preload(MenuSkin.SongFolder, MenuSkin.BarImages.Concat(MenuSkin.SongImages).Concat(MenuSkin.TnSongImages).Concat(SongRecordPanel.Images));
            SongLoading.Preload(); // 曲の読み込み画面の画像(決めた瞬間に止まらないように)
            smallControlGuide?.Unload();
            smallControlGuide = null;
            largeControlGuide?.Unload();
            largeControlGuide = null;
            if (SkinManager.CurrentPath is string skin)
            {
                largeControlGuide = Aup2.Load(Path.Combine(skin, "Image", "99.Common", "Control_Guide", "Large", "Anime.aup2"));
                if (largeControlGuide != null)
                {
                    largeControlGuide.Preload();
                    if (!largeControlGuide.HasAllImages)
                    {
                        largeControlGuide.Unload();
                        largeControlGuide = null;
                    }
                }
                smallControlGuide = Aup2.Load(Path.Combine(skin, "Image", "99.Common", "Control_Guide", "Small", "Anime.aup2"));
                if (smallControlGuide != null)
                {
                    smallControlGuide.Preload();
                    if (!smallControlGuide.HasAllImages)
                    {
                        smallControlGuide.Unload();
                        smallControlGuide = null;
                    }
                }
            }
        }

        public override void Exit()
        {
            chara.Dispose();
            MenuVoice.StopLoop();
            MenuVoice.CancelPending();
            preview.Dispose();
            smallControlGuide?.Unload();
            smallControlGuide = null;
            largeControlGuide?.Unload();
            largeControlGuide = null;
            options.CloseNow();
            join.Dispose();
        }

        // 設定パネルで音量が変わったとき：流れているプレビューの曲に、すぐ新しい音量をかける
        public override void ApplyVolume() => preview.ApplyVolume();

        // ===== 一覧を作る =====

        // 曲の場所(Songs から見た .tja の場所) → 曲
        private readonly Dictionary<string, (SongEntry Entry, SongCategory Category)> byKey = new(StringComparer.OrdinalIgnoreCase);

        // 一覧を作り直す。keepPlace なら、開いていたジャンルと選んでいた曲をできるだけそのままにする
        private void Rebuild(bool keepPlace)
        {
            List<SongCategory> categories = SongList.Categories;
            bool changed = !ReferenceEquals(categories, builtFor);
            builtFor = categories;

            byKey.Clear();
            foreach (SongCategory category in categories)
                foreach (SongEntry song in category.Songs)
                    byKey[song.Key] = (song, category);

            BuildGenres();
            if (changed && !keepPlace) { inSongs = false; genrePos = FirstSelectable(genres); }

            // 開いていたジャンルが無くなっていたら、ジャンルの一覧へ
            if (inSongs && (openSpecial == Special.None && (openCat < 0 || openCat >= categories.Count)))
                inSongs = false;
            if (inSongs)
            {
                BuildSongs();
                if (ownCount <= 1) inSongs = false; // 「とじる」しか無い
                else SelectSong(lastSongKey);
            }
            if (genres.Count > 0 && genres[Mod(genrePos, genres.Count)].Kind == Kind.Label) genrePos = FirstSelectable(genres);
            list.Reset(CurrentPos);
            afterBlink = null;
        }

        private void BuildGenres()
        {
            genres.Clear();
            List<SongCategory> categories = SongList.Categories;

            genres.Add(new Item { Kind = Kind.Genre, Special = Special.Recent, Title = "最近遊んだ曲", Color = new Color(70, 110, 170, 255), SongCount = Recent().Count });
            if (DanCats.Count > 0)
                genres.Add(new Item { Kind = Kind.Genre, Special = Special.DanSongs, Title = "段位道場の課題曲", Color = DanBarColor, SongCount = DanSongItems(danCat).Count });
            genres.Add(new Item { Kind = Kind.Genre, Special = Special.Added, Title = "最近追加された曲", Color = new Color(60, 140, 150, 255), SongCount = Added().Count });
            genres.Add(new Item { Kind = Kind.Genre, Special = Special.Favorites, Title = "お気に入り", Color = new Color(200, 110, 150, 255), SongCount = Favorites().Count });
            // 全部の曲からランダム（11/14 たけらい「ジャンルのところにもランダムに選ぶを追加したい」）
            genres.Add(new Item { Kind = Kind.Random, Title = "ランダムにえらぶ", Color = RandomColor });
            genres.Add(new Item { Kind = Kind.Label, Title = "カテゴリをえらぶ", Color = LabelColor });

            for (int i = 0; i < categories.Count; i++)
            {
                genres.Add(new Item { Kind = Kind.Genre, Cat = i, Title = categories[i].Title, Color = categories[i].Color, SongCount = categories[i].Songs.Count, Category = categories[i] });
            }
        }

        // 開いているジャンルの曲を並べる(7曲ごとに「とじる」、最後に「ランダム」。OpenTaiko の tReinsertBackButtons と同じ)
        //   TaikøNauts と同じく、ジャンルの一覧の中の、開いたジャンルの場所に入れる(検索の結果は曲だけ)
        private void BuildSongs()
        {
            List<Item> own = new();
            List<(SongEntry Entry, SongCategory Category)> list = openSpecial switch
            {
                Special.Recent => Recent(),
                Special.Added => Added(),
                Special.Favorites => Favorites(),
                Special.Search => SearchResults(),
                _ => openCat >= 0 && openCat < SongList.Categories.Count
                    ? SongList.Categories[openCat].Songs.Select(s => (s, SongList.Categories[openCat])).ToList()
                    : new(),
            };

            foreach (var (entry, category) in list)
            {
                own.Add(new Item { Kind = Kind.Song, Song = entry, Title = entry.Title, Color = category.Color, CategoryTitle = category.Title, Category = category });
            }
            if (openSpecial == Special.DanSongs) own.AddRange(DanSongItems(danCat));
            int count = own.Count;
            for (int index = 0; index < count / BackInterval + 1; index++)
            {
                own.Insert(Math.Min(index * (BackInterval + 1), own.Count), new Item { Kind = Kind.Back, Title = "とじる", Color = BackFillColor });
            }
            // 「ランダムにえらぶ」は最初の「とじる」の上（11/14 たけらい。前は一覧の最後だった）
            if (count > 0) own.Insert(0, new Item { Kind = Kind.Random, Title = "ランダムにえらぶ", Color = RandomColor });

            // 開いたジャンルの板の代わりに入れる
            songs.Clear();
            int genreIndex = OpenGenreIndex();
            if (genreIndex >= 0) songs.AddRange(genres.Take(genreIndex));
            songStart = songs.Count;
            ownCount = own.Count;
            songs.AddRange(own);
            if (genreIndex >= 0) songs.AddRange(genres.Skip(genreIndex + 1));
        }

        // ===== 段位道場の課題曲（11/14 たけらい。TaikøNauts と同じ） =====
        //   ジャンルの一覧の「段位道場の課題曲」でドン → 吹き出しが「◀ 段位道場の名前 ▶」になって、カッ・←→ でどの段位道場か選ぶ → ドンで開く
        //   曲は段位の高い順（十段 → 九段 …）。同じ曲が2つの段位にあるときは、高い方に1回だけ
        //   曲の板の左に段位の札（DanIndicator/Plate_Bar_Side.png）、選んでいる曲の上に大きい札（Plate_Bar_Top.png）
        //   遊ぶのはふつうの演奏（段位の合格判定はしない）
        private static readonly Color DanBarColor = new Color(60, 100, 182, 255);   // A-Style の Bar/11.png の色
        private const int DanBarType = 11;
        private static int danCat;          // 選んでいる段位道場（DanList.Categories のうち、遊べる段位があるもの）
        private bool danPick;               // 「◀ 段位道場 ▶」を選んでいる途中
        private static List<DanCategory> DanCats => DanList.Categories.Where(c => c.Courses.Any(x => x.Playable)).ToList();

        private static List<Item> DanSongItems(int cat)
        {
            var cats = DanCats;
            List<Item> items = new();
            if (cats.Count == 0) return items;
            DanCategory category = cats[Mod(cat, cats.Count)];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (DanCourse course in category.Courses.Where(c => c.Playable).OrderByDescending(c => c.DanIndex))
                foreach (DanSong ds in course.Songs)
                {
                    if (ds.FullPath.Length == 0 || !seen.Add(ds.FullPath)) continue;
                    SongEntry? entry = null;
                    foreach (SongCategory sc in SongList.Categories)
                    {
                        entry = sc.Songs.FirstOrDefault(s => string.Equals(s.TjaPath, ds.FullPath, StringComparison.OrdinalIgnoreCase));
                        if (entry != null) break;
                    }
                    entry ??= new SongEntry { TjaPath = ds.FullPath, Title = ds.Title, Subtitle = ds.Subtitle, Courses = new() { (ds.Course, ds.Level) } };
                    items.Add(new Item { Kind = Kind.Song, Song = entry, Title = entry.Title, Color = DanBarColor, Dan = course });
                }
            return items;
        }

        private static string DanCatTitle => DanCats is { Count: > 0 } c ? c[Mod(danCat, c.Count)].Title : "";

        // 段位の札の色（DanIndicator の絵の何番目か）：級 0・初〜五段 1・六〜十段 2・玄人 3・名人・超人・達人 4
        private static int DanTier(DanCourse course)
        {
            string t = course.Title;
            if (t.Contains('級')) return 0;
            if (t.Contains("玄人")) return 3;
            if (t.Contains("名人") || t.Contains("超人") || t.Contains("達人")) return 4;
            foreach (string high in new[] { "六", "七", "八", "九", "十" }) if (t.Contains(high)) return 2;
            return 1;
        }
        private static readonly Color[] DanTierColors =
            { new(255, 210, 120, 255), new(75, 170, 190, 255), new(245, 85, 55, 255), new(200, 205, 215, 255), new(230, 190, 60, 255) };

        // 札の中身：段位の名前（白い字に黒い縁）と、難易度のマーク
        private static void DrawDanPlate(DanCourse course, Rectangle cell, string image, int cellWidth, int cellHeight, bool top, float alpha)
        {
            int tier = DanTier(course);
            CourseType courseType = course.Songs.Count > 0 ? course.Songs[0].Course : CourseType.Oni;
            if (!MenuSkin.Part(Tn + "DanIndicator/" + image, new Rectangle(tier * cellWidth, 0, cellWidth, cellHeight), cell, MenuSkin.White(alpha)))
            {
                Rectangle fill = top ? new Rectangle(cell.X + 39, cell.Y + 29, 222, 48) : new Rectangle(cell.X + 19, cell.Y + 19, 72, 72);
                SmoothShapes.Rounded(new Rectangle(fill.X - 5, fill.Y - 5, fill.Width + 10, fill.Height + 10), 0.2f, 8, Fade(Color.Black, alpha));
                SmoothShapes.Rounded(fill, 0.2f, 8, Fade(DanTierColors[tier], alpha));
            }
            int symbol = (int)courseType switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, _ => 4 };
            using (UseOutlineRatio(4f / 30f))
            {
                if (top)
                {
                    Outlined(course.Title, new Rectangle(cell.X + 45, cell.Y + 30, 135, 46), 34, Fade(Color.White, alpha), Fade(Color.Black, alpha));
                    MenuSkin.Part(Tn + "DanIndicator/CourseSymbol_Large.png", new Rectangle(symbol * 80, 0, 80, 56), new Rectangle(cell.X + 186, cell.Y + 24, 80, 56), MenuSkin.White(alpha));
                }
                else
                {
                    Outlined(course.Title, new Rectangle(cell.X + 19, cell.Y + 20, 72, 34), 26, Fade(Color.White, alpha), Fade(Color.Black, alpha));
                    MenuSkin.Part(Tn + "DanIndicator/CourseSymbol.png", new Rectangle(symbol * 60, 0, 60, 40), new Rectangle(cell.X + 25, cell.Y + 52, 60, 40), MenuSkin.White(alpha));
                }
            }
        }

        // 「◀ 段位道場 ▶」を選んでいる間の操作
        private void UpdateDanPick()
        {
            var cats = DanCats;
            bool left = InputManager.IsDrumPressed(DrumPart.LeftKa) || KeyPressed(KeyboardKey.Left) || KeyPressed(KeyboardKey.Up);
            bool right = InputManager.IsDrumPressed(DrumPart.RightKa) || KeyPressed(KeyboardKey.Right) || KeyPressed(KeyboardKey.Down);
            if (cats.Count > 1 && (left || right))
            {
                danCat = Mod(danCat + (right ? 1 : -1), cats.Count);
                if (CurrentItem is { Special: Special.DanSongs } g) g.SongCount = DanSongItems(danCat).Count;
                MenuSounds.Ka();
            }
            if (InputManager.IsDonPressed() || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
            {
                danPick = false;
                if (CurrentItem is Item item) OpenGenreItem(item);
            }
        }

        // 開いているジャンルが、ジャンルの一覧の何番目か(検索の結果のときは -1)
        private int OpenGenreIndex() => genres.FindIndex(g => g.Kind == Kind.Genre && g.Special == openSpecial
            && openSpecial != Special.Search && (openSpecial != Special.None || g.Cat == openCat));

        private List<(SongEntry, SongCategory)> Recent() => Resolve(SongHistory.Recent);
        private List<(SongEntry, SongCategory)> Favorites() => Resolve(SongHistory.Favorites);

        private List<(SongEntry, SongCategory)> Resolve(IEnumerable<string> keys)
        {
            List<(SongEntry, SongCategory)> list = new();
            foreach (string key in keys)
                if (byKey.TryGetValue(key, out var found) && !found.Entry.HasError) list.Add(found);
            return list;
        }

        private List<(SongEntry, SongCategory)> Added()
        {
            return byKey.Values.Where(v => !v.Entry.HasError && v.Entry.WriteTime > 0)
                .OrderByDescending(v => v.Entry.WriteTime).Take(AddedMax)
                .Select(v => (v.Entry, v.Category)).ToList();
        }

        private List<(SongEntry, SongCategory)> SearchResults()
        {
            string query = SongSearch.Normalize(searchText);
            var key = (query, searchCourse, searchStars, searchGenre);
            if (ReferenceEquals(searchCacheSource, SongList.Categories) && searchCacheKey == key) return searchCache;
            searchCacheSource = SongList.Categories; searchCacheKey = key;
            var result = new List<(SongEntry, SongCategory)>();
            foreach (SongCategory category in SongList.Categories)
                foreach (SongEntry song in category.Songs)
                    if (SongSearch.MatchesFilters(song, category, query, searchCourse, searchStars, searchGenre)) result.Add((song, category));
            return searchCache = result;
        }

        // 曲の一覧で、key の曲を選ぶ(無ければ最初の曲)
        private void SelectSong(string key)
        {
            int index = songs.FindIndex(s => s.Kind == Kind.Song && string.Equals(s.Song!.Key, key, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = FirstSongPos;
            songPos = index;
        }

        private static int FirstSelectable(List<Item> items)
        {
            int index = items.FindIndex(i => i.Kind != Kind.Label);
            return Math.Max(0, index);
        }

        private static int Mod(int value, int count) => count == 0 ? 0 : ((value % count) + count) % count;

        private List<Item> CurrentList => inSongs ? songs : genres;
        private int CurrentPos => inSongs ? songPos : genrePos;
        private Item? CurrentItem => CurrentList.Count == 0 ? null : CurrentList[Mod(CurrentPos, CurrentList.Count)];

        // 難易度の板が出きって、操作できるか
        private bool DiffReady => diffOpen && diffTime >= PanelInEnd;

        // ===== 更新 =====

        // 曲を決めてから幕が閉じるまで（SongLoading の Wait）も、どんちゃんのジャンプは動かす
        // 画面の切り替え中(難易度を決めてから幕が閉じるまで)も、どんちゃんとカーソルの点滅は進める
        public override void UpdateBlocked(float dt)
        {
            chara.Update(dt);
            guideClock += dt;
            if (diffDecideTime >= 0f) diffDecideTime += dt;
        }

        public override void Update(float dt)
        {
            chara.Update(dt);
            guideClock += dt;
            markerBounce += dt;
            if (diffDecideTime >= 0f) diffDecideTime += dt;
            caretTimer += dt;
            if (toastTimer > 0f) toastTimer -= dt;
            if (skipLock > 0f) skipLock -= dt;
            if (skipLock > 0f || searchFocused || options.IsOpen || diffOpen || returnTime >= 0f
                || list.Deciding || flipTime >= 0f || SongList.IsScanning) skipGesture.Reset();
            PreloadListText();
            preview.Update(dt);
            options.Update(dt);
            if (returnTime >= 0f)
            {
                returnTime += dt;
                if (returnSwap != null && returnTime >= ReturnOutSeconds)
                {
                    Action swap = returnSwap;
                    returnSwap = null;
                    swap();
                }
                if (returnTime >= ReturnOutSeconds + ReturnInSeconds) returnTime = -1f;
            }

            // 段位の読み込み（モード選択で始めている）が終わったら、ジャンルの一覧に「段位道場の課題曲」を足す
            if (DanList.IsScanning)
            {
                DanList.Update();
                if (DanList.IsScanned && !inSongs && !list.Deciding && flipTime < 0f)
                {
                    Item? keep = CurrentItem;
                    BuildGenres();
                    if (keep != null) genrePos = Math.Max(0, genres.FindIndex(g => g.Kind == keep.Kind && g.Special == keep.Special && g.Cat == keep.Cat));
                    list.Reset(genrePos);
                }
            }

            // 読み込みが終わったら一覧を作り直す
            bool scanning = SongList.IsScanning;
            if (wasScanning && !scanning) Rebuild(keepPlace: false);
            else if (!scanning && !ReferenceEquals(SongList.Categories, builtFor)) Rebuild(keepPlace: false);
            wasScanning = scanning;

            list.Update(dt, CurrentPos);
            UpdateTnBackground(dt);
            uraClock = list.Expand > 0f ? uraClock + dt : 0f;
            if (diffOpen) diffTime += dt;
            UpdateUraTransition(dt);

            // 背景の色を、選んでいるものの色にだんだん近づける
            Color target = diffOpen ? diffColor : CurrentItem?.Color ?? new Color(30, 40, 70, 255);
            if (CurrentItem is { Kind: Kind.Back or Kind.Label }) target = new Color(30, 40, 70, 255);
            float k = Math.Min(1f, dt * 6f);
            backColor = new Color((byte)(backColor.R + (target.R - backColor.R) * k), (byte)(backColor.G + (target.G - backColor.G) * k),
                (byte)(backColor.B + (target.B - backColor.B) * k), (byte)255);

            if (scanning)
            {
                DiscardTypedChars();
                preview.Request(null, false);
                return;
            }

            // 曲と難易度ではプレビュー。難易度の画面では「とじる」にカーソルがあっても止めない
            //   11/14 たけらい「難易度選択画面で閉じるにカーソル合わせると曲が止まる。前はそんなことなかった」
            string? previewTja = diffOpen ? diffSong?.TjaPath
                : inSongs && CurrentItem is { Kind: Kind.Song } now ? now.Song!.TjaPath : null;
            preview.Request(previewTja, diffOpen || list.Expand > 0f);
            if (returnTime >= 0f) { DiscardTypedChars(); return; }

            // 途中参加の板が出ている間は、そちらだけ動かす（名前の入力があるので、打った文字は捨てない）
            if (join.Active)
            {
                if (join.Update(dt) == PlayerJoin.Result.Setup)
                {
                    // 名前を入れた：「はじめの設定」へ。終わったら選曲に戻る（曲の位置は static で覚えているので同じ所）
                    PlayerJoin.ResumeInfo info = join.MakeResume();
                    PlayerJoin.Pending = info;
                    SceneManager.Simple(new SetupScene(info.Slot, () => new SongSelectScene()));
                }
                return;
            }

            // 検索の箱に入力中
            if (searchFocused)
            {
                UpdateSearchInput();
                return;
            }
            if (options.IsOpen)
            {
                // オプションの板が開いている間は、板の方でキーを受け取る(カッ・ドン・↑↓←→・数字)。F2 で閉じる
                if (InputManager.IsSystemPressed(SystemAction.QuickOption) && !options.IsTyping) options.Close();
                return;
            }
            DiscardTypedChars();

            if (danPick) { UpdateDanPick(); return; }
            if (diffOpen)
            {
                if (DiffReady) UpdateDifficulty();
                return;
            }

            // ジャンル・「とじる」を決定したあとの点滅中は、何もできない。点滅が終わったら切り替える
            //   ジャンルは、点滅のあと白い枠で少し待ってから、めくれる動きに入る(FlipStart)
            if (list.Deciding)
            {
                if (list.DecideTime < (flipOpen != null ? FlipStart : BarList.BlinkEnd)) return;
                list.EndDecide();
                if (flipOpen != null)
                {
                    flipTime = 0f;
                    flipOpened = false;
                    return;
                }
                Action? action = afterBlink;
                afterBlink = null;
                action?.Invoke();
                return;
            }

            // めくれている間も、何もできない。0.70秒で曲の一覧に入れ替える
            if (flipTime >= 0f)
            {
                flipTime += dt;
                if (!flipOpened && flipTime >= FlipSwitch)
                {
                    flipOpened = true;
                    flipOpen?.Invoke();
                    list.OpenNowFrameLater(); // 曲の板は開いた形で出て、黄色い枠は 0.33秒あとに出る
                }
                if (flipTime >= FlipEnd)
                {
                    flipTime = -1f;
                    flipOpen = null;
                    flipFromBack = false;
                }
                return;
            }

            // 2人目の途中参加：まだ使っていない方のキーのドン
            if (!training && afterBlink == null && PlayerJoin.NewPlayerPressed)
            {
                join.OpenForNewPlayer();
                return;
            }

            // F3：検索
            if (InputManager.IsSystemPressed(SystemAction.Search))
            {
                searchFocused = true;
                searchBefore = searchText;
                searchInput.Reset(searchText);
                searchScrollX = 0f;
                searchFilterBefore = (searchCourse, searchStars, searchGenre);
                searchGenres = new[] { "すべてのジャンル" }.Concat(SongList.Categories.Select(c => string.IsNullOrWhiteSpace(c.Genre) ? c.Title : c.Genre)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.Ordinal)).ToArray();
                searchLevels = Enumerable.Range(0, 11).Concat(SongList.Categories.SelectMany(c => c.Songs).SelectMany(s => s.Courses)
                    .Where(c => c.Type <= CourseType.Edit).Select(c => c.Level)).Distinct().OrderBy(n => n).ToArray();
                searchLevelLabels = new[] { "すべて" }.Concat(searchLevels.Select(n => $"★{n}")).ToArray();
                Gui.ClosePopup();
                searchSubmitQueued = false;
                caretTimer = 0f;
                return;
            }

            // F4：お気に入り
            if (inSongs && InputManager.IsSystemPressed(SystemAction.Favorite) && CurrentItem is { Kind: Kind.Song } fav)
                ToggleFavorite(fav.Song!);

            // 最初の側を0.25秒保持。逆側は無視し、同じ側の2打目だけでスキップする。
            bool freshUp = InputManager.IsDrumPressed(DrumPart.LeftKa) || Raylib.IsKeyPressed(KeyboardKey.Up);
            bool freshDown = InputManager.IsDrumPressed(DrumPart.RightKa) || Raylib.IsKeyPressed(KeyboardKey.Down);
            bool up = freshUp || KeyPressed(KeyboardKey.Up);
            bool down = freshDown || KeyPressed(KeyboardKey.Down);
            int move = (down ? 1 : 0) - (up ? 1 : 0);
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel > 0) move--;
            else if (wheel < 0) move++;
            bool breakGesture = wheel != 0 || (up && down) || (!freshUp && !freshDown && (up || down));
            if (breakGesture) skipGesture.Reset();
            // ロック中の入力は捨てる。ドン・Enterによる決定は下で受け付ける。
            if (move != 0 && skipLock <= 0f)
            {
                if (!breakGesture && (freshUp != freshDown))
                {
                    var action = skipGesture.Register(Math.Sign(move), Raylib.GetTime());
                    if (action.Direction != 0)
                    {
                        if (!action.Skip)
                        {
                            // 1打目で通常移動する前の板を覚える。
                            skipStartPosition = CurrentPos;
                            skipStartedOnBack = inSongs && CurrentItem is { Kind: Kind.Back };
                            Move(action.Direction);
                        }
                        else if (skipStartedOnBack) SkipToBack(action.Direction, skipStartPosition);
                        else Move(action.Direction, SkipCount, skipStartPosition);   // 1打目で1つ動いているので、動く前の場所から数える
                    }
                    if (action.Skip) skipLock = SkipInterval;
                }
                else Move(move);
            }

            if (InputManager.IsDonPressed() || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
                Decide();
        }

        // スキップで進む数。「とじる」と見出しは数えない。
        private const int SkipCount = 7;
        // スキップ後0.5秒は一覧の移動を止める。ドン・Enterの決定は使える。
        private const float SkipInterval = 0.5f;
        private float skipLock;
        private readonly SongSelectSkipGesture skipGesture = new();
        private int skipStartPosition;
        private bool skipStartedOnBack;

        // 「とじる」から始めた2打は、同じ方向の次の「とじる」で止める。
        private void SkipToBack(int direction, int start)
        {
            List<Item> items = CurrentList;
            int pos = start;
            for (int guard = 0; guard < items.Count; guard++)
            {
                pos += Math.Sign(direction);
                if (items[Mod(pos, items.Count)].Kind != Kind.Back) continue;
                SetPos(pos);
                MenuSounds.Skip();
                return;
            }
        }

        // 通常移動では「とじる」も選べる。スキップでは「とじる」と見出しを数えない。
        //   from：数え始める場所（スキップは、1打目で動く前の場所から 7つ。11/14 たけらい「いっこあとになる」）
        private void Move(int direction, int steps = 1, int? from = null)
        {
            List<Item> list = CurrentList;
            if (list.Count == 0) return;
            int pos = from ?? CurrentPos;
            for (int step = 0; step < steps; step++)
            {
                bool found = false;
                for (int guard = 0; guard < list.Count; guard++)
                {
                    pos += Math.Sign(direction);
                    Kind kind = list[Mod(pos, list.Count)].Kind;
                    if (kind == Kind.Label || (steps > 1 && kind == Kind.Back)) continue;
                    found = true;
                    break;
                }
                if (!found) return; // 数えられる項目がない一覧では動かさない。
            }
            SetPos(pos);
            if (steps > 1) MenuSounds.Skip();
            else MenuSounds.Ka();
        }

        // カーソルを pos へ(板はすぐ閉じる)
        private void SetPos(int pos)
        {
            if (inSongs) songPos = pos; else genrePos = pos;
            List<Item> list = CurrentList;
            if (inSongs && list.Count > 0 && list[Mod(pos, list.Count)] is { Kind: Kind.Song } s) lastSongKey = s.Song!.Key;
            this.list.Collapse();
            OnCursorMoved();
        }

        // 決定
        // ジャンルを開く（点滅 → 白 → ポンッ → めくれる → 曲の一覧。Update の flipTime で進める）
        private void OpenGenreItem(Item item)
        {
            MenuSounds.Don();
            if (selectPrompt) { MenuVoice.StopLoop(); selectPrompt = false; }
            MenuVoice.CancelPending();
            list.StartDecide();
            flipFromBack = false;
            flipOpen = () =>
            {
                // 曲をえらんでいるときに、ほかのジャンルを決定したら、そのジャンルに開き直す
                if (inSongs) genrePos = Math.Max(0, genres.IndexOf(item));
                OpenGenre(item.Special, item.Cat);
            };
        }

        private void Decide()
        {
            if (CurrentItem is not Item item) return;
            switch (item.Kind)
            {
                case Kind.Genre:
                    if (item.SongCount == 0)
                    {
                        MenuSounds.Ka();
                        ShowToast(item.Special == Special.Favorites ? "お気に入りはまだありません（曲をえらんで F4 で入れられます）" : "曲がありません");
                        return;
                    }
                    if (item.Special == Special.DanSongs && !danPick)
                    {
                        // まず「◀ 段位道場 ▶」を選ぶ
                        MenuSounds.Don();
                        danPick = true;
                        return;
                    }
                    OpenGenreItem(item);
                    break;

                case Kind.Back:
                    // 「とじる」も、ジャンルを開くときと同じ「めくれる」動きでジャンルの一覧に戻る
                    //   11/8 の TaikøNauts の録画（2026-11-08 03-14-18.mp4 の 148.55秒〜）を1/30秒ずつ見た：
                    //   点滅 → 白 → ポンッ → まわりの板が上下へ逃げる → 「とじる」の板が横につぶれて細い線 → 空のジャンルの色の板が横に広がる
                    //   → J-POP の板（開いた形）に入れ替わって中身とまわりが出る → 0.3秒あとに黄色い枠。時間もジャンルを開くときとほぼ同じ（ずれは 0.05秒以内）
                    MenuSounds.Don();
                    MenuSounds.Cancel();
                    list.StartDecide();
                    flipOpen = CloseGenreNow;
                    flipFromBack = true;
                    break;

                case Kind.Random:
                    if (!inSongs) { DecideRandomAll(); break; }
                    List<Item> candidates = songs.Where(s => s.Kind == Kind.Song && !s.Song!.HasError && s.Song.Courses.Count > 0).ToList();
                    if (candidates.Count == 0) return;
                    Item pick = candidates[random.Next(candidates.Count)];
                    songPos = songPos - Mod(songPos, songs.Count) + songs.IndexOf(pick);
                    lastSongKey = pick.Song!.Key;
                    list.Reset(songPos);
                    list.OpenNow(); // 選んだ曲の板を開いた状態で点滅させる
                    MenuSounds.Don();
                    OpenDifficulty(pick);
                    break;

                case Kind.Song:
                    if (item.Song!.HasError || item.Song.Courses.Count == 0)
                    {
                        MenuSounds.Ka();
                        ShowToast(item.Song.HasError ? item.Song.Error : "譜面がありません");
                        return;
                    }
                    MenuSounds.Don();
                    OpenDifficulty(item);
                    break;
            }
        }

        // 開いたジャンルの最初の曲の場所（ランダム → とじる → 曲… の並び。曲が無いときは「とじる」）
        private int FirstSongPos => songStart + (ownCount > 1 ? 2 : 0);

        // ジャンルの一覧の「ランダムにえらぶ」：全部のカテゴリの曲から1曲えらんで、そのジャンルを開いて難易度へ
        private void DecideRandomAll()
        {
            var candidates = new List<(int Cat, SongEntry Song)>();
            for (int c = 0; c < SongList.Categories.Count; c++)
                foreach (SongEntry s in SongList.Categories[c].Songs)
                    if (!s.HasError && s.Courses.Count > 0) candidates.Add((c, s));
            if (candidates.Count == 0) { MenuSounds.Ka(); ShowToast("曲がありません"); return; }
            var (cat, song) = candidates[random.Next(candidates.Count)];
            int gi = genres.FindIndex(g => g.Kind == Kind.Genre && g.Special == Special.None && g.Cat == cat);
            if (gi >= 0) genrePos = gi;
            OpenGenre(Special.None, cat);
            SelectSong(song.Key);
            lastSongKey = song.Key;
            list.Reset(songPos);
            list.OpenNow();
            MenuSounds.Don();
            if (songs[Mod(songPos, songs.Count)] is { Kind: Kind.Song } pick) OpenDifficulty(pick);
        }

        private void OpenGenre(Special special, int cat)
        {
            openSpecial = special;
            openCat = cat;
            inSongs = true;
            BuildSongs();
            songPos = FirstSongPos; // 最初の「とじる」の次(最初の曲)
            if (songs[Mod(songPos, songs.Count)] is { Kind: Kind.Song } first) lastSongKey = first.Song!.Key;
            list.Reset(songPos);
        }

        // Esc で曲の一覧からジャンルの一覧へ：「とじる」を決めたときと同じ「点滅 → めくれる」動きで戻る
        //   11/11 たけらい「戻るアニメーションを見て直して」。前は 0.18秒ずつのフェード(測る前の仮の動き)だった。
        //   録画(2026-11-11 23-46-22.mp4 の 112.5秒〜)の「とじる」は、ジャンルを開くときと同じめくれ方(FlipStart からの時間)。
        //   Esc を押したときの本家の動きは録画に無いので、「とじる」と同じにしている
        private void CloseGenre()
        {
            if (list.Deciding || flipTime >= 0f) return;
            list.StartDecide();
            flipOpen = CloseGenreNow;
            flipFromBack = true;
        }

        private void CloseGenreNow()
        {
            inSongs = false;
            preview.Request(null, false);
            if (openSpecial == Special.Search)
            {
                searchText = "";
                openSpecial = Special.None;
            }
            BuildGenres(); // 最近遊んだ曲・お気に入りの数が変わっているかもしれない
            list.Reset(genrePos);
            ScheduleGenreVoice();   // 戻った先のジャンルの声（本家：「とじる」のあと J-POP の声）
        }

        // Esc：オプション → 難易度 → 曲の一覧 → ジャンルの一覧 → モード選択 の順に1つ戻る
        public override void OnBack()
        {
            if (join.Active) { join.Back(); return; }
            if (danPick) { danPick = false; MenuSounds.Cancel(); return; }
            if (returnTime >= 0f) return;
            if (flipTime >= 0f) return; // ジャンルがめくれている途中は戻れない(途中で止めると一覧がおかしくなる)
            if (searchFocused)
            {
                if (Gui.IsPopupOpen) { Gui.ClosePopup(); return; }
                searchSubmitQueued = false;
                searchFocused = false;
                (searchCourse, searchStars, searchGenre) = searchFilterBefore;
                searchText = searchBefore; // 前の検索の結果を見ていたときは、その文字に戻す
                return;
            }
            if (options.IsOpen) { options.Close(); return; }
            if (diffOpen) { CloseDifficulty(); return; }
            if (afterBlink != null)
            {
                // 点滅中なら、決定をやめる
                afterBlink = null;
                list.EndDecide();
                return;
            }
            if (inSongs) { if (!list.Deciding) { MenuSounds.Cancel(); CloseGenre(); } return; }
            MenuSounds.Cancel();
            SceneManager.ToModeFromSongSelect(new ModeSelectScene());
        }

        // .tja がドロップされたら、テスト用に譜面を流す
        public override void OnFilesDropped(string[] paths)
        {
            string? tja = paths.FirstOrDefault(PlayScene.IsTja);
            if (tja != null) SceneManager.ChangeScene(training ? new TrainingScene(tja) : new PlayScene(tja));
        }

        private void ToggleFavorite(SongEntry song)
        {
            bool added = SongHistory.ToggleFavorite(song.Key);
            ShowToast(added ? $"お気に入りに入れました：{song.Title}" : $"お気に入りから外しました：{song.Title}");
            MenuSounds.Don();
            if (openSpecial == Special.Favorites && !added)
            {
                // お気に入りの一覧から消したときは、並びを作り直す
                int index = Mod(songPos, songs.Count);
                BuildSongs();
                if (ownCount <= 1) { CloseGenreNow(); return; }   // 中身が空になったので、すぐジャンルの一覧へ
                songPos = Math.Min(index, songs.Count - 1);
                list.Reset(songPos);
            }
        }

        private void ShowToast(string text)
        {
            toast = text;
            toastTimer = 2.5f;
        }

        private static bool KeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

        // ===== 難易度をえらぶ =====

        private bool HasCourse(CourseType type) => diffSong != null && diffSong.Courses.Any(c => c.Type == type);
        private int LevelOf(CourseType type) => diffSong?.Courses.FirstOrDefault(c => c.Type == type).Level ?? 0;

        // 2〜5 番目の箱の難易度(おにの箱は、裏のときは裏)
        private CourseType CourseAt(int index) => index == 5 && ura ? CourseType.Edit : PanelCourses[index - 2];

        private void OpenDifficulty(Item item)
        {
            SongEntry song = item.Song!;
            if (!training && LocalSession.Count == 2)
            {
                leavingForDifficulty = true;
                preview.Dispose();
                SceneManager.Simple(new LocalDifficultyScene(song));
                return;
            }
            diffSong = song;
            diffColor = item.Color;
            diffType = BarType(item);
            diffItem = item;
            diffOpen = true;
            diffTime = 0f;
            diffDecideTime = -1f;
            markerBounce = 1f;
            uraCount = 0;
            if (selectPrompt) { MenuVoice.StopLoop(); selectPrompt = false; }
            MenuVoice.PlayAfter(DecideSongVoice, DecideSongVoiceDelay);
            list.StartDecide(); // 枠の点滅(まだ開いていなかったら、すぐ開く)
            bool hasOni = HasCourse(CourseType.Oni), hasUra = HasCourse(CourseType.Edit);
            // おにが無くて裏だけある曲は、最初から裏(OpenTaiko と同じ)
            ura = hasUra && (!hasOni || lastCourse == CourseType.Edit);

            // 前に遊んだ難易度があればそこ、無ければ一番むずかしい難易度
            CourseType start = lastCourse == CourseType.Edit ? CourseType.Oni : lastCourse;
            if (!HasCourse(start) && !(start == CourseType.Oni && hasUra))
            {
                start = PanelCourses.LastOrDefault(c => HasCourse(c) || (c == CourseType.Oni && hasUra), CourseType.Easy);
            }
            diffCursor = 2 + Array.IndexOf(PanelCourses, start);
            // 11/22 TaikøNauts の録画：起動してからまだ遊んでいないと、カーソルは「戻る」から始まる(2曲続けて開いても戻る)
            if (!courseChosenThisRun) diffCursor = 0;
        }

        // 難易度の板を閉じて、曲の一覧へ(曲は流したまま)
        private void CloseDifficulty()
        {
            // 11/8 の TaikøNauts の録画：難易度の板は一瞬で消えて、選んでいた曲の板は閉じた形にもどる。
            //   そこから 0.43秒で黄色い枠、0.45秒で開く（BarList.CollapseAfterDifficulty）。フェードはしない
            MenuSounds.Cancel();
            MenuVoice.Stop();
            options.CloseNow();
            diffOpen = false;
            diffSong = null;
            list.EndDecide();
            list.CollapseAfterDifficulty();
        }

        private void UpdateDifficulty()
        {
            // 裏おに⇔おにのアニメ中は、終わるまで何も受け付けない(TaikøNauts と同じ)
            if (uraFlipTime >= 0f) return;

            // F2：オプション
            if (InputManager.IsSystemPressed(SystemAction.QuickOption))
            {
                options.Open(); // 開く音は板の方で鳴らす(スキンの Option.ogg。無ければドン)
                return;
            }

            // おにの上で右を数えるのは、押し直したときだけ(→キーの押しっぱなしのくり返しは数えない)
            bool freshRight = InputManager.IsDrumPressed(DrumPart.RightKa) || Raylib.IsKeyPressed(KeyboardKey.Right);
            bool right = freshRight || KeyPressed(KeyboardKey.Right);
            bool left = InputManager.IsDrumPressed(DrumPart.LeftKa) || KeyPressed(KeyboardKey.Left);
            if (right && (diffCursor < 5 || freshRight)) DiffRight();
            else if (left && diffCursor > 0)
            {
                diffCursor--;
                uraCount = 0;
                markerBounce = 0f;
                MenuSounds.Ka();
            }

            // Tab：おにを選んでいるときに裏おにへ(TaikøNauts と同じ)
            if (Raylib.IsKeyPressed(KeyboardKey.Tab) && diffCursor == 5) ToggleUra();

            if (InputManager.IsDonPressed() || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
                DecideDifficulty();
        }

        // おにの上で右を何回たたくと、裏おにに切り替わるか(本物の太鼓の達人の新筐体と同じ：右フチ10回)
        //   OpenTaiko は1回で切り替わる(nスイッチカウント が 0 のままで、数える仕組みが使われていない)。たけらいの希望で本物に合わせた
        //   裏からおにに戻るのも10回。左へ動いたら数え直し
        private const int UraSwitchCount = 10;
        private int uraCount;

        // 右へ。おにの上でさらに右を10回たたくと、裏に切り替え
        private void DiffRight()
        {
            if (diffCursor < 5)
            {
                diffCursor++;
                uraCount = 0;
                markerBounce = 0f;
                MenuSounds.Ka();
                return;
            }
            if (!HasCourse(CourseType.Oni) || !HasCourse(CourseType.Edit)) return; // 両方あるときだけ数える
            uraCount++;
            if (uraCount >= UraSwitchCount) ToggleUra();
            else MenuSounds.Ka();
        }

        // おに ⇔ 裏おに(Tab はすぐ切り替え。TaikøNauts と同じ)
        private void ToggleUra()
        {
            // おにと裏の両方があるときだけ切り替えられる
            if (!HasCourse(CourseType.Oni) || !HasCourse(CourseType.Edit)) return;
            uraCount = 0;
            MenuSounds.UraFlip();
            // スキンに Oni_Transition.png があればアニメ(カードの中身は、くるっと回るときに切り替える)。無ければすぐ切り替え
            if (SkinManager.GetTexture(UraTransitionPath) != null) { uraFlipTime = 0f; uraSwapped = false; }
            else ura = !ura;
        }

        // ===== 裏おに⇔おにのアニメ =====
        //   11/19 TaikøNauts の録画(2026-11-19_01-02-29.mp4 38.35秒〜)を 60コマ/秒で測った。t = 10回目を押した瞬間
        //   ・すぐ：黄色いカーソルと 1P の印が消える
        //   ・0.067〜0.15秒：カードの鬼のマーク(直径 約135px)の場所から、大きなマーク(300px)にふくらむ
        //   ・0.567秒を真ん中に ±0.075秒：鬼の顔だけ横に縮んで(コインを回すように)、裏の色の顔になる。ここでカードも裏に変わる
        //   ・0.917〜0.983秒：カードのマークの大きさまで縮んで、1.0秒で消える
        //   ・1.0〜1.067秒：カードのマークの上に白い形(Oni_Transition.png の右下)がパッと出て、1.4秒までに消える
        //   ・1.467秒：カーソルと 1P の印がもどって、また押せるようになる
        //   Oni_Transition.png(600×1200)：300×300 が 2列4段。左＝おに(ピンク)・右＝裏(紫)
        //     1段目 全部入り / 2段目 顔だけ / 3段目 うずまきの円 / 4段目 左＝ふち(まん中が黒)・右＝白い形
        private const string UraTransitionPath = Tn + "Difficulty_Select/Oni_Transition.png";
        private const float UraGrowStart = 0.067f, UraGrowEnd = 0.15f, UraFlipCenter = 0.567f, UraFlipHalf = 0.075f,
            UraShrinkStart = 0.917f, UraShrinkEnd = 0.983f, UraHideAt = 1.0f, UraFlashPeak = 1.067f, UraFlashEnd = 1.4f, UraLockEnd = 1.467f;
        private const float UraSmallScale = 135f / 284f;            // カードのマークの大きさ(ふちの直径 284px → 約135px)
        private static readonly Vector2 UraMarkCenter = new(98f, 144f); // カードの左上からマークの中心まで(カードは 196×354)
        private float uraFlipTime = -1f;
        private bool uraSwapped;

        private void UpdateUraTransition(float dt)
        {
            if (uraFlipTime < 0f) return;
            if (!diffOpen) { uraFlipTime = -1f; if (!uraSwapped) ura = !ura; return; }
            uraFlipTime += dt;
            if (!uraSwapped && uraFlipTime >= UraFlipCenter) { ura = !ura; uraSwapped = true; }
            if (uraFlipTime >= UraLockEnd) uraFlipTime = -1f;
        }

        private void DrawUraTransition(Rectangle card, float scale, float alpha)
        {
            float t = uraFlipTime;
            if (t < UraGrowStart) return;
            Vector2 c = new(card.X + UraMarkCenter.X * scale, card.Y + UraMarkCenter.Y * scale);

            if (t < UraHideAt)
            {
                float s;
                if (t < UraGrowEnd) s = UraSmallScale + (1f - UraSmallScale) * EaseOut((t - UraGrowStart) / (UraGrowEnd - UraGrowStart));
                else if (t < UraShrinkStart) s = 1f;
                else s = 1f - (1f - UraSmallScale) * Math.Clamp((t - UraShrinkStart) / (UraShrinkEnd - UraShrinkStart), 0f, 1f);
                float size = 300f * s * scale;
                Rectangle dest = new(c.X - size / 2f, c.Y - size / 2f, size, size);
                int col = ura ? 1 : 0; // 回る前は元の色、回った後は新しい色(ura はちょうど真ん中で切り替わる)
                float face = Math.Abs(t - UraFlipCenter) < UraFlipHalf ? Math.Abs(t - UraFlipCenter) / UraFlipHalf : 1f;
                Color tint = MenuSkin.White(alpha);
                MenuSkin.Part(UraTransitionPath, new Rectangle(0, 900, 300, 300), dest, tint);        // ふち
                MenuSkin.Part(UraTransitionPath, new Rectangle(col * 300, 600, 300, 300), dest, tint); // うずまき
                Rectangle faceDest = new(c.X - size * face / 2f, dest.Y, size * face, size);
                if (face > 0.01f) MenuSkin.Part(UraTransitionPath, new Rectangle(col * 300, 300, 300, 300), faceDest, tint); // 顔
                return;
            }

            // 白い形：パッと出て消える
            float white = t < UraFlashPeak ? (t - UraHideAt) / (UraFlashPeak - UraHideAt)
                        : 1f - Math.Clamp((t - UraFlashPeak) / (UraFlashEnd - UraFlashPeak), 0f, 1f);
            if (white <= 0f) return;
            float small = 300f * UraSmallScale * scale;
            MenuSkin.Part(UraTransitionPath, new Rectangle(300, 900, 300, 300),
                new Rectangle(c.X - small / 2f, c.Y - small / 2f, small, small), MenuSkin.White(alpha * white));
        }

        private static float EaseOut(float x) { x = Math.Clamp(x, 0f, 1f); return 1f - (1f - x) * (1f - x); }

        private void DecideDifficulty()
        {
            if (diffCursor == 0) { CloseDifficulty(); return; }
            if (diffCursor == 1)
            {
                options.Open(); // 開く音は板の方で鳴らす(スキンの Option.ogg。無ければドン)
                return;
            }

            CourseType type = CourseAt(diffCursor);
            if (!HasCourse(type) || diffSong == null)
            {
                MenuSounds.Ka();
                return;
            }

            MenuSounds.Don();
            MenuVoice.Play(DecideDifficultyVoice);
            diffDecideTime = 0f;
            lastCourse = type;
            courseChosenThisRun = true;
            lastSongKey = diffSong.Key;
            SongHistory.AddPlayed(diffSong.Key);
            preview.Dispose();
            // どんちゃんがジャンプして止まる → 2.53秒後に幕（11/8 たけらい「選択したら動いて、そのあとに移行アニメーション」。TaikøNauts の録画どおり）
            //   どんちゃんがいないスキン（シンプルなど）は、今までどおり 0.4秒で幕
            float? wait = null;
            if (chara.Config is CharaConfig cc && chara.HasAnim(CharaBaker.SongStartAnim))
            {
                chara.PlayHold(CharaBaker.SongStartAnim, cc.SongSelectSongStartTime, cc.SongSelectSongStartJumpIndex);
                wait = SongLoading.WaitWithCharaSeconds;
            }
            // 曲の読み込み画面(虹色の幕)をはさんで演奏へ。読み込みは幕が閉じてから
            Scene target = training ? new TrainingScene(diffSong.TjaPath, type)
                : new PlayScene(diffSong.TjaPath, type, returnToSelect: true);
            SceneManager.ToPlayWithLoading(target, diffSong.Title, diffSong.Subtitle, wait);
        }

        // ===== 検索 =====

        private void UpdateSearchInput()
        {
            // プルダウンを操作中の矢印や文字は、入力欄には渡さない。
            if (Gui.IsPopupOpen) { DiscardTypedChars(); return; }

            bool composing = WindowsIme.IsComposing;
            bool changed = false;
            int c = Raylib.GetCharPressed();
            while (c > 0)
            {
                if (!composing && Rune.IsValid(c) && !Rune.IsControl(new Rune(c)))
                    changed |= searchInput.Insert(char.ConvertFromUtf32(c), SearchMaxLength);
                c = Raylib.GetCharPressed();
            }
            string imeResult = WindowsIme.TakeResultText();
            changed |= searchInput.Insert(imeResult, SearchMaxLength);

            // 変換中・変換を確定したフレームは、IMEのカーソルだけを動かす。
            if (!composing && imeResult.Length == 0)
            {
                if (KeyPressed(KeyboardKey.Left)) changed |= searchInput.MoveLeft();
                if (KeyPressed(KeyboardKey.Right)) changed |= searchInput.MoveRight();
                if (KeyPressed(KeyboardKey.Home)) changed |= searchInput.MoveHome();
                if (KeyPressed(KeyboardKey.End)) changed |= searchInput.MoveEnd();
                if (KeyPressed(KeyboardKey.Backspace)) changed |= searchInput.Backspace();
                if (KeyPressed(KeyboardKey.Delete)) changed |= searchInput.Delete();
            }
            searchText = searchInput.Text;
            if (changed) caretTimer = 0f;
            UpdateSearchScroll(WindowsIme.CompositionText);
            WindowsIme.RequestTextInput(SearchBox,
                new Vector2(SearchTextX - searchScrollX + SearchCaretWidth(WindowsIme.CompositionText), SearchTextY));

            // Enter：検索の結果を開く
            if (imeResult.Length == 0 && !composing && !WindowsIme.IsComposing && (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)))
                searchSubmitQueued = true;
        }

        private void SubmitSearch()
        {
            int count = SearchResults().Count;
            if (count == 0)
            {
                ShowToast("指定した条件に当てはまる曲はありません");
                return;
            }
            Gui.ClosePopup();
            searchFocused = false;
            options.CloseNow();
            diffOpen = false;
            MenuSounds.Don();
            OpenGenre(Special.Search, -1);
        }

        private float SearchCaretWidth(string composition)
        {
            int cursor = Math.Clamp(WindowsIme.CompositionCursor, 0, composition.Length);
            if (cursor > 0 && cursor < composition.Length && char.IsLowSurrogate(composition[cursor])) cursor--;
            return Measure(searchText[..searchInput.Caret] + composition[..cursor], SearchFontSize).X;
        }

        private void UpdateSearchScroll(string composition)
        {
            float visible = SearchBox.Width - (SearchTextX - SearchBox.X) * 2f - 4f;
            float caret = SearchCaretWidth(composition);
            float width = Measure(searchText.Insert(searchInput.Caret, composition), SearchFontSize).X;
            if (caret < searchScrollX + 8f) searchScrollX = Math.Max(0f, caret - 8f);
            if (caret > searchScrollX + visible - 8f) searchScrollX = caret - visible + 8f;
            searchScrollX = Math.Clamp(searchScrollX, 0f, Math.Max(0f, width - visible + 8f));
        }

        private static void DiscardTypedChars()
        {
            while (Raylib.GetCharPressed() > 0) { }
            WindowsIme.TakeResultText();
        }

        // 曲名などの文字をまとめて読み込んだ一覧とフォント(一覧が新しくなったり、スキンを変えたりしたら、もう一度読む)
        private static List<SongCategory>? preloadedCategories;
        private static int preloadedFont = -1;

        // 一覧に出る文字(曲名・サブタイトル・エラー・カテゴリ名)を、まとめて先に読み込む
        //   スクロールで新しい曲名が一度にたくさん出ても、その場で文字を読み込まなくていいように
        private static void PreloadListText()
        {
            List<SongCategory> categories = SongList.Categories;
            if (SongList.IsScanning) return;
            if (ReferenceEquals(categories, preloadedCategories) && preloadedFont == FontManager.SkinMainGeneration) return;

            IEnumerable<string> Texts()
            {
                yield return "演奏ゲーム難易度ジャンルを最近遊んだ曲最近追加された曲お気に入りカテゴリをえらぶとじるランダムにえらぶ全曲もどるオプション易普難鬼裏表なし★♥◀0123456789えらぶ決定1P検索読み込み中件一覧更新設定開閉入外当自己ベストEDCBASかんたんふつうむずかしいおに";
                foreach (SongCategory category in categories)
                {
                    yield return category.Title;
                    foreach (SongEntry song in category.Songs)
                    {
                        yield return song.Title;
                        yield return song.Subtitle;
                        yield return song.Error;
                    }
                }
            }
            FontManager.PreloadSkinMain(Texts()); // 文字はスキンの太字フォントで描く
            preloadedCategories = categories;
            preloadedFont = FontManager.SkinMainGeneration;
        }

        // ===== TaikøNauts のスキン(02.SongSelect の Bar/番号.png など) =====
        //   板・背景・難易度の板は、box.def の #BOXTYPE(板)・#BGTYPE(背景)の番号の画像を使う
        //   番号が書いてないときは、ジャンルの名前から決める(TaikøNauts の動画の色と合わせた)
        //   画像が無いスキンや、番号が決まらないものは、今までどおり(Simple の画像かプログラム)で描く

        private const string Tn = MenuSkin.SongFolder;

        private static readonly Dictionary<string, int> GenreBarTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["J-POP"] = 1, ["ポップス"] = 1, ["POPS"] = 1, ["JPOP"] = 1,
            ["アニメ"] = 3,
            ["ボーカロイド"] = 4, ["VOCALOID"] = 4, ["ボーカロイド曲"] = 4,
            ["どうよう"] = 0, ["キッズ"] = 0, ["バラエティ"] = 0, ["バラエティー"] = 0,
            ["クラシック"] = 7,
            ["ゲームミュージック"] = 5, ["ゲームバラエティ"] = 5,
            ["ナムコオリジナル"] = 6,
        };

        private int StarsOf(CourseType type) => Math.Clamp(LevelOf(type), 0, 10);

        // 板の番号(-1：決まらない)
        // 開いているジャンルの板（ジャンルの一覧の genrePos の板）
        private Item? GenreItemOpen => genres.Count > 0 ? genres[Mod(genrePos, genres.Count)] : null;

        // カテゴリの板の番号(#BOXTYPE → ジャンル名 → -1)。演奏画面のジャンルの札の色にも使う(UI/SongCounter.cs)
        internal static int CategoryBarType(SongCategory category)
        {
            if (category.BoxType is int box) return box;
            if (GenreBarTypes.TryGetValue(category.Genre, out int byGenre)) return byGenre;
            if (GenreBarTypes.TryGetValue(category.Title, out int byTitle)) return byTitle;
            return -1;
        }

        private static int BarType(Item item)
        {
            if (item.Dan != null || item.Kind == Kind.Genre && item.Special == Special.DanSongs) return DanBarType;
            if (item.Kind == Kind.Genre && item.Category == null)
                return item.Special switch { Special.Recent => 9, Special.Favorites => 10, _ => -1 };
            if (item.Kind is Kind.Random) return 0;
            SongCategory? category = item.Category;
            if (category == null) return -1;
            if (category.BoxType is int box) return box;
            if (GenreBarTypes.TryGetValue(category.Genre, out int byGenre)) return byGenre;
            if (GenreBarTypes.TryGetValue(category.Title, out int byTitle)) return byTitle;
            return -1;
        }

        // 背景の番号(#BGTYPE → 板の番号 → 0)
        private static int BackgroundType(Item? item)
        {
            if (item?.Category?.BgType is int bg) return bg;
            int bar = item == null ? -1 : BarType(item);
            return bar >= 0 ? bar : 0;
        }

        private static string TnPanelPath(int type) => Tn + $"Difficulty_Select/Background/{Math.Max(0, type)}.png";

        // 板の土台を描いて、中の明るい板(文字を書く場所)を返す
        //   TaikøNauts：Bar/番号.png(938×133。とじるは Bar/Back.png)＋ Bar/Overlay.png(中の板)＋ 番号0は Bar/Decoration.png(花の飾り)
        //   開いた板：土台と中の板は上下に伸ばす。飾りは伸ばさずに、閉じた板と同じ大きさで上にくっつける(TaikøNauts の動画と同じ)
        //   plate を渡すと、中の板(Bar/Overlay.png の 70,23 から 794×84)をその場所に描く(開いたジャンルの板)
        private const string BarTemplate = Tn + "Bar/1.png";   // ぬりかえの元にする板（飾りの無い板）
        private Rectangle DrawBarBody(Item item, Rectangle area, float alpha, bool plain, Rectangle? plate = null)
        {
            int type = BarType(item);
            string? path = item.Kind == Kind.Back ? Tn + "Bar/Back.png" : type >= 0 ? Tn + $"Bar/{type}.png" : null;
            Texture2D? body = path != null ? SkinManager.GetTexture(path) : null;
            // 絵が無いジャンル（番号が決まらない・その番号の絵が無い）：スキンの Bar/1.png をジャンルの色にぬりかえて使う（Graphics/RecoloredBars.cs）
            if (body == null && item.Kind != Kind.Back && SkinManager.GetTexture(BarTemplate) != null)
                body = RecoloredBars.Get(BarTemplate, item.Color);
            if (body is Texture2D bodyTexture)
            {
                Rectangle dest = new Rectangle(area.X, area.Y - 1, area.Width + 6, area.Height + 9);
                NPatchInfo info = new NPatchInfo
                {
                    Source = new Rectangle(0, 0, bodyTexture.Width, bodyTexture.Height),
                    Left = 40, Top = 40, Right = 40, Bottom = 40, Layout = NPatchLayout.NinePatch,
                };
                Raylib.DrawTextureNPatch(bodyTexture, info, dest, System.Numerics.Vector2.Zero, 0f, MenuSkin.White(alpha));
                if (!plain && plate is Rectangle p)
                {
                    if (p.Height > 0)
                        MenuSkin.NPatchPart(Tn + "Bar/Overlay.png", new Rectangle(70, 23, 794, 84), new Rectangle(p.X, p.Y, p.Width + 2, p.Height), 10, MenuSkin.White(alpha));
                }
                else if (!plain) MenuSkin.NPatch(Tn + "Bar/Overlay.png", dest, 40, MenuSkin.White(alpha));
                if (type == 0 && item.Kind is Kind.Genre or Kind.Song)
                    MenuSkin.Whole(Tn + "Bar/Decoration.png", new Rectangle(dest.X, dest.Y, dest.Width, BarList.BarHeight + 9), MenuSkin.White(alpha));
                return new Rectangle(area.X + 70, area.Y + 22, area.Width - 140, area.Height - 44);
            }
            return list.DrawBody(area, item.Color, alpha, plain, plate);
        }

        // 背景(Background/番号.png。960×1080 を横に並べる)。番号が変わったら0.25秒で入れ替える
        //   背景は左へ流れる(TaikøNauts の動画：0.1秒で10px、1秒で96px。縦には動かない)
        private const float BgScrollSpeed = 96f;
        private int bgShown = -1, bgPrevious = -1;
        private float bgBlend = 1f;
        private float bgScroll;

        private void UpdateTnBackground(float dt)
        {
            bgScroll = (bgScroll + BgScrollSpeed * dt) % 3840f; // 960 の倍数で戻す(数が大きくなりすぎないように)
            Item? item = diffOpen ? diffItem : CurrentItem;
            // 「とじる」「ランダム」には自分の背景が無いので、開いているジャンルの背景のまま
            //   （11/8 の TaikøNauts の録画：「とじる」にカーソルを合わせても背景は J-POP の水色のままだった。
            //    前は番号0（虹色）の背景に切りかわっていた）
            if (item?.Kind is Kind.Back or Kind.Random && inSongs && GenreItemOpen is Item open) item = open;
            int target = BackgroundType(item);
            if (target != bgShown)
            {
                bgPrevious = bgShown;
                bgShown = target;
                bgBlend = bgPrevious < 0 ? 1f : 0f;
            }
            bgBlend = Math.Min(1f, bgBlend + dt / 0.25f);
        }

        private void DrawTnBackground()
        {
            if (bgPrevious >= 0 && bgBlend < 1f) DrawTnBackgroundImage(bgPrevious, 1f);
            DrawTnBackgroundImage(bgShown, bgBlend);
        }

        private void DrawTnBackgroundImage(int type, float alpha)
        {
            if (type < 0 || SkinManager.GetTexture(Tn + $"Background/{type}.png") is not Texture2D texture) return;
            for (float x = -(bgScroll % texture.Width); x < Screen.Width; x += texture.Width)
                Raylib.DrawTexturePro(texture, new Rectangle(0, 0, texture.Width, texture.Height),
                    new Rectangle(x, 0, texture.Width, Screen.Height), Vector2.Zero, 0f, MenuSkin.White(alpha));
        }

        // ===== 描画 =====

        public override void Draw()
        {
            Gui.BeginFrame();
            active = !Game.IsSettingsOpen;
            if (!active || options.IsOpen || searchFocused || join.Active) Gui.SetClip(NoClickArea);
            else Gui.ResetClip();

            Raylib.DrawRectangleGradientV(0, 0, Screen.Width, Screen.Height,
                Raylib.ColorBrightness(backColor, -0.35f), Raylib.ColorBrightness(backColor, -0.7f));
            DrawTnBackground();
            // どんちゃん（背景の上・板より奥）。難易度をえらぶ板が出ているときは、板より手前に出す(11/22 たけらい)
            CharaConfig? charaConfig = CharaBaker.Config;
            if (!diffOpen && charaConfig != null) chara.DrawPlayers(charaConfig.SongSelectX, charaConfig.SongSelectY, charaConfig.SongSelectScale, LocalSession.Players);

            if (SongList.IsScanning) DrawScanning();
            else if (SongList.Categories.Count == 0) DrawEmpty();
            else
            {
                float listAlpha = diffOpen ? 1f - Math.Clamp((diffTime - ListFadeStart) / (ListFadeEnd - ListFadeStart), 0f, 1f) : 1f;
                if (listAlpha > 0f) DrawList(listAlpha * ReturnAlpha);
            }

            if (diffOpen)
            {
                DrawDifficulty();
                if (charaConfig != null) chara.DrawPlayers(charaConfig.SongSelectX, charaConfig.SongSelectY, charaConfig.SongSelectScale, LocalSession.Players);
            }
            DrawSavedSongRecords();
            NamePlate.DrawPlayers(900, ReturnAlpha);
            // 右上：次は何曲目か（特訓モードは出さない）
            if (!training) SongCounter.DrawPlate(SongCounter.SongSelectX, SongCounter.SongSelectY, SongCounter.Number(LocalSession.SongsPlayed + 1), ReturnAlpha);
            // 名札の下に、つけている演奏オプションのアイコンを横1列で（11/12 たけらいのスクショ。左から約76px・名札のすぐ下）
            foreach (int side in LocalSession.Players)
            {
                GameConfig badgeConfig = LocalSession.Count == 2 ? LocalSession.Options[side] : ConfigManager.Game;
                OptionBadges.DrawGrid(badgeConfig, NamePlate.MenuArea(side).X + 76f, 900f + 80f + 8f, 1f, int.MaxValue, gap: 4f, alpha: ReturnAlpha);
            }
            PuchiChara.DrawPlayers("songSelect");
            DrawHeader();

            Gui.ResetClip();
            if (!active) Gui.SetClip(NoClickArea);
            options.Draw(active);
            Gui.ResetClip();

            // 操作説明は出さない。入力値のエラーは引き続き知らせる。
            if (options.IsOpen && options.ErrorMessage is string error)
                Text(error, 44, 1037, 24, ErrorColor);
            if (toastTimer > 0f) DrawToast();
            if (searchFocused) DrawSearchBox();
            join.Draw(active);
        }

        private void DrawHeader()
        {
            if (training)
            {
                Raylib.DrawRectangle(1490, 120, 370, 52, new Color(18, 25, 38, 235));
                Text("特訓モード", 1530, 128, 28, Color.SkyBlue);
            }
            // 左上の太鼓の案内(難易度をえらんでいる間・検索の箱が出ている間は出さない)
            if (!SkinManager.SystemSkin && largeControlGuide is Aup2 guide && !(diffOpen && diffTime >= GuideHideAt)
                && !SongList.IsScanning && SongList.Categories.Count > 0)
                guide.Draw(guide.LoopedFrameAt(guideClock), GuideOffset, ReturnAlpha);
            // 左上の「演奏ゲーム」「ジャンルをえらぶ」は出さない(スキンの画像とかぶって、じゃまになる)。
            // どのジャンルにいるかは一覧の板そのもので分かるので、文字は要らない

        }

        // 一覧(斜めに並べる。上下はぐるっとつながっている)
        private void DrawList(float alpha)
        {
            List<Item> items = CurrentList;
            if (items.Count == 0) return;

            int baseIndex = (int)MathF.Round(list.Shown);
            var (before, after) = BarList.VisibleRange(items.Count);
            bool clickable = returnTime < 0f && !diffOpen && !list.Deciding && flipTime < 0f;
            var flip = FlipLook();

            // 選んでいる板は、ほかの板の上に描くので最後
            int selectedPos = CurrentPos;
            Rectangle? selectedArea = null;
            Item? selectedItem = null;

            for (int k = -before; k <= after; k++)
            {
                int pos = baseIndex + k;
                Item item = items[Mod(pos, items.Count)];
                Rectangle area = BarList.BarArea(pos - list.Shown);
                if (area.Y > Screen.Height || area.Y + area.Height < 0) continue;

                bool selected = pos == selectedPos;
                if (selected)
                {
                    selectedArea = list.OpenArea(area); // 開いた板(上下に広がる)
                    selectedItem = item;
                }
                else
                {
                    // めくれる前半：上の板は上へ、下の板は下へすべって消える
                    float shift = flip.Shift * Math.Sign(pos - selectedPos);
                    DrawBar(item, new Rectangle(area.X, area.Y + shift, area.Width, area.Height), 0f, alpha * flip.Others);
                    // 段位道場の課題曲：板の左に段位の札（TaikøNauts のスクショ：札の左上は板の左はしから -124、上から +5）
                    if (item.Dan is DanCourse dc)
                        DrawDanPlate(dc, new Rectangle(area.X - 124, area.Y + shift + 5, 110, 110), "Plate_Bar_Side.png", 110, 110, false, alpha * flip.Others);
                }

                // クリック：選んでいる項目なら決定、ほかなら選ぶ
                if (clickable && item.Kind != Kind.Label && Gui.Clicked(selected && selectedArea is Rectangle open ? open : area))
                {
                    if (selected) Decide();
                    else if (skipLock <= 0f)
                    {
                        skipGesture.Reset();
                        SetPos(pos);
                        MenuSounds.Ka();
                    }
                }
            }

            if (selectedArea is Rectangle sel && selectedItem != null)
            {
                if (flip.Active)
                {
                    // めくれている間：枠は出さない。板は真ん中を中心に大きく・細くする。中身はだんだん消す・出す
                    Rectangle box = new Rectangle(sel.X + sel.Width * (1f - flip.ScaleX) / 2f, sel.Y + sel.Height * (1f - flip.ScaleY) / 2f,
                        sel.Width * flip.ScaleX, sel.Height * flip.ScaleY);
                    if (flipTall && !flipOpened) { box.Y -= FlipTallExtra; box.Height += FlipTallExtra; }   // 下そろえで上に高くする
                    // 「とじる」：細い線から広がるところは、戻り先のジャンルの色の空の板（11/8 の TaikøNauts の録画では J-POP の水色だった）
                    Item flipItem = flipFromBack && !flipOpened && flipTime >= FlipThinEnd && GenreItemOpen is Item back ? back : selectedItem;
                    DrawBar(flipItem, box, list.Expand, alpha, framed: !(flipTall && !flipOpened) && (!flipOpened || list.FrameShown), contentAlpha: flip.Content);   // 広がる途中は、中まで明るい空の板(TaikøNauts と同じ)
                    if (selectedItem.Kind == Kind.Genre && !flipOpened) DrawGenrePop(selectedItem, sel, alpha * flip.Content, force: true);
                    return;
                }
                if (!(diffOpen && diffTime >= FrameHideAt)) list.DrawFrame(sel, alpha); // 曲を決めたときは、白くなった枠が 0.65秒で消える
                if (danPick) Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 110));   // 段位道場を選んでいる間は、まわりを暗く
                DrawBar(selectedItem, sel, list.Expand, alpha, list.FrameShown);
                if (selectedItem.Kind == Kind.Genre) DrawGenrePop(selectedItem, sel, alpha);
                // 段位道場の課題曲：選んでいる曲の上に大きい札（TaikøNauts のスクショ：札の絵 300×120 を画面のまんなか、開いた板の上から -88）
                if (selectedItem.Dan is DanCourse top && list.FrameShown)
                {
                    float a = alpha * Math.Clamp((list.OpenSeconds - BarList.ExpandSeconds) / BalloonFade, 0f, 1f);
                    if (a > 0f) DrawDanPlate(top, new Rectangle(sel.X + sel.Width / 2f - 150f, sel.Y - 88f, 300, 120), "Plate_Bar_Top.png", 300, 120, true, a);
                }
            }
        }

        // めくれている間の見た目(Active が false なら、ふつうに描く)
        //   Others：選んでいない板・キャラ・吹き出しの濃さ、Content：選んでいる板の中身(文字・札)の濃さ
        //   Shift：上下の板をずらす量(px。上の板は上へ、下の板は下へ)
        private (bool Active, float ScaleX, float ScaleY, float Others, float Content, float Shift) FlipLook()
        {
            if (flipTime < 0f) return (false, 1f, 1f, 1f, 1f, 0f);
            float t = flipTime;
            if (t < FlipSwitch)
            {
                float fade = 1f - Math.Clamp(t / FlipFadeEnd, 0f, 1f);
                float o = Math.Clamp((t - FlipOthersStart) / (FlipOthersEnd - FlipOthersStart), 0f, 1f);
                float others = 1f - Math.Clamp((o - 0.8f) / 0.2f, 0f, 1f), shift = FlipOthersShift * MathF.Pow(o, 2.5f); // だんだん速くすべっていく(最後の2割だけ消える)
                float sx = 1f, sy = 1f;
                bool tall = false;
                if (t < FlipPopEnd) { sx = FlipPopScaleX; sy = FlipPopScaleY; }
                else if (t >= FlipSquashStart && t < FlipSquashEnd)
                {
                    // はじめが少しゆっくり → まんなかは一定 → 最後はゆっくり細くなる(測った幅の表をなめらかにつなぐ)
                    float p = (t - FlipSquashStart) / (FlipSquashEnd - FlipSquashStart) * (FlipSquashWidths.Length - 1);
                    int i = Math.Clamp((int)p, 0, FlipSquashWidths.Length - 2);
                    sx = FlipSquashWidths[i] + (FlipSquashWidths[i + 1] - FlipSquashWidths[i]) * (p - i);
                }
                else if (t >= FlipSquashEnd && t < FlipThinEnd) sx = FlipThinScale;
                else if (t >= FlipThinEnd)
                {
                    // 開いた板の高さで、加速しながら元の幅まで広がる(半分の時間で 27%。ほぼ p²)
                    float p = Math.Clamp((t - FlipThinEnd) / (FlipGrowEnd - FlipThinEnd), 0f, 1f);
                    sx = 0.01f + 0.99f * p * p;
                    tall = !flipFromBack;
                }
                flipTall = tall;
                return (true, sx, sy, others, fade, shift);
            }
            float rest = 1f - Math.Clamp((t - FlipSwitch) / (FlipEnd - FlipSwitch), 0f, 1f);
            float content = Math.Clamp((t - FlipSwitch) / FlipContentFade, 0f, 1f);
            return (true, 1f, 1f, 1f, content, FlipInShift * rest * rest * rest);
        }

        // ===== 開いたジャンルの板のまわり(TaikøNauts の動画を測った値) =====
        // 両わきのキャラ：Box_Pop/番号.png(1344×486)を原寸で (288, 271) に。枠が出たときから0.13秒でじわっと出て、カーソルが動くと消える
        private const float PopX = 288, PopY = 271, PopFade = 0.13f;
        // 曲の数の吹き出し：Maker_Balloon.png(870×122)を原寸で、開いた板の左上から (+33, -94) に(= 528, 291)
        //   板が開ききってから0.07秒で出る。文字は吹き出しの (435, 58) が真ん中
        private const float BalloonX = 33, BalloonY = -94, BalloonWidth = 870, BalloonHeight = 122, BalloonFade = 0.07f;
        private static readonly Color BalloonTextColor = new Color(40, 40, 40, 255);

        //   force：めくれている途中(枠が消えていても、消えていくまで描く)
        private void DrawGenrePop(Item item, Rectangle open, float alpha, bool force = false)
        {
            if (!list.FrameShown && !force) return;
            if (alpha <= 0f) return;
            int type = BarType(item);
            // キャラは枠が出たときからじわっと出て、板が開ききるころ(0.13秒)にはっきり見える
            float pop = Math.Clamp(list.FrameSeconds / PopFade, 0f, 1f);
            if (type >= 0) MenuSkin.Whole(Tn + $"Box_Pop/{type}.png", new Rectangle(PopX, PopY, 1344, 486), MenuSkin.White(alpha * pop));

            float a = alpha * Math.Clamp((list.OpenSeconds - BarList.ExpandSeconds) / BalloonFade, 0f, 1f);
            if (a <= 0f) return;
            Rectangle balloon = new Rectangle(open.X + BalloonX, open.Y + BalloonY, BalloonWidth, BalloonHeight);
            if (!MenuSkin.Whole(Tn + "Maker_Balloon.png", balloon, MenuSkin.White(a)))
            {
                // 画像が無いとき(テスト用)：白い角丸の箱
                Rectangle body = new Rectangle(balloon.X + 45, balloon.Y + 24, BalloonWidth - 90, 70);
                SmoothShapes.Rounded(body, 1f, 16, Fade(InkColor, a));
                Rectangle fill = new Rectangle(body.X + 6, body.Y + 6, body.Width - 12, body.Height - 12);
                SmoothShapes.Rounded(fill, 1f, 16, Fade(new Color(244, 244, 244, 255), a));
            }
            if (item.Special == Special.DanSongs && danPick)
            {
                // 「◀ 段位道場の名前 ▶」（黄色い矢印は、カッで変えられる合図）
                Centered(DanCatTitle, new Rectangle(balloon.X + 110, balloon.Y + 58 - 25, BalloonWidth - 220, 50), 36, Fade(BalloonTextColor, a));
                Color arrow = Fade(new Color(255, 230, 0, 255), a);
                using (UseOutlineRatio(3f / 44f))
                {
                    Outlined("◀", new Rectangle(balloon.X + 40, balloon.Y + 28, 60, 60), 44, arrow, Fade(Color.Black, a));
                    Outlined("▶", new Rectangle(balloon.X + BalloonWidth - 100, balloon.Y + 28, 60, 60), 44, arrow, Fade(Color.Black, a));
                }
                return;
            }
            string count = item.SongCount > 0 ? $"全 {item.SongCount} 曲" : "曲がありません";
            Centered(count, new Rectangle(balloon.X + 60, balloon.Y + 58 - 25, BalloonWidth - 120, 50), 36, Fade(BalloonTextColor, a));
        }

        // 1本の板。open は開き具合(0→1)。framed は黄色い枠が出ている(選んで止まっている)板
        //   contentAlpha：中身(文字・札)だけの濃さ(ジャンルを決めたあとのめくれる動きで使う)
        private void DrawBar(Item item, Rectangle area, float open, float alpha, bool framed = false, float contentAlpha = 1f)
        {
            if (item.Kind == Kind.Label)
            {
                Rectangle labelInner = new Rectangle(area.X + 70, area.Y + 20, area.Width - 140, area.Height - 40);
                if (!MenuSkin.NPatch(MenuSkin.SongFolder + "Bar_Label.png", area, 30, MenuSkin.White(alpha)))
                {
                    SmoothShapes.Rounded(area, Roundness(area, 22), 10, Fade(LabelColor, alpha));
                    SmoothShapes.Rounded(labelInner, Roundness(labelInner, 8), 8, Fade(new Color(120, 122, 134, 200), alpha));
                }
                Outlined(item.Title, labelInner, 46, Fade(new Color(225, 225, 240, 255), alpha), Fade(new Color(60, 50, 110, 255), alpha));
                return;
            }

            // 縁 → 色 → 中の明るい板(スキンの画像があればそれで)
            Color ink = InkFor(item, false);
            bool plain = item.Kind is Kind.Back or Kind.Random; // 「とじる」「ランダム」は中の板なし
            // 枠が出ているジャンルの板は、中の板をジャンル名の帯より下だけに描く(開くにつれて下へ広がる)
            Rectangle? plate = framed && item.Kind == Kind.Genre ? BarList.OpenPlateArea(area) : null;
            Rectangle inner = DrawBarBody(item, area, alpha, plain, plate);
            alpha *= contentAlpha; // ここから下は中身
            if (alpha <= 0f) return;

            switch (item.Kind)
            {
                case Kind.Genre:
                    // 開いても、ジャンル名は閉じた板と同じ場所・同じ大きさ(板の一番上。TaikøNauts と同じ)
                    //   曲の数は、板の上の吹き出しに出す(DrawGenrePop)
                    float topPad = inner.Y - area.Y;
                    Rectangle titleArea = new Rectangle(inner.X, inner.Y, inner.Width, BarList.BarHeight - topPad * 2);
                    using (UseOutlineRatio(GenreTitleOutline / GenreTitleSize))
                        Outlined(item.Title, titleArea, GenreTitleSize, Fade(Color.White, alpha), Fade(ink, alpha));
                    // 段位道場の課題曲の説明（TaikøNauts のスクショ：開いた板の上から +180・+230 が行のまんなか）
                    if (item.Special == Special.DanSongs && open > 0.9f)
                    {
                        float da = alpha * Math.Clamp((open - 0.9f) / 0.1f, 0f, 1f);
                        OutlinedDetail("課題曲をあつめたよ!", new Rectangle(area.X, area.Y + 180 - 25, area.Width, 50), 30, Fade(Color.White, da), Fade(ink, da), 4f);
                        OutlinedDetail("※段位道場モードにはいけません", new Rectangle(area.X, area.Y + 230 - 25, area.Width, 50), 30, Fade(Color.White, da), Fade(ink, da), 4f);
                    }
                    break;

                case Kind.Back:
                    using (UseOutlineRatio(6f / 45f))
                        Outlined(item.Title, inner, open > 0f ? 56 : 45, Fade(Color.White, alpha), Fade(BackBoxColor, alpha));
                    break;

                case Kind.Random:
                    Outlined(item.Title, inner, open > 0f ? 56 : 46, Fade(Color.White, alpha), Fade(ink, alpha));
                    break;

                case Kind.Song:
                    DrawSongBar(item, inner, open, ink, alpha);
                    break;
            }
        }

        // 文字の大きさと縁（11/14 たけらいがくれた TaikøNauts のスクショ 1920×1080 を 1px ずつ測った）
        //   曲の板の曲名：白い字の高さ 37〜38px・縁 5px。開いた板の曲名も同じ。サブタイトル：高さ 21px・縁 3px
        //   ジャンルの板の名前：高さ 47〜49px・縁 7〜8px（曲名より大きい）
        //   うちのフォントは 大きさ×0.84 くらいが白い字の高さなので、そこから大きさを決めた
        private const float SongTitleSize = 45f, SongTitleOutline = 5f;
        private const float GenreTitleSize = 57f, GenreTitleOutline = 7.5f;

        // 曲名の縁の色：板の色を、色合いはそのまま、少しだけあざやかにして暗くした色
        //   TaikøNauts のナムコオリジナル（オレンジの板）は (146,26,0) のあざやかな暗い赤だった。
        //   11/14 最初は全部の板を「あざやかさ100%・明るさ58%」にしたら、淡い色の板（ボーカロイド・アニメなど）が
        //   どぎつい青やピンクになった（たけらい「色やばいｗ」）。なので、あざやかさは元の 1.15 倍まで、明るさは 45% にした
        //   灰色っぽい板は、今まで通り暗くするだけ
        // A-Style の板の絵の番号ごとの、字の縁の色（11/14 たけらいがくれた TaikøNauts のスクショで測った。ジャンル名・曲名・サブタイトル共通）
        //   0 どうよう・バラエティー（白い板）／3 アニメ／4 ボーカロイド／5 ゲームミュージック／6 ナムコオリジナル／7 クラシック／10 お気に入り
        //   ここに無い番号（J-POP など）と、絵の無い板は、板の色から作る（SongInk・65% 暗く）
        private static readonly Dictionary<int, Color> BarInks = new()
        {
            [0] = new Color(96, 6, 18, 255), [3] = new Color(150, 24, 96, 255), [4] = new Color(36, 48, 72, 255),
            [5] = new Color(78, 24, 114, 255), [6] = new Color(144, 30, 0, 255), [7] = new Color(96, 66, 42, 255),
            [10] = new Color(71, 54, 0, 255), [11] = new Color(2, 26, 92, 255),
        };

        private static Color InkFor(Item item, bool song)
        {
            int type = BarType(item);
            if (type >= 0 && BarInks.TryGetValue(type, out Color fixedInk) && SkinManager.GetTexture(Tn + $"Bar/{type}.png") != null)
                return fixedInk;
            return song ? SongInk(item.Color) : Raylib.ColorBrightness(item.Color, -0.65f);
        }

        private static Color SongInk(Color bar)
        {
            System.Numerics.Vector3 hsv = Raylib.ColorToHSV(bar);
            if (hsv.Y < 0.15f) return Raylib.ColorBrightness(bar, -0.65f);
            return Raylib.ColorFromHSV(hsv.X, Math.Min(1f, hsv.Y * 1.15f), 0.45f);
        }

        private void DrawSongBar(Item item, Rectangle inner, float open, Color ink, float alpha)
        {
            SongEntry song = item.Song!;
            bool favorite = SongHistory.IsFavorite(song.Key);
            Color titleColor = song.HasError ? ErrorColor : Color.White;

            if (open <= 0f)
            {
                using (UseOutlineRatio(SongTitleOutline / SongTitleSize))
                    Outlined(song.Title, new Rectangle(inner.X + 30, inner.Y, inner.Width - 60, inner.Height), SongTitleSize, Fade(titleColor, alpha), Fade(InkFor(item, true), alpha));
                if (favorite) Text("♥", inner.X + inner.Width - 36, inner.Y + inner.Height / 2f - 14, 26, Fade(new Color(230, 80, 130, 255), alpha));
                return;
            }

            float a = alpha * open; // 開くあいだに、だんだん出す

            // 上：お気に入りの印
            //   11/12 たけらい「検索結果の板の左上のジャンルはいらない。板の色で分かるから。最近遊んだ曲もそう」
            //   前は、検索・最近追加・お気に入りの一覧で、左上にジャンル名を書いていた
            if (favorite) Text("♥ お気に入り", inner.X + inner.Width - 150, inner.Y + 8, 20, Fade(new Color(230, 80, 130, 255), a));

            // 曲名・サブタイトル(TaikøNauts と同じく板の上の方)
            using (UseOutlineRatio(SongTitleOutline / SongTitleSize))
                Outlined(song.Title, new Rectangle(inner.X + 20, inner.Y + 32, inner.Width - 40, 66), SongTitleSize, Fade(titleColor, alpha), Fade(InkFor(item, true), alpha));
            string sub = song.HasError ? song.Error : song.Subtitle;
            if (sub.Length > 0)
                // サブタイトルは曲名と同じスキンのメインのフォント（11/14 たけらい「フォント違う」。細かい字のフォントは曲数・ジャンルなどの方）
                using (UseOutlineRatio(3f / 25f))
                    Outlined(sub, new Rectangle(inner.X + 20, inner.Y + 96, inner.Width - 40, 34), 25, Fade(song.HasError ? ErrorColor : Color.White, a), Fade(InkFor(item, true), a));   // 縁は曲名と同じ板の色の濃い色（11/14 たけらい「黒で固定はめっちゃ違和感」）

            // 下：難易度と★
            //   11/12：札は原寸の 176×86 で、上が 572（開いた板の下 695 から 123px 上）。11/11 の録画に札の絵を重ねて合わせた。
            //   前は 175×85 に少し縮めて 573 に置いていたので、★と数字が 1px ずれて、ぼやけていた
            DrawCourseChips(song, new Rectangle(inner.X, inner.Y + inner.Height - 101, inner.Width, 86), a);
        }

        // 難易度の札(左に名前、右に★レベル)を、真ん中にそろえて並べる
        //   おにと裏おにの両方がある曲は、おにの場所で「おに」と「裏おに」を1.5秒ごとに入れ替える(TaikøNauts と同じ)
        private void DrawCourseChips(SongEntry song, Rectangle area, float alpha)
        {
            List<(CourseType Type, int Level)> courses = song.Courses
                .Where(c => c.Type <= CourseType.Edit).OrderBy(c => c.Type).ToList();
            if (courses.Count == 0) return;
            bool hasOni = courses.Any(c => c.Type == CourseType.Oni);
            int uraLevel = courses.FirstOrDefault(c => c.Type == CourseType.Edit).Level;
            bool alternate = hasOni && courses.Any(c => c.Type == CourseType.Edit);
            if (alternate) courses.RemoveAll(c => c.Type == CourseType.Edit); // 裏はおにの場所に出す

            const float chipWidth = 176, gap = 6;   // 札の絵と同じ大きさ。となりの札とは 182px ごと（絵の並びと同じ）
            float total = courses.Count * chipWidth + (courses.Count - 1) * gap;
            float x = area.X + (area.Width - total) / 2f;
            foreach (var (type, level) in courses)
            {
                Rectangle chip = new Rectangle(x, area.Y, chipWidth, area.Height);
                DrawChip(type, level, chip, alpha);
                if (alternate && type == CourseType.Oni)
                {
                    float ura = UraBlend(uraClock);
                    if (ura > 0f) DrawChip(CourseType.Edit, uraLevel, chip, alpha * ura); // おにの上に、だんだん重ねる
                }
                x += chipWidth + gap;
            }
        }

        // おに → 裏おに の重ね具合(0 おに、1 裏おに)。3秒で1周：おに1.25秒 → 0.25秒で裏へ → 裏1.25秒 → 0.25秒でおにへ
        private const float UraCycle = 3.0f, UraHold = 1.25f, UraFade = 0.25f;

        private static float UraBlend(float clock)
        {
            float t = clock % UraCycle;
            if (t < UraHold) return 0f;
            if (t < UraHold + UraFade) return (t - UraHold) / UraFade;
            if (t < UraHold * 2 + UraFade) return 1f;
            return 1f - (t - UraHold * 2 - UraFade) / UraFade;
        }

        // 難易度の札を1つ(スキンの Difficulty_Panel.png があればそれ。数字はプログラムが書く)
        private static void DrawChip(CourseType type, int level, Rectangle chip, float alpha)
        {
            Color color = CourseColor(type);
            Color ink = Fade(Raylib.ColorBrightness(color, -0.7f), alpha);
            // TaikøNauts のスキン：Difficulty_Panel.png(横に5つ。1つ 176×86、182px ごと。★まで描いてある)
            //   数字は Difficulty_Level_Number.png(0〜9。1文字 31×37)で、★のすぐ右に書く(TaikøNauts の動画で測った場所)
            if (MenuSkin.Part(Tn + "Difficulty_Panel.png", new Rectangle(3 + 182 * (int)type, 2, 176, 86), chip, MenuSkin.White(alpha)))
            {
                float k = chip.Height / 86f;
                if (!MenuSkin.Digits(Tn + "Difficulty_Level_Number.png", level.ToString(), chip.X + ChipNumberX * k, chip.Y + ChipNumberY * k, ChipNumberAdvance, k, MenuSkin.White(alpha)))
                    Outlined(level.ToString(), new Rectangle(chip.X + 128, chip.Y + 20, 44, 46), 36, Fade(Color.White, alpha), Fade(InkColor, alpha));
                return;
            }
            // 画像が無いとき(シンプルスキンなど)はプログラムで描く
            //   11/14 たけらいが見せてくれた TaikøNauts の SimpleStyle の Difficulty_Panel.png（1枚 63×102）のまね：
            //     上から下へ少し暗くなる板（角の丸み 8）→ 上の 45% に英語の難易度名（明るい字に細い黒ふち）
            //     → 下に一段暗い角丸の箱（左右 4px あけ、丸み 6）→ その中に白い★と数字
            //   絵は使わず、プログラムで同じ形を描いている（絵は TaikøNauts のものなので入れない）
            //   本家の字はななめ（イタリック）だけど、今のフォントの描き方ではななめにできないので、まっすぐのまま
            SmoothShapes.Rounded(chip, Roundness(chip, 8), 8, Fade(color, alpha));
            Rectangle shade = new Rectangle(chip.X + 3, chip.Y + chip.Height * 0.5f, chip.Width - 6, chip.Height * 0.5f - 3);
            Raylib.DrawRectangleGradientV((int)shade.X, (int)shade.Y, (int)shade.Width, (int)shade.Height,
                Fade(new Color(0, 0, 0, 0), alpha), Fade(new Color(0, 0, 0, 40), alpha));
            float wordH = chip.Height * 0.42f;
            using (UseOutlineRatio(2f / 22f))
                Outlined(CourseWord(type), new Rectangle(chip.X + 4, chip.Y + 2, chip.Width - 8, wordH), 22,
                    Fade(MenuDraw.Tint(color, 0.35f), alpha), Fade(Color.Black, alpha));
            Rectangle box = new Rectangle(chip.X + 4, chip.Y + wordH + 2, chip.Width - 8, chip.Height - wordH - 6);
            SmoothShapes.Rounded(box, Roundness(box, 6), 6, Fade(Raylib.ColorBrightness(color, -0.32f), alpha));
            StarLevel(level, box, 30, 6f, Fade(Color.White, alpha), ink);
        }

        // 難易度の英語の名前（TaikøNauts の SimpleStyle と同じ書き方）
        private static string CourseWord(CourseType type) => type switch
        {
            CourseType.Easy => "EASY",
            CourseType.Normal => "NORMAL",
            CourseType.Hard => "HARD",
            CourseType.Oni => "ONI",
            CourseType.Edit => "EDIT",
            _ => "",
        };

        // 「★4」を、★と数字それぞれのインク（実際に色がつく部分）の真ん中で、四角の高さの真ん中にそろえて書く
        //   11/14 たけらい「シンプルスキンの数字の表示が若干下」：1行まとめて書くと、フォントの数字が★より下に寄って見えていた
        private static void StarLevel(int level, Rectangle area, float size, float gap, Color fill, Color ink)
        {
            string number = level.ToString();
            Rectangle star = FontManager.MeasureSkinMainInk("★", size), num = FontManager.MeasureSkinMainInk(number, size);
            float total = star.Width + gap + num.Width, limit = area.Width - OutlineWidth(size) * 2;
            if (total > limit && total > 0)
            {
                size *= limit / total; gap *= limit / total;
                star = FontManager.MeasureSkinMainInk("★", size); num = FontManager.MeasureSkinMainInk(number, size);
                total = star.Width + gap + num.Width;
            }
            float x = area.X + (area.Width - total) / 2f, cy = area.Y + area.Height / 2f;
            OutlinedAt("★", x - star.X, cy - (star.Y + star.Height / 2f), size, fill, ink);
            OutlinedAt(number, x + star.Width + gap - num.X, cy - (num.Y + num.Height / 2f), size, fill, ink);
        }

        // 難易度の色
        private static Color CourseColor(CourseType type) => type switch
        {
            CourseType.Easy => new Color(240, 90, 40, 255),
            CourseType.Normal => new Color(130, 190, 40, 255),
            CourseType.Hard => new Color(70, 140, 170, 255),
            CourseType.Oni => new Color(210, 50, 160, 255),
            CourseType.Edit => new Color(120, 70, 210, 255),
            _ => new Color(150, 150, 160, 255),
        };


        // ===== 難易度をえらぶ板 =====

        private void DrawDifficulty()
        {
            if (diffSong == null || diffTime < PanelInStart) return;
            float t = Math.Clamp((diffTime - PanelInStart) / (PanelInEnd - PanelInStart), 0f, 1f);
            float alpha = t * ReturnAlpha;
            // 10/29のお手本70.40〜70.70秒：A-Styleの板は原寸のままフェードする。
            float scale = MenuSkin.Has(TnPanelPath(diffType)) ? 1f : 0.92f + 0.08f * EaseOut(t);
            Vector2 center = new Vector2(DiffPanel.X + DiffPanel.Width / 2f, DiffPanel.Y + DiffPanel.Height / 2f);
            Rectangle S(Rectangle r) => new Rectangle(center.X + (r.X - center.X) * scale, center.Y + (r.Y - center.Y) * scale, r.Width * scale, r.Height * scale);

            // 大きい板(縁 → 曲のカテゴリの色)と、曲名の箱
            //   スキンに Panel_Base.png(白い土台。色をかける)と Panel_Overlay.png(縁と曲名の箱)があれば、それで描く
            Rectangle panel = S(DiffPanel);
            Rectangle titleBox = S(DiffTitleBox);
            // TaikøNauts のスキン：Difficulty_Select/Background/番号.png(1242×779。中身は 10,10 から 1220×758)
            Rectangle tnPanel = new Rectangle(panel.X - 5 * scale, panel.Y - 6 * scale, panel.Width + 10 * scale, panel.Height + 12 * scale);
            if (MenuSkin.Part(TnPanelPath(diffType), new Rectangle(10, 10, 1220, 758), tnPanel, MenuSkin.White(alpha))) { }
            else if (MenuSkin.Has(MenuSkin.SongFolder + "Panel_Base.png", MenuSkin.SongFolder + "Panel_Overlay.png"))
            {
                MenuSkin.Whole(MenuSkin.SongFolder + "Panel_Base.png", panel, Fade(diffColor, alpha));
                MenuSkin.Whole(MenuSkin.SongFolder + "Panel_Overlay.png", panel, MenuSkin.White(alpha));
            }
            else
            {
                SmoothShapes.Rounded(panel, Roundness(panel, 40), 12, Fade(InkColor, alpha));
                Rectangle panelBody = new Rectangle(panel.X + 7, panel.Y + 7, panel.Width - 14, panel.Height - 14);
                SmoothShapes.Rounded(panelBody, Roundness(panelBody, 34), 12, Fade(diffColor, alpha));
                SmoothShapes.Rounded(titleBox, Roundness(titleBox, 10), 8, Fade(Tint(diffColor, 0.55f), alpha));
            }

            Color ink = Raylib.ColorBrightness(diffColor, -0.7f);
            // 曲名・サブタイトル：スキンのメインのフォントに真っ黒の縁（11/14 たけらい：細かい字のフォントは曲数・ジャンルなどの方）
            //   縁の太さは前と同じ（曲名 6px、サブタイトル 4px）
            //   11/14 TaikøNauts のスクショ：曲名は白い字の高さ 50px・縁 7px、サブタイトルは高さ 30px・縁 4px（どちらも黒）
            using (UseOutlineRatio(7f / 60f))
                Outlined(diffSong.Title, new Rectangle(titleBox.X + 30, titleBox.Y + 55 * scale, titleBox.Width - 60, 80 * scale), 60 * scale,
                    Fade(Color.White, alpha), Fade(Color.Black, alpha));
            if (diffSong.Subtitle.Length > 0)
                using (UseOutlineRatio(4f / 36f))
                    Outlined(diffSong.Subtitle, new Rectangle(titleBox.X + 30, titleBox.Y + 135 * scale, titleBox.Width - 60, 40 * scale), 36 * scale,
                        Fade(Color.White, alpha), Fade(Color.Black, alpha));

            // 箱：戻る・オプション・かんたん〜おに
            bool ready = returnTime < 0f && DiffReady && active && !options.IsOpen && uraFlipTime < 0f;
            for (int i = 0; i < 6; i++)
            {
                Rectangle box = S(DiffBoxArea(i));
                bool selected = i == diffCursor;
                if (selected && uraFlipTime < 0f) DrawDiffCursor(box, alpha, diffDecideTime);
                DrawDiffBox(i, box, alpha, scale);
                if (i >= 2 && HasCourse(CourseAt(i))) SongRecordPanel.DrawCard(diffSong.Key, CourseAt(i), box, alpha);
                if (ready && Gui.Clicked(box))
                {
                    if (i == diffCursor) DecideDifficulty();
                    else { diffCursor = i; markerBounce = 0f; MenuSounds.Ka(); }
                }
            }

            if (uraFlipTime >= 0f) DrawUraTransition(S(DiffBoxArea(5)), scale, alpha);

            // 1P の印(カーソルの上)。裏おにのアニメ中は消す
            if (diffTime >= MarkerAt && uraFlipTime < 0f)
            {
                Rectangle at = DiffBoxArea(diffCursor);
                if (markerBounce < MarkerBounceSeconds)
                    at.Y += MarkerBounceDrop * MathF.Sin(MathF.PI * markerBounce / MarkerBounceSeconds);
                DrawPlayerMarker(at, ReturnAlpha * Math.Clamp((diffTime - MarkerAt) / MarkerFade, 0f, 1f));
            }


            // 下のまんなか：えらぶ・決定
            DrawDiffHint(alpha);
        }

        private void DrawSavedSongRecords()
        {
            if (SongList.IsScanning || SongList.Categories.Count == 0) return;
            RecordGroup group = ConfigManager.Game.RecordGroup;
            CourseType preferred = group == RecordGroup.OniAndUra
                ? lastCourse == CourseType.Edit ? CourseType.Edit : CourseType.Oni : (CourseType)group;
            if (diffOpen && diffCursor >= 2) preferred = CourseAt(diffCursor);
            SongEntry? song = diffOpen ? diffSong : CurrentItem?.Song;
            foreach (int side in LocalSession.Players)
            {
                int profile = LocalSession.Profiles[side];
                SongRecordPanel.DrawSummary(group, ReturnAlpha, profile: profile, side: side);
                if (song == null) continue;
                CourseType course = preferred;
                if (!song.Courses.Any(c => c.Type == course))
                    course = song.Courses.Where(c => c.Type <= CourseType.Edit).Select(c => c.Type).DefaultIfEmpty(CourseType.Oni).Max();
                SongRecordPanel.DrawBest(song, course, ReturnAlpha, profile, side);
            }
        }

        // 難易度の箱の場所(0 戻る、1 オプション、2〜5 かんたん〜おに)
        private static Rectangle DiffBoxArea(int index) => index switch
        {
            0 => DiffBackBox,
            1 => DiffOptionBox,
            _ => new Rectangle(DiffCardX[index - 2], DiffCardY, DiffCardWidth, DiffCardHeight),
        };

        // 選んでいる箱のまわりの黄色い枠
        //   decide：難易度を決めてからの秒(-1 なら決めていない)。黄色 ⇔ 白 を4回 → 黄色 → 消える
        private static void DrawDiffCursor(Rectangle box, float alpha, float decide = -1f)
        {
            float white = 0f;
            if (decide >= 0f)
            {
                if (decide >= DiffCursorGoneAt) return;
                if (decide < DiffBlinkSeconds)
                {
                    float phase = decide / DiffBlinkSeconds * DiffBlinkCount % 1f;   // 0 黄色 → 0.5 白 → 1 黄色
                    white = 1f - MathF.Abs(phase * 2f - 1f);
                }
                else if (decide > DiffCursorHoldEnd)
                    alpha *= 1f - (decide - DiffCursorHoldEnd) / (DiffCursorGoneAt - DiffCursorHoldEnd);
            }
            Rectangle frame = new Rectangle(box.X - 12, box.Y - 12, box.Width + 24, box.Height + 24);
            // 光(枠の外のぼやけた黄色)：Cursor_Effect.png の2つ目(240×400 の1マスまるごと)、小さい箱は Cursor_Effect_Small.png の2つ目(160×160)。
            //   決めた後は点滅させない
            if (decide < 0f)
            {
                bool smallBox = box.Width < 150;
                string glowPath = Tn + (smallBox ? "Difficulty_Select/Cursor_Effect_Small.png" : "Difficulty_Select/Cursor_Effect.png");
                Rectangle glow = smallBox
                    ? new Rectangle(frame.X - 13, frame.Y - 10, frame.Width + 26, frame.Height + 19)
                    : new Rectangle(frame.X - 12, frame.Y - 9, frame.Width + 24, frame.Height + 18);
                Rectangle glowSrc = smallBox ? new Rectangle(160, 0, 160, 160) : new Rectangle(240, 0, 240, 400);
                if (SkinManager.GetTexture(glowPath) != null)
                    MenuSkin.NPatchPart(glowPath, glowSrc, glow, 40, MenuSkin.White(alpha * MenuDraw.CursorPulse));
            }
            // 白い枠は Cursor_Effect.png の3つ目(480px 右)。カードのときだけ(小さい箱は決めても点滅しない)
            if (white > 0f && box.Width >= 150 && SkinManager.GetTexture(Tn + "Difficulty_Select/Cursor_Effect.png") != null)
            {
                MenuSkin.NPatchPart(Tn + "Difficulty_Select/Cursor_Effect.png", new Rectangle(12, 9, 216, 382), frame, 30, MenuSkin.White(alpha));
                MenuSkin.NPatchPart(Tn + "Difficulty_Select/Cursor_Effect.png", new Rectangle(12 + 480, 9, 216, 382), frame, 30, MenuSkin.White(alpha * white));
                return;
            }
            // TaikøNauts のスキン：カードは Cursor_Effect.png(1つ目 12,9 から 216×382)、小さい箱は Cursor_Effect_Small.png(1つ目 13,10 から 134×141)
            bool small = box.Width < 150;
            if (small ? MenuSkin.NPatchPart(Tn + "Difficulty_Select/Cursor_Effect_Small.png", new Rectangle(13, 10, 134, 141), frame, 30, MenuSkin.White(alpha))
                      : MenuSkin.NPatchPart(Tn + "Difficulty_Select/Cursor_Effect.png", new Rectangle(12, 9, 216, 382), frame, 30, MenuSkin.White(alpha))) return;
            SmoothShapes.Rounded(frame, Roundness(frame, 26), 10, Fade(CursorColor, alpha));
        }

        private void DrawDiffBox(int index, Rectangle box, float alpha, float scale)
        {
            if (DrawDiffBoxImage(index, box, alpha, scale)) return;

            SmoothShapes.Rounded(box, Roundness(box, 18), 10, Fade(InkColor, alpha));
            Rectangle body = new Rectangle(box.X + 5, box.Y + 5, box.Width - 10, box.Height - 10);

            if (index <= 1)
            {
                Color fill = index == 0 ? BackFillColor : new Color(220, 228, 70, 255);
                SmoothShapes.Rounded(body, Roundness(body, 14), 10, Fade(fill, alpha));
                if (index == 1) Icons.Gear(body.X + body.Width / 2f, body.Y + body.Height / 2f - 8, Fade(new Color(90, 90, 100, 255), alpha), Fade(fill, alpha));
                else Outlined("◀", new Rectangle(body.X, body.Y + 6, body.Width, body.Height * 0.6f), 48 * scale, Fade(Color.White, alpha), Fade(BackBoxColor, alpha));
                Outlined(index == 0 ? "もどる" : "オプション", new Rectangle(body.X + 4, body.Y + body.Height - 34 * scale, body.Width - 8, 28 * scale), 22 * scale,
                    Fade(Color.White, alpha), Fade(InkColor, alpha));
                return;
            }

            CourseType type = CourseAt(index);
            bool exists = HasCourse(type);
            Color color = exists ? CourseColor(type) : new Color(120, 122, 132, 255);
            // 上は明るく、下は濃く
            SmoothShapes.Rounded(body, Roundness(body, 14), 10, Fade(color, alpha));
            Rectangle upper = new Rectangle(body.X + 6, body.Y + 6, body.Width - 12, body.Height * 0.55f);
            SmoothShapes.Rounded(upper, Roundness(upper, 10), 8, Fade(Tint(color, 0.55f), alpha));
            Color ink = Fade(Raylib.ColorBrightness(color, -0.7f), alpha);

            // 印(絵の代わり)
            Vector2 mark = new Vector2(body.X + body.Width / 2f, body.Y + 110 * scale);
            SmoothShapes.Circle(mark, 62 * scale, Fade(Color.White, alpha * 0.85f));
            Outlined(CourseWord(type), new Rectangle(mark.X - 60 * scale, mark.Y - 50 * scale, 120 * scale, 100 * scale), 44 * scale, Fade(color, alpha), ink);

            // 名前・★レベル・★の列
            string name = type == CourseType.Edit ? "裏おに" : Chart.GetCourseName(type);
            Outlined(name, new Rectangle(body.X + 6, box.Y + 198 * scale, body.Width - 12, 44 * scale), 36 * scale, Fade(Color.White, alpha), ink);
            // 無い難易度は、灰色にするだけ（11/12 たけらい「なしって書いてあるけどなくして、ただのグレーアウトに」）
            if (!exists) return;
            Rectangle strip = new Rectangle(body.X + 8, box.Y + 280 * scale, body.Width - 16, 22 * scale);
            SmoothShapes.Rounded(strip, 1f, 8, Fade(Raylib.ColorBrightness(color, -0.45f), alpha));
            DrawCardLevel(index, type, box, alpha, scale, emptyDots: true);
        }

        // カードの「★ レベル」と★の列(★の列の下地はカードの画像・プログラムのどちらかで描いてある)。裏がある曲は右上に「裏」「表」
        private void DrawCardLevel(int index, CourseType type, Rectangle box, float alpha, float scale, bool emptyDots)
        {
            Color color = CourseColor(type);
            Color ink = Fade(Raylib.ColorBrightness(color, -0.7f), alpha);
            int level = LevelOf(type);
            Rectangle body = new Rectangle(box.X + 5, box.Y + 5, box.Width - 10, box.Height - 10);
            StarLevel(level, new Rectangle(body.X, box.Y + 240 * scale, body.Width, 44 * scale), 36 * scale, 10f * scale, Fade(Color.White, alpha), ink);

            Rectangle strip = new Rectangle(body.X + 8, box.Y + 280 * scale, body.Width - 16, 22 * scale);
            int stars = Math.Clamp(level, 0, 10);
            float cell = strip.Width / 10f;
            for (int s = 0; s < 10; s++)
            {
                Vector2 c = new Vector2(strip.X + cell * (s + 0.5f), strip.Y + strip.Height / 2f);
                if (s < stars) Outlined("★", new Rectangle(c.X - cell / 2f, c.Y - 11 * scale, cell, 22 * scale), 18 * scale, Fade(Color.White, alpha), Fade(Raylib.ColorBrightness(color, -0.45f), alpha));
                else if (emptyDots) SmoothShapes.Circle(c, 2.5f * scale, Fade(new Color(255, 255, 255, 140), alpha));
            }

            if (index == 5 && HasCourse(CourseType.Oni) && HasCourse(CourseType.Edit))
                Outlined(ura ? "裏" : "表", new Rectangle(body.X + body.Width - 44 * scale, body.Y + 6 * scale, 38 * scale, 38 * scale), 26 * scale, Fade(Color.White, alpha), ink);
        }

        // ===== 難易度の数字と★の場所(TaikøNauts の画像と動画を測った値。画像の中の座標) =====
        // 開いた曲の板の札(176×86)：数字の真ん中
        //   11/11：録画(2026-11-11 23-44-20.mp4 の 10秒)に A-Style の札と数字を 0.5px ずつずらして重ねて、いちばん合う所にした
        //   (前は 146, 43。右へ 1.5px・下へ 0.5px ずれていた)
        private const float ChipNumberX = 147.5f, ChipNumberY = 43.5f, ChipNumberAdvance = 24;
        // 難易度のカード(196×354)：★の右に数字、下の点々の列に★
        //   ★は 64〜91・250〜277(真ん中 77.5,263.5)、点々は 30,45,…,165 の10個・高さ 293(画像を測った)
        //   11/11：録画(同じ動画の 80秒)にカード・数字・★を 0.5px ずつずらして重ねて、いちばん合う所にした
        //   (前は 数字 108, 264・★ 30, 293。数字は右へ 3.5px・上へ 1px、★は右へ 1px・上へ 1px ずれていた)
        private const float CardNumberX = 111.5f, CardNumberY = 263, CardNumberAdvance = 19;
        private const float CardDotX = 31, CardDotStep = 15, CardDotY = 292;
        // 無い難易度のカード：絵も数字も半分の明るさ。数字は「0」(同じ録画。前は少し青い灰色で「なし」と書いていた)
        private static readonly Color MissingCardTint = new Color(128, 128, 128, 255);

        // カードの数字と★の列(カードの画像の上に。★の列は、画像の点々に1つずつ重ねる)
        private void DrawCardLevel(CourseType type, Rectangle box, float alpha)
        {
            // 横と縦で別々に倍率を出す(ふつうは原寸なので両方 1。板が広がる途中だけ小さくなる)
            float k = box.Width / 196f, ky = box.Height / 354f;
            string level = LevelOf(type).ToString();
            Color ink = Fade(Raylib.ColorBrightness(CourseColor(type), -0.7f), alpha);
            if (!MenuSkin.Digits(Tn + "Difficulty_Select/Difficulty_Level_Number.png", level, box.X + CardNumberX * k, box.Y + CardNumberY * ky, CardNumberAdvance, k, MenuSkin.White(alpha)))
                Outlined(level, new Rectangle(box.X + (CardNumberX - 18) * k, box.Y + (CardNumberY - 20) * ky, 36 * k, 40 * k), 32 * k, Fade(Color.White, alpha), ink);

            int stars = StarsOf(type);
            Texture2D? star = SkinManager.GetTexture(Tn + "Difficulty_Select/Difficulty_Level_Star.png");
            for (int s = 0; s < stars; s++)
            {
                Vector2 c = new Vector2(box.X + (CardDotX + CardDotStep * s) * k, box.Y + CardDotY * ky);
                if (star is Texture2D tex)
                    Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
                        new Rectangle(c.X - tex.Width * k / 2f, c.Y - tex.Height * k / 2f, tex.Width * k, tex.Height * k), Vector2.Zero, 0f, MenuSkin.White(alpha));
                else
                    Outlined("★", new Rectangle(c.X - 9 * k, c.Y - 10 * k, 18 * k, 20 * k), 16 * k, Fade(Color.White, alpha), ink);
            }
        }

        // スキンの画像で、難易度の箱を描く(Difficulty_Select/Difficulty_Panel.png。Simple も A-Style も同じ並び)。画像が無ければ false
        private bool DrawDiffBoxImage(int index, Rectangle box, float alpha, float scale)
        {
            // TaikøNauts のスキン：Difficulty_Select/Difficulty_Panel.png(戻る 114×112・オプション 114×112・カード 196×354 が横に並ぶ)
            if (SkinManager.GetTexture(Tn + "Difficulty_Select/Difficulty_Panel.png") != null)
            {
                string sheet = Tn + "Difficulty_Select/Difficulty_Panel.png";
                if (index == 0) return MenuSkin.Part(sheet, new Rectangle(1, 1, 114, 112), box, MenuSkin.White(alpha));
                if (index == 1) return MenuSkin.Part(sheet, new Rectangle(133, 1, 114, 112), box, MenuSkin.White(alpha));
                CourseType course = CourseAt(index);
                bool has = HasCourse(course);
                // 無い難易度は、暗くして描く
                Color tint = has ? MenuSkin.White(alpha) : Fade(MissingCardTint, alpha);
                MenuSkin.Part(sheet, new Rectangle(265 + 215 * (int)course, 1, 196, 354), box, tint);
                if (has) DrawCardLevel(course, box, alpha);
                // 無い難易度：「なし」は書かない（11/12 たけらい）。TaikøNauts と同じく、カードの★の横に暗い「0」だけ出す
                else MenuSkin.Digits(Tn + "Difficulty_Select/Difficulty_Level_Number.png", "0", box.X + CardNumberX * box.Width / 196f, box.Y + CardNumberY * box.Height / 354f, CardNumberAdvance, box.Width / 196f, tint);
                return true;
            }

            return false;
        }

        // 1P の印(赤い丸に「1P」、下に三角)
        private static void DrawPlayerMarker(Rectangle box, float alpha)
        {
            Vector2 c = new Vector2(box.X + box.Width / 2f, 358 + (box.Y - DiffCardY));
            // TaikøNauts のスキン：Difficulty_Select/Player_Cursor.png(縦に 1P・2P・…。1段 136×130、中身は 14,0 から 114×113)
            if (MenuSkin.Part(Tn + "Difficulty_Select/Player_Cursor.png", new Rectangle(14, 0, 114, 113), new Rectangle(c.X - 57, c.Y - 56, 114, 113), MenuSkin.White(alpha))) return;
            if (MenuSkin.Whole(MenuSkin.SongFolder + "Marker_1P.png", new Rectangle(c.X - 56, c.Y - 56, 112, 118), MenuSkin.White(alpha))) return;
            SmoothShapes.Triangle(new Vector2(c.X - 14, c.Y + 34), new Vector2(c.X, c.Y + 58), new Vector2(c.X + 14, c.Y + 34), Fade(InkColor, alpha));
            SmoothShapes.Circle(c, 53, Fade(InkColor, alpha));
            SmoothShapes.Circle(c, 48, Fade(MarkerColor, alpha));
            SmoothShapes.Triangle(new Vector2(c.X - 10, c.Y + 36), new Vector2(c.X, c.Y + 52), new Vector2(c.X + 10, c.Y + 36), Fade(MarkerColor, alpha));
            Outlined("1P", new Rectangle(c.X - 44, c.Y - 30, 88, 60), 50, Fade(Color.White, alpha), Fade(InkColor, alpha));
        }

        // 下のまんなかの「えらぶ・決定」
        private void DrawDiffHint(float alpha)
        {
            if (!SkinManager.SystemSkin && smallControlGuide is Aup2 guide)
            {
                // Smallは原寸500×124。従来の案内と同じ中心へ置く（位置は実機で確認予定）。
                Vector2 at = new(DiffHintBox.X + (DiffHintBox.Width - guide.Width) / 2f,
                    DiffHintBox.Y + (DiffHintBox.Height - guide.Height) / 2f);
                guide.Draw(guide.LoopedFrameAt(Math.Max(0f, diffTime - PanelInStart)), at, alpha);
                // 「えらぶ」「決定」の字は Base.png にもう描いてあるので、プログラムでは書かない（11/12。書くと重なる）
                return;
            }
            if (MenuSkin.Whole(MenuSkin.SongFolder + "Hint.png", DiffHintBox, MenuSkin.White(alpha))) return;
            SmoothShapes.Rounded(DiffHintBox, 0.3f, 10, Fade(Color.White, alpha));
            Rectangle choose = new Rectangle(DiffHintBox.X + 18, DiffHintBox.Y + 18, 152, 50);
            Rectangle decide = new Rectangle(DiffHintBox.X + 190, DiffHintBox.Y + 18, 152, 50);
            SmoothShapes.Rounded(choose, 1f, 10, Fade(InkColor, alpha));
            SmoothShapes.Rounded(decide, 1f, 10, Fade(InkColor, alpha));
            SmoothShapes.Rounded(new Rectangle(choose.X + 3, choose.Y + 3, choose.Width - 6, choose.Height - 6), 1f, 10, Fade(Color.White, alpha));
            SmoothShapes.Rounded(new Rectangle(decide.X + 3, decide.Y + 3, decide.Width - 6, decide.Height - 6), 1f, 10, Fade(Color.White, alpha));
            Centered("カッ：えらぶ", choose, 26, Fade(InkColor, alpha));
            Centered("ドン：決定", decide, 26, Fade(InkColor, alpha));
        }

        // 検索の箱
        private void DrawSearchBox()
        {
            Gui.ResetClip();
            Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 170));
            // 11/12：設定と同じ色と形にそろえた(パネル・細いふち・桜色の小見出し)
            Rectangle searchPanel = new(360, 210, 1200, 610);
            SmoothShapes.Rectangle(searchPanel, 20, Gui.PanelColor);
            SmoothShapes.Border(searchPanel, 20, 1.5f, Gui.BorderColor);
            FontManager.Draw("曲を検索", 410, 250, 36, Gui.TextColor);
            if (Gui.Button(new(1460, 238, 50, 50), "×")) { OnBack(); return; }
            Gui.Glow(SearchBox, 10, Gui.AccentColor, 0.6f);
            SmoothShapes.Rectangle(SearchBox, 10, Gui.BackgroundColor);
            SmoothShapes.Border(SearchBox, 10, 1.5f, Gui.AccentColor);
            string composition = WindowsIme.CompositionText;
            if (!WindowsIme.IsComposing && Gui.Clicked(SearchBox))
            {
                searchInput.MoveToNearest(Screen.GetMousePosition().X - SearchTextX + searchScrollX,
                    text => Measure(text, SearchFontSize).X);
                caretTimer = 0f;
            }
            UpdateSearchScroll(composition);
            Screen.BeginScissorMode((int)SearchBox.X + 12, (int)SearchBox.Y + 4, (int)SearchBox.Width - 24, (int)SearchBox.Height - 8);
            float x = SearchTextX - searchScrollX;
            if (searchText.Length == 0 && composition.Length == 0)
                FontManager.Draw("曲名・アーティスト・フォルダ名", x, SearchTextY, 28, Gui.MutedTextColor);
            string before = searchText[..searchInput.Caret], after = searchText[searchInput.Caret..];
            Text(before, x, SearchTextY, SearchFontSize, Color.White);
            float insertX = x + Measure(before, SearchFontSize).X;
            if (composition.Length > 0)
            {
                Text(composition, insertX, SearchTextY, SearchFontSize, CompositionColor);
            }
            Text(after, insertX + Measure(composition, SearchFontSize).X, SearchTextY, SearchFontSize, Color.White);
            if ((caretTimer % 1f) < .5f)
                SmoothShapes.Rectangle(new Rectangle(x + SearchCaretWidth(composition), SearchTextY + 2, 2, SearchFontSize), 0f, Color.White);
            Screen.EndScissorMode();
            FontManager.Draw("難易度", 410, 435, 24, Gui.SakuraColor);
            FontManager.Draw("★", 785, 435, 24, Gui.SakuraColor);
            FontManager.Draw("ジャンル", 1160, 435, 24, Gui.SakuraColor);
            int course = Gui.Dropdown("search/course", new(410, 475, 350, 56), SearchCourses, searchCourse == null ? 0 : (int)searchCourse + 1, 8);
            searchCourse = course == 0 ? null : (CourseType)(course - 1);
            int star = Gui.Dropdown("search/stars", new(785, 475, 350, 56), searchLevelLabels,
                searchStars == null ? 0 : Math.Max(0, Array.IndexOf(searchLevels, searchStars.Value) + 1), 8);
            searchStars = star == 0 ? null : searchLevels[star - 1];
            int genre = Gui.Dropdown("search/genre", new(1160, 475, 350, 56), searchGenres,
                searchGenre == null ? 0 : Math.Max(0, Array.IndexOf(searchGenres, searchGenre)), 8);
            searchGenre = genre == 0 ? null : searchGenres[genre];
            int count = SearchResults().Count;
            FontManager.Draw($"一致する曲：{count}曲", 410, 600, 28, count == 0 ? Gui.SubTextColor : Gui.TextColor);
            FontManager.Draw("指定しない項目は「すべて」のままで検索できます", 410, 650, 24, Gui.SubTextColor);
            if (Gui.Button(new(410, 725, 200, 56), "条件をクリア"))
            { searchText = ""; searchInput.Reset(""); searchScrollX = caretTimer = 0f; searchCourse = null; searchStars = null; searchGenre = null; }
            if (Gui.Button(new(1060, 725, 200, 56), "戻る")) OnBack();
            if (Gui.PrimaryButton(new(1280, 725, 230, 56), "検索する")) SubmitSearch();
            Gui.DrawPopups();
            if (searchSubmitQueued) { searchSubmitQueued = false; SubmitSearch(); }
        }

        private void DrawToast()
        {
            Gui.Notice(toast);
        }

        // 曲を読み込み中(「読み込んだ数 / 曲ファイルの総数」とバー)
        private static void DrawScanning()
        {
            int total = SongList.ScanTotal;
            int done = Math.Min(SongList.ScanDone, Math.Max(total, 0));
            string text = total < 0 ? "曲ファイルを探しています…" : $"曲を読み込み中…　{done} / {total}";
            Text(text, 160, 420, 40, Color.White);

            float progress = total > 0 ? (float)done / total : 0f;
            Rectangle bar = new Rectangle(160, 490, 1600, 20);
            Raylib.DrawRectangleRec(bar, new Color(30, 34, 48, 255));
            Raylib.DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * progress, bar.Height), CursorColor);
        }

        // 曲が1つも無いとき
        private static void DrawEmpty()
        {
            Text("曲がありません", 160, 360, 40, ErrorColor);
            Text("下のフォルダに、曲のフォルダ（.tja と .ogg が入ったもの）を入れてください。", 160, 430, 28, Color.White);
            // フォルダのパスは薄い灰色だと読めないので、うすい黄色にする
            Text(SongList.SongsFolder, 160, 480, 24, new Color(255, 236, 160, 255));
            Text("カテゴリ分けしたいときは、box.def の入ったフォルダの中に入れてください。", 160, 530, 28, Color.White);
            Text("入れたら、設定（F1）の「一般設定」→［曲の一覧を更新］を押してください。", 160, 580, 28, Color.White);
        }
    }
}



