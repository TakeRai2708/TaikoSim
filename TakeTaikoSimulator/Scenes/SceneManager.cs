using Raylib_cs;
using TakeTaikoSimulator.Diagnostics;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.Scenes
{
    // 画面(シーン)の切り替え係
    //
    // 切り替えるときに、暗くする → 真っ黒のまま待つ → 明るくする、ができる(TaikøNauts の動画を測った秒数で呼ぶ)
    //   ・暗くしている間と真っ黒の間は、今の画面の Update を止める(ボタンを押しても反応しない)
    //   ・真っ黒になった瞬間に、次の画面に切り替える(重い準備は真っ黒の間にやる)
    //   ・明るくしている間は、次の画面はもう動いている
    public static class SceneManager
    {
        // 今の画面
        public static Scene? Current { get; private set; }

        // 次に切り替える画面(フレームの途中で切り替えると危ないので、次のフレームの最初に切り替える)
        private static Scene? next;

        // ===== 暗くする・明るくする =====
        private enum Phase { None, FadeOut, Hold, FadeIn }
        private static Phase phase = Phase.None;
        private static float timer, fadeOutSeconds, holdSeconds, fadeInSeconds;
        private static Scene? waiting; // 暗くし終わったら切り替える画面

        // ===== TaikøNauts の動画を測った秒数(暗くする・真っ黒・明るくする) =====
        // モード選択 → 選曲：0.5秒で暗く → 1.05秒真っ黒 → 0.3秒で明るく
        public static void ToSongSelectFromMode(Scene scene) => ChangeScene(scene, 0.5f, 1.05f, 0.3f);
        // 選曲 → モード選択：0.5秒で暗く → 0.8秒真っ黒 → 0.65秒で明るく
        // モード選択 → 段位選択：0.5秒で暗く → 0.4秒真っ黒 → 0.3秒で扉の絵が出てくる（10/31 の動画 01-44-20 の25.8〜27.0秒）
        public static void ToDanSelectFromMode(Scene scene) => ChangeScene(scene, 0.5f, 0.4f, 0.3f);
        public static void ToModeFromSongSelect(Scene scene) => ChangeScene(scene, 0.5f, 0.8f, 0.65f);
        // リザルト → 次の画面：暗くするのはリザルトが自分でやる(曲もいっしょに小さくするので)。そのあと 0.5秒で明るく
        public static void AfterResult(Scene scene) => ChangeScene(scene, 0f, 0f, 0.5f);
        // 起動の読み込みが終わったとき：0.3秒で暗く → 0.5秒で明るく(TaikøNauts は IC カードの画面なので、秒数は TakeTaikoSimulator で決めた)
        public static void AfterBoot(Scene scene) => ChangeScene(scene, 0.3f, 0f, 0.5f);
        // そのほか(クレジット・セットアップ・演奏をやめたとき)：0.3秒で暗く → 0.3秒で明るく
        public static void Simple(Scene scene) => ChangeScene(scene, 0.3f, 0f, 0.3f);
        // 選曲 → 演奏：黒くせずに、曲の読み込み画面(虹色の幕)をはさむ。動きは SongLoading にまとめた
        public static void ToPlayWithLoading(Scene scene, string title, string subtitle, float? wait = null) => SongLoading.Start(scene, title, subtitle, wait);
        // 段位選択 → 段位の演奏：道場の絵の読み込み画面をはさむ（DanLoading）
        public static void ToDanPlay(Scene scene, Charts.DanCourse course) => DanLoading.Start(scene, course);

        // 暗くしている途中・真っ黒の間(今の画面を操作させない)
        //   曲の読み込み画面の、幕が開き始めるまでも同じ
        public static bool IsBlocking => phase is Phase.FadeOut or Phase.Hold || IntroPlaying || SongLoading.IsBlocking || DanLoading.IsBlocking;

        // 11/22 たけらい「完全に画面が表示してから操作、音出し」：起動画面 → 最初の画面のフェードインが終わるまでは、操作も音も止める
        private static bool bootIntro;
        public static bool IntroPlaying => bootIntro && (phase != Phase.None || LoadingScene.LiftActive);

        /// <summary>起動画面（LoadingScene）から最初の画面へ。明るくなりきるまで操作・BGM・ボイスを待たせる</summary>
        public static void FromBoot(Scene scene, float fadeIn)
        {
            ChangeScene(scene, 0f, 0f, fadeIn);
            bootIntro = true;
        }

        // 画面を切り替える予約をする
        //   fadeOut：今の画面をだんだん暗くする秒数(0 ならいきなり)
        //   hold   ：真っ黒のまま待つ秒数
        //   fadeIn ：次の画面をだんだん明るくする秒数(0 ならいきなり)
        public static void ChangeScene(Scene scene, float fadeOut = 0f, float hold = 0f, float fadeIn = 0f)
        {
            fadeOutSeconds = Math.Max(0f, fadeOut);
            holdSeconds = Math.Max(0f, hold);
            fadeInSeconds = Math.Max(0f, fadeIn);
            timer = 0f;

            if (fadeOutSeconds > 0f)
            {
                waiting = scene;
                phase = Phase.FadeOut;
            }
            else
            {
                next = scene;
                phase = holdSeconds > 0f ? Phase.Hold : fadeInSeconds > 0f ? Phase.FadeIn : Phase.None;
            }
        }

        // 毎フレーム(Game が ApplyChange の前に呼ぶ)。暗くし終わったら、次の画面を予約する
        public static void Update(float dt)
        {
            SongLoading.Update(dt); // 曲の読み込み画面(読み込む時が来たら ChangeScene を呼ぶ)
            DanLoading.Update(dt);  // 段位の読み込み画面(同じ)
            if (phase == Phase.None) return;
            // 画面の準備で1フレームが長くなっても、明るくするのが一瞬で終わらないように、1回に進める時間をおさえる
            timer += Math.Min(dt, 1f / 30f);
            switch (phase)
            {
                case Phase.FadeOut:
                    if (timer < fadeOutSeconds) return;
                    next = waiting;
                    waiting = null;
                    timer = 0f;
                    phase = holdSeconds > 0f ? Phase.Hold : fadeInSeconds > 0f ? Phase.FadeIn : Phase.None;
                    break;
                case Phase.Hold:
                    if (next != null || timer < holdSeconds) return; // 切り替えが終わってから数える
                    timer = 0f;
                    phase = fadeInSeconds > 0f ? Phase.FadeIn : Phase.None;
                    break;
                case Phase.FadeIn:
                    if (timer >= fadeInSeconds) phase = Phase.None;
                    break;
            }
            if (bootIntro && !IntroPlaying) bootIntro = false;
        }

        // 予約されていたら実際に切り替える(Game が毎フレーム最初に呼ぶ)
        public static void ApplyChange()
        {
            if (next == null) return;

            string from = Current?.GetType().Name ?? "なし";
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            Current?.Exit();
            Current = next;
            next = null;
            DebugLog.SceneChanged(from, Current.GetType().Name);
            Current.Enter();
            DebugLog.Event("画面の準備", $"{Current.GetType().Name} の準備(Exit + Enter)にかかった時間",
                System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (phase == Phase.Hold) timer = 0f; // 準備に時間がかかっても、真っ黒の時間はそのあと数える
        }

        // 黒い幕と、曲の読み込み画面(Game が画面を描いたあとに呼ぶ)
        public static void DrawFade()
        {
            SongLoading.Draw();
            DanLoading.Draw();
            LoadingScene.DrawLift();   // 起動画面が透けて消えていく幕（クロスフェード）
            float black = phase switch
            {
                Phase.FadeOut => fadeOutSeconds > 0f ? Math.Clamp(timer / fadeOutSeconds, 0f, 1f) : 1f,
                Phase.Hold => 1f,
                Phase.FadeIn => fadeInSeconds > 0f ? 1f - Math.Clamp(timer / fadeInSeconds, 0f, 1f) : 0f,
                _ => 0f,
            };
            if (black <= 0f) return;
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, Raylib.ColorAlpha(Color.Black, black));
        }
    }
}
