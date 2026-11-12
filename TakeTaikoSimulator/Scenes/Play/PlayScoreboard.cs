using System.Globalization;
using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.Scenes
{
    // 良・可・不可・連打数の板(TaikøNauts は左上。TakeTaikoSimulator は左下に出す)
    //   大きさ・数字の位置は TaikøNauts の動画で測った値(板はスキンの画像そのままの大きさ)
    public sealed class PlayScoreboard
    {
        private const string BaseImage = "Image/04.EnsoGame/Scoreboard/Base.png";     // 244×160 が縦2段(上:1P 赤、下:2P 水色)
        private const string NumberImage = "Image/04.EnsoGame/Scoreboard/Number.png"; // 20×23 が横10

        // 板の左上(左下の隅。見えている板の部分が、左から約25px・下から約25px になる)
        private const float BoardX = 8f, BoardY = 920f;
        private const float BoardWidth = 244f, BoardHeight = 160f;
        // 板は少し透けている(TaikøNauts の動画で、後ろのキャラが透けて見えた。濃さは見た目で合わせた推測の値)。数字は透けない
        private const float BoardAlpha = 0.8f;
        // 数字：真ん中そろえ。真ん中の x と、行ごとの上端(板の左上から)
        private const float NumberCenterX = 176f, DigitWidth = 20f, DigitHeight = 23f, DigitStep = 16f;
        private static readonly float[] RowY = { 23f, 49f, 74f, 100f };

        private readonly string[] texts = { "0", "0", "0", "0" };
        private readonly int[] values = new int[4];

        // 良・可・不可・連打数(変わったときだけ文字を作り直す)
        public void Set(int good, int ok, int bad, int rolls)
        {
            SetValue(0, good);
            SetValue(1, ok);
            SetValue(2, bad);
            SetValue(3, rolls);
        }

        private void SetValue(int row, int value)
        {
            if (values[row] == value && texts[row].Length > 0) return;
            values[row] = value;
            texts[row] = value.ToString(CultureInfo.InvariantCulture);
        }

        public void Draw()
        {
            if (SkinManager.GetTexture(BaseImage) is not Texture2D board) return;
            Raylib.DrawTexturePro(board, new Rectangle(0, 0, BoardWidth, BoardHeight),
                new Rectangle(BoardX, BoardY, BoardWidth, BoardHeight), Vector2.Zero, 0f, Raylib.ColorAlpha(Color.White, BoardAlpha));

            Texture2D? digits = SkinManager.GetTexture(NumberImage);
            for (int row = 0; row < texts.Length; row++)
            {
                string text = texts[row];
                float width = (text.Length - 1) * DigitStep + DigitWidth;
                float x = BoardX + NumberCenterX - width / 2f;
                float y = BoardY + RowY[row];
                if (digits is not Texture2D tex)
                {
                    FontManager.Draw(text, x, y, 22, Color.White);
                    continue;
                }
                for (int i = 0; i < text.Length; i++)
                {
                    int digit = text[i] - '0';
                    Raylib.DrawTexturePro(tex, new Rectangle(digit * DigitWidth, 0, DigitWidth, DigitHeight),
                        new Rectangle(x + i * DigitStep, y, DigitWidth, DigitHeight), Vector2.Zero, 0f, Color.White);
                }
            }
        }
    }
}