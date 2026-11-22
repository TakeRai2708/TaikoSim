using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TakeTaikoSimulator.Updates;

internal static class StartupUpdate
{
    private const string ApplyMode = "--apply-taketaiko-update";
    private const string RestartMode = "--finish-taketaiko-update";
    private const string WorkerExe = "TakeTaikoUpdateWorker.exe";

    internal static bool TryStartGame(string[] args, out IDisposable? applicationLock)
    {
        applicationLock = null;
        string root = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        bool setup = string.Equals(Path.GetFileName(Environment.ProcessPath), "TakeTaikoSetup.exe", StringComparison.OrdinalIgnoreCase);
        bool ownsStage = false;
        string? stage = null;
        bool restart = args.Length > 0 && args[0] == RestartMode;
        bool worker = args.Length > 0 && args[0] == ApplyMode;
        bool updated = false;
        try
        {
            if (worker)
            {
                RunWorker(root, args);
                return false;
            }
            if (setup)
            {
                UpdateDialog.InstallerAppearance = true;
                FreeConsole();
                Install(root);
                return false;
            }

            applicationLock = Acquire(root, TimeSpan.Zero);
            if (applicationLock is null)
                return false;

            if (restart)
            {
                stage = GetRestartStage(root, args);
                RemoveStage(stage);
                return true;
            }

            if (HasInterruptedUpdate(root))
            {
                UpdateDialog.Notice("更新の復旧が必要です",
                    "前回の更新が途中で止まりました。起動を中止します。ゲームフォルダーの temp 内にバックアップが残っている場合があります。");
                applicationLock.Dispose();
                applicationLock = null;
                return false;
            }

            using var settings = OpenSettings(root);
            if (settings is null)
                return true;

            var installed = UpdatePackage.ReadVersion(Path.Combine(root, UpdatePackage.GameExe));
            using var http = CreateClient();
            var feed = new UpdateFeed(http);
            GameRelease release = ReadLatest(feed, settings);
            if (release.Version <= installed ||
                !UpdateDialog.Confirm("更新を確認しました",
                    $"バージョン {release.Version}\n\n{Notes(release)}\n\n更新しますか？"))
                return true;

            stage = CreateStage(root);
            ownsStage = true;
            Download(feed, release, stage, install: false);
            string workerPath = Path.Combine(stage, WorkerExe);
            File.Copy(Path.Combine(root, UpdatePackage.GameExe), workerPath);
            UpdateDialog.RestartNotice();
            using var current = Process.GetCurrentProcess();
            using var child = StartProcess(workerPath, stage, ApplyMode,
                current.Id.ToString(CultureInfo.InvariantCulture), current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
            updated = true;
            return false;
        }
        catch (OperationCanceledException)
        {
            if (setup)
                SafeNotice("インストールを完了できませんでした", "ダウンロードの中止または通信のタイムアウトが発生しました。");
            return !setup && !worker;
        }
        catch (Exception ex) when (ExpectedError(ex))
        {
            if (setup || worker || ownsStage)
                SafeNotice(setup ? "インストールできませんでした" : "更新できませんでした",
                    ex is RollbackFailedExceptionMarker ? "一部のファイルを元に戻せませんでした。temp 内のバックアップを保持しました。" :
                    ex is InvalidDataException ? ex.Message : "配布先への接続、空き容量、書き込み権限を確認してください。");
            else
                Console.WriteLine("[更新] 更新情報を確認できませんでした。");
            return !setup && !worker;
        }
        finally
        {
            if (ownsStage && !updated && stage is not null)
                RemoveStage(stage);
            if (setup || worker || updated)
            {
                applicationLock?.Dispose();
                applicationLock = null;
            }
        }
    }

    private static void Install(string setupDirectory)
    {
        using var settings = OpenSettings(setupDirectory);
        if (settings is null)
        {
            SafeNotice("配布先が設定されていません", "配布担当者から受け取った update-feed.json を、インストーラーの隣に置いてください。");
            return;
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
            throw new IOException("インストール先を取得できません。");
        using var http = CreateClient();
        var feed = new UpdateFeed(http);
        GameRelease release = ReadLatest(feed, settings);
        settings.Position = 0;
        byte[] installedSettings = new byte[checked((int)settings.Length)];
        settings.ReadExactly(installedSettings);
        string location = Path.Combine(local, "Programs", "TakeTaiko");
        string prompt = $"最新版 {release.Version}\n\n保存先を選んで「インストール」を押すと、ゲーム本体と必要なファイルをダウンロードします。";
        while (UpdateDialog.ChooseInstallFolder(prompt, location) is { } selected)
        {
            if (!Path.IsPathFullyQualified(selected))
            {
                SafeNotice("保存先を確認してください", "ドライブ名から始まるフォルダーの場所を指定してください。");
                continue;
            }
            location = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
            string drive = Path.GetPathRoot(location) ?? throw new IOException("インストール先を取得できません。");
            if (location.Equals(drive, StringComparison.OrdinalIgnoreCase))
            {
                SafeNotice("保存先を選び直してください", "ドライブの直下にある空のフォルダーを選んでください。");
                continue;
            }

            UpdatePackage.RejectLinks(drive, Path.GetRelativePath(drive, location).Replace('\\', '/'));
            using (var installLock = Acquire(location, TimeSpan.Zero))
            {
                if (installLock is null || (Directory.Exists(location) && Directory.EnumerateFileSystemEntries(location).Any()))
                {
                    SafeNotice("保存先を選び直してください", $"この TakeTaiko フォルダーにはすでにファイルがあります。「参照…」で作成場所を選んでください。\n\n{location}");
                    continue;
                }

                string stage = CreateStage(location);
                bool keepStage = false;
                try
                {
                    Download(feed, release, stage, install: true);
                    File.WriteAllBytes(Path.Combine(stage, "expanded", UpdatePackage.FeedFile), installedSettings);
                    ApplyWithMarker(location, stage, install: true);
                }
                catch (RollbackFailedExceptionMarker)
                {
                    keepStage = true;
                    throw;
                }
                finally
                {
                    if (!keepStage)
                        RemoveStage(stage);
                }
            }

            CreateStartMenuShortcut(location);
            string game = Path.Combine(location, UpdatePackage.GameExe);
            if (UpdateDialog.Confirm("インストールが完了しました", $"TakeTaiko を起動しますか？\n\n{location}"))
            {
                try { using var started = StartProcess(game, location); }
                catch (Exception ex) when (ExpectedError(ex))
                {
                    SafeNotice("TakeTaiko を起動できませんでした", $"インストール先から TakeTaiko.exe を開いてください。\n\n{location}");
                }
            }
            return;
        }
    }

    internal static string InstallLocationUnder(string parent)
    {
        for (int suffix = 1; suffix < 1000; suffix++)
        {
            string candidate = Path.Combine(parent, suffix == 1 ? "TakeTaiko" : $"TakeTaiko ({suffix})");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
        throw new IOException("TakeTaiko フォルダーを作成できる場所が見つかりません。");
    }

    private static void RunWorker(string stage, string[] args)
    {
        if (args.Length != 3 || !IsStageName(Path.GetFileName(stage)) ||
            !string.Equals(Path.GetFileName(Environment.ProcessPath), WorkerExe, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新の起動情報が無効です。");

        var temp = Directory.GetParent(stage) ?? throw new InvalidDataException();
        if (!temp.Name.Equals("temp", StringComparison.OrdinalIgnoreCase) || temp.Parent is null)
            throw new InvalidDataException();
        string root = temp.Parent.FullName;
        UpdatePackage.RejectLinks(root, "temp/" + Path.GetFileName(stage));

        WaitForProcess(args[1], args[2]);
        using (Acquire(root, TimeSpan.FromSeconds(20)) ?? throw new IOException("ゲームが起動中です。"))
        {
            if (UpdatePackage.ReadVersion(Path.Combine(stage, "expanded", UpdatePackage.GameExe)) <=
                UpdatePackage.ReadVersion(Path.Combine(root, UpdatePackage.GameExe)))
                throw new InvalidDataException("同じバージョンか、それより新しい版がすでにあります。");
            try { ApplyWithMarker(root, stage, install: false); }
            catch (RollbackFailedExceptionMarker) { throw; }
            catch
            {
                if (!File.Exists(Path.Combine(stage, "applying")))
                    RemoveStage(stage);
                throw;
            }
        }
        using var current = Process.GetCurrentProcess();
        using var game = StartProcess(Path.Combine(root, UpdatePackage.GameExe), root, RestartMode, Path.GetFileName(stage),
            current.Id.ToString(CultureInfo.InvariantCulture), current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
    }

    private static void ApplyWithMarker(string root, string stage, bool install)
    {
        string marker = Path.Combine(stage, "applying");
        File.WriteAllText(marker, "TakeTaikoSimulator");
        try
        {
            UpdatePackage.Apply(root, stage, install);
        }
        catch (UpdatePackage.RollbackFailedException)
        {
            throw new RollbackFailedExceptionMarker();
        }
        catch
        {
            File.Delete(marker);
            throw;
        }
        File.Delete(marker);
    }

    private static bool HasInterruptedUpdate(string root)
    {
        string temp = Path.Combine(root, "temp");
        if (!Directory.Exists(temp))
            return false;
        UpdatePackage.RejectLinks(root, "temp");
        return Directory.EnumerateDirectories(temp).Any(path =>
            IsStageName(Path.GetFileName(path)) && File.Exists(Path.Combine(path, "applying")));
    }

    private static string GetRestartStage(string root, string[] args)
    {
        if (args.Length != 4 || !IsStageName(args[1]))
            throw new InvalidDataException();
        string relative = "temp/" + args[1];
        UpdatePackage.RejectLinks(root, relative);
        string stage = Path.Combine(root, relative);
        WaitForProcess(args[2], args[3]);
        if (File.Exists(Path.Combine(stage, "applying")))
            throw new IOException("更新が完了していません。");
        return stage;
    }

    private static void WaitForProcess(string pid, string ticks)
    {
        if (!int.TryParse(pid, out int id) || id <= 0 || id == Environment.ProcessId ||
            !long.TryParse(ticks, out long start))
            throw new InvalidDataException();
        try
        {
            using var process = Process.GetProcessById(id);
            if (process.StartTime.ToUniversalTime().Ticks == start && !process.WaitForExit(20000))
                throw new IOException("ゲームが終了していません。");
        }
        catch (ArgumentException) { }
    }

    private static string CreateStage(string root)
    {
        UpdatePackage.RejectLinks(root, "temp");
        string temp = Path.Combine(root, "temp");
        Directory.CreateDirectory(temp);
        string stage = Path.Combine(temp, "TakeTaikoUpdate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        return stage;
    }

    private static bool IsStageName(string name) =>
        name.StartsWith("TakeTaikoUpdate-", StringComparison.Ordinal) && Guid.TryParseExact(name[16..], "N", out _);

    private static void RemoveStage(string stage)
    {
        try
        {
            Directory.Delete(stage, recursive: true);
            string temp = Path.GetDirectoryName(stage)!;
            if (!Directory.EnumerateFileSystemEntries(temp).Any())
                Directory.Delete(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static HttpClient CreateClient() =>
        new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };

    private static Stream? OpenSettings(string folder)
    {
        // 配布時に組み込んだ公開鍵と配布先を優先する。
        var embedded = typeof(StartupUpdate).Assembly.GetManifestResourceStream("TakeTaiko.UpdateFeed.json");
        string local = Path.Combine(folder, UpdatePackage.FeedFile);
        var settings = embedded ?? (File.Exists(local) ? File.OpenRead(local) : null);
        if (settings is not null && settings.Length > UpdateFeed.MetadataLimit)
        {
            settings.Dispose();
            throw new InvalidDataException("配布設定が大きすぎます。");
        }
        return settings;
    }

    private static GameRelease ReadLatest(UpdateFeed feed, Stream settings)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        return feed.CheckAsync(settings, timeout.Token).GetAwaiter().GetResult();
    }

    private static void Download(UpdateFeed feed, GameRelease release, string stage, bool install) =>
        UpdateDialog.Download(async (progress, token) =>
        {
            string zip = await feed.DownloadAsync(release, stage, progress, token);
            UpdatePackage.Extract(zip, stage, release.Version, install, token);
            return true;
        }, release.Size);

    private static string Notes(GameRelease release) =>
        string.IsNullOrWhiteSpace(release.Notes) ? "更新内容の記載はありません。" : release.Notes;

    private static Process StartProcess(string exe, string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo(exe) { WorkingDirectory = workingDirectory, UseShellExecute = false };
        foreach (string arg in args)
            start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new IOException("プログラムを起動できません。");
    }

    private static IDisposable? Acquire(string root, TimeSpan timeout)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())));
        var mutex = new Mutex(false, @"Local\TakeTaikoSimulator-" + hash);
        try
        {
            if (mutex.WaitOne(timeout))
                return new GameLock(mutex);
        }
        catch (AbandonedMutexException) { return new GameLock(mutex); }
        mutex.Dispose();
        return null;
    }

    private sealed class GameLock(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    private static void CreateStartMenuShortcut(string root)
    {
        // .url はシェルが開けるローカルの exe を指す。インターネットには接続しない。
        string menu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrEmpty(menu))
            return;
        try
        {
            var executable = new Uri(Path.Combine(root, UpdatePackage.GameExe));
            File.WriteAllText(Path.Combine(menu, "TakeTaiko.url"), "[InternetShortcut]\r\nURL=" + executable.AbsoluteUri + "\r\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool ExpectedError(Exception ex) =>
        ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or Win32Exception or
        CryptographicException or ArgumentException or JsonException or FormatException or InvalidOperationException or
        KeyNotFoundException or ExternalException or EntryPointNotFoundException;

    private static void SafeNotice(string title, string text)
    {
        try { UpdateDialog.Notice(title, text); }
        catch (Exception ex) when (ExpectedError(ex)) { Console.WriteLine($"{title} {text}"); }
    }

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    private sealed class RollbackFailedExceptionMarker : IOException;
}
