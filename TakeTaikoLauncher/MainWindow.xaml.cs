using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace TakeTaikoLauncher;

public partial class MainWindow : Window
{
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "launcher-feed.json");
    private static readonly string StoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TakeTaikoLauncher");
    private static readonly string Downloads = Path.Combine(StoragePath, "Downloads");

    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private CancellationTokenSource? operation;
    private GameRelease? offeredRelease;
    private bool closeAfterOperation;

    public MainWindow()
    {
        InitializeComponent();
        LauncherVersion.Text = $"ランチャー {Assembly.GetExecutingAssembly().GetName().Version}";
        RefreshGameVersion();
        Activated += (_, _) => RefreshGameVersion();
        Status.Text = File.Exists(SettingsPath)
            ? "配布元の更新情報を確認できます。"
            : "更新の配布先が設定されていません。";
    }

    private Version? RefreshGameVersion()
    {
        bool exists = File.Exists(GameInstallation.Executable);
        PlayButton.IsEnabled = exists;
        Version? version = null;
        try
        {
            version = GameInstallation.ReadVersion();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            // バージョンを読めなくても、手動起動はできるようにする。
        }

        GameVersion.Text = version is not null ? $"インストール済み {version}" :
            exists ? "バージョンを取得できません" : "同じフォルダにゲームが見つかりません";
        return version;
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var game = GameInstallation.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "ゲームを起動できませんでした。ゲーム本体と配布時のDLLを確認してください。",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        if (operation is not null)
            return;

        offeredRelease = null;
        OpenButton.Visibility = Visibility.Collapsed;
        DownloadProgress.Visibility = Visibility.Collapsed;
        if (!File.Exists(SettingsPath))
        {
            Status.Text = "更新の配布先が設定されていません。";
            ShowOffer();
            return;
        }

        var feed = new UpdateFeed(http, Downloads, Path.Combine(StoragePath, "Temp"));
        await RunAsync(TimeSpan.FromSeconds(30), "更新情報を確認中…", false, async token =>
        {
            var release = await feed.CheckAsync(SettingsPath, token);
            if (NeedsRelease(release))
            {
                offeredRelease = release;
                Status.Text = $"更新版 {release.Version} があります。ZIP を保存できます。";
            }
        });
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (operation is not null || offeredRelease is not { } release)
            return;
        if (!NeedsRelease(release))
        {
            offeredRelease = null;
            ShowOffer();
            return;
        }

        var feed = new UpdateFeed(http, Downloads, Path.Combine(StoragePath, "Temp"));
        DownloadProgress.Maximum = release.Size;
        DownloadProgress.Value = 0;
        DownloadProgress.Visibility = Visibility.Visible;
        await RunAsync(TimeSpan.FromMinutes(30), "ダウンロードを開始しています…", true, async token =>
        {
            await feed.DownloadAsync(release, received =>
            {
                DownloadProgress.Value = received;
                Status.Text = received == release.Size
                    ? "受信完了。配布ファイルを検証しています…"
                    : $"{100.0 * received / release.Size:0.0}%（{received:N0} / {release.Size:N0} バイト）";
            }, token);

            offeredRelease = null;
            OpenButton.Visibility = Visibility.Visible;
            Status.Text = $"版 {release.Version} の検証済み ZIP を保存しました。適用は手動です。";
        });
        DownloadProgress.Visibility = Visibility.Collapsed;
    }

    private bool NeedsRelease(GameRelease release)
    {
        Version? installed = RefreshGameVersion();
        if (installed is null && PlayButton.IsEnabled)
        {
            Status.Text = "ゲーム本体のバージョンを読み取れず、更新の要否を判定できません。";
            return false;
        }
        if (installed is not null && installed >= release.Version)
        {
            Status.Text = "利用できる新しい更新はありません。";
            return false;
        }
        return true;
    }

    private async Task RunAsync(TimeSpan timeout, string initialMessage, bool canCancel, Func<CancellationToken, Task> action)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var userCancellation = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, userCancellation.Token);
        operation = userCancellation;
        CheckButton.IsEnabled = false;
        CheckButton.Content = "更新を確認";
        Status.Text = initialMessage;
        DownloadButton.Visibility = Visibility.Collapsed;
        LaterButton.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        LaterButton.Content = "中止";

        try
        {
            await action(linked.Token);
        }
        catch (OperationCanceledException)
        {
            Status.Text = timeoutSource.IsCancellationRequested
                ? "時間内に完了しませんでした。再試行できます。"
                : "ダウンロードを中止しました。";
            CheckButton.Content = "再試行";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or
            CryptographicException or ArgumentException or JsonException or FormatException or
            InvalidOperationException or KeyNotFoundException)
        {
            // HTTP 例外の詳細には、署名付き URL が含まれる場合がある。
            Status.Text = ex switch
            {
                InvalidDataException => ex.Message,
                HttpRequestException => "配布先に接続できませんでした。接続とアクセス権を確認してください。",
                IOException or UnauthorizedAccessException => "ファイルを読み書きできませんでした。空き容量とアクセス権を確認してください。",
                _ => "更新情報の形式または配布元の公開鍵を確認できませんでした。"
            };
            offeredRelease = null;
            CheckButton.Content = "再試行";
        }
        finally
        {
            operation = null;
            CheckButton.IsEnabled = true;
            ShowOffer();
            if (closeAfterOperation)
                Close();
        }
    }

    private void ShowOffer()
    {
        DownloadButton.Visibility = offeredRelease is null ? Visibility.Collapsed : Visibility.Visible;
        LaterButton.Visibility = DownloadButton.Visibility;
        LaterButton.Content = "後で";
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        offeredRelease = null;
        if (operation is not null)
        {
            operation.Cancel();
            return;
        }
        Status.Text = "更新を保留しました。このままゲームを起動できます。";
        ShowOffer();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var folder = Process.Start(new ProcessStartInfo(Downloads) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            Status.Text = "保存先を開けませんでした。";
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (operation is not null)
        {
            e.Cancel = true;
            closeAfterOperation = true;
            operation.Cancel();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        http.Dispose();
        base.OnClosed(e);
    }
}
