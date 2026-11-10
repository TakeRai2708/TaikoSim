using System.Numerics;
using System.Text;
using Raylib_cs;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;

namespace TakeTaikoSimulator.UI;

// 名前入力は通常のドン操作から切り離す。IME確定のEnterで次の画面へ進まない。
public sealed class PlayerNameInput
{
    private static readonly Rectangle Box = new(520, 480, 880, 96);
    public string Value { get; private set; } = "";
    public string Error { get; set; } = "";
    private int confirmGuard;
    public void Open()
    {
        Value = ""; Error = ""; confirmGuard = 2;
        while (Raylib.GetCharPressed() != 0) { }
        WindowsIme.TakeResultText();
    }
    public bool Update()
    {
        WindowsIme.RequestTextInput(Box, new Vector2(Box.X + 25 + FontManager.Measure(Value, FittedSize(Value + WindowsIme.CompositionText)).X, Box.Y + 18));
        bool composing = WindowsIme.IsComposing;
        string result = WindowsIme.TakeResultText();
        int c;
        while ((c = Raylib.GetCharPressed()) > 0)
            if (Rune.IsValid(c) && !Rune.IsControl(new Rune(c))) Value += new Rune(c).ToString();
        Value += result;
        Value = NamePlateConfig.CleanText(Value, 24, trim: false);
        if (!composing && (Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && Value.Length > 0)
        {
            int[] positions = System.Globalization.StringInfo.ParseCombiningCharacters(Value);
            Value = Value[..positions[^1]];
        }
        if (composing || result.Length > 0) confirmGuard = 2;
        if (confirmGuard > 0) { confirmGuard--; return false; }
        return (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)) && Value.Trim().Length > 0;
    }
    private static float FittedSize(string text) => 40 * Math.Min(1, 830 / Math.Max(1, FontManager.Measure(text, 40).X));
    public void Draw()
    {
        Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 165));
        Raylib.DrawRectangleRounded(new(440, 250, 1040, 570), .02f, 8, new Color(13, 23, 38, 255));
        FontManager.Draw("プレイヤー名を入力", 520, 300, 48, Color.White);
        FontManager.Draw("24文字まで。あとからNamePlateConfig.jsonでも変更できます", 520, 390, 25, Color.LightGray);
        Raylib.DrawRectangleRounded(Box, .06f, 8, new Color(26, 40, 59, 255));
        string text = Value + WindowsIme.CompositionText;
        float size = FittedSize(text);
        FontManager.Draw(text, Box.X + 25, Box.Y + 18, size, Color.White);
        Raylib.DrawRectangle((int)(Box.X + 25 + FontManager.Measure(text, size).X), (int)Box.Y + 20, 2, 52, Color.SkyBlue);
        FontManager.Draw("Enter：作成　Esc：データ選択へ", 520, 660, 30, Color.SkyBlue);
        if (Error.Length > 0) FontManager.Draw(Error, 520, 730, 27, Color.Orange);
    }
}
