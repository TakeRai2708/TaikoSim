namespace TakeTaikoSimulator.Updates;

// インストーラー・更新の画面を出す入り口（見た目は UpdateWindow）
internal static class UpdateDialog
{
    internal static bool InstallerAppearance { get; set; }
    // インストーラーの左の進み具合（0 インストール先・1 ダウンロード・2 完了）と下の帯
    internal static int InstallerStep { get; set; }
    internal static string[]? InstallerFooter { get; set; }

    internal static bool Confirm(string heading, string text) =>
        Ask(heading, null, text, "はい", "いいえ");

    // 見出し・説明（見出しの下の小さい字）・本文・ボタンの文字・札の記号を決めて聞く
    internal static bool Ask(string heading, string? subtitle, string text, string primary, string secondary, char? icon = null) =>
        Create(heading, subtitle, text, primary, secondary, icon).Show() == UpdateWindow.Accept;

    internal static (string Folder, bool DesktopShortcut)? ChooseInstallFolder(
        string text, string defaultFolder, bool desktopShortcut)
    {
        InstallerStep = 0;
        var window = Create("インストール先", "TakeTaiko を入れる場所を選んでください", "", "インストール", "やめる", UpdateWindow.IconFolder);
        window.Folder = defaultFolder;
        window.DesktopShortcut = desktopShortcut;
        return window.Show() == UpdateWindow.Accept && window.Folder is { } folder
            ? (folder, window.DesktopShortcut) : null;
    }

    internal static void Notice(string heading, string text) =>
        Create(heading, null, text, "OK", null, UpdateWindow.IconInfo).Show();

    internal static T Download<T>(Func<Action<long>, CancellationToken, Task<T>> action, long total)
    {
        long received = 0;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        if (InstallerAppearance) InstallerStep = 1;
        var window = Create("ダウンロード中", null, "", null, "キャンセル", UpdateWindow.IconDownload);
        Task<T> task = Task.Run(() => action(bytes => Interlocked.Exchange(ref received, bytes), cancel.Token));
        window.Progress = 0;
        window.ProgressText = Megabytes(0, total);
        window.Tick = () =>
        {
            long now = Interlocked.Read(ref received);
            window.Progress = total > 0 ? (int)(now * 100 / total) : 0;
            window.ProgressText = Megabytes(now, total);
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

    internal static string Megabytes(long bytes, long total) =>
        $"{bytes / 1048576.0:0.0} / {total / 1048576.0:0.0} MB";

    private static UpdateWindow Create(string heading, string? subtitle, string text, string? primary, string? secondary, char? icon) =>
        new(heading, text, primary, secondary, InstallerAppearance)
        {
            Subtitle = subtitle,
            Icon = icon,
            Step = InstallerStep,
            Footer = InstallerAppearance ? InstallerFooter : null
        };
}
