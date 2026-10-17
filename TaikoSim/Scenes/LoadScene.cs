using Raylib_cs;
using TaikoSim.Charts;
using TaikoSim.Configs;
using TaikoSim.Graphics;

namespace TaikoSim.Scenes
{
    // 起動したときの読み込み画面
    // スキン → 曲 の順に読んで、終わったらホームへ(叩くキーが決まっていなければ初回起動セットアップへ)
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
        private float doneTimer = 0f;

        // 読み終わってから次へ進むまでの時間(秒)
        private const float DoneWait = 0.5f;  // ふつう(「完了」がチラッと見える)
        private const float ErrorWait = 2.0f; // 読めなかった曲があるとき(読めるように長め。Enter で飛ばせる)

        // ===== 配置(1920×1080) =====
        private const float Left = 160;
        private const float ValueX = 420;   // 「A-Style」「123 / 456」を書く場所
        private const float StatusX = 1560; // 「読み込み中…」「完了」を書く場所
        private const float Right = 1760;

        private static readonly Color BackColor = new Color(20, 22, 32, 255);
        private static readonly Color SubColor = new Color(150, 156, 175, 255);
        private static readonly Color BarBackColor = new Color(40, 44, 60, 255);
        private static readonly Color BarColor = new Color(255, 200, 60, 255);
        private static readonly Color DoneColor = new Color(110, 220, 120, 255);
        private static readonly Color ErrorColor = new Color(255, 110, 100, 255);

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
                    // スキンの画像は GPU に送る都合で、裏では読めない(読んでいる間は画面が止まる)
                    // なので、先に1回「読み込み中…」を画面に出してから、次のフレームで読む
                    skinFrames++;
                    if (skinFrames < 2) return;

                    SkinManager.Init();
                    skinDone = true;

                    SongList.StartScan(); // ここから先は裏で読むので、画面は止まらない
                    step = Step.Songs;
                    break;

                case Step.Songs:
                    if (SongList.IsScanning) return; // 入れ替えは Game が毎フレーム SongList.Update() でやっている
                    step = Step.Done;
                    break;

                case Step.Done:
                    doneTimer += dt;
                    float wait = SongList.ErrorCount > 0 ? ErrorWait : DoneWait;
                    bool skip = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
                    if (doneTimer >= wait || skip) GoNext();
                    break;
            }
        }

        // 次の画面へ(叩くキーが決まっていなければ初回起動セットアップ)
        private void GoNext()
        {
            step = Step.Leave;

            if (ConfigManager.Key.NeedsSetup())
            {
                SceneManager.ChangeScene(new SetupScene());
            }
            else
            {
                SceneManager.ChangeScene(new HomeScene());
            }
        }

        // ===== 描画 =====

        public override void Draw()
        {
            Raylib.ClearBackground(BackColor);

            FontManager.Draw("TaikoSim", Left, 260, 80, Color.White);

            DrawSkinLine(460);
            DrawSongLine(540);
            DrawBar(620);

            // 読めなかった曲があれば、数を出す
            if (step >= Step.Done && SongList.ErrorCount > 0)
            {
                FontManager.Draw($"読めなかった曲：{SongList.ErrorCount}曲（選曲画面に赤い字で出ます）", Left, 680, 30, ErrorColor);
                FontManager.Draw("Enter：次へ", Left, 1000, 26, SubColor);
            }
        }

        // スキンの行
        private void DrawSkinLine(float y)
        {
            FontManager.Draw("スキン", Left, y, 40, Color.White);

            if (skinDone)
            {
                string name = SkinManager.CurrentName;
                FontManager.Draw(string.IsNullOrEmpty(name) ? "（スキンがありません）" : name, ValueX, y, 40, Color.White);
                FontManager.Draw("完了", StatusX, y, 40, DoneColor);
            }
            else
            {
                FontManager.Draw("読み込み中…", StatusX, y, 40, SubColor);
            }
        }

        // 曲の行(「読み込んだ数 / 曲ファイルの総数」)
        private void DrawSongLine(float y)
        {
            FontManager.Draw("曲", Left, y, 40, Color.White);

            if (step == Step.Skin)
            {
                FontManager.Draw("スキンのあとで読みます", ValueX, y, 40, SubColor);
                return;
            }

            int total = SongList.ScanTotal;
            if (total < 0)
            {
                FontManager.Draw("曲ファイルを探しています…", ValueX, y, 40, SubColor);
            }
            else
            {
                int done = step >= Step.Done ? total : Math.Min(SongList.ScanDone, total);
                FontManager.Draw($"{done} / {total}", ValueX, y, 40, Color.White);
            }

            if (step >= Step.Done) FontManager.Draw("完了", StatusX, y, 40, DoneColor);
            else FontManager.Draw("読み込み中…", StatusX, y, 40, SubColor);
        }

        // 進み具合のバー(曲の数で進む)
        private void DrawBar(float y)
        {
            float progress;
            if (step >= Step.Done) progress = 1f;
            else if (step == Step.Skin || SongList.ScanTotal <= 0) progress = 0f;
            else progress = Math.Clamp((float)SongList.ScanDone / SongList.ScanTotal, 0f, 1f);

            Rectangle bar = new Rectangle(Left, y, Right - Left, 20);
            Raylib.DrawRectangleRec(bar, BarBackColor);
            Raylib.DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * progress, bar.Height), step >= Step.Done ? DoneColor : BarColor);

            string percent = $"{(int)(progress * 100)}%";
            FontManager.Draw(percent, Right - FontManager.Measure(percent, 26).X, y + 30, 26, SubColor);
        }
    }
}