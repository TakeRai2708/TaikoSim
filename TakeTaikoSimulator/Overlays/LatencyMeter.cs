using System.Diagnostics;
using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator.Overlays
{
    // 「音の遅れを測る」画面(設定 → 音 から開く)
    //   1. 流れてくる音符に合わせて叩く … 目で見て叩いたときのズレ(指の遅れ＋画面の遅れ)
    //   2. 音に合わせて叩く … 耳で聞いて叩いたときのズレ(指の遅れ＋まだ合っていない音の遅れ)
    //   2 − 1 ＝ 曲の時計に残っている音のズレ。これを今の「遅れの補正」に足す
    //   音は効果音ではなく、曲と同じ鳴らし方(Song)でカチッを流す。曲の時計(PositionMs)で測るので、
    //   今の補正(機器の遅れ・バッファ・遅れの補正)を全部通したあとのズレがそのまま出る
    public sealed class LatencyMeter
    {
        private enum Step { Off, Ready1, Visual, Ready2, Audio, Result }

        private const int Beats = 16;           // 叩く回数
        private const int SkipBeats = 2;        // 最初の何回かは慣らしで使わない
        private const double LeadMs = 1500;     // 始まってから1回目までの間
        private const double IntervalMs = 600;  // 100 BPM
        private const double MatchMs = 250;     // これより離れた叩きは数えない
        private const double MaxSpreadMs = 35;  // ばらつき(中央値からの平均の離れ)がこれより大きいとやり直し
        private const int MinValid = 8;

        private Step step = Step.Off;
        private readonly Stopwatch clock = new();
        private Song? click;
        private readonly double?[] taps = new double?[Beats];
        private double visualMs, audioMs;
        private string error = "";
        private int resultFix;

        public bool IsActive => step != Step.Off;

        // 結果を使うとき(新しい補正値)。設定画面が受け取る
        public Action<int>? OnApply;

        public void Open()
        {
            error = "";
            step = Step.Ready1;
        }

        public void Close()
        {
            StopClick();
            step = Step.Off;
        }

        public void Update()
        {
            if (step == Step.Off) return;
            if (InputManager.IsBackPressed()) { Close(); return; }

            bool hit = InputManager.IsDonPressed() || InputManager.IsKaPressed() || Raylib.IsKeyPressed(KeyboardKey.Space);
            switch (step)
            {
                case Step.Ready1:
                case Step.Ready2:
                    if (hit) Begin(step == Step.Ready1 ? Step.Visual : Step.Audio);
                    break;

                case Step.Visual:
                case Step.Audio:
                    double now = Now();
                    if (hit) Record(now);
                    if (now > LeadMs + (Beats - 1) * IntervalMs + 800) Finish();
                    break;

                case Step.Result:
                    if (hit) Open();
                    break;
            }
        }

        // 今の時刻(ms)。光は時計、音は曲の時計(補正込み)
        private double Now() => step == Step.Audio && click != null ? click.PositionMs : clock.Elapsed.TotalMilliseconds;

        private void Begin(Step next)
        {
            Array.Clear(taps);
            error = "";
            if (next == Step.Audio)
            {
                if (!PrepareClick()) { step = Step.Ready2; return; }
                click!.SetVolume((float)ConfigManager.Sound.GetFinalVolume(ConfigManager.Sound.MusicVolume));
                click.Play(0);
            }
            clock.Restart();
            step = next;
        }

        // 一番近いカチッ(光)に結びつける
        private void Record(double now)
        {
            int beat = (int)Math.Round((now - LeadMs) / IntervalMs);
            if (beat < 0 || beat >= Beats) return;
            double delta = now - (LeadMs + beat * IntervalMs);
            if (Math.Abs(delta) > MatchMs) return;
            if (taps[beat] == null || Math.Abs(delta) < Math.Abs(taps[beat]!.Value)) taps[beat] = delta;
        }

        private void Finish()
        {
            bool visual = step == Step.Visual;
            StopClick();
            List<double> list = taps.Skip(SkipBeats).Where(t => t != null).Select(t => t!.Value).ToList();
            if (list.Count < MinValid)
            {
                error = $"叩けた回数が少なかったよ（{list.Count}回）。もう一回やってね";
                step = visual ? Step.Ready1 : Step.Ready2;
                return;
            }
            double median = Median(list);
            double spread = list.Average(t => Math.Abs(t - median));
            if (spread > MaxSpreadMs)
            {
                error = $"ばらつきが大きかったよ（±{spread:0}ms）。もう一回やってね";
                step = visual ? Step.Ready1 : Step.Ready2;
                return;
            }

            if (visual) { visualMs = median; step = Step.Ready2; return; }

            audioMs = median;
            int current = ConfigManager.Sound.GetLatencyFix(AudioManager.CurrentType);
            resultFix = Math.Clamp((int)Math.Round(current + (audioMs - visualMs)), SoundConfig.LatencyFixMin, SoundConfig.LatencyFixMax);
            step = Step.Result;
        }

        private static double Median(List<double> values)
        {
            values.Sort();
            int n = values.Count;
            return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
        }

        // カチッを16回鳴らす wav を作って、曲として読む(出力方式を変えたら作り直す)
        private bool PrepareClick()
        {
            StopClick();
            try
            {
                string folder = Path.Combine(AppContext.BaseDirectory, "Cache");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "LatencyClick.wav");
                File.WriteAllBytes(path, MakeClickWav());
                click = Song.Load(path, out string loadError);
                if (click == null) { error = $"カチッの音を読めなかった：{loadError}"; return false; }
                return true;
            }
            catch (IOException ex)
            {
                error = $"カチッの音を作れなかった：{ex.Message}";
                return false;
            }
        }

        private void StopClick()
        {
            click?.Stop();
            click?.Dispose();
            click = null;
        }

        // 44100Hz・モノラル・16bit。2kHz の短い音(20ms、すぐ小さくなる)を間隔ごとに置く
        private static byte[] MakeClickWav()
        {
            const int rate = 44100;
            double totalMs = LeadMs + Beats * IntervalMs + 1000;
            int samples = (int)(rate * totalMs / 1000);
            short[] data = new short[samples];
            int clickLen = rate * 20 / 1000;
            for (int b = 0; b < Beats; b++)
            {
                int start = (int)Math.Round((LeadMs + b * IntervalMs) * rate / 1000);
                for (int i = 0; i < clickLen && start + i < samples; i++)
                {
                    double env = Math.Exp(-i / (rate * 0.004));
                    data[start + i] = (short)(Math.Sin(2 * Math.PI * 2000 * i / rate) * env * 26000);
                }
            }
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int bytes = samples * 2;
            w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(bytes);
            foreach (short s in data) w.Write(s);
            w.Flush();
            return ms.ToArray();
        }

        public void Draw()
        {
            if (step == Step.Off) return;
            Raylib.DrawRectangle(0, 0, Screen.Width, Screen.Height, new Color(0, 0, 0, 170));

            Rectangle box = new Rectangle(460, 220, 1000, 640);
            SmoothShapes.Rectangle(box, 20, Gui.PanelColor);
            SmoothShapes.Border(box, 20, 1.5f, Gui.BorderColor);
            FontManager.Draw($"音の遅れを測る（{AudioManager.GetTypeName(AudioManager.CurrentType)}）", box.X + 40, box.Y + 32, 34, Gui.SakuraColor);

            Vector2 center = new Vector2(box.X + box.Width / 2, box.Y + 330);
            switch (step)
            {
                case Step.Ready1:
                    Lines(box, "1 / 2：音符に合わせて叩く",
                        "流れてくる音符が、左の枠に重なった瞬間に叩いてね（16回）。",
                        "叩くと始まるよ。");
                    DrawLane(box, -10000);
                    break;

                case Step.Ready2:
                    Lines(box, "2 / 2：音に合わせて叩く",
                        "カチッという音に合わせて叩いてね（16回）。画面は見なくていいよ。",
                        "叩くと始まるよ。");
                    DrawCircle(center, 0);
                    break;

                case Step.Visual:
                case Step.Audio:
                    {
                        double now = Now();
                        int beat = (int)Math.Floor((now - LeadMs) / IntervalMs);
                        float since = (float)(now - LeadMs - beat * IntervalMs);
                        float light = step == Step.Visual && beat >= 0 && beat < Beats ? Math.Max(0f, 1f - since / 150f) : 0f;
                        int done = Math.Clamp(beat + 1, 0, Beats);
                        Lines(box, step == Step.Visual ? "音符に合わせて叩いて！" : "音に合わせて叩いて！", $"{done} / {Beats}", "");
                        if (step == Step.Visual) DrawLane(box, now);
                        else DrawCircle(center, light);
                        break;
                    }

                case Step.Result:
                    {
                        double diff = audioMs - visualMs;
                        string dir = diff > 0 ? "遅れて" : "早く";
                        Lines(box, "測り終わったよ！",
                            $"音符とのズレ {visualMs:+0;-0;0}ms ／ 音とのズレ {audioMs:+0;-0;0}ms",
                            $"曲が {Math.Abs(diff):0}ms {dir}聞こえていたので、遅れの補正を {resultFix}ms にするといいよ");
                        Rectangle use = new Rectangle(box.X + 40, box.Y + 540, 440, 60);
                        Rectangle again = new Rectangle(box.X + box.Width - 480, box.Y + 540, 440, 60);
                        if (Gui.Button(use, $"{resultFix}ms にする"))
                        {
                            OnApply?.Invoke(resultFix);
                            Close();
                            return;
                        }
                        if (Gui.Button(again, "もう一回（ドン）")) Open();
                        break;
                    }
            }

            if (error.Length > 0) FontManager.Draw(error, box.X + 40, box.Y + 470, 24, new Color(255, 120, 120, 255));
            FontManager.Draw("Esc：やめる", box.X + 40, box.Y + box.Height - 36, 20, Gui.MutedTextColor);
        }

        private static void Lines(Rectangle box, string title, string line1, string line2)
        {
            FontManager.Draw(title, box.X + 40, box.Y + 100, 30, Gui.TextColor);
            FontManager.Draw(line1, box.X + 40, box.Y + 150, 24, Gui.SubTextColor);
            if (line2.Length > 0) FontManager.Draw(line2, box.X + 40, box.Y + 185, 24, Gui.SubTextColor);
        }

        // 1/2 の画面：演奏と同じように、右から音符が流れてきて左の枠に重なる
        //   11/19 たけらい：光るだけだと「むずい」。光ってから叩く(反応の遅れ +130ms くらい)になって測れなかった。
        //   流れてくる音符なら、重なる瞬間を先読みして叩けるので、演奏中と同じ見え方で測れる
        private const float LaneSpeed = 0.55f; // px/ms
        private static void DrawLane(Rectangle box, double now)
        {
            float y = box.Y + 330, left = box.X + 40, right = box.X + box.Width - 40;
            float judgeX = left + 90;
            SmoothShapes.Rectangle(new Rectangle(left, y - 60, right - left, 120), 12, Gui.BoxColor);
            SmoothShapes.CircleLines(new Vector2(judgeX, y), 46, Gui.SakuraColor);
            for (int b = 0; b < Beats; b++)
            {
                double dt = LeadMs + b * IntervalMs - now;
                float x = judgeX + (float)(dt * LaneSpeed);
                if (x < left + 20 || x > right - 20) continue;
                if (dt < -60) continue; // 通り過ぎた音符は消す
                SmoothShapes.Circle(new Vector2(x, y), 38, Gui.AccentColor);
                SmoothShapes.CircleLines(new Vector2(x, y), 38, Color.White);
            }
        }

        private static void DrawCircle(Vector2 center, float light)
        {
            if (light > 0f) Gui.GlowCircle(center, 70, Gui.AccentColor, light);
            SmoothShapes.Circle(center, 70, Gui.Lerp(Gui.TrackColor, Gui.AccentColor, light));
        }
    }
}
