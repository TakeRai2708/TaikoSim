using TakeTaikoSimulator.Charts;
using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;
using static TakeTaikoSimulator.UI.MenuDraw;

namespace TakeTaikoSimulator.Scenes
{
    // モード選択(起動したら最初に出る画面。前のホーム画面のかわり)
    //   演奏ゲーム → 選曲画面
    //   段位道場 → 段位選択画面／特訓モード → 共通の選曲から専用の演奏画面
    //   設定 → 設定パネル、クレジット → クレジット画面
    // 見た目と動きは選曲画面と同じ(UI/BarList。TaikøNauts の選曲画面を測った値)
    //   ・板は斜めに並ぶ。先頭と末尾で止まる
    //   ・カーソルは0.15秒で1つ動く。止まってから0.43秒で選んでいる板が開く
    //   ・決定すると、黄色い枠が点滅してから次の画面へ
    // 操作：カッ・↑↓・ホイールで選ぶ、ドン・Enter・クリックで決定、Esc を2回で終了
    public class ModeSelectScene : Scene
    {
        public override string? MenuBgmFile => MenuBgm.EntryFile;

        private sealed record Item(string Title, string Description, Color Color, bool Ready);

        private static readonly Item[] Items =
        {
            new("演奏ゲーム", "すきな曲・むずかしさをえらんであそぶ", new Color(225, 70, 80, 255), true),
            // 説明は TaikøNauts と同じ3行（10/31 の動画 2026-10-31_01-44-20.mp4 の16秒あたり）
            new("段位道場", "3曲連続して演奏しながら、\nお題の合格を目指そう!\n合格すると腕前が認定されるよ!", new Color(70, 90, 170, 255), true),
            new("特訓モード", "小節をえらんで、ゆっくり練習する", new Color(60, 150, 110, 255), true),
            new("着せ替え", "どんちゃん・ぷちキャラ・ネームプレートをえらぶ", new Color(135, 90, 170, 255), true),
            new("設定", "音・キー・画面・スキンなどの設定", new Color(90, 100, 125, 255), true),
            new("クレジット", "使っているソフト・フォント・ライセンス", new Color(60, 130, 150, 255), true),
        };

        // 選んでいる項目(ほかの画面から戻ってきても同じ場所)。先頭・末尾を越えない
        private static int cursor = 0;
        private readonly BarList list = new(MenuSkin.ModeFolder);
        private Item? deciding;                // 決定して、枠が点滅している項目
        private const string EntryFolder = "Image/01.Entry/";
        private const string EntryBarFolder = EntryFolder + "Bar/";
        private const string EntryBackground = EntryFolder + "Background.png";
        private float backgroundTime;

        // お知らせ
        private string toast = "";
        private float toastTimer = 0f;

        // 段位道場のカテゴリ選び（dan.def ごと）。TaikøNauts と同じく、モード選択で段位道場を決めると
        //   板の上に吹き出し「◀ 2026段位道場 ▶」が出て、←→で選んでドンで決める（10/31 の動画 01-44-20 の17〜23秒）。
        //   吹き出しは A-Style の 01.Entry/DanCategorySelect/Popup.png（870×122）を原寸で (525, 258)。
        //   動画では吹き出しの外は暗くなる。矢印は Cursor.png（50×50、右は左右反転）
        private const string CategoryPopup = EntryFolder + "DanCategorySelect/Popup.png";
        private const string CategoryCursor = EntryFolder + "DanCategorySelect/Cursor.png";
        private static readonly Rectangle PopupArea = new(525f, 258f, 870f, 122f);
        private bool choosingCategory;
        // 2人目の途中参加（データ選択 → 通信中 → 完了）。まだ使っていない方のキーのドンで開く
        private readonly PlayerJoin join = new();
        public override bool IsWaitingKey => join.Naming;
        public override bool CanOpenSettings => !join.Naming && !join.Busy;
        private int categoryChoice;
        private float popupAge;

        // どんちゃんの声（11/9 の TaikøNauts の録画 2026-11-09 02-11-13.mp4・02-23-14.mp4 で確かめた）
        //   ・画面に入る → 「モードを選んでね」（SelectMode）→ 鳴り終わったら、カーソルの項目の声をくり返す
        //     （「演奏ゲーム」は 2.31秒ごと、「段位道場」は 4.35秒ごとに、何もしなければずっと）
        //   ・カーソルを動かす → 板が開いてからその項目の声をくり返す（声の無い項目なら止める）
        //   ・決める → くり返しをやめて「ModeSelected」（段位道場はカテゴリを決めたとき）
        //   前は段位道場の声を「決めたとき」に鳴らしていたけど、本家はカーソルが乗っている間の声だった
        private const string SelectModeVoice = "Sound/Entry/SelectMode.ogg";
        private const string ModeSelectedVoice = "Sound/Entry/ModeSelected.ogg";

        private static string? VoiceOf(Item item) => item.Title switch
        {
            "演奏ゲーム" => "Sound/Entry/EnsoGame.ogg",
            "段位道場" => "Sound/Entry/DaniDojo.ogg",
            _ => null,
        };

        private bool itemVoicePending;

        private static void LoopItemVoice()
        {
            if (VoiceOf(Current) is string voice) MenuVoice.Loop(voice);
            else MenuVoice.Stop();
        }

        private static Item Current => Items[cursor];

        private readonly MenuChara chara = new();   // 左下のどんちゃん（A-Style だけ。MenuChara.cs）

        // ===== エントリーから入ってきたときの出かた =====
        //   11/11 の録画（2026-11-11 23-40-44.mp4 の 105.9秒〜）を 1/60秒ずつ見た。エントリーの板が消えきった時を 0 として：
        //     0.07     選んでいる板（閉じた形）とネームプレートが出る
        //     0.33〜0.48 板が開く（BarList のいつもの動き。ここでは 0.43〜0.53秒になる）
        //     0.33〜0.53 どんちゃんが左からすべって入ってくる
        //     0.47〜0.62 左上の太鼓の案内が、じわっと出る
        //     0.55〜0.67 ほかの板が、上と下の外からすべって入ってくる
        //   その間は操作できない。暗い幕（エントリーの板のうしろ）は、最初の 0.2秒で消える
        //   本家は板がじわっと出る（0.07〜0.2秒）けど、ここではパッと出している
        private const float IntroBarAt = 0.07f, IntroCharaStart = 0.33f, IntroCharaEnd = 0.53f, IntroCharaShift = 400f;
        private const float IntroGuideStart = 0.47f, IntroGuideEnd = 0.62f;
        private const float IntroOthersStart = 0.55f, IntroOthersEnd = 0.67f, IntroOthersShift = 500f, IntroEnd = 0.70f;
        private const float IntroDimSeconds = 0.2f;
        private float introTime = -1f;   // 入ってきてからの秒（-1 なら、ふつうに全部出す）

        // 左上の太鼓の案内（選曲画面と同じ Control_Guide/Large。11/11 の録画でモード選択にも出ていた）
        private Aup2? controlGuide;
        private float guideClock;

        public ModeSelectScene(bool intro = false)
        {
            introTime = intro ? 0f : -1f;
        }

        public override void Enter()
        {
            NamePlate.Preload();
            chara.Load();
            CharaConfig? cc = CharaBaker.Config;
            CharaLoop();
            cursor = Math.Clamp(cursor, 0, Items.Length - 1);
            list.Reset(cursor);
            deciding = null;
            backgroundTime = 0f;
            guideClock = 0f;
            controlGuide?.Unload();
            controlGuide = null;
            if (SkinManager.CurrentPath is string skinPath && !SkinManager.SystemSkin)
            {
                controlGuide = Aup2.Load(Path.Combine(skinPath, "Image", "99.Common", "Control_Guide", "Large", "Anime.aup2"));
                if (controlGuide != null)
                {
                    controlGuide.Preload();
                    if (!controlGuide.HasAllImages) { controlGuide.Unload(); controlGuide = null; }
                }
            }
            MenuSkin.Preload(MenuSkin.ModeFolder, MenuSkin.BarImages.Concat(new[] { "Background.png", "Background/0.png" }));
            MenuSkin.Preload(EntryFolder, new[] { "Background.png", "Header.png", "Background_Fireworks.png" });
            MenuSkin.Preload(EntryBarFolder, new[] { "Bar/0.png", "Bar/1.png", "Title/0.png", "Title/1.png",
                "Overlay.png", "Selected.png", "Pop/0.png", "1POnly.png" });
            choosingCategory = false;
            if (VoiceOf(Current) is string itemVoice) MenuVoice.PlayThenLoop(SelectModeVoice, itemVoice);
            else MenuVoice.Play(SelectModeVoice);
            itemVoicePending = false;
            // 段位のカテゴリを、決める前に読んでおく（裏で読むので止まらない）
            if (!DanList.IsScanned && !DanList.IsScanning) DanList.StartScan();
            // 「はじめの設定」から戻ってきた：途中参加の通信中から続ける
            if (PlayerJoin.Pending is PlayerJoin.ResumeInfo pending) join.Resume(pending);
        }

        public override void Exit()
        {
            chara.Dispose();
            controlGuide?.Unload();
            controlGuide = null;
            MenuVoice.CancelPending();
            MenuVoice.StopLoop();
            join.Dispose();
        }

        // Escでエントリーへ戻る。この戻り方では効果音を鳴らさない。
        public override void OnBack()
        {
            if (join.Active) { join.Back(); return; }
            if (choosingCategory)
            {
                choosingCategory = false;
                LoopItemVoice();
                return;
            }
            if (deciding != null)
            {
                deciding = null;
                list.EndDecide();
                LoopItemVoice();
                CharaBackToLoop();
                return;
            }
            SceneManager.Simple(new EntryScene());
        }

        public override void Update(float dt)
        {
            backgroundTime = (backgroundTime + dt) % 3.2f;
            chara.Update(dt);
            if (toastTimer > 0f) toastTimer -= dt;
            list.Update(dt, cursor);
            guideClock += dt;
            if (introTime >= 0f)
            {
                // 入ってくる動きの間は、何もできない
                introTime += dt;
                if (introTime >= IntroEnd) introTime = -1f;
                else return;
            }
            // 途中参加の板が出ている間は、そちらだけ動かす
            if (join.Active)
            {
                PlayerJoin.Result result = join.Update(dt);
                if (result == PlayerJoin.Result.Finished) LoopItemVoice();   // 「完了」の声で止まった、項目の声を戻す
                else if (result == PlayerJoin.Result.Setup)
                {
                    // 名前を入れた：「はじめの設定」へ行って、終わったらこの画面に戻って通信中から続ける（11/13 たけらい ②B）
                    PlayerJoin.ResumeInfo info = join.MakeResume();
                    PlayerJoin.Pending = info;
                    SceneManager.Simple(new SetupScene(info.Slot, () => new ModeSelectScene()));
                }
                return;
            }
            if (itemVoicePending && list.Expand > 0 && deciding == null && !choosingCategory)
            {
                itemVoicePending = false;
                LoopItemVoice();
            }
            DanList.Update();
            if (choosingCategory)
            {
                UpdateCategory(dt);
                return;
            }

            // 決定したあとの点滅中は、何もできない。点滅が終わったら次の画面へ
            if (deciding != null)
            {
                // 演奏ゲームは板を消して背景だけを見せてから暗転する。
                // お手本36.5〜37秒付近の背景だけの区間。待ち時間0.4秒は仮調整。
                float backgroundWait = deciding.Title == "演奏ゲーム" ? 0.4f : 0f;
                if (list.DecideTime < BarList.BlinkEnd + backgroundWait) return;
                Item item = deciding;
                hideDecidedList = item.Title == "演奏ゲーム";
                deciding = null;
                list.EndDecide();
                Open(item);
                CharaBackToLoop();   // 画面が替わらなかったとき（設定など）のため
                return;
            }

            // 2人目の途中参加：まだ使っていない方のキーのドン（11/12 たけらい「モード選択の時に2Pキーを押すとエントリーできて…」）
            if (PlayerJoin.NewPlayerPressed)
            {
                join.OpenForNewPlayer();
                return;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.F5))
            {
                SceneManager.Simple(new EntryScene(selectData: true));
                return;
            }
            // ← カッ(左縁)・↑ で上、カッ(右縁)・↓ で下
            int move = 0;
            if (InputManager.IsDrumPressed(DrumPart.LeftKa) || KeyPressed(KeyboardKey.Up)) move--;
            if (InputManager.IsDrumPressed(DrumPart.RightKa) || KeyPressed(KeyboardKey.Down)) move++;
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel > 0) move--;
            else if (wheel < 0) move++;
            if (move != 0) Move(move);

            if (InputManager.IsDonPressed() || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
                Decide();
        }

        private void Move(int direction)
        {
            int next = Math.Clamp(cursor + Math.Sign(direction), 0, Items.Length - 1);
            if (next == cursor) return;
            cursor = next;
            list.Collapse();
            MenuSounds.Ka();
            MenuVoice.Stop();
            itemVoicePending = true;
        }

        private void Decide()
        {
            Item item = Current;
            if (LocalSession.Count == 2 && item.Title is "段位道場" or "特訓モード")
            {
                ShowToast("このモードは1人プレイ専用です");
                MenuSounds.Ka();
                return;
            }
            if (!item.Ready)
            {
                MenuSounds.Ka();
                ShowToast($"{item.Title}は準備中です");
                return;
            }
            MenuSounds.Don();

            // 段位道場：段位のカテゴリが読めていれば、先にカテゴリを選ぶ
            if (item.Title == "段位道場" && DanList.IsScanned && DanList.Categories.Count > 0)
            {
                choosingCategory = true;
                categoryChoice = Math.Clamp(DanSelectScene.CategoryIndex, 0, DanList.Categories.Count - 1);
                popupAge = 0f;
                MenuVoice.StopLoop();   // 本家：カテゴリの吹き出しが出たら、段位道場の声のくり返しが止まった
                return;
            }
            StartDecide(item);
        }

        private void StartDecide(Item item)
        {
            hideDecidedList = false;
            deciding = item;
            list.StartDecide();
            MenuVoice.Play(ModeSelectedVoice);
            // どんちゃんがジャンプして止まる（選曲で難易度を決めたときと同じ動き）
            //   11/12 たけらい「モード選択時も選択したらどんちゃんが難易度選んだ時みたいに動いてほしい」
            if (chara.Config is CharaConfig cc && chara.HasAnim(CharaBaker.SongStartAnim))
                chara.PlayHold(CharaBaker.SongStartAnim, cc.SongSelectSongStartTime, cc.SongSelectSongStartJumpIndex);
        }

        // どんちゃんを、いつものくり返しの動きに戻す（決定をやめたとき・設定のように同じ画面に残るとき）
        private void CharaBackToLoop() => CharaLoop();

        // モード選択のどんちゃんの通常の動き：entry_loop（entryModeLoopTime = 500ms で1周）
        //   11/13 たけらい「モード選択のどんちゃんの通常時は演奏画面のうんうんしてる時と同じ」
        //   11/11 の録画（2026-11-11 23-40-44.mp4 108〜114秒）で測ると、モード選択のどんちゃんは 0.5秒で1周していた
        //   （選曲の songselect_loop は 1秒）。どんちゃんの Config.json の entryModeLoopTime と同じなので entry_loop を使う。
        //   前は songselect_loop（ジャンプ）だった。entry_loop が無いキャラは songselect_loop のまま
        private void CharaLoop()
        {
            if (chara.Config is not CharaConfig cc) return;
            if (chara.HasAnim("entry_loop")) chara.Loop("entry_loop", cc.EntryModeLoopTime);
            else chara.Loop("songselect_loop", cc.SongSelectLoopTime);
        }

        // カテゴリの吹き出し：カッ・←→で選ぶ、ドンで決める（決めると板が光って段位道場へ）
        private void UpdateCategory(float dt)
        {
            popupAge += dt;
            int count = DanList.Categories.Count;
            if (count == 0) { choosingCategory = false; return; }

            int move = 0;
            if (InputManager.IsDrumPressed(DrumPart.LeftKa) || KeyPressed(KeyboardKey.Left)) move--;
            if (InputManager.IsDrumPressed(DrumPart.RightKa) || KeyPressed(KeyboardKey.Right)) move++;
            if (move != 0)
            {
                categoryChoice = (categoryChoice + move + count) % count;
                MenuSounds.Ka();
            }

            if (InputManager.IsDonPressed() || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
            {
                MenuSounds.Don();
                DanSelectScene.CategoryIndex = categoryChoice;
                choosingCategory = false;
                StartDecide(Current);
            }
        }

        // 点滅が終わったら、その項目の画面を開く
        private static void Open(Item item)
        {
            switch (item.Title)
            {
                case "演奏ゲーム": SceneManager.ToSongSelectFromMode(new SongSelectScene()); break;
                case "特訓モード": SceneManager.ToSongSelectFromMode(new SongSelectScene(training: true)); break;
                case "段位道場": SceneManager.ToDanSelectFromMode(new DanSelectScene(intro: true)); break;
                case "着せ替え": SceneManager.Simple(new DressingScene()); break;
                case "設定": Game.OpenSettings(); break;
                case "クレジット": SceneManager.Simple(new CreditsScene()); break;
            }
        }

        private void ShowToast(string text)
        {
            toast = text;
            toastTimer = 2f;
        }

        // .tja がドロップされたら、テスト用に譜面を流す(前のホーム画面と同じ)
        public override void OnFilesDropped(string[] paths)
        {
            string? tja = paths.FirstOrDefault(PlayScene.IsTja);
            if (tja != null) SceneManager.ChangeScene(new PlayScene(tja));
        }

        private static bool KeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

        // ===== 描画 =====

        public override void Draw()
        {
            Gui.BeginFrame();
            // 途中参加の板が出ている間は、下の画面はマウスで押せない
            if (join.Active) Gui.SetClip(new Rectangle(0, 0, 0, 0));
            else Gui.ResetClip();
            bool entryBackground = MenuSkin.Whole(EntryBackground, new Rectangle(0, 0, Screen.Width, Screen.Height), Color.White);
            if (entryBackground) DrawEntryFireworks();
            else if (!DrawSkinBackground())
            {
                Color baseColor = Current.Color;
                Raylib.DrawRectangleGradientV(0, 0, Screen.Width, Screen.Height,
                    Raylib.ColorBrightness(baseColor, -0.55f), Raylib.ColorBrightness(baseColor, -0.8f));
            }

            // どんちゃん：モード選択も選曲と同じ songselect_loop（11/8 の録画で確かめた。板より奥）
            if (CharaBaker.Config is CharaConfig ec && (introTime < 0f || introTime >= IntroCharaStart))
            {
                // 入ってきたとき：左の外からすべって入ってくる（だんだんゆっくり）
                float slide = introTime < 0f ? 0f
                    : IntroCharaShift * (1f - EaseOut(Math.Clamp((introTime - IntroCharaStart) / (IntroCharaEnd - IntroCharaStart), 0f, 1f)));
                Rlgl.PushMatrix();
                Rlgl.Translatef(-slide, 0f, 0f);
                chara.DrawPlayers(ec.EntryX, ec.EntryY, ec.EntryScale, LocalSession.Players);
                Rlgl.PopMatrix();
            }

            DrawList();

            if (entryBackground)
            {
                // 上のプレイ情報は板より手前に出す（板が上へ流れても隠さない）
                MenuSkin.Whole(EntryFolder + "Header.png", new Rectangle(0, 0, Screen.Width, Screen.Height), Color.White);
                FreePlay();
            }
            else
            {
                Text("モードをえらぶ", 40, 26, 44, Color.White);
                DrawHint("カッ・↑↓：えらぶ　ドン・Enter：決定　Esc：エントリーへ", 1030);
                DrawHint("テスト：.tja ファイルをウィンドウにドラッグ＆ドロップすると譜面が流れます", 984, new Color(240, 205, 120, 255), 20);
            }

            // 左上の太鼓の案内
            if (controlGuide is Aup2 guide && deciding == null && !hideDecidedList)
            {
                float guideAlpha = introTime < 0f ? 1f : Math.Clamp((introTime - IntroGuideStart) / (IntroGuideEnd - IntroGuideStart), 0f, 1f);
                if (guideAlpha > 0f) guide.Draw(guide.LoopedFrameAt(guideClock), SongSelectScene.GuideOffset, guideAlpha);
            }

            if (introTime < 0f || introTime >= IntroBarAt) NamePlate.DrawPlayers();
            PuchiChara.DrawPlayers("entry");
            // エントリーの板のうしろにあった暗い幕が、消えていく
            if (introTime >= 0f && introTime < IntroDimSeconds)
                Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, (int)(150 * (1f - introTime / IntroDimSeconds))));
            foreach (int side in LocalSession.Players)
                if (!Game.IsSettingsOpen && deciding == null && !choosingCategory && Gui.Clicked(NamePlate.MenuArea(side)))
                { SceneManager.Simple(new EntryScene(selectData: true)); break; }
            if (choosingCategory) DrawCategoryPopup();
            if (toastTimer > 0f) DrawBanner(toast, new Color(255, 200, 60, 255));
            join.Draw(!Game.IsSettingsOpen);
        }

        // カテゴリの吹き出し（まわりは暗くする）
        private void DrawCategoryPopup()
        {
            float t = Math.Clamp(popupAge / 0.1f, 0f, 1f);
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, (int)(110 * t)));
            if (DanList.Categories.Count == 0) return;
            string title = DanList.Categories[Math.Clamp(categoryChoice, 0, DanList.Categories.Count - 1)].Title;
            Color ink = new(12, 12, 16, 255);

            if (!MenuSkin.Whole(CategoryPopup, PopupArea, MenuSkin.White(t)))
            {
                Rectangle pill = new(PopupArea.X + 20f, PopupArea.Y + 18f, PopupArea.Width - 40f, 72f);
                SmoothShapes.Rounded(pill, 1f, 12, Fade(ink, t));
                SmoothShapes.Rounded(new Rectangle(pill.X + 6f, pill.Y + 6f, pill.Width - 12f, pill.Height - 12f), 1f, 12,
                    Fade(new Color(244, 244, 244, 255), t));
            }
            Outlined(title, new Rectangle(PopupArea.X + 90f, PopupArea.Y + 22f, PopupArea.Width - 180f, 66f), 34f,
                Fade(Color.White, t), Fade(ink, t));

            // 左右の矢印（カテゴリが2つ以上のときだけ。ゆっくり明るくなったり暗くなったり）
            if (DanList.Categories.Count < 2) return;
            float pulse = 0.55f + 0.45f * MathF.Abs(MathF.Sin(popupAge * MathF.PI));
            Rectangle left = new(PopupArea.X + 25f, PopupArea.Y + 31f, 50f, 50f);
            Rectangle right = new(PopupArea.X + PopupArea.Width - 75f, PopupArea.Y + 31f, 50f, 50f);
            if (!MenuSkin.Whole(CategoryCursor, left, MenuSkin.White(t * pulse)))
                Outlined("◀", left, 34f, Fade(new Color(255, 236, 110, 255), t * pulse), Fade(ink, t));
            if (!MenuSkin.Part(CategoryCursor, new Rectangle(0, 0, -50, 50), right, MenuSkin.White(t * pulse)))
                Outlined("▶", right, 34f, Fade(new Color(255, 236, 110, 255), t * pulse), Fade(ink, t));
        }

        // 01.Entry が無いスキンはモード専用画像を試し、無ければシステムのグラデーションに戻す
        private static bool DrawSkinBackground()
        {
            Rectangle screen = new(0, 0, Screen.Width, Screen.Height);
            return MenuSkin.Whole(MenuSkin.ModeFolder + "Background.png", screen, Color.White)
                || MenuSkin.Whole(MenuSkin.ModeFolder + "Background/0.png", screen, Color.White);
        }

        private void DrawEntryFireworks()
        {
            // 背景と別になっている花火(300×300 の2コマ)をゆっくり浮かべる
            DrawFirework(380, 75, 0, 0.25f);
            DrawFirework(1360, 85, 1, 1.45f);
        }

        private void DrawFirework(float x, float y, int cell, float offset)
        {
            if (SkinManager.GetTexture(EntryFolder + "Background_Fireworks.png") is not Texture2D texture) return;
            float age = (backgroundTime + offset) % 3.2f;
            if (age >= 1.4f) return;
            float size = 300f * (0.7f + 0.3f * age / 1.4f);
            float alpha = MathF.Sin(age / 1.4f * MathF.PI) * 0.6f;
            Raylib.DrawTexturePro(texture, new Rectangle(cell * 300, 0, 300, 300),
                new Rectangle(x - size / 2, y - size / 2, size, size), System.Numerics.Vector2.Zero, 0f, Fade(Color.White, alpha));
        }

        // 項目(斜めに並べる。先頭と末尾の外には項目を描かない)
        private bool hideDecidedList;

        private void DrawList()
        {
            if (hideDecidedList || deciding?.Title == "演奏ゲーム" && list.DecideTime >= BarList.BlinkEnd) return;
            bool clickable = deciding == null && introTime < 0f;
            bool entryLayout = EntryBarType(Items[0]) >= 0;
            int selectedPos = cursor;

            // 選んでいる板は、ほかの板の上に描くので最後
            Rectangle? selectedArea = null;
            for (int pos = 0; pos < Items.Length; pos++)
            {
                Item item = Items[pos];
                float distance = pos - list.Shown;
                Rectangle area = BarList.BarArea(distance);
                // モード選択の動画は選曲より上下の板が50px離れている
                if (entryLayout) area.Y += Math.Clamp(distance, -1f, 1f) * 50f;
                if (area.Y > Screen.Height || area.Y + area.Height < 0) continue;

                bool selected = pos == selectedPos;
                if (selected)
                {
                    Rectangle openedArea = list.OpenArea(area);
                    if (entryLayout)
                    {
                        float spread = 23f * EaseOut(list.Expand);
                        openedArea.Y -= spread;
                        openedArea.Height += spread * 2;
                    }
                    selectedArea = openedArea;
                }
                else if (introTime < 0f) DrawItem(item, area, 0f);
                else if (introTime >= IntroOthersStart)
                {
                    // 入ってきたとき：上の板は上から、下の板は下から、すべって入ってくる
                    float p = Math.Clamp((introTime - IntroOthersStart) / (IntroOthersEnd - IntroOthersStart), 0f, 1f);
                    Rlgl.PushMatrix();
                    Rlgl.Translatef(0f, MathF.Sign(distance) * IntroOthersShift * (1f - p) * (1f - p), 0f);
                    DrawItem(item, area, 0f);
                    Rlgl.PopMatrix();
                }

                // クリック：ほかの項目なら選ぶ、選んでいる項目なら決定
                if (clickable && Gui.Clicked(selected && selectedArea is Rectangle open ? open : area))
                {
                    if (selected) Decide();
                    else
                    {
                        cursor = pos;
                        list.Collapse();
                        MenuSounds.Ka();
                    }
                }
            }

            if (selectedArea is Rectangle sel && (introTime < 0f || introTime >= IntroBarAt))
            {
                Item selectedItem = Items[selectedPos];
                int entryType = EntryBarType(selectedItem);
                list.DrawFrame(sel, 1f, entryType >= 0 ? EntryBarFolder + "Selected.png" : null);
                DrawItem(selectedItem, sel, list.Expand);
                if (entryType == 0 && list.FrameShown)
                {
                    float alpha = Math.Clamp(list.FrameSeconds / 0.13f, 0f, 1f);
                    MenuSkin.Whole(EntryBarFolder + $"Pop/{entryType}.png", new Rectangle(288, 271, 1344, 486), MenuSkin.White(alpha));
                }
            }
        }

        // A-Style の 2・3 は「プレイヤー設定」「AIバトル演奏」で、TakeTaikoSimulator の項目とは違う
        private static int EntryBarType(Item item)
        {
            int type = item.Title switch { "演奏ゲーム" => 0, "段位道場" => 1, _ => -1 };
            return type >= 0 && MenuSkin.Has(EntryBarFolder + $"Bar/{type}.png", EntryBarFolder + $"Title/{type}.png")
                ? type : -1;
        }

        // 1本の板。open は開き具合(0→1)
        private void DrawItem(Item item, Rectangle area, float open)
        {
            int entryType = EntryBarType(item);
            if (entryType >= 0)
            {
                DrawEntryBar(item, entryType, area, open);
                return;
            }

            Color fill = item.Ready ? item.Color : Raylib.ColorBrightness(item.Color, -0.4f);
            Rectangle inner = list.DrawBody(area, fill, 1f, plain: false);
            Color ink = Raylib.ColorBrightness(fill, -0.65f);
            Color text = item.Ready ? Color.White : new Color(215, 215, 225, 255);

            if (open <= 0f)
            {
                Outlined(item.Title, inner, 50, text, ink);
                return;
            }

            // 開いた板：上に名前、下に説明
            Outlined(item.Title, new Rectangle(inner.X + 20, inner.Y + 30, inner.Width - 40, 90), 72, text, ink);
            Rectangle pill = new Rectangle(inner.X + 60, inner.Y + inner.Height - 110, inner.Width - 120, 70);
            SmoothShapes.Rounded(pill, 1f, 12, Fade(new Color(255, 255, 255, 210), open));
            // スキンの画像が無いときの細い帯は1行しか入らないので、行をつなげて書く（はみ出すぶんは小さくなる）
            Centered(item.Description.Replace("\n", ""), new Rectangle(pill.X + 20, pill.Y, pill.Width - 40, pill.Height), 32, Fade(ink, open));
        }

        // 980×160 の板とタイトル。開いたときは土台だけ伸ばし、タイトルは原寸で上に置く
        private static void DrawEntryBar(Item item, int type, Rectangle area, float open)
        {
            Rectangle imageArea = new(area.X - 24, area.Y - 18, area.Width + 48, area.Height + 36);
            MenuSkin.NPatch(EntryBarFolder + $"Bar/{type}.png", imageArea, 40, Color.White);

            if (open <= 0f)
                MenuSkin.Whole(EntryBarFolder + "Overlay.png", imageArea, Color.White);
            else
            {
                // 閉じた板の白い帯を、開いた板の説明欄に伸ばす
                Rectangle plate = new(area.X + 70, area.Y + 135, area.Width - 140, area.Height - 157);
                if (plate.Height > 0)
                    MenuSkin.NPatchPart(EntryBarFolder + "Overlay.png", new Rectangle(93, 38, 794, 84), plate, 10, Color.White);

                if (plate.Height > 90)
                {
                    Color ink = new(20, 20, 24, 255);
                    if (type == 0)
                    {
                        Outlined("すきな曲や、むずかしさを", new Rectangle(plate.X + 20, plate.Y + 42, plate.Width - 40, 48), 36, Color.White, ink);
                        Outlined("えらんであそべるよ！", new Rectangle(plate.X + 20, plate.Y + 100, plate.Width - 40, 48), 36, Color.White, ink);
                    }
                    else
                    {
                        // 段位道場は3行（本家：行の間 55px くらい・白い字に黒い縁）
                        string[] lines = item.Description.Split('\n');
                        float lineHeight = Math.Min(55f, (plate.Height - 20f) / Math.Max(1, lines.Length));
                        float top = plate.Y + (plate.Height - lineHeight * lines.Length) / 2f;
                        for (int i = 0; i < lines.Length; i++)
                            Outlined(lines[i], new Rectangle(plate.X + 20, top + i * lineHeight, plate.Width - 40, lineHeight), 34, Color.White, ink);
                    }
                }
            }

            MenuSkin.Whole(EntryBarFolder + $"Title/{type}.png",
                new Rectangle(imageArea.X, imageArea.Y, imageArea.Width, 160), Color.White);
            if (type == 1)
                MenuSkin.Whole(EntryBarFolder + "1POnly.png",
                    new Rectangle(area.X - 80, area.Y - 35, 400, 70), Color.White);
        }

        // 下の操作説明(うしろに薄い帯)
        private static void DrawHint(string hint, float y, Color? color = null, float size = 24)
        {
            var measured = Measure(hint, size);
            SmoothShapes.Rounded(new Rectangle(28, y - 7, measured.X + 32, measured.Y + 14), 0.5f, 8, new Color(0, 0, 0, 150));
            Text(hint, 44, y, size, color ?? new Color(220, 224, 236, 255));
        }

        private static void DrawBanner(string text, Color color) => Gui.Notice(text);
    }
}

