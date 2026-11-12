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
        public override Platform.DiscordPresence.Info? Presence => new("曲を選んでいます");

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
        public SongSelectScene(bool training = false) => this.training = training;

        // ===== 一覧の項目 =====

        private enum Special { None, Recent, Added, Favorites, Search }
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
        private static string searchText = "";
        private static List<SongCategory>? builtFor;   // 今の一覧を作ったときのカテゴリ(読み直したら作り直す)

        private readonly List<Item> genres = new();
        private readonly List<Item> songs = new();     // 曲をえらんでいるときの一覧(ジャンルの一覧の、開いたジャンルの場所に曲を入れたもの)
        private int songStart;                         // songs の中で、開いたジャンルの曲が始まる場所(最初の「とじる」)
        private int ownCount;                          // 開いたジャンルの項目の数(とじる・曲・ランダム)
        private readonly BarList list = new(MenuSkin.SongFolder);         // 斜めの板の動き
        private Action? afterBlink;                    // 決定の点滅が終わったらすること(ジャンルを開く・とじる)

        // ===== ジャンルを決めたあとの「ポンッ → めくれる」 =====
        //   決めた瞬間を 0 として：0〜0.53秒 枠が点滅 → 0.63秒まで白 → ここから下の時間(FlipStart からの秒)
        //   10/30 の TaikøNauts の動画(ボーカロイドを決めたところ)を 1/60秒ずつ測り直した
        //   (10/29 の A.mp4 は、広がる途中で 0.13秒止まってからパッと戻っていた。曲の一覧を作る重さで止まっていたみたい)
        //   0〜0.067   板がポンッと大きくなる(横 1.21倍・縦 1.14倍)。枠は消える
        //   0〜0.12    ジャンル名・キャラ・吹き出しが消えていく(中身の無い板だけが残る)
        //   0.11〜0.22 上の板は上へ、下の板は下へ 150px すべりながら消える
        //   0.26〜0.41 板が横につぶれていく(幅 933 → 794 → 556 → 355 → 204 → 0px。はじめが速い)
        //   0.41〜0.60 細い線のまま
        //   0.60〜0.77 元の幅まで広がる(161 → 363 → 654 → 933px。ほぼ一定の速さ。中身は空っぽ)
        //   0.77      中身が最初の曲の板(開いた形)になる
        //   0.77〜0.90 曲の板の中身と、上下の板がじわっと出る。黄色い枠は 0.77 から 0.33秒あとに出る
        private const float FlipStart = 0.63f;
        private const float FlipPopEnd = 0.067f, FlipPopScaleX = 1.21f, FlipPopScaleY = 1.14f;
        private const float FlipFadeEnd = 0.12f;
        private const float FlipOthersStart = 0.11f, FlipOthersEnd = 0.22f, FlipOthersShift = 150f;
        private const float FlipSquashStart = 0.26f, FlipSquashEnd = 0.41f, FlipThinEnd = 0.60f, FlipSwitch = 0.77f, FlipEnd = 0.90f;
        private const float FlipThinScale = 5f / 933f;
        private float flipTime = -1f;      // めくれ始めてからの時間(-1 ならめくっていない)
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
        public override bool MenuBgmMuted => preview.HasPlaybackRequest || leavingForDifficulty || SongLoading.IsActive;
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
            if (CharaBaker.Config is CharaConfig cc) chara.Loop("songselect_loop", cc.SongSelectLoopTime);
            MenuVoice.Loop(SelectStartVoice);
            selectPrompt = true;
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
            genres.Add(new Item { Kind = Kind.Genre, Special = Special.Added, Title = "最近追加された曲", Color = new Color(60, 140, 150, 255), SongCount = Added().Count });
            genres.Add(new Item { Kind = Kind.Genre, Special = Special.Favorites, Title = "お気に入り", Color = new Color(200, 110, 150, 255), SongCount = Favorites().Count });
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
            int count = list.Count;
            for (int index = 0; index < count / BackInterval + 1; index++)
            {
                own.Insert(Math.Min(index * (BackInterval + 1), own.Count), new Item { Kind = Kind.Back, Title = "とじる", Color = BackFillColor });
            }
            if (count > 0) own.Add(new Item { Kind = Kind.Random, Title = "ランダムにえらぶ", Color = RandomColor });

            // 開いたジャンルの板の代わりに入れる
            songs.Clear();
            int genreIndex = OpenGenreIndex();
            if (genreIndex >= 0) songs.AddRange(genres.Take(genreIndex));
            songStart = songs.Count;
            ownCount = own.Count;
            songs.AddRange(own);
            if (genreIndex >= 0) songs.AddRange(genres.Skip(genreIndex + 1));
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
            if (index < 0) index = songStart + (ownCount > 1 ? 1 : 0);
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

            // 読み込みが終わったら一覧を作り直す
            bool scanning = SongList.IsScanning;
            if (wasScanning && !scanning) Rebuild(keepPlace: false);
            else if (!scanning && !ReferenceEquals(SongList.Categories, builtFor)) Rebuild(keepPlace: false);
            wasScanning = scanning;

            list.Update(dt, CurrentPos);
            UpdateTnBackground(dt);
            uraClock = list.Expand > 0f ? uraClock + dt : 0f;
            if (diffOpen) diffTime += dt;

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

            // 曲と難易度ではプレビュー。「戻る」を選んでいる間は止め、選曲BGMへ戻す。
            string? previewTja = diffOpen ? (diffCursor == 0 ? null : diffSong?.TjaPath)
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
                        else Move(action.Direction, SkipCount);
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
        private void Move(int direction, int steps = 1)
        {
            List<Item> list = CurrentList;
            if (list.Count == 0) return;
            int pos = CurrentPos;
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
                    MenuSounds.Don();
                    if (selectPrompt) { MenuVoice.StopLoop(); selectPrompt = false; }
                    MenuVoice.CancelPending();
                    list.StartDecide();
                    // 点滅 → 白 → ポンッ → めくれる → 曲の一覧(Update の flipTime で進める)
                    flipFromBack = false;
                    flipOpen = () =>
                    {
                        // 曲をえらんでいるときに、ほかのジャンルを決定したら、そのジャンルに開き直す
                        if (inSongs) genrePos = Math.Max(0, genres.IndexOf(item));
                        OpenGenre(item.Special, item.Cat);
                    };
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

        private void OpenGenre(Special special, int cat)
        {
            openSpecial = special;
            openCat = cat;
            inSongs = true;
            BuildSongs();
            songPos = songStart + (ownCount > 1 ? 1 : 0); // 最初の「とじる」の次(最初の曲)
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
        }

        // 難易度の板を閉じて、曲の一覧へ(曲は流したまま)
        private void CloseDifficulty()
        {
            // 11/8 の TaikøNauts の録画：難易度の板は一瞬で消えて、選んでいた曲の板は閉じた形にもどる。
            //   そこから 0.43秒で黄色い枠、0.45秒で開く（BarList.CollapseAfterDifficulty）。フェードはしない
            MenuSounds.Cancel();
            options.CloseNow();
            diffOpen = false;
            diffSong = null;
            list.EndDecide();
            list.CollapseAfterDifficulty();
        }

        private void UpdateDifficulty()
        {
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
            ura = !ura;
            uraCount = 0;
            MenuSounds.UraFlip();
        }

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
            if (!Gui.IsPopupOpen) WindowsIme.RequestTextInput(SearchBox,
                new Vector2(SearchTextX + Measure(searchText, SearchFontSize).X, SearchTextY));

            int c = Raylib.GetCharPressed();
            while (c > 0)
            {
                AddSearchChar(c);
                c = Raylib.GetCharPressed();
            }
            string imeResult = WindowsIme.TakeResultText();
            foreach (Rune rune in imeResult.EnumerateRunes()) AddSearchChar(rune.Value);

            if (!WindowsIme.IsComposing && imeResult.Length == 0 && KeyPressed(KeyboardKey.Backspace) && searchText.Length > 0)
            {
                int remove = searchText.Length >= 2 && char.IsLowSurrogate(searchText[^1]) ? 2 : 1;
                searchText = searchText[..^remove];
            }

            // Enter：検索の結果を開く
            if (imeResult.Length == 0 && !WindowsIme.IsComposing && !Gui.IsPopupOpen && (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)))
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

        private static void AddSearchChar(int c)
        {
            bool usable = c >= 32 && c <= 0x10FFFF && (c < 0xD800 || c > 0xDFFF);
            if (usable && searchText.Length < SearchMaxLength) searchText += char.ConvertFromUtf32(c);
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
            preloadedCategories = categories;
            preloadedFont = FontManager.SkinMainGeneration;

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
            FontManager.PreloadDetail(Texts());   // 難易度をえらぶ画面の曲名・サブタイトルは細かい字のフォント
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

        private static int BarType(Item item)
        {
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
        private Rectangle DrawBarBody(Item item, Rectangle area, float alpha, bool plain, Rectangle? plate = null)
        {
            int type = BarType(item);
            string? path = item.Kind == Kind.Back ? Tn + "Bar/Back.png" : type >= 0 ? Tn + $"Bar/{type}.png" : null;
            if (path != null && SkinManager.GetTexture(path) != null)
            {
                Rectangle dest = new Rectangle(area.X, area.Y - 1, area.Width + 6, area.Height + 9);
                MenuSkin.NPatch(path, dest, 40, MenuSkin.White(alpha));
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
            // どんちゃん（背景の上・板より奥）
            if (CharaBaker.Config is CharaConfig sc) chara.DrawPlayers(sc.SongSelectX, sc.SongSelectY, sc.SongSelectScale, LocalSession.Players);

            if (SongList.IsScanning) DrawScanning();
            else if (SongList.Categories.Count == 0) DrawEmpty();
            else
            {
                float listAlpha = diffOpen ? 1f - Math.Clamp((diffTime - ListFadeStart) / (ListFadeEnd - ListFadeStart), 0f, 1f) : 1f;
                if (listAlpha > 0f) DrawList(listAlpha * ReturnAlpha);
            }

            if (diffOpen) DrawDifficulty();
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
                    // 「とじる」：細い線から広がるところは、戻り先のジャンルの色の空の板（11/8 の TaikøNauts の録画では J-POP の水色だった）
                    Item flipItem = flipFromBack && !flipOpened && flipTime >= FlipThinEnd && GenreItemOpen is Item back ? back : selectedItem;
                    DrawBar(flipItem, box, list.Expand, alpha, framed: !flipOpened || list.FrameShown, contentAlpha: flip.Content);
                    if (selectedItem.Kind == Kind.Genre && !flipOpened) DrawGenrePop(selectedItem, sel, alpha * flip.Content, force: true);
                    return;
                }
                if (!(diffOpen && diffTime >= FrameHideAt)) list.DrawFrame(sel, alpha); // 曲を決めたときは、白くなった枠が 0.65秒で消える
                DrawBar(selectedItem, sel, list.Expand, alpha, list.FrameShown);
                if (selectedItem.Kind == Kind.Genre) DrawGenrePop(selectedItem, sel, alpha);
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
                float others = 1f - o, shift = FlipOthersShift * o * o; // だんだん速くすべっていく
                float sx = 1f, sy = 1f;
                if (t < FlipPopEnd) { sx = FlipPopScaleX; sy = FlipPopScaleY; }
                else if (t >= FlipSquashStart && t < FlipSquashEnd)
                {
                    // はじめが速く、だんだんゆっくり細くなる(測った幅：85% → 60% → 38% → 22% → 0%)
                    float p = (t - FlipSquashStart) / (FlipSquashEnd - FlipSquashStart);
                    sx = Math.Max(FlipThinScale, MathF.Pow(1f - p, 1.3f));
                }
                else if (t >= FlipSquashEnd && t < FlipThinEnd) sx = FlipThinScale;
                else if (t >= FlipThinEnd)
                {
                    // ほぼ一定の速さで元の幅まで広がる(17% → 39% → 70% → 100%)
                    float p = (t - FlipThinEnd) / (FlipSwitch - FlipThinEnd);
                    sx = FlipThinScale + (1f - FlipThinScale) * p;
                }
                return (true, sx, sy, others, fade, shift);
            }
            float appear = Math.Clamp((t - FlipSwitch) / (FlipEnd - FlipSwitch), 0f, 1f);
            return (true, 1f, 1f, appear, appear, 0f);
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
            Color ink = Raylib.ColorBrightness(item.Color, -0.65f);
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
                    Outlined(item.Title, titleArea, 46, Fade(Color.White, alpha), Fade(ink, alpha));
                    break;

                case Kind.Back:
                    Outlined(item.Title, inner, open > 0f ? 56 : 46, Fade(Color.White, alpha), Fade(BackBoxColor, alpha));
                    break;

                case Kind.Random:
                    Outlined(item.Title, inner, open > 0f ? 56 : 46, Fade(Color.White, alpha), Fade(ink, alpha));
                    break;

                case Kind.Song:
                    DrawSongBar(item, inner, open, ink, alpha);
                    break;
            }
        }

        private void DrawSongBar(Item item, Rectangle inner, float open, Color ink, float alpha)
        {
            SongEntry song = item.Song!;
            bool favorite = SongHistory.IsFavorite(song.Key);
            Color titleColor = song.HasError ? ErrorColor : Color.White;

            if (open <= 0f)
            {
                Outlined(song.Title, new Rectangle(inner.X + 30, inner.Y, inner.Width - 60, inner.Height), 46, Fade(titleColor, alpha), Fade(ink, alpha));
                if (favorite) Text("♥", inner.X + inner.Width - 36, inner.Y + inner.Height / 2f - 14, 26, Fade(new Color(230, 80, 130, 255), alpha));
                return;
            }

            float a = alpha * open; // 開くあいだに、だんだん出す

            // 上：お気に入りの印
            //   11/12 たけらい「検索結果の板の左上のジャンルはいらない。板の色で分かるから。最近遊んだ曲もそう」
            //   前は、検索・最近追加・お気に入りの一覧で、左上にジャンル名を書いていた
            if (favorite) Text("♥ お気に入り", inner.X + inner.Width - 150, inner.Y + 8, 20, Fade(new Color(230, 80, 130, 255), a));

            // 曲名・サブタイトル(TaikøNauts と同じく板の上の方)
            Outlined(song.Title, new Rectangle(inner.X + 20, inner.Y + 32, inner.Width - 40, 66), 48, Fade(titleColor, alpha), Fade(ink, alpha));
            string sub = song.HasError ? song.Error : song.Subtitle;
            if (sub.Length > 0)
                OutlinedDetail(sub, new Rectangle(inner.X + 20, inner.Y + 96, inner.Width - 40, 34), 24, Fade(song.HasError ? ErrorColor : Color.White, a), Fade(Color.Black, a), 3f);

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
            Rectangle right = new Rectangle(chip.X + 92, chip.Y + 6, chip.Width - 98, chip.Height - 12);
            // TaikøNauts のスキン：Difficulty_Panel.png(横に5つ。1つ 176×86、182px ごと。★まで描いてある)
            //   数字は Difficulty_Level_Number.png(0〜9。1文字 31×37)で、★のすぐ右に書く(TaikøNauts の動画で測った場所)
            if (MenuSkin.Part(Tn + "Difficulty_Panel.png", new Rectangle(3 + 182 * (int)type, 2, 176, 86), chip, MenuSkin.White(alpha)))
            {
                float k = chip.Height / 86f;
                if (!MenuSkin.Digits(Tn + "Difficulty_Level_Number.png", level.ToString(), chip.X + ChipNumberX * k, chip.Y + ChipNumberY * k, ChipNumberAdvance, k, MenuSkin.White(alpha)))
                    Outlined(level.ToString(), new Rectangle(chip.X + 128, chip.Y + 20, 44, 46), 36, Fade(Color.White, alpha), Fade(InkColor, alpha));
                return;
            }
            // 画像が無いとき(システムのスキンなど)はプログラムで描く
            Raylib.DrawRectangleRec(chip, Fade(color, alpha));
            SmoothShapes.Rounded(right, 0.2f, 6, Fade(Raylib.ColorBrightness(color, -0.25f), alpha));
            Outlined(CourseMark(type), new Rectangle(chip.X + 6, chip.Y + 4, 82, 50), 40, Fade(Color.White, alpha), ink);
            Outlined(type == CourseType.Edit ? "裏おに" : Chart.GetCourseName(type), new Rectangle(chip.X + 4, chip.Y + 56, 86, 26), 20, Fade(Color.White, alpha), ink);
            Outlined($"★{level}", right, 36, Fade(Color.White, alpha), ink);
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

        // 難易度の印(絵の代わりの1文字)
        private static string CourseMark(CourseType type) => type switch
        {
            CourseType.Easy => "易",
            CourseType.Normal => "普",
            CourseType.Hard => "難",
            CourseType.Oni => "鬼",
            CourseType.Edit => "裏",
            _ => "",
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
            // 曲名・サブタイトル：細かい字のフォントに真っ黒の縁（11/13 たけらい「作曲者の文字は真っ黒でいい」。TaikøNauts の録画と同じ）
            OutlinedDetail(diffSong.Title, new Rectangle(titleBox.X + 30, titleBox.Y + 55 * scale, titleBox.Width - 60, 80 * scale), 64 * scale,
                Fade(Color.White, alpha), Fade(Color.Black, alpha), 6f * scale);
            if (diffSong.Subtitle.Length > 0)
                OutlinedDetail(diffSong.Subtitle, new Rectangle(titleBox.X + 30, titleBox.Y + 135 * scale, titleBox.Width - 60, 40 * scale), 32 * scale,
                    Fade(Color.White, alpha), Fade(Color.Black, alpha), 4f * scale);

            // 箱：戻る・オプション・かんたん〜おに
            bool ready = returnTime < 0f && DiffReady && active && !options.IsOpen;
            for (int i = 0; i < 6; i++)
            {
                Rectangle box = S(DiffBoxArea(i));
                bool selected = i == diffCursor;
                if (selected) DrawDiffCursor(box, alpha, diffDecideTime);
                DrawDiffBox(i, box, alpha, scale);
                if (i >= 2 && HasCourse(CourseAt(i))) SongRecordPanel.DrawCard(diffSong.Key, CourseAt(i), box, alpha);
                if (ready && Gui.Clicked(box))
                {
                    if (i == diffCursor) DecideDifficulty();
                    else { diffCursor = i; markerBounce = 0f; MenuSounds.Ka(); }
                }
            }

            // 1P の印(カーソルの上)
            if (diffTime >= MarkerAt)
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
            Outlined(CourseMark(type), new Rectangle(mark.X - 60 * scale, mark.Y - 50 * scale, 120 * scale, 100 * scale), 84 * scale, Fade(color, alpha), ink);

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
            Outlined($"★ {level}", new Rectangle(body.X, box.Y + 240 * scale, body.Width, 44 * scale), 36 * scale, Fade(Color.White, alpha), ink);

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
            Screen.BeginScissorMode((int)SearchBox.X + 12, (int)SearchBox.Y + 4, (int)SearchBox.Width - 24, (int)SearchBox.Height - 8);
            float width = Measure(searchText + composition, SearchFontSize).X;
            float x = SearchTextX - Math.Max(0, width - SearchBox.Width + 60);
            if (searchText.Length == 0 && composition.Length == 0)
                FontManager.Draw("曲名・アーティスト・フォルダ名", x, SearchTextY, 28, Gui.MutedTextColor);
            Text(searchText, x, SearchTextY, SearchFontSize, Color.White);
            float endX = x + Measure(searchText, SearchFontSize).X;
            if (composition.Length > 0)
            {
                Text(composition, endX, SearchTextY, SearchFontSize, CompositionColor);
                int cursor = Math.Clamp(WindowsIme.CompositionCursor, 0, composition.Length);
                endX += Measure(composition[..cursor], SearchFontSize).X;
            }
            if ((caretTimer % 1f) < .5f) Text("|", endX, SearchTextY, SearchFontSize, Color.White);
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
            { searchText = ""; searchCourse = null; searchStars = null; searchGenre = null; }
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


