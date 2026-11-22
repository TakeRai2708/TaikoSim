using System.Diagnostics;
using System.IO;

namespace TakeTaikoLauncher;

internal static class GameInstallation
{
    internal static string Executable { get; } =
        Path.Combine(AppContext.BaseDirectory, "TakeTaikoSimulator.exe");

    internal static Version? ReadVersion()
    {
        if (!File.Exists(Executable))
            return null;

        FileVersionInfo info = FileVersionInfo.GetVersionInfo(Executable);
        return info.FileVersion is null ? null :
            new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
    }

    internal static Process Start()
    {
        return Process.Start(new ProcessStartInfo(Executable)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("ゲームを起動できませんでした。");
    }
}
