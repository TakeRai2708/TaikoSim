using System.Runtime.InteropServices;

namespace TakeTaikoSimulator.Updates;

// Native AOT でも動くよう、Windows の Shell Link を直接使う。
internal static unsafe class ShellShortcut
{
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkInterface = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PersistFileInterface = new("0000010B-0000-0000-C000-000000000046");
    private const int SetWorkingDirectory = 9;
    private const int SetRelativePath = 18;
    private const int SetPath = 20;
    private const int Save = 6;

    internal static void CreateOnDesktop(string executable, string workingDirectory)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop))
            throw new IOException("デスクトップが見つかりません。");

        string temporary = Path.Combine(desktop, ".TakeTaiko-" + Guid.NewGuid().ToString("N") + ".lnk");
        using (File.Open(temporary, FileMode.CreateNew, FileAccess.Write)) { }
        try
        {
            SaveLink(temporary, executable, workingDirectory);
            for (int number = 1; number <= 999; number++)
            {
                string name = number == 1 ? "TakeTaiko.lnk" : $"TakeTaiko ({number}).lnk";
                string destination = Path.Combine(desktop, name);
                try
                {
                    File.Move(temporary, destination);
                    return;
                }
                catch (IOException) when (File.Exists(destination) || Directory.Exists(destination)) { }
            }
            throw new IOException("空いているショートカット名がありません。");
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void SaveLink(string destination, string executable, string workingDirectory)
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2)); // COINIT_APARTMENTTHREADED
        nint shellLink = 0;
        nint persist = 0;
        try
        {
            Marshal.ThrowExceptionForHR(CoCreateInstance(in ShellLinkClass, 0, 1, in ShellLinkInterface, out shellLink));
            nint* shellMethods = *(nint**)shellLink;
            fixed (char* target = executable, directory = workingDirectory)
            {
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int>)shellMethods[SetPath])(shellLink, target));
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int>)shellMethods[SetWorkingDirectory])(shellLink, directory));
            }

            if (string.Equals(Path.GetPathRoot(destination), Path.GetPathRoot(executable), StringComparison.OrdinalIgnoreCase))
            {
                // 一時リンクも最終リンクと同じフォルダーにあるので、相対パスの基準は同じ。
                fixed (char* linkPath = destination)
                    Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, uint, int>)shellMethods[SetRelativePath])(
                        shellLink, linkPath, 0));
            }

            Guid persistInterface = PersistFileInterface;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)shellMethods[0])(
                shellLink, &persistInterface, &persist));
            nint* persistMethods = *(nint**)persist;
            fixed (char* name = destination)
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)persistMethods[Save])(persist, name, 0));
        }
        finally
        {
            Release(persist);
            Release(shellLink);
            CoUninitialize();
        }
    }

    private static void Release(nint instance)
    {
        if (instance != 0)
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2])(instance);
    }

    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(nint reserved, uint model);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void CoUninitialize();
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}
