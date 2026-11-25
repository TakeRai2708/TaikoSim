using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using TakeTaikoSimulator.Platform;

namespace TakeTaikoSimulator.Updates;

// 描画方式の選択より先に使う画面なので、ゲームの描画ライブラリは初期化しない。
internal sealed class UpdateWindow(string heading, string body, string? primary, string? secondary, bool installer)
{
    internal const int Accept = 101;
    private const int Cancel = 102;
    private const int Close = 103;
    private const int Browse = 104;
    private const string DesktopShortcutLabel = "デスクトップにショートカットを追加";
    private const uint GetCheck = 0x00F0;
    private const uint SetCheck = 0x00F1;
    private const string WindowClass = "TakeTaiko.UpdateWindow";
    private static readonly WindowProc Callback = Dispatch;
    private static readonly WindowProc CheckBoxCallback = DrawCheckBox;
    private static bool registered;
    [ThreadStatic] private static UpdateWindow? current;

    private static readonly uint Background = Rgb(18, 10, 13);
    private static readonly uint Panel = Rgb(27, 16, 20);
    private static readonly uint Sakura = Rgb(249, 188, 206);
    private static readonly uint Text = Rgb(252, 238, 242);
    private static readonly uint Muted = Rgb(191, 165, 174);
    private static readonly uint Accent = Rgb(163, 4, 27);
    private static readonly uint Border = Rgb(67, 42, 53);

    private nint window, brand, title, details, folderField, question;
    private nint desktopCheckBox;
    private nint originalCheckBoxProc;
    private nint inputBrush;
    private Rect inputArea;
    private nint regularFont, headingFont, brandFont, backgroundBrush, panelBrush;
    private readonly Dictionary<int, nint> buttons = [];
    private Rect panel, progress;
    private int result;
    private bool complete;
    private Exception? failure;
    private int? percent;
    private double scale = 1;

    internal string? Folder { get; set; }
    internal bool EditableBody { get; set; }
    internal string BodyText { get; private set; } = body;
    internal bool DesktopShortcut { get; set; }
    // インストール先の欄が無い画面でも、左下にチェックボックスを1つ出せる（スキンの更新の「ダウンロードフォルダーに保存」など）
    //   値は DesktopShortcut に入る
    internal string? CheckBoxLabel { get; set; }
    private string CheckText => Folder is not null ? DesktopShortcutLabel : CheckBoxLabel ?? "";
    internal Action? Tick { get; set; }
    internal int? Progress
    {
        get => percent;
        set
        {
            if (percent == value)
                return;
            percent = value;
            if (window != 0)
                InvalidateRect(window, ref progress, false);
        }
    }

    internal int Show()
    {
        nint instance = GetModuleHandle(null);
        if (!registered)
        {
            var definition = new WindowDefinition
            {
                Proc = Marshal.GetFunctionPointerForDelegate(Callback),
                Instance = instance,
                Cursor = LoadCursor(0, 32512),
                ClassName = WindowClass
            };
            if (RegisterClass(ref definition) == 0)
                throw new Win32Exception();
            registered = true;
        }

        int logicalWidth = installer ? 660 : 600;
        int logicalHeight = installer ? 396 : 360;
        if (!SystemParametersInfo(0x0030, 0, out Rect area, 0))
            throw new Win32Exception();
        scale = Math.Min(installer ? 1.15 : 1.0, Math.Min(
            (area.Right - area.Left - 24) / (double)logicalWidth,
            (area.Bottom - area.Top - 24) / (double)logicalHeight));
        int width = Scale(logicalWidth);
        int height = Scale(logicalHeight);
        current = this;
        try
        {
            window = CreateWindowEx(0x00040000, WindowClass, "TakeTaiko", 0x82080000,
                (area.Left + area.Right - width) / 2, (area.Top + area.Bottom - height) / 2,
                width, height, 0, 0, instance, 0);
            if (window == 0)
                throw new Win32Exception();
            nint focus = CreateControls(logicalWidth, logicalHeight);
            ShowWindow(window, 5);
            SetFocus(focus);
            if (Tick is not null && SetTimer(window, 1, 50, 0) == 0)
                throw new Win32Exception();

            while (!complete)
            {
                int received = GetMessage(out Message message, 0, 0, 0);
                if (received < 0)
                    throw new Win32Exception();
                if (received == 0)
                {
                    PostQuitMessage((int)message.Parameter);
                    break;
                }
                if (message.Id == 0x0100 && message.Parameter == 27 && CanClose)
                {
                    Finish(Cancel);
                    continue;
                }
                if (message.Id == 0x0100 && message.Parameter == 13 &&
                    (buttons.ContainsValue(GetFocus()) || GetFocus() == desktopCheckBox && desktopCheckBox != 0))
                {
                    SendMessage(GetFocus(), 0x00F5, 0, 0); // BM_CLICK
                    continue;
                }
                if (!IsDialogMessage(window, ref message))
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            if (failure is not null)
                throw new InvalidOperationException("更新画面の表示に失敗しました。", failure);
            return result;
        }
        finally
        {
            if (window != 0)
            {
                KillTimer(window, 1);
                DestroyWindow(window);
                window = 0;
            }
            foreach (nint resource in new[] { regularFont, headingFont, brandFont, backgroundBrush, panelBrush, inputBrush })
                if (resource != 0)
                    DeleteObject(resource);
            current = null;
        }
    }

    private nint CreateControls(int width, int height)
    {
        const int margin = 24;
        regularFont = MakeFont(Scale(16), 400);
        headingFont = MakeFont(Scale(26), 600);
        brandFont = MakeFont(Scale(15), 600);
        backgroundBrush = CreateSolidBrush(Background);
        panelBrush = CreateSolidBrush(Panel);
        if (EditableBody)
            inputBrush = CreateSolidBrush(Rgb(38, 23, 29));
        brand = Child("STATIC", installer ? "TakeTaiko インストーラー" : "TakeTaiko", 0,
            margin, 12, width - 100, 20, 0, brandFont);
        if (CanClose)
            Button("閉じる", width - 54, 4, 46, 34, Close);

        panel = new Rect(Scale(margin), Scale(52), Scale(width - margin), Scale(height - 67));
        title = Child("STATIC", heading, 0, margin + 18, 69, width - 84, 35, 0, headingFont);
        int bodyBottom = Folder is null ? height - (percent is null ? 88 : 129) : height - 159;
        int bodyHeight = Math.Max(34, bodyBottom - 122);
        string content = BodyText.ReplaceLineEndings("\r\n");
        int charsPerLine = Math.Max(1, (width - 84) / 15);
        int lines = content.Split('\n').Sum(line => Math.Max(1, (line.TrimEnd('\r').Length + charsPerLine - 1) / charsPerLine));
        uint scroll = lines * 20 > bodyHeight ? 0x00200000u : 0;
        int inputTop = EditableBody ? 140 : 122;
        int inputHeight = EditableBody ? bodyHeight - 40 : bodyHeight;
        inputArea = new Rect(Scale(margin + 17), Scale(inputTop - 7),
            Scale(width - margin - 17), Scale(inputTop + inputHeight + 7));
        if (EditableBody)
        {
            Child("STATIC", "ご意見・不具合の内容（ここに入力）", 0, margin + 18, 111,
                width - 84, 24, 0, regularFont);
            Child("STATIC", "1400文字まで・次の画面で確認して投稿（GitHubログインが必要）", 0,
                margin + 18, height - 91, width - 84, 22, 0, regularFont);
        }
        details = Child("EDIT", content, EditableBody ? 0x00011044u : scroll | 0x0844,
            margin + (EditableBody ? 28 : 18), inputTop, width - (EditableBody ? 104 : 84),
            inputHeight, 0, regularFont);
        if (EditableBody)
            SendMessage(details, 0x00C5, 1400, 0); // EM_SETLIMITTEXT

        if (Folder is not null)
        {
            question = Child("STATIC", "インストール先", 0,
                margin + 18, height - 145, width - 84, 20, 0, regularFont);
            folderField = Child("EDIT", Folder, 0x00010080, margin + 18, height - 119,
                width - 207, 25, 0, regularFont);
            Button("参照…", width - 151, height - 124, 102, 33, Browse);
        }
        if (Folder is not null || CheckBoxLabel is not null)
        {
            desktopCheckBox = Child("BUTTON", CheckText, 0x00010003,
                margin, height - 51, Math.Max(200, width - 300), 30, 105, regularFont);
            originalCheckBoxProc = SetWindowLongPtr(desktopCheckBox, -4,
                Marshal.GetFunctionPointerForDelegate(CheckBoxCallback)); // GWLP_WNDPROC
            if (originalCheckBoxProc == 0)
                throw new Win32Exception();
            if (DesktopShortcut)
                SendMessage(desktopCheckBox, SetCheck, 1, 0);
        }
        progress = new Rect(Scale(margin + 18), Scale(height - 121), Scale(width - margin - 18), Scale(height - 83));
        nint focus = 0;
        if (primary is not null)
            focus = Button(primary, width - (secondary is null ? 138 : 254), height - 53, 114, 35, Accept);
        if (secondary is not null)
            focus = Button(secondary, width - 132, height - 53, 108, 35, Cancel);
        return EditableBody ? details : focus == 0 ? window : focus;
    }

    private bool CanClose => primary is not null || secondary is not null;
    internal void Complete() => Finish(Accept);

    private void Finish(int value)
    {
        if (EditableBody && details != 0)
        {
            var text = new StringBuilder(GetWindowTextLength(details) + 1);
            GetWindowText(details, text, text.Capacity);
            BodyText = text.ToString();
        }
        result = value;
        complete = true;
    }

    private nint Button(string label, int x, int y, int width, int height, int id)
    {
        nint button = Child("BUTTON", label, 0x0001000B, x, y, width, height, id, regularFont);
        buttons.Add(id, button);
        return button;
    }

    private nint Child(string type, string label, uint style, int x, int y, int width, int height, int id, nint font)
    {
        nint child = CreateWindowEx(0, type, label, 0x50000000 | style,
            Scale(x), Scale(y), Scale(width), Scale(height), window, id, 0, 0);
        if (child == 0)
            throw new Win32Exception();
        SendMessage(child, 0x0030, font, 1);
        return child;
    }

    private int Scale(int value) => (int)Math.Round(value * scale);

    private static nint Dispatch(nint hwnd, uint message, nuint parameter, nint data)
    {
        var dialog = current;
        if (dialog is null)
            return DefWindowProc(hwnd, message, parameter, data);
        try { return dialog.Handle(hwnd, message, parameter, data); }
        catch (Exception ex)
        {
            dialog.failure = ex;
            dialog.Finish(Cancel);
            return 0;
        }
    }

    private nint Handle(nint hwnd, uint message, nuint parameter, nint data)
    {
        switch (message)
        {
            case 0x000F: // WM_PAINT
                nint canvas = BeginPaint(hwnd, out PaintData paint);
                try
                {
                    GetClientRect(hwnd, out Rect client);
                    FillRect(canvas, ref client, backgroundBrush);
                    Rounded(canvas, panel, Panel, Border, Scale(18));
                    if (EditableBody)
                        Rounded(canvas, inputArea, Rgb(38, 23, 29), Sakura, Scale(10));
                    if (percent is int value)
                    {
                        Rect track = new(progress.Left, progress.Top + Scale(24), progress.Right, progress.Top + Scale(31));
                        Rounded(canvas, track, Border, Border, Scale(6));
                        track.Right = track.Left + (track.Right - track.Left) * value / 100;
                        if (track.Right > track.Left)
                            Rounded(canvas, track, Rgb(226, 58, 94), Rgb(226, 58, 94), Scale(6));
                        DrawLabel(canvas, $"{value}%", progress, regularFont, Sakura, 0x20);
                    }
                }
                finally { EndPaint(hwnd, ref paint); }
                return 0;
            case 0x0014: // WM_ERASEBKGND
                return 1;
            case 0x0133: // WM_CTLCOLOREDIT
            case 0x0138: // WM_CTLCOLORSTATIC
                if (EditableBody && data == details)
                {
                    SetTextColor((nint)parameter, Text);
                    SetBkColor((nint)parameter, Rgb(38, 23, 29));
                    return inputBrush;
                }
                bool inPanel = data == title || data == details || data == folderField || data == question;
                SetTextColor((nint)parameter, data == title || data == brand || data == question ? Sakura :
                    data == folderField ? Muted : Text);
                SetBkColor((nint)parameter, inPanel ? Panel : Background);
                return inPanel ? panelBrush : backgroundBrush;
            case 0x002B: // WM_DRAWITEM
                DrawButton(Marshal.PtrToStructure<DrawItem>(data));
                return 1;
            case 0x0111: // WM_COMMAND
                int id = (int)(parameter & 0xffff);
                if ((parameter >> 16) != 0 || !buttons.TryGetValue(id, out nint button) || data != button)
                    return 0;
                switch (id)
                {
                    case Accept:
                        if (folderField != 0)
                        {
                            var path = new StringBuilder(GetWindowTextLength(folderField) + 1);
                            GetWindowText(folderField, path, path.Capacity);
                            Folder = path.ToString().Trim();
                        }
                        if (desktopCheckBox != 0)
                            DesktopShortcut = SendMessage(desktopCheckBox, GetCheck, 0, 0) == 1;
                        if (primary is not null) Finish(Accept);
                        break;
                    case Cancel:
                    case Close:
                        if (CanClose) Finish(Cancel);
                        break;
                    case Browse:
                        string? selected = FolderPicker.Pick("TakeTaiko フォルダーを作る場所を選んでください。", hwnd);
                        if (selected is not null)
                        {
                            Folder = StartupUpdate.InstallLocationUnder(selected);
                            SetWindowText(folderField, Folder);
                        }
                        break;
                }
                return 0;
            case 0x0084: // WM_NCHITTEST
                var point = new Point { X = (short)(data & 0xffff), Y = (short)(data >> 16) };
                ScreenToClient(hwnd, ref point);
                if (point.Y >= 0 && point.Y < Scale(44))
                {
                    GetClientRect(hwnd, out Rect bounds);
                    if (!CanClose || point.X < bounds.Right - Scale(57))
                        return 2; // HTCAPTION
                }
                break;
            case 0x0010: // WM_CLOSE
                if (CanClose) Finish(Cancel);
                return 0;
            case 0x0113: // WM_TIMER
                Tick?.Invoke();
                return 0;
        }
        return DefWindowProc(hwnd, message, parameter, data);
    }

    private void DrawButton(DrawItem item)
    {
        bool close = item.ControlId == Close;
        bool pressed = (item.State & 1) != 0;
        bool focused = (item.State & 0x10) != 0;
        bool inPanel = item.ControlId == Browse;
        FillRect(item.Canvas, ref item.Bounds, inPanel ? panelBrush : backgroundBrush);
        uint fill = item.ControlId == Accept ? Accent : Rgb(51, 32, 42);
        if (!close || pressed || focused)
            Rounded(item.Canvas, item.Bounds, pressed ? Border : fill,
                !close && focused ? Sakura : pressed ? Border : fill, Scale(9));
        string label = item.ControlId switch
        {
            Accept => primary!,
            Cancel => secondary!,
            Browse => "参照…",
            _ => "×"
        };
        DrawLabel(item.Canvas, label, item.Bounds, close ? headingFont : regularFont, Text, 0x25);
    }

    // Windows のチェック状態・キーボード操作は使い、見た目だけを描き替える。
    private static nint DrawCheckBox(nint control, uint message, nuint parameter, nint data)
    {
        var owner = current;
        if (owner is null)
            return DefWindowProc(control, message, parameter, data);
        if (message is 0x000F or 0x0318) // WM_PAINT / WM_PRINTCLIENT
        {
            PaintData paint = default;
            bool repaint = message == 0x000F;
            nint canvas = repaint ? BeginPaint(control, out paint) : (nint)parameter;
            try
            {
                GetClientRect(control, out Rect bounds);
                FillRect(canvas, ref bounds, owner.backgroundBrush);
                bool isChecked = SendMessage(control, GetCheck, 0, 0) == 1;
                bool focused = GetFocus() == control;
                int top = (bounds.Bottom - owner.Scale(18)) / 2;
                var indicator = new Rect(owner.Scale(1), top, owner.Scale(19), top + owner.Scale(18));
                Rounded(canvas, indicator, isChecked ? Sakura : Panel, isChecked || focused ? Sakura : Muted,
                    owner.Scale(6));
                if (isChecked)
                {
                    nint pen = CreatePen(0, owner.Scale(2), Background);
                    nint previous = SelectObject(canvas, pen);
                    MoveToEx(canvas, owner.Scale(5), top + owner.Scale(9), 0);
                    LineTo(canvas, owner.Scale(8), top + owner.Scale(12));
                    LineTo(canvas, owner.Scale(15), top + owner.Scale(5));
                    SelectObject(canvas, previous);
                    DeleteObject(pen);
                }
                bounds.Left = owner.Scale(28);
                DrawLabel(canvas, owner.CheckText, bounds, owner.regularFont, Text, 0x24);
            }
            finally
            {
                if (repaint)
                    EndPaint(control, ref paint);
            }
            return 0;
        }
        if (message == 0x0014) // WM_ERASEBKGND
            return 1;

        nint result = CallWindowProc(owner.originalCheckBoxProc, control, message, parameter, data);
        if (message is SetCheck or 0x0007 or 0x0008 or 0x0202 or 0x0101)
        {
            GetClientRect(control, out Rect bounds);
            InvalidateRect(control, ref bounds, false);
        }
        return result;
    }

    private static void Rounded(nint canvas, Rect box, uint fill, uint edge, int radius)
    {
        nint brush = CreateSolidBrush(fill);
        nint pen = CreatePen(0, 1, edge);
        nint oldBrush = SelectObject(canvas, brush);
        nint oldPen = SelectObject(canvas, pen);
        RoundRect(canvas, box.Left, box.Top, box.Right, box.Bottom, radius, radius);
        SelectObject(canvas, oldPen);
        SelectObject(canvas, oldBrush);
        DeleteObject(pen);
        DeleteObject(brush);
    }

    private static void DrawLabel(nint canvas, string text, Rect box, nint font, uint color, uint format)
    {
        nint old = SelectObject(canvas, font);
        SetBkMode(canvas, 1);
        SetTextColor(canvas, color);
        DrawText(canvas, text, text.Length, ref box, format | 0x800);
        SelectObject(canvas, old);
    }

    private static nint MakeFont(int size, int weight) =>
        CreateFont(-size, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, "Yu Gothic UI");

    private static uint Rgb(uint red, uint green, uint blue) => red | (green << 8) | (blue << 16);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProc(nint window, uint message, nuint parameter, nint data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowDefinition
    {
        public uint Style;
        public nint Proc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, BackgroundBrush;
        public string? MenuName, ClassName;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect(int left, int top, int right, int bottom)
    {
        public int Left = left, Top = top, Right = right, Bottom = bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint Parameter;
        public nint Data;
        public uint Time;
        public Point Cursor;
        public uint Private;
    }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct PaintData
    {
        public nint Canvas;
        public int Erase;
        public Rect Region;
        public int Restore, Incremental;
        public fixed byte Reserved[32];
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawItem
    {
        public uint Type, ControlId, ItemId, Action, State;
        public nint Control, Canvas;
        public Rect Bounds;
        public nuint ItemData;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW")] private static extern nint GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string? module);
    [DllImport("user32.dll", EntryPoint = "RegisterClassW", SetLastError = true)] private static extern ushort RegisterClass(ref WindowDefinition definition);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint extended, string type, string title, uint style, int x, int y, int width, int height, nint parent, nint id, nint instance, nint parameter);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nuint parameter, nint data);
    [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "IsDialogMessageW")] private static extern bool IsDialogMessage(nint window, ref Message message);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "LoadCursorW")] private static extern nint LoadCursor(nint instance, nint id);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SystemParametersInfo(uint action, uint parameter, out Rect value, uint flags);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Rect bounds);
    [DllImport("user32.dll", SetLastError = true)] private static extern nuint SetTimer(nint window, nuint id, uint interval, nint callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(nint window, nuint id);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint window, out PaintData paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint window, ref PaintData paint);
    [DllImport("user32.dll")] private static extern int FillRect(nint canvas, ref Rect bounds, nint brush);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint window, ref Rect region, bool erase);
    [DllImport("user32.dll", EntryPoint = "DrawTextW", CharSet = CharSet.Unicode)] private static extern int DrawText(nint canvas, string text, int length, ref Rect bounds, uint format);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nint parameter, nint data);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(nint window, string text);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int capacity);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(nint procedure, nint window, uint message, nuint parameter, nint data);
    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)] private static extern nint CreateFont(int height, int width, int angle, int orientation, int weight, uint italic, uint underline, uint strike, uint charset, uint precision, uint clip, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern nint CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint canvas, nint handle);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")] private static extern bool RoundRect(nint canvas, int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool MoveToEx(nint canvas, int x, int y, nint previous);
    [DllImport("gdi32.dll")] private static extern bool LineTo(nint canvas, int x, int y);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint canvas, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(nint canvas, uint color);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint canvas, int mode);
}
