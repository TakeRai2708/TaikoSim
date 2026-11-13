using System.Globalization;
using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;

namespace TakeTaikoSimulator.Scenes
{
    // 演奏の結果(演奏画面からリザルト画面へ渡すもの)
    public sealed class PlayResultData
    {
        public string Title = "";
        public CourseType Course = CourseType.Oni;
        public PlayResult Result;
        public PlayStats Stats = new();   // スコア・魂ゲージ・ノルマ(演奏画面のものをそのまま使う)
        public int Good, Ok, Bad, RollHits, MaxCombo;
        public SongRecordUpdate? RecordUpdate;
        public bool AutoPlayed;   // オートで演奏した・ゲストだった（記録は残していない。リザルトに帯で知らせる）
    }

    // リザルト画面
    //   位置・時間は TaikøNauts の動画(1920×1080・60fps)を 30fps でコマ送りして測った値(クリア成功・クリア失敗の2つ)。
    //   時間は「画面が真っ暗から始まったとき」を 0秒 にしている。
    //   ドン・Esc・Enter で選曲画面へ戻る(何もしなければ 20秒で戻る)。
    public sealed class ResultScene : Scene
    {
        public override Platform.DiscordPresence.Info? Presence => new("リザルト");   // 11/11 たけらい：スコアランクは出さない

        private const string Dir = "Image/05.Result/";
        private const string BackgroundImage = Dir + "Background.png";          // 1920×2160(上半分が1P)
        private const string BackgroundClearImage = Dir + "Background_Clear.png"; // 1920×1080
        private const string HeaderImage = Dir + "Header.png";
        private const string PanelImage = Dir + "Panel.png";                    // 1920×1080(左半分が1P)
        private const string ScoreNumberImage = Dir + "Score_Number.png";       // 70×85 が横10、縦2段(上:黒いふち 下:白い数字)
        private const string JudgeNumberImage = Dir + "Judge_Number.png";       // 39×50 が横10
        private const string CourseImage = Dir + "CourseSymbol.png";            // 272×95 が横5(かんたん〜おに裏)
        private const string RankImage = Dir + "ScoreRank.png";                 // 326×276 が横7(粋 白・銅・銀、雅 金・桃・紫、極)、縦4(光るコマ)
        private const string CrownImage = Dir + "Crown.png";                    // 170×160 が横3(クリア・フルコンボ・ドンダ)、縦4(光るコマ)
        private const string BalloonImage = Dir + "Speech_Balloon.png";         // 536×350 が縦4(桃・黄・青・灰)
        private const string BalloonTextImage = Dir + "Speech_Balloon_Text.png"; // 536×350 が横3・縦4

        private const string SoundDir = "Sound/Result/";
        private const string BgmFile = "Sound/BGM/Result.ogg";
        private const string DonSoundFile = "Sound/Taiko/0.Taiko/don.ogg";

        // ===== 位置(1920×1080) =====
        private const float TitleCenterX = 960f, TitleY = 66f, TitleSize = 60f, TitleOutline = 4f;
        private const float CourseX = 31f, CourseY = 153f;
        // 演奏画面のゲージを 70% にした大きさ
        //   11/13 たけらい「左上の座標ずれ」：TaikøNauts の録画（2026-11-11 23-46-22.mp4 90秒）と、たけらいのスクショを重ねた。
        //   ゲージの右（クリアの所）は合っていて、左はしだけ 8px 右にずれていた → 倍率が少し小さかった（0.69 → 0.70）
        private const float GaugeX = 79f, GaugeY = 209f, GaugeScale = 0.70f;
        private const float ScoreRight = 459f, ScoreY = 321f, ScoreStep = 46f;  // スコアの数字(右寄せ)
        private const float JudgeRight = 879f, JudgeStep = 30f;                 // 良・可・不可・連打数・最大コンボ数(右寄せ)
        private static readonly float[] JudgeY = { 286f, 348f, 410f, 472f, 534f };
        private const float RankCenterX = 213f, RankCenterY = 510f;
        private const float CrownCenterX = 392f, CrownCenterY = 513f;
        private const float BalloonX = 386f, BalloonY = 613f;

        // ===== 時間(秒) =====
        private const float FadeInStart = 0.07f, FadeInEnd = 0.40f;
        private const float BgmStart = 0.10f;
        private const float RevealTime = 1.30f;          // スコア・数字が出る(ScoreDon の音)。ゲージが虹になる
        private const float GaugeValueTime = RevealTime - 0.25f;   // ゲージの中身がパッと出る（虹はまだ）
        private const float ScoreBumpSeconds = 0.20f;
        private const float RankTime = 2.03f;            // 粋・雅・極(ScoreRank の音は 0.07秒後)
        private const float CrownTime = 2.30f;           // 王冠(Crown_ の音は 0.07秒後)
        private const float ClearBalloonTime = 3.57f;    // クリア：吹き出し・背景がクリアの色に(Glad の音は 0.05秒後)
        private const float FailBalloonTime = 2.27f;     // クリア失敗：吹き出し(Sad の音は 0.06秒後)
        private const float SoundDelay = 0.06f;
        private const float BackgroundSwitchSeconds = 0.2f;
        private const float AcceptAfterBalloon = 0.3f;   // 吹き出しが出てから、これだけ経ったら戻れる
        private const float AutoReturnSeconds = 20f;     // 何もしなければ戻る
        // 暗くする 0.5秒 → 真っ黒 1.05秒(TaikøNauts の動画：0.45秒で暗く・1.05秒真っ黒・0.5秒で明るく)。明るくするのは次の画面(SceneManager)
        private const float ExitFadeSeconds = 0.5f, ExitBlackSeconds = 1.05f;

        private readonly PlayResultData data;
        private readonly bool returnToSelect;
        private readonly PlayHUD hud = new();
        private float elapsed;
        private float exitElapsed = -1f;  // 戻り始めてからの時間(-1 ならまだ)
        private int skinGeneration = -1;

        private PlayBackground.Aup2Clip[] normalParts = Array.Empty<PlayBackground.Aup2Clip>();
        private PlayBackground.Aup2Clip[] clearParts = Array.Empty<PlayBackground.Aup2Clip>();

        private Song? bgm;
        private SoundEffect? scoreSound, rankSound, crownSound, balloonSound, donSound, bestSound;
        private bool bestPlayed;
        private bool scorePlayed, rankPlayed, crownPlayed, balloonPlayed, bgmStarted;

        private readonly string scoreText;
        private readonly string[] judgeTexts;

        private bool IsClear => data.Result != PlayResult.Failed;
        private int Rank => data.Stats.ScoreRank;          // -1 ならランク無し
        private float BalloonTime => IsClear ? ClearBalloonTime : FailBalloonTime;

        public ResultScene(PlayResultData data, bool returnToSelect)
        {
            this.data = data;
            this.returnToSelect = returnToSelect;
            scoreText = data.Stats.Score.ToString(CultureInfo.InvariantCulture);
            judgeTexts = new[] { data.Good, data.Ok, data.Bad, data.RollHits, data.MaxCombo }
                .Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray();
        }

        public override bool CanOpenSettings => false;

        private readonly MenuChara chara = new();   // 左下（スコアの板の上）のどんちゃん（A-Style だけ。MenuChara.cs）
        private bool charaSwitched;   // 吹き出しが出て、クリア・失敗のアニメに切りかえたか

        public override void Enter()
        {
            chara.Load();
            if (CharaBaker.Config is CharaConfig cc) chara.Loop("result_loop", cc.ResultLoopTime);
            FontManager.PrepareSkinMainText(data.Title);
            LoadSkin();
            LoadSounds();
        }

        public override void Exit()
        {
            chara.Dispose();
            bgm?.Dispose();
            bgm = null;
            foreach (SoundEffect? sound in new[] { scoreSound, rankSound, crownSound, balloonSound, donSound, bestSound }) sound?.Dispose();
            scoreSound = rankSound = crownSound = balloonSound = donSound = bestSound = null;
        }

        // 背景のアニメ(aup2)と、白く光らせる画像を準備する
        private void LoadSkin()
        {
            skinGeneration = SkinManager.Generation;
            List<PlayBackground.Aup2Clip> normal = new(), clear = new();
            string root = Dir + "Background";
            string? full = SkinManager.CurrentPath == null ? null
                : Path.Combine(SkinManager.CurrentPath, root.Replace('/', Path.DirectorySeparatorChar));
            if (full != null && Directory.Exists(full))
            {
                // 0：山、1：雲、2：花火、3：光(クリアだけ)。番号の順に重ねる
                foreach (string folder in Directory.EnumerateDirectories(full)
                    .OrderBy(path => int.TryParse(Path.GetFileName(path), out int n) ? n : int.MaxValue))
                {
                    string preset = root + "/" + Path.GetFileName(folder);
                    normal.AddRange(LoadFirst(preset, "bgInEndFrame", "Normal_1P", "Normal"));
                    clear.AddRange(LoadFirst(preset, "clearBgInEndFrame", "Clear_1P", "Clear"));
                }
            }
            normalParts = normal.ToArray();
            clearParts = clear.ToArray();

            SkinManager.GetWhiteTexture(RankImage);
            SkinManager.GetWhiteTexture(CrownImage);
        }

        private static PlayBackground.Aup2Clip[] LoadFirst(string preset, string loopKey, params string[] children)
        {
            foreach (string child in children)
            {
                var parts = PlayBackground.LoadParts(preset, child, loopKey);
                if (parts.Length > 0) return parts;
            }
            return Array.Empty<PlayBackground.Aup2Clip>();
        }

        private void LoadSounds()
        {
            string? skin = SkinManager.CurrentPath;
            if (skin == null) return;
            SoundEffect? Load(string file) => SoundEffect.Load(Path.Combine(skin, file), out _, 1);

            scoreSound = Load(SoundDir + "ScoreDon.ogg");
            // ベストスコア更新の声（11/11 の録画：スコアが出るのと同時。ScoreDon と重なって鳴る）
            if (ShowsBestUpdate) bestSound = Load(SoundDir + "BestScore_1P.ogg");
            rankSound = Load(SoundDir + "ScoreRank.ogg");
            crownSound = Load(SoundDir + data.Result switch
            {
                PlayResult.DondaFullCombo => "Crown_DondaFullCombo.ogg",
                PlayResult.FullCombo => "Crown_FullCombo.ogg",
                _ => "Crown_Clear.ogg",
            });
            // 推測：桃色の吹き出し(魂ゲージ満タン)は Glad_2、黄色は Glad_1、クリア失敗は Sad
            balloonSound = Load(SoundDir + (BalloonRow == 0 ? "Glad_2.ogg" : IsClear ? "Glad_1.ogg" : "Sad.ogg"));
            donSound = SoundEffect.Load(Path.Combine(skin, DonSoundFile), out _, 2);

            bgm = Song.Load(Path.Combine(skin, BgmFile), out _);
            bgm?.SetLoop(true);
            SetBgmVolume(1f);
        }

        // ===== 更新 =====

        public override void Update(float dt)
        {
            if (skinGeneration != SkinManager.Generation) LoadSkin();
            elapsed += dt;
            hud.Update(dt, data.Stats.Score);
            // どんちゃん：吹き出しが出たら、クリアは result_clear_loop、失敗は result_failed_in → result_failed_loop
            //   （11/8 の録画：クリアのとき、吹き出しと同時に result_loop から result_clear_loop に変わった）
            if (!charaSwitched && elapsed >= BalloonTime && CharaBaker.Config is CharaConfig rc)
            {
                charaSwitched = true;
                if (IsClear) chara.Loop("result_clear_loop", rc.ResultClearLoopTime);
                else chara.PlayThen("result_failed_in", rc.ResultFailedInTime, "result_failed_loop", rc.ResultFailedLoopTime);
            }
            chara.Update(dt);

            if (!bgmStarted && elapsed >= BgmStart)
            {
                bgm?.Play(0);
                bgmStarted = true;
            }
            PlayAt(ref scorePlayed, RevealTime, scoreSound, false);
            PlayAt(ref bestPlayed, RevealTime, bestSound, true);
            if (Rank >= 0) PlayAt(ref rankPlayed, RankTime + SoundDelay, rankSound, false);
            if (IsClear) PlayAt(ref crownPlayed, CrownTime + SoundDelay, crownSound, false);
            PlayAt(ref balloonPlayed, BalloonTime + SoundDelay, balloonSound, true);

            if (exitElapsed >= 0f)
            {
                exitElapsed += dt;
                SetBgmVolume(1f - Math.Clamp(exitElapsed / ExitFadeSeconds, 0f, 1f));
                if (exitElapsed >= ExitFadeSeconds + ExitBlackSeconds) GoBack();
                return;
            }

            float acceptAt = BalloonTime + AcceptAfterBalloon;
            if (elapsed < acceptAt) return;
            bool pressed = InputManager.IsDonPressed()
                || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
            if (pressed) StartExit(true);
            else if (elapsed >= acceptAt + AutoReturnSeconds) StartExit(false);
        }

        // Esc(戻る)でも戻る
        public override void OnBack()
        {
            if (elapsed >= BalloonTime + AcceptAfterBalloon) StartExit(true);
        }

        private void StartExit(bool byInput)
        {
            if (exitElapsed >= 0f) return;
            exitElapsed = 0f;
            if (byInput) donSound?.Play(SeVolume);
        }

        private void GoBack()
        {
            bgm?.Stop();
            if (returnToSelect) SceneManager.AfterResult(new SongSelectScene());
            else SceneManager.AfterResult(new ModeSelectScene());
        }

        private void PlayAt(ref bool played, float time, SoundEffect? sound, bool voice)
        {
            if (played || elapsed < time) return;
            played = true;
            SoundConfig config = ConfigManager.Sound;
            sound?.Play((float)config.GetFinalVolume(voice ? config.VoiceVolume : config.SeVolume));
        }

        private static float SeVolume
        {
            get
            {
                SoundConfig config = ConfigManager.Sound;
                return (float)config.GetFinalVolume(config.SeVolume);
            }
        }

        // 設定パネルで音量が変わったとき(戻る途中で小さくしているときは、その割合のまま)
        public override void ApplyVolume() =>
            SetBgmVolume(exitElapsed >= 0f ? 1f - Math.Clamp(exitElapsed / ExitFadeSeconds, 0f, 1f) : 1f);

        private void SetBgmVolume(float rate)
        {
            SoundConfig config = ConfigManager.Sound;
            bgm?.SetVolume((float)config.GetFinalVolume(config.BgmVolume) * rate);
        }

        // ===== 描画 =====

        public override void Draw()
        {
            Raylib.ClearBackground(Color.Black);
            DrawBackground();
            SkinManager.Draw(HeaderImage, 0, 0);
            DrawTitle();
            UI.SongCounter.DrawPlate(UI.SongCounter.ResultX, UI.SongCounter.ResultY, UI.SongCounter.Number(LocalSession.SongsPlayed));
            if (SkinManager.GetTexture(PanelImage) is Texture2D panel)
                Raylib.DrawTexturePro(panel, new Rectangle(0, 0, 960, 1080), new Rectangle(0, 0, 960, 1080),
                    Vector2.Zero, 0f, Color.White);
            DrawCourse();
            // ゲージは板といっしょに出す（録画では、板が出た 0.1秒後にはもうゲージがあった。数字はそのあと）
            DrawGauge();

            if (elapsed >= RevealTime)
            {
                DrawScore();
                DrawJudgeNumbers();
                DrawBestRecord();
            }
            if (Rank >= 0 && elapsed >= RankTime) DrawRank();
            if (IsClear && elapsed >= CrownTime) DrawCrown();
            // どんちゃん（スコアの板の左下。吹き出しより奥。録画では最初のフェードから出ていた）
            if (CharaBaker.Config is CharaConfig dc) chara.Draw(dc.ResultX[0], dc.ResultY[0], dc.ResultScale);
            if (elapsed >= BalloonTime) DrawBalloon();
            // 名札：TaikøNauts の録画と重ねて (16,950) → (37,932)。前は右の奥に、板の影が黒くはみ出して見えていた
            NamePlate.Draw(SongHistory.ActiveProfile, LocalSession.Controller, 37, 932);
            PuchiChara.Draw(SongHistory.ActiveProfile, LocalSession.Controller, "result");
            // オートのときは記録を残していないので、帯で知らせる（演奏画面と同じ場所。リザルトではずっと出す）
            if (data.AutoPlayed) UI.InfoBar.Draw(UI.InfoBar.PlayX, UI.InfoBar.PlayY, UI.InfoBar.NotSavedText);

            // 最初は真っ暗からだんだん明るく。戻るときはだんだん暗く
            float black = elapsed < FadeInStart ? 1f : 1f - Math.Clamp((elapsed - FadeInStart) / (FadeInEnd - FadeInStart), 0f, 1f);
            if (exitElapsed >= 0f) black = Math.Max(black, Math.Clamp(exitElapsed / ExitFadeSeconds, 0f, 1f));
            if (black > 0f) Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, Raylib.ColorAlpha(Color.Black, black));
        }

        // ===== ベストスコア更新（スコアの上の「スコア」の字の所に、金色の帯が出る） =====
        //   11/11 の録画（2026-11-11 23-46-22.mp4。1回目 100点 → 2回目 12200点）を 1/60秒ずつ見た。スコアが出た時（RevealTime）を 0 として：
        //     0        金色の帯が 11px 上に出る（右に黒い札「12100↑」＝前の自己ベストから増えた点）。BestScore_1P の声（ScoreDon と同時）
        //     0.13〜0.20 帯が下りて、元の場所に止まる
        //     0.23〜0.28 「ベストスコア更新！」の字が出る
        //     0.32〜0.47 帯が黄色く光って、消えていく
        //     0.50〜0.78 白い光が、左から右へななめに横切る
        //   初めて遊んだ曲（前の記録が無い）ときは出ない（1回目のリザルトは「スコア」の字のままだった）。
        //   帯は A-Style の画像の名前が分からないので、プログラムで描いている（色と場所は録画から測った）。
        //   前は、右側に暗い四角を出して文字で知らせていた（チャッピーの仮の表示）。本家に無いのでやめた
        private static readonly Rectangle BestRibbon = new Rectangle(75, 277, 422, 41);
        private static readonly Rectangle BestPill = new Rectangle(356, 283, 138, 29);
        private const float BestRise = 11f, BestHoldEnd = 0.13f, BestLandEnd = 0.20f;
        private const float BestTextStart = 0.23f, BestTextEnd = 0.28f;
        private const float BestFlashStart = 0.317f, BestFlashEnd = 0.467f;
        private const float BestShineStart = 0.50f, BestShineEnd = 0.78f, BestShineWidth = 120f, BestShineSlant = 40f;

        private bool ShowsBestUpdate => data.RecordUpdate is SongRecordUpdate u && u.Saved && u.IsNewBest && u.Before.Score > 0;

        private void DrawBestRecord()
        {
            if (data.RecordUpdate is not SongRecordUpdate update) return;
            if (!update.Saved)
            {
                // 記録を保存できなかったときだけ、下に小さく知らせる
                FontManager.Draw("記録を保存できませんでした", 1340, 1030, 26, new Color(255, 120, 110, 255));
                return;
            }
            if (!ShowsBestUpdate) return;

            float t = elapsed - RevealTime;
            float lift = t < BestHoldEnd ? BestRise : BestRise * (1f - Math.Clamp((t - BestHoldEnd) / (BestLandEnd - BestHoldEnd), 0f, 1f));
            float alpha = Math.Clamp(t / 0.033f, 0f, 1f);
            if (DrawBestRecordImages(update, t, lift, alpha)) return;
            Rectangle ribbon = new Rectangle(BestRibbon.X, BestRibbon.Y - lift, BestRibbon.Width, BestRibbon.Height);
            Rectangle pill = new Rectangle(BestPill.X, BestPill.Y - lift, BestPill.Width, BestPill.Height);

            // 帯：白いふち → 金色（上がこい金、下が明るい）
            SmoothShapes.Rounded(ribbon, 1f, 12, Raylib.ColorAlpha(Color.White, alpha));
            Rectangle gold = new Rectangle(ribbon.X + 3, ribbon.Y + 3, ribbon.Width - 6, ribbon.Height - 6);
            SmoothShapes.Rounded(gold, 1f, 12, Raylib.ColorAlpha(new Color(255, 236, 150, 255), alpha));
            Screen.BeginScissorMode((int)gold.X, (int)gold.Y, (int)gold.Width, (int)(gold.Height * 0.55f));
            SmoothShapes.Rounded(gold, 1f, 12, Raylib.ColorAlpha(new Color(236, 180, 24, 255), alpha));
            Screen.EndScissorMode();

            // 黒い札：増えた点と、上向きの矢印
            SmoothShapes.Rounded(pill, 1f, 10, Raylib.ColorAlpha(new Color(24, 20, 16, 255), alpha));
            string gain = (update.After.Score - update.Before.Score).ToString();
            const float gainSize = 26f;
            float arrowX = pill.X + pill.Width - 18f, midY = pill.Y + pill.Height / 2f;
            // 増えた点は、スキンの細かい字のフォント（Default2.ttf）で
            Vector2 gm = FontManager.MeasureDetail(gain, gainSize);
            FontManager.DrawDetail(gain, arrowX - 9f - gm.X, midY - gm.Y / 2f, gainSize, Raylib.ColorAlpha(Color.White, alpha));
            Color arrow = Raylib.ColorAlpha(Color.White, alpha);
            SmoothShapes.Triangle(new Vector2(arrowX, midY - 9f), new Vector2(arrowX - 7f, midY - 1f), new Vector2(arrowX + 7f, midY - 1f), arrow);
            Raylib.DrawRectangleRec(new Rectangle(arrowX - 3f, midY - 1f, 6f, 9f), arrow);

            // 「ベストスコア更新！」
            float textAlpha = Math.Clamp((t - BestTextStart) / (BestTextEnd - BestTextStart), 0f, 1f);
            if (textAlpha > 0f)
                UI.MenuDraw.Outlined("ベストスコア更新！", new Rectangle(ribbon.X + 30, ribbon.Y, 250, ribbon.Height), 28,
                    Raylib.ColorAlpha(Color.White, textAlpha), Raylib.ColorAlpha(new Color(40, 24, 0, 255), textAlpha));

            // 黄色く光る
            if (t >= BestFlashStart && t < BestFlashEnd)
                SmoothShapes.Rounded(ribbon, 1f, 12,
                    Raylib.ColorAlpha(new Color(255, 255, 0, 255), 0.9f * (1f - (t - BestFlashStart) / (BestFlashEnd - BestFlashStart))));

            // 白い光が、左から右へ（帯の中だけ）
            if (t >= BestShineStart && t < BestShineEnd)
            {
                float p = (t - BestShineStart) / (BestShineEnd - BestShineStart);
                float lead = ribbon.X + p * (ribbon.Width + BestShineWidth + BestShineSlant);
                float top = ribbon.Y, bottom = ribbon.Y + ribbon.Height;
                Screen.BeginScissorMode((int)ribbon.X, (int)ribbon.Y, (int)ribbon.Width, (int)ribbon.Height);
                // ななめの帯（上が右に寄った平行四辺形）を、三角形2つで
                Vector2 a = new Vector2(lead - BestShineWidth, top), b = new Vector2(lead, top);
                Vector2 c = new Vector2(lead - BestShineSlant, bottom), d = new Vector2(lead - BestShineWidth - BestShineSlant, bottom);
                SmoothShapes.Triangle(a, d, c, Color.White);
                SmoothShapes.Triangle(a, c, b, Color.White);
                Screen.EndScissorMode();
            }
        }

        // スキンの絵で描く（A-Style の 05.Result/BestScore_*.png）。絵が無ければ false（プログラムで描く）
        //   11/13 たけらいの録画（2026-11-13_09-41-48.mp4 110秒）に絵を重ねて測った：
        //     札 BestScore_Plate.png（437×54。右の黒い札と↑の矢印も入っている）の左上 (71,272)
        //     数字 BestScore_Number.png（15×18 が10個、14px おき）は右はし 札+394、上 札+14
        //     「ベストスコア更新!」は丸い字に黒いふち、真ん中 札+(142, 23.5)、縁こみの高さ 27px
        //     黄色い光 BestScore_Effect.png（444×72）は札の真ん中にそろえる。白い光 BestScore_SlashEffect.png（185×56）は左から右へ
        //   出かたの時間は前に測ったまま（Best* の数字）
        private const string BestPlateImage = "Image/05.Result/BestScore_Plate.png";
        private const string BestNumberImage = "Image/05.Result/BestScore_Number.png";
        private const string BestEffectImage = "Image/05.Result/BestScore_Effect.png";
        private const string BestSlashImage = "Image/05.Result/BestScore_SlashEffect.png";

        private bool DrawBestRecordImages(SongRecordUpdate update, float t, float lift, float alpha)
        {
            if (SkinManager.GetTexture(BestPlateImage) is not Texture2D plate) return false;
            float px = 71f, py = 272f - lift;
            Color white = Raylib.ColorAlpha(Color.White, alpha);
            Raylib.DrawTextureV(plate, new Vector2(px, py), white);

            string gain = (update.After.Score - update.Before.Score).ToString();
            if (SkinManager.GetTexture(BestNumberImage) is Texture2D num)
            {
                for (int i = 0; i < gain.Length; i++)
                {
                    int d = gain[i] - '0';
                    float x = px + 394f - 15f - (gain.Length - 1 - i) * 14f;
                    Raylib.DrawTexturePro(num, new Rectangle(d * 15, 0, 15, 18), new Rectangle(x, py + 14f, 15, 18), Vector2.Zero, 0f, white);
                }
            }
            else
            {
                Vector2 gm = FontManager.MeasureDetail(gain, 22f);
                FontManager.DrawDetail(gain, px + 394f - gm.X, py + 27f - gm.Y / 2f, 22f, Raylib.ColorAlpha(new Color(250, 240, 190, 255), alpha));
            }

            float textAlpha = Math.Clamp((t - BestTextStart) / (BestTextEnd - BestTextStart), 0f, 1f);
            if (textAlpha > 0f)
                using (UI.MenuDraw.UseOutlineRatio(3f / 24f))
                    UI.MenuDraw.Outlined("ベストスコア更新!", new Rectangle(px + 22f, py + 7f, 240f, 33f), 24f,
                        Raylib.ColorAlpha(Color.White, textAlpha), Raylib.ColorAlpha(Color.Black, textAlpha));

            if (t >= BestFlashStart && t < BestFlashEnd && SkinManager.GetTexture(BestEffectImage) is Texture2D flash)
            {
                float a = 1f - (t - BestFlashStart) / (BestFlashEnd - BestFlashStart);
                Raylib.DrawTextureV(flash, new Vector2(px + 218.5f - flash.Width / 2f, py + 27f - flash.Height / 2f), Raylib.ColorAlpha(Color.White, a));
            }
            if (t >= BestShineStart && t < BestShineEnd && SkinManager.GetTexture(BestSlashImage) is Texture2D slash)
            {
                float p = (t - BestShineStart) / (BestShineEnd - BestShineStart);
                float x = px - slash.Width + p * (plate.Width + slash.Width);
                Screen.BeginScissorMode((int)px, (int)py, plate.Width, plate.Height);
                Raylib.DrawTextureV(slash, new Vector2(x, py - 1f), Color.White);
                Screen.EndScissorMode();
            }
            return true;
        }

        private void DrawBackground()
        {
            // 演奏画面の背景と同じく、半透明を重ねてもキャンバスが透けないようにする
            Rlgl.SetBlendFactorsSeparate(Rlgl.SRC_ALPHA, Rlgl.ONE_MINUS_SRC_ALPHA,
                Rlgl.ONE, Rlgl.ONE_MINUS_SRC_ALPHA, Rlgl.FUNC_ADD, Rlgl.FUNC_ADD);
            Raylib.BeginBlendMode(BlendMode.CustomSeparate);

            float clear = IsClear ? Math.Clamp((elapsed - ClearBalloonTime) / BackgroundSwitchSeconds, 0f, 1f) : 0f;
            if (SkinManager.GetTexture(BackgroundImage) is Texture2D back)
                Raylib.DrawTexturePro(back, new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1080),
                    Vector2.Zero, 0f, Color.White);
            if (clear > 0f && SkinManager.GetTexture(BackgroundClearImage) is Texture2D clearBack)
                Raylib.DrawTexture(clearBack, 0, 0, Raylib.ColorAlpha(Color.White, clear));

            if (clear < 1f)
                foreach (var part in normalParts) part.Draw(elapsed, 0f, 1f);
            if (clear > 0f)
            {
                // クリアのアニメは、切り替わった瞬間から始める
                double clearSeconds = elapsed - ClearBalloonTime;
                foreach (var part in clearParts) part.Draw(clearSeconds, 0f, clear);
            }
            Raylib.EndBlendMode();
        }

        private void DrawTitle()
        {
            if (data.Title.Length == 0) return;
            float width = FontManager.MeasureSkinMain(data.Title, TitleSize).X;
            float size = width > 1100f ? TitleSize * 1100f / width : TitleSize;
            float x = TitleCenterX - FontManager.MeasureSkinMain(data.Title, size).X / 2f;
            // 黒い縁 → 白い文字(SDF フォントで2回)
            FontManager.DrawSkinMainOutlined(data.Title, x, TitleY, size, Color.White, Color.Black, TitleOutline);
        }

        private void DrawCourse()
        {
            if (SkinManager.GetTexture(CourseImage) is not Texture2D tex) return;
            int index = Math.Clamp((int)data.Course, 0, 4);
            Raylib.DrawTexturePro(tex, new Rectangle(index * 272, 0, 272, 95), new Rectangle(CourseX, CourseY, 272, 95),
                Vector2.Zero, 0f, Color.White);
        }

        // 演奏画面と同じ魂ゲージを、小さくして描く(クリアしていれば黄色く点滅、満タンなら虹)
        private void DrawGauge()
        {
            Rlgl.DrawRenderBatchActive();
            Rlgl.PushMatrix();
            Rlgl.Translatef(GaugeX - PlayHUD.GaugeLeft * GaugeScale, GaugeY - PlayHUD.GaugeTop * GaugeScale, 0f);
            Rlgl.Scalef(GaugeScale, GaugeScale, 1f);
            // 11/13 たけらいの録画（2026-11-13_09-41-48.mp4 105.38〜106.63秒、満タンでクリア）：
            //   板が出てから 1.0秒は枠だけ → 1.0秒でパッと中身（満タンでも、まず赤い光） → 1.25秒（スコアと同じ）で虹に
            //   左から伸びていくアニメーションは無かった
            hud.DrawGauge(data.Stats, result: true, showValue: elapsed >= GaugeValueTime, allowRainbow: elapsed >= RevealTime);
            Rlgl.DrawRenderBatchActive();
            Rlgl.PopMatrix();
        }

        private void DrawScore()
        {
            if (SkinManager.GetTexture(ScoreNumberImage) is not Texture2D tex)
            {
                float w = FontManager.Measure(scoreText, 80).X;
                FontManager.Draw(scoreText, ScoreRight - w, ScoreY, 80, Color.White);
                return;
            }
            // 出た瞬間に、少しだけ縦に伸びる
            float t = Math.Clamp((elapsed - RevealTime) / ScoreBumpSeconds, 0f, 1f);
            float bump = 1f + 0.15f * MathF.Sin(MathF.PI * t);
            float height = 85f * bump, y = ScoreY + 85f - height;
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < scoreText.Length; i++)
                {
                    int digit = scoreText[i] - '0';
                    float x = ScoreRight - 70f - (scoreText.Length - 1 - i) * ScoreStep;
                    Raylib.DrawTexturePro(tex, new Rectangle(digit * 70, row * 85, 70, 85), new Rectangle(x, y, 70, height),
                        Vector2.Zero, 0f, Color.White);
                }
        }

        private void DrawJudgeNumbers()
        {
            Texture2D? tex = SkinManager.GetTexture(JudgeNumberImage);
            for (int r = 0; r < judgeTexts.Length; r++)
            {
                string text = judgeTexts[r];
                if (tex is not Texture2D t)
                {
                    float w = FontManager.Measure(text, 44).X;
                    FontManager.Draw(text, JudgeRight - w, JudgeY[r], 44, Color.Black);
                    continue;
                }
                for (int i = 0; i < text.Length; i++)
                {
                    int digit = text[i] - '0';
                    float x = JudgeRight - 39f - (text.Length - 1 - i) * JudgeStep;
                    Raylib.DrawTexturePro(t, new Rectangle(digit * 39, 0, 39, 50), new Rectangle(x, JudgeY[r], 39, 50),
                        Vector2.Zero, 0f, Color.White);
                }
            }
        }

        // 粋・雅・極：大きく(3倍)から 0.13秒で元の大きさへ。そのあと白い光が 0.5秒で消える
        private void DrawRank()
        {
            float t = elapsed - RankTime;
            DrawStamp(RankImage, Rank, 326f, 276f, RankCenterX, RankCenterY, t, 3.0f, 0.13f, 0.7f, 0.5f,
                ShineFrame(t, 0.6f));
        }

        // 王冠：2.5倍から 0.17秒で元の大きさへ。白い光が 0.5秒で消える。吹き出しが出るときに光が横切る
        private void DrawCrown()
        {
            float t = elapsed - CrownTime;
            int col = data.Result switch { PlayResult.DondaFullCombo => 2, PlayResult.FullCombo => 1, _ => 0 };
            DrawStamp(CrownImage, col, 170f, 160f, CrownCenterX, CrownCenterY, t, 2.5f, 0.17f, 1f, 0.5f,
                ShineFrame(t, ClearBalloonTime - CrownTime));
        }

        // 光が横切るコマ(1〜3。0 なら光っていない)。first 秒後に1回、そのあと3秒ごと(推測)
        private static int ShineFrame(float t, float first)
        {
            if (t < first) return 0;
            float local = (t - first) % 3f;
            int frame = (int)(local / (2f / 30f)) + 1; // 1コマ 2/30秒
            return frame <= 3 ? frame : 0;
        }

        private static void DrawStamp(string path, int col, float w, float h, float cx, float cy, float t,
            float fromScale, float inSeconds, float whiteFrom, float whiteSeconds, int shineRow)
        {
            if (SkinManager.GetTexture(path) is not Texture2D tex) return;
            float p = Math.Clamp(t / inSeconds, 0f, 1f);
            float ease = 1f - (1f - p) * (1f - p);
            float scale = fromScale + (1f - fromScale) * ease;
            float alpha = Math.Clamp(p * 1.5f, 0f, 1f);
            Rectangle dest = new Rectangle(cx - w * scale / 2f, cy - h * scale / 2f, w * scale, h * scale);
            Raylib.DrawTexturePro(tex, new Rectangle(col * w, shineRow * h, w, h), dest, Vector2.Zero, 0f,
                Raylib.ColorAlpha(Color.White, alpha));

            float white = whiteFrom * (1f - Math.Clamp((t - inSeconds) / whiteSeconds, 0f, 1f));
            if (t < inSeconds) white = whiteFrom;
            if (white > 0f && SkinManager.GetWhiteTexture(path) is Texture2D whiteTex)
                Raylib.DrawTexturePro(whiteTex, new Rectangle(col * w, 0, w, h), dest, Vector2.Zero, 0f,
                    Raylib.ColorAlpha(Color.White, white * alpha));
        }

        // 吹き出しの色(段)
        //   推測：魂ゲージ満タンでクリア → 桃、クリア → 黄、クリア失敗でノルマの半分以上 → 青、それより下 → 灰
        private int BalloonRow => data.Result == PlayResult.Failed
            ? (data.Stats.Gauge >= data.Stats.Norma / 2.0 ? 2 : 3)
            : data.Stats.IsMax ? 0 : 1;

        // 言葉(列)。推測：おに・裏 → 右、むずかしい → 真ん中、それ以外 → 左
        //   (動画は3本ともおにで、3本とも右の列の言葉だった)
        private int BalloonColumn => data.Course switch
        {
            CourseType.Oni or CourseType.Edit => 2,
            CourseType.Hard => 1,
            _ => 0,
        };

        // 吹き出し：薄く小さく出て、少し大きくなってから元の大きさへ(0.13秒)
        private static readonly float[] BalloonScale = { 0.85f, 1.0f, 1.12f, 1.12f, 1.0f };
        private static readonly float[] BalloonAlpha = { 0.3f, 0.8f, 1f, 1f, 1f };

        private void DrawBalloon()
        {
            if (SkinManager.GetTexture(BalloonImage) is not Texture2D balloon) return;
            float frame = (elapsed - BalloonTime) * 30f;
            float scale = Table(BalloonScale, frame), alpha = Table(BalloonAlpha, frame);
            const float w = 536f, h = 350f;
            float cx = BalloonX + w / 2f, cy = BalloonY + h / 2f;
            Rectangle dest = new Rectangle(cx - w * scale / 2f, cy - h * scale / 2f, w * scale, h * scale);
            Color tint = Raylib.ColorAlpha(Color.White, alpha);
            Raylib.DrawTexturePro(balloon, new Rectangle(0, BalloonRow * h, w, h), dest, Vector2.Zero, 0f, tint);
            if (SkinManager.GetTexture(BalloonTextImage) is Texture2D text)
                Raylib.DrawTexturePro(text, new Rectangle(BalloonColumn * w, BalloonRow * h, w, h), dest, Vector2.Zero, 0f, tint);
        }

        private static float Table(float[] table, float index)
        {
            if (index <= 0f) return table[0];
            if (index >= table.Length - 1) return table[^1];
            int i = (int)index;
            return table[i] + (table[i + 1] - table[i]) * (index - i);
        }
    }
}