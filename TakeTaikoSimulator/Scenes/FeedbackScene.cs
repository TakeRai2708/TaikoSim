using System.Diagnostics;
using Raylib_cs;
using TakeTaikoSimulator.Updates;

namespace TakeTaikoSimulator.Scenes;

public sealed class FeedbackScene : Scene
{
    private static string draft = "";

    public override void Enter()
    {
        var editor = new UpdateWindow("ご意見・不具合を書いてください", draft,
            "GitHubで送信", "戻る", true) { EditableBody = true };
        int result = editor.Show();
        draft = editor.BodyText;
        if (result == UpdateWindow.Accept && !string.IsNullOrWhiteSpace(draft))
        {
            string url = "https://github.com/TakeRai2708/TaikoSim/issues/new?title=" +
                Uri.EscapeDataString("TakeTaiko フィードバック") + "&body=" + Uri.EscapeDataString(draft);
            try
            {
                using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                UpdateDialog.Notice("送信画面を開けませんでした", "入力内容はこのゲームを閉じるまで保持します。もう一度お試しください。");
            }
        }
        SceneManager.Simple(new ModeSelectScene());
    }

    public override void Draw() => Raylib.ClearBackground(new Color(18, 10, 13, 255));
}
