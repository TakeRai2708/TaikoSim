using Raylib_cs;
using TaikoSim.Graphics;

namespace TaikoSim.Scenes
{
    // 演奏画面のどんちゃん（左上。風船のときは判定枠の近く）
    //
    //   動き方は OpenTaiko の CActImplCharacter をそのまま移した。モーションの名前と速さは TaikøNauts（A-Style）のもの。
    //   ・アニメ1周を Config.json の「○○MotionBeat」拍で再生する（曲の BPM に合わせて動く）
    //       カウンター += (BPM ÷ 60 × 経過秒) ÷ 拍数、　コマ = カウンター × コマ数
    //   ・ループのアニメ（ふつう・ゴーゴー・クリア…）は今の状態で毎フレーム選び直す（カウンターは続ける）
    //   ・1回だけのアニメ（10コンボ・ゴーゴー開始・クリアになった…）は、最後まで行ったら
    //     ループのアニメに戻る（カウンターは 0 から）
    //
    //   ループのアニメの選び方（OpenTaiko の ReturnDefaultAnime と同じ順）
    //     ① 風船を叩いている途中 → balloon_breaking（風船の位置に出す）
    //     ② 曲の終わりでクリア失敗になった → failed_loop（そのあとはずっとこれ）
    //     ③ ゴーゴータイム       → gogo（不可が出ていても、ゴーゴー中はこっち）
    //     ④ 不可のあと           → 6回続けて不可なら failed_loop、それより少なければ miss_loop
    //     ⑤ ノルマクリア（魂ゲージMAXもふくむ）→ clear
    //     ⑥ それ以外             → normal
    //
    //   1回だけのアニメ（OpenTaiko で呼んでいるところと同じタイミング）
    //     ・10コンボごと（ゴーゴー中は出さない）→ 10combo、魂ゲージMAXなら 10combomax
    //     ・ゴーゴー開始               → gogostart
    //     ・ノルマクリアになった瞬間    → clearin、魂ゲージMAXになった瞬間 → soulin
    //     ・風船が割れた → balloon_broke、割れないで終わった → balloon_miss（どちらも風船の位置）
    //     ・不可のあと（miss_loop・failed_loop）に良か可を出した → combocut（立ち直る。OpenTaiko の Return と同じ。ゴーゴー中は出さない）
    //     ・曲の終わりでクリア失敗（段位は不合格）→ failed_down → そのまま failed_loop
    //
    //   ★ 11/8 の TaikøNauts の録画（2026-11-08 00-39-05.mp4、123〜恋がはじまる〜 かんたん）で確かめたこと
    //     ・不可を1つ出す → すぐ miss_loop（1回目の不可でも。コンボが0でも）
    //     ・不可が6回続く → 6回目で failed_loop（3回とも、不可の数を数えたらぴったり6回目）。あいだに failed_down は出ない
    //     ・そのあと良を出す → その瞬間に combocut（寝そべって手足をばたばた）→ normal。4回とも同じ
    //       （最初は「コンボが切れたとき」と思っていたけど、ちがった）
    //     ・ゴーゴー中は不可を出しても gogo のまま（OpenTaiko と同じ）
    //     ・ゲージがクリアになった瞬間と10コンボが同時のときは、10combo が勝つ（OpenTaiko もゲージ → コンボの順）
    //     ・風船：叩いている間は (212, -8) ×0.95、割れたら balloon_broke も同じ場所（1拍で終わる）
    //   failed_down は、この録画ではクリアしたので出なかった。曲の終わりのクリア失敗で出す、はまだ推測。
    //   Simple スキンにはどんちゃんがいないので、何も描かない。
    public sealed class PlayChara : IDisposable
    {
        private CharaStream? stream;
        private CharaConfig? config;

        private string anime = "normal";
        private bool oneShot;            // 1回だけのアニメの途中
        private double counter;          // 0〜1 でアニメ1周
        private float beat = 4f;         // アニメ1周の拍数
        private bool timeBased;          // 拍ではなく秒で進める（拍数の設定が無いアニメ：balloon_breaking・failed_down）

        // 状態
        private bool goGo;
        private bool cleared, maxed;
        private bool missing;            // 不可のあと、まだ良・可を出していない
        private int missStreak;          // 続けて出した不可の数
        private bool failed;             // 曲の終わりでクリア失敗になった
        private bool balloon;            // 風船を叩いている途中

        private const int MissDownCount = 6;   // 不可がこれだけ続いたら failed_loop（録画で確かめた。OpenTaiko の MissDown も 6）

        // 段位道場：ゲージがクリア・MAX になっても clear・clearin・soulin・10combomax は出さない
        //   11/8 の TaikøNauts の録画（十段をオート、2026-11-08 03-23-19.mp4）で、魂ゲージが 100%（虹）になっても
        //   最後まで normal のままで、10コンボも 10combo（MAX 用ではない）だった
        private bool dan;

        /// <summary>演奏画面に入るときに呼ぶ（焼けているアニメを読む）。isDan は段位道場のとき true</summary>
        public void Load(bool isDan = false)
        {
            Dispose();
            dan = isDan;
            config = CharaBaker.Config;
            if (config == null || CharaBaker.CacheFolder == null) return;
            stream = new CharaStream(CharaBaker.CacheFolder);
            foreach (string anim in CharaBaker.PlayAnims) stream.Has(anim);   // 焼けているものは先にテクスチャを作っておく
        }

        public void Dispose()
        {
            stream?.Dispose();
            stream = null;
        }

        /// <summary>最初から（段位の次の曲も。ゲージは続くので、そのときの状態から始める）</summary>
        public void Reset(bool isClear, bool isMax)
        {
            goGo = false;
            cleared = isClear && !dan;
            maxed = isMax && !dan;
            missing = false;
            missStreak = 0;
            failed = false;
            balloon = false;
            oneShot = false;
            counter = 0;
            anime = "";   // かならず選び直す（拍数も入れ直す）
            ReturnDefault(true);
        }

        // ===== 演奏から呼ぶ =====

        /// <summary>
        /// 良・可・不可が出た（見逃しの不可もふくむ）。combo は足したあとの数。
        /// isClear・isMax は、その音符のぶんをゲージに足したあとの状態（クリアになった → 10コンボ の順に動かすため）
        /// </summary>
        public void OnJudge(bool bad, int combo, bool noteInGoGo, bool isClear, bool isMax)
        {
            UpdateGauge(isClear, isMax);
            if (bad)
            {
                missStreak++;
                missing = true;
                return;
            }

            // 不可のあとの良・可：立ち直るアニメ（ゴーゴー中は出さない。OpenTaiko の Return と同じ）
            bool recovered = missing;
            missing = false;
            missStreak = 0;
            if (recovered && !goGo && !failed && !balloon) Play("combocut");

            // 10コンボごと（OpenTaiko：ゴーゴー中の音符では出さない。同じアニメの途中なら最初からにしない）
            if (combo > 0 && combo % 10 == 0 && !noteInGoGo)
            {
                string next = maxed ? "10combomax" : "10combo";
                if (!(oneShot && anime == next)) Play(next);
            }
        }

        /// <summary>毎フレーム：ゴーゴー・ゲージ・風船の状態を渡す</summary>
        public void SetState(bool isGoGo, bool isClear, bool isMax, bool balloonHitting)
        {
            if (isGoGo && !goGo) Play("gogostart", config?.GoGoStartMotionFromBeginning ?? true);
            goGo = isGoGo;

            UpdateGauge(isClear, isMax);

            if (balloonHitting && !balloon)
            {
                balloon = true;
                Change("balloon_breaking", true, false);
            }
            else if (!balloonHitting && balloon)
            {
                balloon = false;
                if (anime == "balloon_breaking") ReturnDefault(true);
            }
        }

        // ゲージがクリア・MAX になった瞬間のアニメ
        private void UpdateGauge(bool isClear, bool isMax)
        {
            if (dan) { isClear = false; isMax = false; }
            if (isMax && !maxed) Play("soulin");
            else if (isClear && !cleared) Play("clearin");
            maxed = isMax;
            cleared = isClear;
        }

        /// <summary>曲の終わりでクリア失敗になった（段位は不合格）</summary>
        public void OnFailed()
        {
            if (failed) return;
            failed = true;
            balloon = false;
            if (stream != null && stream.Has("failed_down")) Play("failed_down");
            else ReturnDefault(true);
        }

        /// <summary>風船が割れた（true）・割れずに終わった（false）</summary>
        public void OnBalloonEnd(bool popped)
        {
            balloon = false;
            Play(popped ? "balloon_broke" : "balloon_miss");
        }

        // ===== アニメの切りかえ =====

        private void Play(string anim, bool fromBeginning = true)
        {
            if (stream == null || !stream.Has(anim)) return;   // そのアニメが無い（まだ焼けていない）なら変えない
            Change(anim, fromBeginning, true);
        }

        private void Change(string anim, bool resetCounter, bool isOneShot)
        {
            anime = anim;
            oneShot = isOneShot;
            if (resetCounter) counter = 0;
            timeBased = anim is "balloon_breaking" or "failed_down";
            beat = BeatOf(anim);
        }

        // ループのアニメに戻す（OpenTaiko の ReturnDefaultAnime）
        private void ReturnDefault(bool resetCounter)
        {
            string anim;
            if (balloon) anim = "balloon_breaking";
            else if (failed) anim = "failed_loop";
            else if (goGo) anim = "gogo";
            else if (missing) anim = missStreak >= MissDownCount ? "failed_loop" : "miss_loop";
            else if (cleared || maxed) anim = "clear";
            else anim = "normal";

            // そのアニメが無いスキン（まだ焼けていないときも）は、ふつうのアニメで代わりにする
            if (stream != null && !stream.Has(anim)) anim = "normal";
            if (anim != anime || oneShot) Change(anim, resetCounter, false);
        }

        private float BeatOf(string anim)
        {
            CharaConfig? c = config;
            if (c == null) return 4f;
            return anim switch
            {
                "normal" => c.NormalMotionBeat,
                "10combo" => c.Combo10MotionBeat,
                "10combomax" => c.Combo10MaxMotionBeat,
                "gogostart" => c.GoGoStartMotionBeat,
                "gogo" => c.GoGoMotionBeat,
                "clearin" => c.ClearInMotionBeat,
                "clear" => c.ClearMotionBeat,
                "soulin" => c.SoulInMotionBeat,
                "combocut" => c.ComboCutMotionBeat,
                "miss_loop" => c.MissLoopMotionBeat,
                "failed_loop" => c.FailedLoopMotionBeat,
                "balloon_broke" => c.BalloonBrokeMotionBeat,
                "balloon_miss" => c.BalloonMissMotionBeat,
                _ => 4f,
            };
        }

        // ===== 進める・描く =====

        /// <summary>bpm は今の BPM</summary>
        public void Update(float dt, double bpm)
        {
            if (stream == null) return;

            if (timeBased)
            {
                // 拍数の設定が無いアニメは BPM と関係なく、元の 60fps のまま回す
                //   balloon_breaking：OpenTaiko は 0.2秒で1周だけど、OpenTaiko のキャラはコマが少ないので、A-Style の 39コマだと速すぎる
                //   failed_down：175コマ → 約2.9秒
                int source = Math.Max(1, stream.SourceFrameCount(anime));
                counter += dt / (source / 60.0);
            }
            else
            {
                counter += Math.Abs(bpm) / 60.0 * dt / Math.Max(0.01f, beat);
            }

            if (counter >= 1)
            {
                if (oneShot) { oneShot = false; counter = 0; ReturnDefault(true); return; }
                counter = double.IsFinite(counter) ? counter % 1 : 0;
            }

            // ループのアニメは、状態が変わったらすぐ切りかえる（カウンターはそのまま）
            if (!oneShot) ReturnDefault(false);
        }

        /// <summary>ふつうの位置のどんちゃん（背景の上・レーンの下）</summary>
        public void DrawNormal()
        {
            if (!IsBalloonAnim) DrawAt(false);
        }

        /// <summary>風船のときのどんちゃん（音符の上）</summary>
        public void DrawBalloon()
        {
            if (IsBalloonAnim) DrawAt(true);
        }

        private bool IsBalloonAnim => CharaBaker.IsBalloonAnim(anime);

        private void DrawAt(bool balloonPlace)
        {
            if (stream == null || config == null || SkinManager.SystemSkin) return;
            int count = stream.FrameCount(anime);
            if (count <= 0) return;
            int frame = Math.Clamp((int)(counter * count), 0, count - 1);

            float x = balloonPlace ? config.EnsoGameBalloonX[0] : config.EnsoGameX[0];
            float y = balloonPlace ? config.EnsoGameBalloonY[0] : config.EnsoGameY[0];
            float scale = balloonPlace ? config.EnsoGameBalloonScale : config.EnsoGameNormalScale;
            stream.Draw(anime, frame, x, y, scale, Color.White);
        }
    }
}
