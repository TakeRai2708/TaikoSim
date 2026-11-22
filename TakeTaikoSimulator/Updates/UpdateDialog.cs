using System.Runtime.InteropServices;

namespace TakeTaikoSimulator.Updates;

internal static class UpdateDialog
{
    private const uint Timer = 0x0800;
    private const uint ShowProgress = 0x0200;
    private const uint TimerNotification = 4;
    private const uint ClickButton = 0x0400 + 102;
    private const uint SetProgress = 0x0400 + 106;
    internal static bool InstallerAppearance { get; set; }

    internal static bool Confirm(string heading, string text) => Show(heading, text, 6, 7) == 6;
    internal static void Notice(string heading, string text) => Show(heading, text, 1, 1);

    internal static void RestartNotice()
    {
        bool elapsed = false;
        Show("ダウンロード完了しました", "再起動します。", 1, 1, Timer,
        (window, notification, milliseconds, _, _) =>
        {
            if (notification == TimerNotification && milliseconds >= 3000)
            {
                elapsed = true;
                PostMessage(window, ClickButton, 1, 0);
            }
            return notification == 2 && !elapsed ? 1 : 0;
        });
    }

    internal static T Download<T>(Func<Action<long>, CancellationToken, Task<T>> action, long total)
    {
        long received = 0;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        Task<T> task = Task.Run(() => action(bytes => Interlocked.Exchange(ref received, bytes), cancel.Token));
        try
        {
            Show("ダウンロードしています", "受信した更新ファイルを検証して展開します。", 8, 2,
                Timer | ShowProgress, (window, notification, _, _, _) =>
                {
                    if (notification == TimerNotification)
                    {
                        PostMessage(window, SetProgress, (nuint)(Interlocked.Read(ref received) * 100 / total), 0);
                        if (task.IsCompleted)
                            PostMessage(window, ClickButton, 2, 0);
                    }
                    return 0;
                });
        }
        finally
        {
            if (!task.IsCompleted)
            {
                cancel.Cancel();
                // ダイアログのエラー時も、一時フォルダを消す前に書き込みの終了を待つ。
                try { task.GetAwaiter().GetResult(); }
                catch (Exception) { }
            }
        }
        return task.GetAwaiter().GetResult();
    }

    private static int Show(string heading, string text, uint buttons, int defaultButton,
        uint flags = 0, DialogCallback? callback = null)
    {
        var config = new DialogConfig
        {
            Size = (uint)Marshal.SizeOf<DialogConfig>(), Flags = flags, CommonButtons = buttons,
            Title = "TakeTaiko", Instruction = heading, Content = text, DefaultButton = defaultButton,
            Callback = callback is null ? 0 : Marshal.GetFunctionPointerForDelegate(callback),
            Width = InstallerAppearance ? 365u : 330u
        };
        int result = TaskDialogIndirect(ref config, out int button, 0, 0);
        GC.KeepAlive(callback);
        Marshal.ThrowExceptionForHR(result);
        return button;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DialogCallback(nint window, uint notification, nuint parameter, nint data, nint context);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct DialogConfig
    {
        public uint Size;
        public nint Parent, Instance;
        public uint Flags, CommonButtons;
        public string? Title;
        public nint Icon;
        public string? Instruction, Content;
        public uint ButtonCount;
        public nint Buttons;
        public int DefaultButton;
        public uint RadioButtonCount;
        public nint RadioButtons;
        public int DefaultRadioButton;
        public nint Verification, ExpandedInformation, ExpandedControl, CollapsedControl, FooterIcon, Footer, Callback, CallbackData;
        public uint Width;
    }

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern int TaskDialogIndirect(ref DialogConfig config, out int button, nint radio, nint verification);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(nint window, uint message, nuint parameter, nint data);
}
