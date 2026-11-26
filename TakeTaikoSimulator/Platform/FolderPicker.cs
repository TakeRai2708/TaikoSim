using System.Runtime.InteropServices;

namespace TakeTaikoSimulator.Platform
{
    // フォルダを選ぶ Windows の画面を出す(スキンフォルダの選択で使う)
    //
    // 前は Windows フォームの FolderBrowserDialog を使っていたが、Windows フォームは Native AOT で使えない
    // (逆コンパイル対策で exe を機械語にするときに、動かなくなる)。
    // なので、Windows の shell32.dll の SHBrowseForFolder を直接呼ぶ(P/Invoke)。これは AOT でも動く。
    //
    // ・SHBrowseForFolder は、昔からある「フォルダの参照」ダイアログ(ドラッグ&ドロップでも選べるので、これで十分)
    // ・新しい見た目(BIF_NEWDIALOGSTYLE)にするには OLE を初期化した STA スレッドで呼ぶ必要があるので、専用のスレッドで出す
    // ・最初に開くフォルダの指定はしていない(コールバックが要って複雑なので。開いたあと自分でたどってもらう)
    internal static class FolderPicker
    {
        private const uint BIF_RETURNONLYFSDIRS = 0x0001; // ファイルシステムの実在フォルダだけ選べる
        private const uint BIF_NEWDIALOGSTYLE = 0x0040;   // 新しい見た目(新規作成ボタンなど)
        private const uint BIF_EDITBOX = 0x0010;
        private const int MAX_PATH = 260;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BROWSEINFO
        {
            public IntPtr hwndOwner;
            public IntPtr pidlRoot;
            public IntPtr pszDisplayName;
            public IntPtr lpszTitle;   // 文字列は自分で確保して渡す
            public uint ulFlags;
            public IntPtr lpfn;
            public IntPtr lParam;
            public int iImage;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHBrowseForFolderW(ref BROWSEINFO lpbi);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SHGetPathFromIDListW(IntPtr pidl, [Out] char[] pszPath);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        [DllImport("ole32.dll")]
        private static extern int OleInitialize(IntPtr pvReserved);

        [DllImport("ole32.dll")]
        private static extern void OleUninitialize();

        // フォルダを選ばせて、選んだフォルダの場所を返す(キャンセルや失敗なら null)
        public static string? Pick(string title, IntPtr owner = default)
        {
            if (OperatingSystem.IsMacOS()) return PickMac(title);
            if (OperatingSystem.IsLinux()) return PickLinux(title);
            if (!OperatingSystem.IsWindows()) return null;   // iOS・Android は、それぞれの画面を作るときに別で用意する

            if (owner != IntPtr.Zero && Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return PickCore(title, owner);

            string? result = null;
            // SHBrowseForFolder の新しい見た目は STA スレッドでないと出ないので、専用スレッドで出す
            Thread thread = new Thread(() => result = PickCore(title, owner));
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();
            return result;
        }

        private static string? PickCore(string title, IntPtr owner)
        {
            OleInitialize(IntPtr.Zero);
            IntPtr titlePtr = Marshal.StringToCoTaskMemUni(title);
            IntPtr pidl = IntPtr.Zero;
            try
            {
                BROWSEINFO bi = new BROWSEINFO
                {
                    hwndOwner = owner,
                    lpszTitle = titlePtr,
                    ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | (owner == IntPtr.Zero ? 0u : BIF_EDITBOX),
                };
                pidl = SHBrowseForFolderW(ref bi);
                if (pidl == IntPtr.Zero) return null; // キャンセル

                char[] buffer = new char[MAX_PATH];
                if (!SHGetPathFromIDListW(pidl, buffer)) return null;

                string path = new string(buffer);
                int end = path.IndexOf('\0');
                if (end >= 0) path = path.Substring(0, end);
                return path.Length > 0 ? path : null;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                // Windows 以外や、shell32 が無い環境(ふつう起きない)
                Console.WriteLine($"[FolderPicker] フォルダ選択の画面を出せませんでした: {ex.Message}");
                return null;
            }
            finally
            {
                if (pidl != IntPtr.Zero) CoTaskMemFree(pidl);
                Marshal.FreeCoTaskMem(titlePtr);
                OleUninitialize();
            }
        }
    
        // macOS：AppleScript の「フォルダを選択」を出す(osascript。macOS に最初から入っている)
        private static string? PickMac(string title)
        {
            string escaped = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return RunAndReadLine("/usr/bin/osascript", ["-e", $"POSIX path of (choose folder with prompt \"{escaped}\")"]);
        }

        // Linux：zenity があれば、それでフォルダを選ばせる(無ければ選べない)
        private static string? PickLinux(string title) =>
            RunAndReadLine("zenity", ["--file-selection", "--directory", $"--title={title}"]);

        // コマンドを動かして、1行目(選んだ場所)を返す。キャンセル・失敗なら null
        private static string? RunAndReadLine(string file, string[] arguments)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo(file) { RedirectStandardOutput = true, UseShellExecute = false };
                foreach (string argument in arguments) info.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(info);
                if (process == null) return null;
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode != 0 || output.Length == 0) return null;
                return Path.TrimEndingDirectorySeparator(output);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                Console.WriteLine($"[FolderPicker] フォルダ選択の画面を出せませんでした: {ex.Message}");
                return null;
            }
        }
}
}
