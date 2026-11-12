using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.Scenes
{
    // 演奏中のスコアランク(粋・雅・極)。スコアが境目を超えたら、そのランクの絵を3秒出す
    //   動きは OpenTaiko の CActImplScoreRank と同じ(下から浮かんで出る → ぽよんと大きく → 3秒で上へ消える)
    //   位置は OpenTaiko の初期値(720p の 87, 98)を 1.5倍した仮の値
    //   ※ TaikøNauts の動画(演奏中に50万点を超えたところ)では出ていなかった。要らなければ ShowInPlay を false に
    public sealed class PlayScoreRank
    {
        public static bool ShowInPlay = true;

        private const string ImagePath = "Image/04.EnsoGame/ScoreRankSymbol.png"; // 200×200 が横に7つ(粋の白・銅・銀、雅の金・桃・紫、極)
        private const float Size = 200f;
        private const float CenterX = 130f, CenterY = 147f;
        private const float MovePixels = 30f;      // 出るとき・消えるときに動く量(OpenTaiko の Game_Judge_Move 20 × 1.5)

        // OpenTaiko のカウンター(1ms ずつ 0〜3000)
        private const float InMs = 255f, PopMs = 180f, OutStartMs = 2745f, EndMs = 3000f;

        // ランクごとの「出てからの時間(ms)」。null ならまだ出ていない
        private readonly float?[] shownMs = new float?[PlayStats.ScoreRankCount];

        public void Reset() => Array.Clear(shownMs);

        public void Update(float dt, PlayStats stats)
        {
            for (int i = 0; i < shownMs.Length; i++)
            {
                if (shownMs[i] is float ms) shownMs[i] = Math.Min(EndMs, ms + dt * 1000f);
                else if (stats.Score >= stats.ScoreRankBorders[i]) shownMs[i] = 0f; // 超えた
            }
        }

        public void Draw()
        {
            if (!ShowInPlay || SkinManager.GetTexture(ImagePath) is not Texture2D tex) return;
            for (int i = 0; i < shownMs.Length; i++)
            {
                if (shownMs[i] is not float v || v >= EndMs) continue;

                float alpha = 1f, scale = 1f, y = 0f;
                if (v <= InMs)
                {
                    alpha = v / InMs;
                    y = (1f - v / InMs) * MovePixels;              // 下から上がってくる
                }
                else if (v <= InMs + PopMs)
                {
                    scale = 1f + MathF.Sin((v - InMs) * MathF.PI / 180f) * 0.2f; // ぽよん(最大1.2倍)
                }
                else if (v >= OutStartMs)
                {
                    float t = (v - OutStartMs) / (EndMs - OutStartMs);
                    alpha = 1f - t;
                    y = -t * MovePixels;                           // 上へ消える
                }

                float size = Size * scale;
                Raylib.DrawTexturePro(tex, new Rectangle(i * Size, 0, Size, Size),
                    new Rectangle(CenterX - size / 2f, CenterY + y - size / 2f, size, size),
                    Vector2.Zero, 0f, Raylib.ColorAlpha(Color.White, alpha));
            }
        }
    }
}