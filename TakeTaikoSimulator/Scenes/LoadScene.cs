using System.Numerics;
using Raylib_cs;
using System.Diagnostics;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.Scenes
{
    // 起動したときの読み込み画面
    // スキン → 曲 の順に読んで、終わったらモード選択へ(叩くキーが決まっていなければ初回起動セットアップへ)
    public class LoadingScene : Scene
    {
        private enum Step
        {
            Skin,  // スキンを読む
            Songs, // 曲を読む(裏で)
            Done,  // 読み終わった(少しだけ「完了」を見せてから次へ)
            Leave, // 次の画面へ行った
        }

        private Step step = Step.Skin;
        private int skinFrames = 0;     // スキンを読む前に、画面を何回描いたか
        private bool skinDone = false;
        private IEnumerator<(int Done, int Total, string Stage)>? skinLoader;
        private int skinLoaded, skinTotal;
        private string skinStage = "スキンを準備しています";
        private float doneTimer = 0f;

        // 読み終わってから次へ進むまでの時間(秒)
        private const float DoneWait = 0.5f;  // ふつう(「完了」がチラッと見える)
        private const float ErrorWait = 2.0f; // 読めなかった曲があるとき(読めるように長め。Enter で飛ばせる)

        // ===== 配置(1920×1080) =====
        private const float Left = 160;
        private const float Right = 1760;

        private static readonly Color BackColor = new Color(20, 22, 32, 255);
        private static readonly Color SubColor = new Color(150, 156, 175, 255);
        private static readonly Color BarColor = new Color(255, 200, 60, 255);
        private static readonly Color ErrorColor = new Color(255, 110, 100, 255);

        // ===== 見た目（Motion Prompt #089 ローディング画面。11/10） =====
        // Motion Prompt #089 · MIT License (c) 2026 Shohei Ohtaki · https://showheyohtaki.github.io/motion-prompt/#089
        //   暗い画面のまんなかに、ロゴ・大きな % ・細いバー。読み終わったら、画面ごと上へ抜けて（0.55秒・easeInOutCubic）、
        //   下から次の画面が明るくなって出てくる（0.6秒）。
        //   #089 は % が決まった時間で 0→100 に進むけど、TakeTaikoSimulator は本当の進み具合（スキン半分・曲半分）を、なめらかに追いかけて出す。
        //   スキン・曲の細かい進み具合は、前と同じ内容を小さい字で下に残す（cdc7b38 の進捗バー）
        private const float LiftSeconds = 0.55f, NextFadeIn = 0.6f;
        private const float BarWidth = 640f, BarHeight = 8f;
        private float shownProgress;   // 画面に出している進み具合（本当の値をなめらかに追いかける）

        // 上へ抜けていく幕（次の画面に切りかわったあとも、SceneManager.DrawFade から描く）
        private static float liftTime = -1f;
        private static string liftSkin = "";

        /// <summary>上へ抜けていく幕を描く（SceneManager.DrawFade から毎フレーム）</summary>
        public static void DrawLift()
        {
            if (liftTime < 0f) return;
            liftTime += Raylib.GetFrameTime();
            float k = Math.Clamp(liftTime / LiftSeconds, 0f, 1f);
            float e = k < 0.5f ? 4f * k * k * k : 1f - MathF.Pow(-2f * k + 2f, 3f) / 2f;   // easeInOutCubic
            if (k >= 1f) { liftTime = -1f; return; }
            Rlgl.PushMatrix();
            Rlgl.Translatef(0f, -Screen.Height * e, 0f);
            DrawCurtain(1f, liftSkin, null);
            Rlgl.PopMatrix();
        }

        // 幕の中身：背景・ロゴ・%・バー（detail は下の小さい字。null なら出さない）
        private static void DrawCurtain(float progress, string skinLine, Action? detail)
        {
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, BackColor);
            float cx = Screen.Width / 2f;
            Vector2 logo = FontManager.Measure("TakeTaikoSimulator", 80);
            FontManager.Draw("TakeTaikoSimulator", cx - logo.X / 2f, 330, 80, Color.White);
            string percent = $"{(int)MathF.Round(progress * 100f)}%";
            Vector2 p = FontManager.Measure(percent, 96);
            FontManager.Draw(percent, cx - p.X / 2f, 470, 96, Color.White);
            Rectangle bar = new(cx - BarWidth / 2f, 610, BarWidth, BarHeight);
            Raylib.DrawRectangleRounded(bar, 1f, 8, new Color(255, 255, 255, 38));   // #089：白 15%
            if (progress > 0f)
                Raylib.DrawRectangleRounded(new Rectangle(bar.X, bar.Y, BarWidth * progress, BarHeight), 1f, 8, BarColor);
            if (skinLine.Length > 0)
            {
                Vector2 s = FontManager.Measure(skinLine, 26);
                FontManager.Draw(skinLine, cx - s.X / 2f, 650, 26, SubColor);
            }
            detail?.Invoke();
        }

        // 読み込み中は設定パネルを開けない(スキンを読む前なので、パネルの絵が出せない)
        public override bool CanOpenSettings => false;

        public override void Enter()
        {
        }

        // 読み込み中は Esc を使わない
        public override void OnBack()
        {
        }

        public override void Update(float dt)
        {
            switch (step)
            {
                case Step.Skin:
                    // GPUへの転送はメインスレッドで少しずつ行い、合間に進捗を描く。
                    skinFrames++;
                    if (skinFrames < 2) return;

                    skinLoader ??= SkinManager.ReloadSteps().GetEnumerator();
                    // 1フレームの処理は約8msを目安に区切る。画像1枚やフォントなどの
                    // 単独工程に8ms以上かかる場合は、その工程が終わってから画面を描く。
                    long started = Stopwatch.GetTimestamp();
                    bool finished = false;
                    do
                    {
                        if (!skinLoader.MoveNext()) { finished = true; break; }
                        (skinLoaded, skinTotal, skinStage) = skinLoader.Current;
                        if (skinLoaded == skinTotal) break; // 100%の画面を1フレーム見せる
                    }
                    while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 8);
                    if (!finished) return;
                    skinLoader.Dispose();
                    skinLoader = null;
                    skinDone = true;

                    SongList.StartScan(); // ここから先は裏で読むので、画面は止まらない
                    step = Step.Songs;
                    break;

                case Step.Songs:
                    if (SongList.IsScanning) return; // 入れ替えは Game が毎フレーム SongList.Update() でやっている
                    step = Step.Done;
                    break;

                case Step.Done:
                    if (shownProgress < 0.995f) return;   // 100% を見せてから
                    doneTimer += dt;
                    float wait = SongList.ErrorCount > 0 ? ErrorWait : DoneWait;
                    bool skip = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
                    if (doneTimer >= wait || skip) GoNext();
                    break;
            }
        }

        public override void Exit()
        {
            skinLoader?.Dispose();
            skinLoader = null;
        }

        // 次の画面へ(叩くキーが決まっていなければ初回起動セットアップ)
        private void GoNext()
        {
            step = Step.Leave;

            // #089：この画面が上へ抜けて、次の画面が下から明るくなって出る（幕は SceneManager.DrawFade が描き続ける）
            liftTime = 0f;
            liftSkin = SkinLine();
            Scene next = ConfigManager.Key.NeedsSetup() ? new SetupScene() : new EntryScene();
            SceneManager.ChangeScene(next, 0f, 0f, NextFadeIn);
        }

        private string SkinLine()
        {
            if (!skinDone) return skinStage;
            string name = SkinManager.CurrentName;
            return $"スキン：{(string.IsNullOrEmpty(name) ? "（スキンがありません）" : name)}";
        }

        // ===== 描画 =====

        public override void Draw()
        {
            // 本当の進み具合：スキンで半分、曲で半分
            float skin = skinDone ? 1f : skinTotal > 0 ? (float)skinLoaded / skinTotal : 0f;
            float songs = step >= Step.Done ? 1f
                : step == Step.Skin || SongList.ScanTotal <= 0 ? 0f
                : Math.Clamp((float)SongList.ScanDone / SongList.ScanTotal, 0f, 1f);
            float actual = skin * 0.5f + songs * 0.5f;
            shownProgress += (actual - shownProgress) * (1f - MathF.Exp(-10f * Raylib.GetFrameTime()));
            if (actual - shownProgress < 0.002f) shownProgress = actual;

            DrawCurtain(shownProgress, SkinLine(), () =>
            {
                // 曲の行（小さい字で）
                string songText = step == Step.Skin ? "曲：スキンのあとで読みます"
                    : SongList.ScanTotal < 0 ? "曲：曲ファイルを探しています…"
                    : $"曲：{(step >= Step.Done ? SongList.ScanTotal : Math.Min(SongList.ScanDone, SongList.ScanTotal))} / {SongList.ScanTotal}";
                Vector2 m = FontManager.Measure(songText, 26);
                FontManager.Draw(songText, Screen.Width / 2f - m.X / 2f, 690, 26, SubColor);

                // 読めなかった曲があれば、数を出す
                if (step >= Step.Done && SongList.ErrorCount > 0)
                {
                    string error = $"読めなかった曲：{SongList.ErrorCount}曲（選曲画面に赤い字で出ます）";
                    Vector2 em = FontManager.Measure(error, 30);
                    FontManager.Draw(error, Screen.Width / 2f - em.X / 2f, 760, 30, ErrorColor);
                    FontManager.Draw("Enter：次へ", Left, 1000, 26, SubColor);
                }
            });
        }
    }
}
