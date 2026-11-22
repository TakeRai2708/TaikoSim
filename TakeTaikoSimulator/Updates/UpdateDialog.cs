using System.Diagnostics;

namespace TakeTaikoSimulator.Updates;

internal static class UpdateDialog
{
    internal static bool InstallerAppearance { get; set; }

    internal static bool Confirm(string heading, string text) =>
        Show(heading, text, "はい", "いいえ") == UpdateWindow.Accept;

    internal static string? ChooseInstallFolder(string text, string defaultFolder)
    {
        var window = new UpdateWindow("TakeTaiko をインストール", text, "インストール", "キャンセル", true)
        {
            Folder = defaultFolder
        };
        return window.Show() == UpdateWindow.Accept ? window.Folder : null;
    }

    internal static void Notice(string heading, string text) => Show(heading, text, "OK");

    internal static void RestartNotice()
    {
        var clock = Stopwatch.StartNew();
        var window = new UpdateWindow("ダウンロード完了しました", "再起動します。", null, null, InstallerAppearance);
        window.Tick = () =>
        {
            if (clock.ElapsedMilliseconds >= 3000)
                window.Complete();
        };
        window.Show();
    }

    internal static T Download<T>(Func<Action<long>, CancellationToken, Task<T>> action, long total)
    {
        long received = 0;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        string description = InstallerAppearance
            ? "ゲーム本体と必要なファイルをダウンロードしています。"
            : "受信した更新ファイルを検証して展開します。";
        var window = new UpdateWindow("ダウンロードしています", description,
            null, "中止", InstallerAppearance);
        Task<T> task = Task.Run(() => action(bytes => Interlocked.Exchange(ref received, bytes), cancel.Token));
        window.Progress = 0;
        window.Tick = () =>
        {
            window.Progress = (int)(Interlocked.Read(ref received) * 100 / total);
            if (task.IsCompleted)
                window.Complete();
        };

        try { window.Show(); }
        finally
        {
            if (!task.IsCompleted)
            {
                cancel.Cancel();
                // 一時フォルダーの削除前に、書き込みが止まるまで待つ。
                try { task.GetAwaiter().GetResult(); }
                catch (Exception) { }
            }
        }
        return task.GetAwaiter().GetResult();
    }

    private static int Show(string heading, string text, string primary, string? secondary = null) =>
        new UpdateWindow(heading, text, primary, secondary, InstallerAppearance).Show();
}
