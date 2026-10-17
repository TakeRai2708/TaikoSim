using System.Numerics;
using Raylib_cs;
using TaikoSim.Charts;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.Input;

namespace TaikoSim.Scenes
{
    // テスト用：.tja の譜面を読み込んで音符を流すだけの画面(まだ叩けない・音も出ない)
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
        private const float PixelsPerBeat = 179f; // 1拍で進む距離(はやさ1倍)

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
        private static readonly string[] SeFallbackText =
            { "ドン", "ド", "コ", "カ", "カッ", "ドン(大)", "カッ(大)", "連打", "っ!!", "ふうせん", "くすだま" };

        // スキンが無いときの色
        private static readonly Color DonColor = new Color(235, 90, 60, 255);
        private static readonly Color KaColor = new Color(90, 190, 230, 255);
        private static readonly Color RollColor = new Color(240, 180, 40, 255);

        private readonly string path;
        private Chart? chart;
        private Course? course;
        private string error = "";

        // 今の時間(曲の頭が0。マイナスは曲が始まる前)
        private double nowMs;

        // 演奏中は設定パネルを開かない
        public override bool CanOpenSettings => false;

        public TestPlayScene(string path)
        {
            this.path = path;
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
                course = chart.GetPreferredCourse();
                if (course == null) error = "譜面（#START〜#END）が見つかりませんでした";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = $"読み込めませんでした：{ex.Message}";
            }
            Restart();
        }

        // 最初から(最初の小節の3秒前から始める)
        private void Restart()
        {
            double first = (course != null && course.BarLines.Count > 0) ? course.BarLines[0].TimeMs : 0;
            nowMs = Math.Min(0, first) - 3000;
        }

        public override void Update(float dt)
        {
            // クイックリスタート(F5)で最初から
            if (InputManager.IsSystemPressed(SystemAction.QuickRestart))
            {
                Restart();
                return;
            }
            nowMs += dt * 1000.0;
        }

        // Esc でホームに戻る
        public override void OnBack()
        {
            SceneManager.ChangeScene(new HomeScene());
        }

        // 別の .tja がドロップされたら、そっちに切り替える
        public override void OnFilesDropped(string[] paths)
        {
            string? tja = paths.FirstOrDefault(IsTja);
            if (tja != null) SceneManager.ChangeScene(new TestPlayScene(tja));
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

            // 譜面オフセット(見た目だけずらす。マイナスで早く来る)
            double viewMs = nowMs - ConfigManager.Game.ChartOffsetMs;

            DrawLane();
            DrawBarLines(course, viewMs);

            // 後ろ(右)の音符から描いて、手前(左)の音符が上に重なるようにする
            for (int i = course.Notes.Count - 1; i >= 0; i--) DrawNote(course.Notes[i], viewMs);
            for (int i = course.Notes.Count - 1; i >= 0; i--) DrawSeText(course.Notes[i], viewMs);

            // 左のパネル(判定枠を通りすぎた音符はこの下に隠れる)
            if (!SkinManager.Draw(PanelImage, 0, LaneY))
            {
                Raylib.DrawRectangle(0, (int)LaneY, (int)LaneX + 5, (int)LaneHeight, new Color(170, 50, 40, 255));
            }

            DrawInfo(chart, course);
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
                FontManager.Draw(SeFallbackText[row], x - 30, SeTextY + 4, 26, Color.White);
                return;
            }

            if (note.IsRoll)
            {
                // 「連打」と「っ!!」の間を線でつなぐ
                if (SkinManager.GetTexture(SeRollingImage) is Texture2D line)
                {
                    float lineStart = x + SeWidth / 2f;
                    Raylib.DrawTexturePro(line, new Rectangle(0, 0, line.Width, line.Height),
                        new Rectangle(lineStart, SeTextY, Math.Max(0, endX - lineStart), SeHeight), Vector2.Zero, 0f, Color.White);
                }
                DrawSeRow(se, SeRollEnd, endX);
            }
            DrawSeRow(se, row, x);
        }

        // SENotes.png の row 段目を、centerX を中心にして描く
        private static void DrawSeRow(Texture2D se, int row, float centerX)
        {
            Raylib.DrawTexturePro(se, new Rectangle(0, row * SeHeight, SeWidth, SeHeight),
                new Rectangle(centerX - SeWidth / 2f, SeTextY, SeWidth, SeHeight), Vector2.Zero, 0f, Color.White);
        }

        // 上と下の情報
        private void DrawInfo(Chart chart, Course course)
        {
            FontManager.Draw(chart.Title, 40, 20, 48, Color.White);
            if (chart.Subtitle.Length > 0) FontManager.Draw(chart.Subtitle, 40, 80, 28, Color.LightGray);

            FontManager.Draw($"{Chart.GetCourseName(course.Type)} ★{course.Level}　音符 {course.Notes.Count}個　小節 {course.BarLines.Count}",
                40, 125, 28, Color.White);
            FontManager.Draw($"時間 {nowMs / 1000.0:F2} 秒　BPM {GetCurrentBpm(chart, course):0.##}", 40, 165, 28, Color.White);

            KeyboardKey restartKey = ConfigManager.Key.SystemKeys.GetValueOrDefault(SystemAction.QuickRestart, KeyboardKey.F5);
            FontManager.Draw($"{restartKey}：最初から　Esc：もどる　（テスト表示：まだ叩けません・音も出ません）", 40, 600, 28, Color.SkyBlue);
            FontManager.Draw($"ファイル：{path}", 40, 645, 22, Color.Gray);
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