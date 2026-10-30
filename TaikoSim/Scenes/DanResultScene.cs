using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Charts;
using TaikoSim.Diagnostics;
using TaikoSim.Graphics;
using TaikoSim.Input;
using TaikoSim.UI;
using static TaikoSim.UI.MenuDraw;

namespace TaikoSim.Scenes
{
    // 段位リザルト画面
    //   条件ごとの達成状況と、不合格／赤合格／金合格を出す。
    //   合格したときは、dan.json の shodanTextPaths（昇段演出のテキスト画像）があればそれを重ねる。
    //   ドン・Enter・Esc で段位選択画面へ（何もしなければ 25秒で戻る）。
    public sealed class DanResultScene : Scene
    {
        private readonly DanPlay dan;
        private readonly DanResultType result;

        private float age;
        private const float AutoBackSeconds = 25f;

        // 演出の時間（TaikøNauts の実機は測れていないので、見ていて気持ちいい値にした）
        private const float TitleIn = 0.35f;           // 段位名が出るまで
        private const float RowDelay = 0.18f;          // 条件が1つずつ出る間隔
        private const float RowStart = 0.75f;          // 1つ目の条件が出る時間
        private const float VerdictDelay = 0.5f;       // 条件が全部出たあと、合否が出るまで
        private const float ShodanDelay = 0.6f;        // 合否のあと、昇段演出が出るまで

        private readonly List<Texture2D> shodanTextures = new();
        private Texture2D plate;

        private const float PanelX = 420f, PanelY = 250f, PanelWidth = 1080f;

        public DanResultScene(DanPlay dan)
        {
            this.dan = dan;
            result = dan.Overall();
        }

        public override void Enter()
        {
            age = 0f;
            LoadImages();
            DebugLog.Event("段位", $"{dan.Course.Title}：{Verdict(result)}　ゲージ {dan.Gauge:0.#}%");
        }

        public override void Exit()
        {
            foreach (Texture2D t in shodanTextures) if (t.Id != 0) Raylib.UnloadTexture(t);
            shodanTextures.Clear();
            if (plate.Id != 0) { Raylib.UnloadTexture(plate); plate = default; }
        }

        private void LoadImages()
        {
            // 段位プレート
            if (dan.Course.PlatePath.Length > 0) plate = LoadFrom(dan.Course.PlatePath);

            // 昇段演出のテキスト画像（合格したときだけ）
            if (result is DanResultType.Red or DanResultType.Gold)
            {
                foreach (string rel in dan.Course.ShodanTextFor(result == DanResultType.Gold))
                {
                    Texture2D t = LoadFrom(rel);
                    if (t.Id != 0) shodanTextures.Add(t);
                }
            }
        }

        private Texture2D LoadFrom(string relative)
        {
            try
            {
                string full = Path.Combine(dan.Course.FolderPath, relative);
                if (File.Exists(full)) return Raylib.LoadTexture(full);
            }
            catch { /* 読めなければ画像なしで出す */ }
            return default;
        }

        public override void OnBack() => Back();

        private static void Back() => SceneManager.Simple(new DanSelectScene());

        public override void Update(float dt)
        {
            age += dt;
            if (age >= AutoBackSeconds) { Back(); return; }

            // 演出が終わる前に押したら、演出を飛ばす。終わっていたら段位選択へ
            bool pressed = InputManager.IsDonPressed()
                           || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
            if (!pressed) return;

            if (age < FullyShownAt)
            {
                age = FullyShownAt;
                MenuSounds.Ka();
            }
            else
            {
                MenuSounds.Don();
                Back();
            }
        }

        // 全部出きる時間
        private float FullyShownAt =>
            RowStart + (RowCount + 1) * RowDelay + VerdictDelay + (shodanTextures.Count > 0 ? ShodanDelay + 0.4f : 0f);

        // ゲージ＋条件の数
        private int RowCount => 1 + dan.Course.Exams.Count;

        // ===== 描画 =====

        public override void Draw()
        {
            Gui.BeginFrame();
            DrawBackground();
            DrawTitle();
            DrawRows();
            DrawVerdict();
            DrawShodan();
            DrawFooter();
        }

        private void DrawBackground()
        {
            Color top = result switch
            {
                DanResultType.Gold => new Color(70, 58, 18, 255),
                DanResultType.Red => new Color(66, 24, 30, 255),
                _ => new Color(22, 24, 34, 255),
            };
            Raylib.DrawRectangleGradientV(0, 0, Screen.Width, Screen.Height, top, new Color(10, 11, 18, 255));
        }

        private void DrawTitle()
        {
            float t = Math.Clamp(age / TitleIn, 0f, 1f);
            float y = 90f - (1f - EaseOut(t)) * 40f;

            if (plate.Id != 0)
            {
                float h = 120f;
                float w = h * plate.Width / Math.Max(1, plate.Height);
                Raylib.DrawTexturePro(plate, new Rectangle(0, 0, plate.Width, plate.Height),
                    new Rectangle(PanelX - 170f, y - 10f, w, h), System.Numerics.Vector2.Zero, 0f, Fade(Color.White, t));
            }

            Outlined(dan.Course.Title, new Rectangle(PanelX, y, PanelWidth, 90f), 72,
                Fade(Color.White, t), Fade(new Color(12, 12, 16, 255), t));
        }

        // 条件ごとの達成状況
        private void DrawRows()
        {
            float y = PanelY;
            int index = 0;

            DrawRow(ref y, index++, "魂ゲージ", $"{dan.Gauge:0.#}%",
                GaugeNeedText(), dan.GaugeStatus());

            foreach (DanExam exam in dan.Course.Exams)
            {
                string value = exam.PerSong
                    ? string.Join(" / ", Enumerable.Range(0, Math.Min(dan.SongCount, exam.Thresholds.Length))
                        .Select(i => dan.Tallies[i].Value(exam.Type).ToString()))
                    : dan.Total(exam.Type).ToString();

                string need = exam.PerSong
                    ? string.Join(" / ", exam.Thresholds.Select(t => t.Red.ToString())) + (exam.IsLess ? " 未満" : " 以上")
                    : $"{exam.Thresholds[0].Red} {(exam.IsLess ? "未満" : "以上")}";

                DrawRow(ref y, index++, exam.Label, value, need, dan.Status(exam));
            }
        }

        private string GaugeNeedText()
        {
            DanThreshold g = dan.Course.Gauge;
            if (g.Red <= 0) return "条件なし";
            return dan.Course.HasGoldGauge ? $"{g.Red}% 以上（金 {g.Gold}%）" : $"{g.Red}% 以上";
        }

        private void DrawRow(ref float y, int index, string label, string value, string need, DanResultType status)
        {
            float appear = RowStart + index * RowDelay;
            if (age < appear) { y += 62f; return; }

            float t = Math.Clamp((age - appear) / 0.22f, 0f, 1f);
            float x = PanelX - (1f - EaseOut(t)) * 40f;
            float alpha = t;

            Rectangle row = new(x, y, PanelWidth, 54f);
            Raylib.DrawRectangleRounded(row, Roundness(row, 12f), 8, Fade(new Color(255, 255, 255, 26), alpha));

            Color mark = status switch
            {
                DanResultType.Gold => new Color(255, 215, 90, 255),
                DanResultType.Red => new Color(255, 120, 130, 255),
                _ => new Color(150, 155, 170, 255),
            };
            string markText = status == DanResultType.Failed ? "×" : "○";

            Text(markText, x + 18f, y + 10f, 34, Fade(mark, alpha));
            Text(label, x + 66f, y + 13f, 28, Fade(Color.White, alpha));
            Text(value, x + 330f, y + 13f, 28, Fade(mark, alpha));
            Text(need, x + 640f, y + 15f, 24, Fade(new Color(200, 206, 222, 255), alpha));
            if (status == DanResultType.Gold)
                Text("金", x + PanelWidth - 56f, y + 13f, 28, Fade(new Color(255, 215, 90, 255), alpha));

            y += 62f;
        }

        private void DrawVerdict()
        {
            float appear = RowStart + (RowCount + 1) * RowDelay + VerdictDelay;
            if (age < appear) return;

            float t = Math.Clamp((age - appear) / 0.3f, 0f, 1f);
            float scale = 1f + (1f - EaseOut(t)) * 0.4f;
            float size = 96f * scale;

            Color color = result switch
            {
                DanResultType.Gold => new Color(255, 215, 90, 255),
                DanResultType.Red => new Color(255, 110, 120, 255),
                _ => new Color(190, 196, 212, 255),
            };

            float y = PanelY + RowCount * 62f + 40f;
            Outlined(Verdict(result), new Rectangle(PanelX, y, PanelWidth, size + 20f), size,
                Fade(color, t), Fade(new Color(12, 12, 16, 255), t));
        }

        // 昇段演出のテキスト画像（あれば）
        private void DrawShodan()
        {
            if (shodanTextures.Count == 0) return;
            float appear = RowStart + (RowCount + 1) * RowDelay + VerdictDelay + ShodanDelay;
            if (age < appear) return;

            float t = Math.Clamp((age - appear) / 0.4f, 0f, 1f);
            // 複数枚あるときは、横に並べて順に出す
            float gap = 16f;
            float totalWidth = shodanTextures.Sum(x => (float)x.Width) + gap * (shodanTextures.Count - 1);
            float scale = Math.Min(1f, (Screen.Width - 160f) / Math.Max(1f, totalWidth));
            float x = (Screen.Width - totalWidth * scale) / 2f;
            float y = Screen.Height - 260f;

            for (int i = 0; i < shodanTextures.Count; i++)
            {
                Texture2D tex = shodanTextures[i];
                float each = Math.Clamp((age - appear - i * 0.12f) / 0.4f, 0f, 1f);
                float w = tex.Width * scale, h = tex.Height * scale;
                Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
                    new Rectangle(x, y + (1f - EaseOut(each)) * 24f, w, h),
                    System.Numerics.Vector2.Zero, 0f, Fade(Color.White, each));
                x += w + gap * scale;
            }
            _ = t;
        }

        private void DrawFooter()
        {
            string hint = age < FullyShownAt ? "ドン・Enter：演出を飛ばす" : "ドン・Enter・Esc：段位選択へ";
            var measured = Measure(hint, 24);
            Raylib.DrawRectangleRounded(new Rectangle(28, 1023, measured.X + 32, measured.Y + 14), 0.5f, 8, new Color(0, 0, 0, 150));
            Text(hint, 44, 1030, 24, new Color(220, 224, 236, 255));

            // 何曲目まで行ったか（途中で終わったときに分かるように）
            string songs = $"{Math.Min(dan.SongIndex + 1, dan.SongCount)} / {dan.SongCount} 曲";
            Text(songs, Screen.Width - 180f, 1030f, 24, new Color(200, 206, 222, 255));
        }

        private static string Verdict(DanResultType result) => result switch
        {
            DanResultType.Gold => "金合格",
            DanResultType.Red => "合格",
            _ => "不合格",
        };
    }
}
