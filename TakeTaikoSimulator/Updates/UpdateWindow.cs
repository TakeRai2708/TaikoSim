using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using TakeTaikoSimulator.Platform;

namespace TakeTaikoSimulator.Updates;

// インストーラー・更新の画面（描画方式の選択より先に使う画面なので、ゲームの描画ライブラリは使わない）
//   11/26 たけらい「アップデーターとインストーラーのUIださい。今のピンクっぽくしてほしい」→ 見本（チャットの絵）の形
//   インストーラー：左に「インストール先 → ダウンロード → 完了」の進み具合、右に札・見出し・カード、下に版などの帯
//   更新など　　　：左上に札と見出し、まん中にカード、右下にボタン
//   文字は Windows の文字（ClearType）、角丸・丸・スイッチは GDI+ でなめらかに描く（Gdip.cs）
internal sealed class UpdateWindow(string heading, string body, string? primary, string? secondary, bool installer)
{
    internal const int Accept = 101;
    private const int Cancel = 102;
    private const int Close = 103;
    private const int Browse = 104;
    private const string DesktopShortcutLabel = "デスクトップにショートカット";
    private const uint GetCheck = 0x00F0;
    private const uint SetCheck = 0x00F1;
    private const string WindowClass = "TakeTaiko.UpdateWindow";
    private static readonly WindowProc Callback = Dispatch;
    private static readonly WindowProc SwitchCallback = DrawSwitch;
    private static bool registered;
    [ThreadStatic] private static UpdateWindow? current;

    // 札に出す記号（Segoe MDL2 Assets の文字）。null のときは「T」の札
    internal const char IconFolder = '', IconDownload = '', IconDone = '', IconInfo = '', IconSkin = '';
    private static readonly string[] Steps = ["インストール先", "ダウンロード", "完了"];

    // 色（ゲームの設定画面と同じ桜色）
    private static readonly uint Background = Rgb(18, 10, 13);
    private static readonly uint Card = Rgb(27, 16, 20);
    private static readonly uint Field = Rgb(38, 23, 29);
    private static readonly uint FieldEdge = Rgb(74, 50, 60);
    private static readonly uint Raised = Rgb(51, 32, 42);
    private static readonly uint Sakura = Rgb(249, 188, 206);
    private static readonly uint Text = Rgb(252, 238, 242);
    private static readonly uint Muted = Rgb(191, 165, 174);
    private static readonly uint Dim = Rgb(122, 100, 109);
    private static readonly uint Accent = Rgb(226, 58, 94);
    private static readonly uint AccentPressed = Rgb(190, 40, 74);
    private static readonly uint Border = Rgb(67, 42, 53);
    private static readonly uint White = Rgb(255, 255, 255);

    private nint window, details, folderField, toggle;
    private nint originalSwitchProc;
    private nint regularFont, smallFont, headingFont, brandFont, iconFont, bigIconFont;
    private nint backgroundBrush, cardBrush, fieldBrush;
    private readonly Dictionary<int, nint> buttons = [];
    private Rect cardArea, fieldArea, progressArea;
    private int contentLeft, logicalWidth, logicalHeight;
    private int result;
    private bool complete;
    private Exception? failure;
    private int? percent;
    private string? progressText;
    private double scale = 1;

    internal string? Folder { get; set; }
    internal bool DesktopShortcut { get; set; }
    // インストール先の欄が無い画面でも、カードの下にスイッチを1つ出せる（スキンの更新の「ダウンロードフォルダーに保存」など）
    //   値は DesktopShortcut に入る
    internal string? CheckBoxLabel { get; set; }
    private string SwitchText => Folder is not null ? DesktopShortcutLabel : CheckBoxLabel ?? "";
    internal string? Subtitle { get; set; }
    internal char? Icon { get; set; }
    // インストーラーの左の進み具合（0 インストール先・1 ダウンロード・2 完了）
    internal int Step { get; set; }
    // インストーラーの下の帯（版・大きさなど。null なら出さない）
    internal string[]? Footer { get; set; }
    internal Action? Tick { get; set; }
    internal int? Progress
    {
        get => percent;
        set
        {
            if (percent == value)
                return;
            percent = value;
            Repaint();
        }
    }
    // ダウンロード中の「22.9 / 33.6 MB」（見出しの下に出す）
    internal string? ProgressText
    {
        get => progressText;
        set
        {
            if (progressText == value)
                return;
            progressText = value;
            Repaint();
        }
    }

    private void Repaint()
    {
        if (window == 0)
            return;
        GetClientRect(window, out Rect client);
        InvalidateRect(window, ref client, false);
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

        logicalWidth = installer ? 760 : 600;
        logicalHeight = installer ? 460 : 360;
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
            // Windows 11 のウィンドウと同じように角を少し丸くする（11/26 たけらい）。Windows 10 では何もしない
            int round = 2; // DWMWCP_ROUND
            try { DwmSetWindowAttribute(window, 33, ref round, sizeof(int)); } // DWMWA_WINDOW_CORNER_PREFERENCE
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
            nint focus = CreateControls();
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
                    (buttons.ContainsValue(GetFocus()) || GetFocus() == toggle && toggle != 0))
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
            foreach (nint resource in new[] { regularFont, smallFont, headingFont, brandFont, iconFont, bigIconFont, backgroundBrush, cardBrush, fieldBrush })
                if (resource != 0)
                    DeleteObject(resource);
            current = null;
        }
    }

    // ===== 置き場所（すべて 1 倍のときの座標。Scale で画面の大きさに合わせる） =====
    private const int SideWidth = 180;
    private const int FooterHeight = 34;
    private int ButtonTop => logicalHeight - (Footer is not null ? FooterHeight : 0) - 54;

    private nint CreateControls()
    {
        int width = logicalWidth;
        regularFont = MakeFont(Scale(15), 400);
        smallFont = MakeFont(Scale(12), 400);
        headingFont = MakeFont(Scale(20), 700);
        brandFont = MakeFont(Scale(15), 700);
        iconFont = MakeFont(Scale(20), 400, "Segoe MDL2 Assets");
        bigIconFont = MakeFont(Scale(16), 400, "Segoe MDL2 Assets");
        backgroundBrush = CreateSolidBrush(Background);
        cardBrush = CreateSolidBrush(Card);
        fieldBrush = CreateSolidBrush(Field);

        contentLeft = installer ? SideWidth + 22 : 24;
        int right = width - 24;
        int cardTop = 88;
        int cardBottom = ButtonTop - 14;
        cardArea = new Rect(Scale(contentLeft), Scale(cardTop), Scale(right), Scale(cardBottom));

        if (CanClose)
            Button("", width - 48, 10, 36, 32, Close);

        int inner = contentLeft + 16, innerRight = right - 16;
        int y = cardTop + 14;
        if (Folder is not null)
        {
            // 保存先：入力欄（角丸の枠は WM_PAINT で描く）と［参照…］
            fieldArea = new Rect(Scale(inner), Scale(y + 22), Scale(innerRight - 104), Scale(y + 22 + 36));
            folderField = Child("EDIT", Folder, 0x00010080, inner + 12, y + 31, innerRight - 104 - inner - 24, 20, 0, regularFont);
            Button("参照…", innerRight - 96, y + 22, 96, 36, Browse);
            y += 22 + 36 + 18;
        }
        if (percent is not null)
        {
            progressArea = new Rect(Scale(inner), Scale(y), Scale(innerRight), Scale(y + 36));
            y += 50;
        }
        bool hasSwitch = Folder is not null || CheckBoxLabel is not null;
        int switchTop = cardBottom - 14 - 34;
        if (Folder is null && body.Length > 0)
        {
            int bottom = (hasSwitch ? switchTop - 8 : cardBottom - 12);
            string content = body.ReplaceLineEndings("\r\n");
            int charsPerLine = Math.Max(1, (innerRight - inner) / 15);
            int lines = content.Split('\n').Sum(line => Math.Max(1, (line.TrimEnd('\r').Length + charsPerLine - 1) / charsPerLine));
            uint scroll = lines * 22 > bottom - y ? 0x00200000u : 0;
            details = Child("EDIT", content, scroll | 0x0844, inner, y, innerRight - inner, Math.Max(24, bottom - y), 0, regularFont);
        }
        if (hasSwitch)
        {
            toggle = Child("BUTTON", SwitchText, 0x00010003, inner, Folder is not null ? y : switchTop, innerRight - inner, 34, 105, regularFont);
            originalSwitchProc = SetWindowLongPtr(toggle, -4, Marshal.GetFunctionPointerForDelegate(SwitchCallback)); // GWLP_WNDPROC
            if (originalSwitchProc == 0)
                throw new Win32Exception();
            if (DesktopShortcut)
                SendMessage(toggle, SetCheck, 1, 0);
        }

        nint focus = 0;
        int buttonY = ButtonTop;
        if (primary is not null)
            focus = Button(primary, right - (secondary is null ? 0 : 120) - 120, buttonY, 120, 38, Accept);
        if (secondary is not null)
        {
            nint second = Button(secondary, right - 120, buttonY, 120, 38, Cancel);
            if (focus == 0) focus = second;
        }
        return focus == 0 ? window : focus;
    }

    private bool CanClose => primary is not null || secondary is not null;
    internal void Complete() => Finish(Accept);

    private void Finish(int value)
    {
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
    private float Scale(float value) => (float)(value * scale);
    private Rect Box(int x, int y, int width, int height) => new(Scale(x), Scale(y), Scale(x + width), Scale(y + height));

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
                // 11/26 たけらい「画面更新時に点滅する」→ 画面に直接描くと「背景で塗りつぶす→上に描く」途中が一瞬見えるので、
                //   見えない紙（メモリ上の絵）に全部描いてから、1 回で画面に写す（ダブルバッファ）
                nint canvas = BeginPaint(hwnd, out PaintData paint);
                try
                {
                    GetClientRect(hwnd, out Rect client);
                    int w = Math.Max(1, client.Right - client.Left), h = Math.Max(1, client.Bottom - client.Top);
                    nint memory = CreateCompatibleDC(canvas);
                    nint bitmap = memory != 0 ? CreateCompatibleBitmap(canvas, w, h) : 0;
                    if (bitmap == 0)
                    {
                        if (memory != 0) DeleteDC(memory);
                        Paint(canvas);
                    }
                    else
                    {
                        nint old = SelectObject(memory, bitmap);
                        try
                        {
                            Paint(memory);
                            BitBlt(canvas, 0, 0, w, h, memory, 0, 0, 0x00CC0020); // SRCCOPY
                        }
                        finally
                        {
                            SelectObject(memory, old);
                            DeleteObject(bitmap);
                            DeleteDC(memory);
                        }
                    }
                }
                finally { EndPaint(hwnd, ref paint); }
                return 0;
            case 0x0014: // WM_ERASEBKGND
                return 1;
            case 0x0133: // WM_CTLCOLOREDIT
            case 0x0138: // WM_CTLCOLORSTATIC
                bool field = data == folderField;
                SetTextColor((nint)parameter, Text);
                SetBkColor((nint)parameter, field ? Field : Card);
                return field ? fieldBrush : cardBrush;
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
                        if (toggle != 0)
                            DesktopShortcut = SendMessage(toggle, GetCheck, 0, 0) == 1;
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
            case 0x0084: // WM_NCHITTEST：上の帯をつかんでウィンドウを動かせる（×ボタンの所は除く）
                var point = new Point { X = (short)(data & 0xffff), Y = (short)(data >> 16) };
                ScreenToClient(hwnd, ref point);
                if (point.Y >= 0 && point.Y < Scale(80))
                {
                    GetClientRect(hwnd, out Rect bounds);
                    if (!CanClose || point.X < bounds.Right - Scale(56))
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

    // ===== 描く =====

    private void Paint(nint canvas)
    {
        GetClientRect(window, out Rect client);
        FillRect(canvas, ref client, backgroundBrush);
        int width = logicalWidth;

        if (installer)
            PaintSidebar(canvas);

        // 見出し：札（記号）＋大きい文字＋小さい説明
        int headerTop = 22;
        Rect tile = Box(contentLeft, headerTop, 44, 44);
        if (Icon is char glyph)
        {
            Rounded(canvas, tile, Raised, Raised, Scale(14));
            DrawLabel(canvas, glyph.ToString(), tile, iconFont, Sakura, 0x25);
        }
        else
        {
            if (!DrawAppIcon(canvas, tile))
            {
                Rounded(canvas, tile, Accent, Accent, Scale(14));
                DrawLabel(canvas, "T", tile, headingFont, White, 0x25);
            }
        }
        int textLeft = contentLeft + 58;
        string? subtitle = progressText ?? Subtitle;
        DrawLabel(canvas, heading, Box(textLeft, subtitle is null ? headerTop + 8 : headerTop - 1, width - 70 - textLeft, 28), headingFont, Text, 0x24);
        if (subtitle is not null)
            DrawLabel(canvas, subtitle, Box(textLeft, headerTop + 27, width - 70 - textLeft, 20), smallFont, Muted, 0x24);

        // カード
        Rounded(canvas, cardArea, Card, Border, Scale(16));
        if (Folder is not null)
        {
            DrawLabel(canvas, "保存先", new Rect(fieldArea.Left, fieldArea.Top - Scale(22), fieldArea.Right, fieldArea.Top - Scale(4)), smallFont, Muted, 0x24);
            Rounded(canvas, fieldArea, Field, FieldEdge, Scale(10));
        }
        if (percent is int value)
        {
            int barTop = progressArea.Top + Scale(24);
            Rect track = new(progressArea.Left, barTop, progressArea.Right, barTop + Scale(8));
            Rounded(canvas, track, FieldEdge, FieldEdge, Scale(8));
            Rect fill = track;
            fill.Right = track.Left + (track.Right - track.Left) * Math.Clamp(value, 0, 100) / 100;
            if (fill.Right - fill.Left >= Scale(8))
                Rounded(canvas, fill, Accent, Accent, Scale(8));
            DrawLabel(canvas, $"{value}%", new Rect(progressArea.Left, progressArea.Top, progressArea.Right, barTop - Scale(4)), brandFont, Sakura, 0x2A);
        }

        // インストーラーの下の帯（版・大きさ・取得元）
        if (Footer is { Length: > 0 } cells)
        {
            int top = logicalHeight - FooterHeight;
            FillColor(canvas, Box(0, top, width, 1), Border);
            int cellWidth = width / cells.Length;
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) FillColor(canvas, Box(i * cellWidth, top, 1, FooterHeight), Border);
                DrawLabel(canvas, cells[i], Box(i * cellWidth + 16, top, cellWidth - 24, FooterHeight), smallFont, Muted, 0x24);
            }
        }
    }

    // exe に入っているアイコン（csproj の ApplicationIcon）を、その大きさで取り出して描く。大きさごとに 1 回だけ取り出して使い回す
    private readonly Dictionary<int, nint> appIcons = new();

    private bool DrawAppIcon(nint canvas, Rect area)
    {
        int size = Math.Min(area.Right - area.Left, area.Bottom - area.Top);
        if (size <= 0) return false;
        if (!appIcons.TryGetValue(size, out nint icon))
        {
            icon = 0;
            try
            {
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    var icons = new nint[1];
                    var ids = new uint[1];
                    if (PrivateExtractIcons(exe, 0, size, size, icons, ids, 1, 0) >= 1)
                        icon = icons[0];
                }
            }
            catch { icon = 0; }
            appIcons[size] = icon;
        }
        if (icon == 0) return false;
        int x = area.Left + (area.Right - area.Left - size) / 2;
        int y = area.Top + (area.Bottom - area.Top - size) / 2;
        return DrawIconEx(canvas, x, y, icon, size, size, 0, 0, 3);
    }

    private void PaintSidebar(nint canvas)
    {
        int bottom = logicalHeight - (Footer is not null ? FooterHeight : 0);
        FillColor(canvas, Box(0, 0, SideWidth, bottom), Card);
        FillColor(canvas, Box(SideWidth, 0, 1, bottom), Border);

        // ロゴの札と名前
        Rect logo = Box(18, 22, 36, 36);
        // 11/26 たけらい「左上のアイコンは今使ってるのにしよ」→ exe に入っているアイコン（AppIcon.ico）。取れないときだけ前の「T」の札
        if (!DrawAppIcon(canvas, logo))
        {
            Rounded(canvas, logo, Accent, Accent, Scale(12));
            DrawLabel(canvas, "T", logo, brandFont, White, 0x25);
        }
        DrawLabel(canvas, "TakeTaiko", Box(64, 22, SideWidth - 70, 20), brandFont, Text, 0x24);
        DrawLabel(canvas, "インストーラー", Box(64, 41, SideWidth - 70, 18), smallFont, Muted, 0x24);

        // 進み具合（終わった所＝桜色のチェック・今の所＝枠つき・まだの所＝うすい番号）
        for (int i = 0; i < Steps.Length; i++)
        {
            int y = 88 + i * 46;
            bool done = i < Step, now = i == Step;
            if (now)
                Rounded(canvas, Box(10, y, SideWidth - 20, 38), Raised, Accent, Scale(12));
            Rect circle = Box(22, y + 8, 22, 22);
            if (done)
            {
                Rounded(canvas, circle, Sakura, Sakura, Scale(22));
                CheckMark(canvas, circle.Left + Scale(6f), circle.Top + Scale(11f), circle.Left + Scale(9.5f), circle.Top + Scale(14.5f),
                    circle.Left + Scale(16f), circle.Top + Scale(7.5f), Scale(2f), Card);
            }
            else
            {
                Rounded(canvas, circle, now ? Accent : Card, now ? Accent : Dim, Scale(22));
                DrawLabel(canvas, (i + 1).ToString(), circle, smallFont, now ? White : Dim, 0x25);
            }
            DrawLabel(canvas, Steps[i], Box(54, y, SideWidth - 64, 38), regularFont, now ? Sakura : done ? Text : Dim, 0x24);
        }
    }

    private void DrawButton(DrawItem item)
    {
        bool close = item.ControlId == Close;
        bool pressed = (item.State & 1) != 0;
        bool focused = (item.State & 0x10) != 0;
        bool inCard = item.ControlId == Browse;
        FillRect(item.Canvas, ref item.Bounds, inCard ? cardBrush : backgroundBrush);
        if (close)
        {
            if (pressed || focused)
                Rounded(item.Canvas, item.Bounds, Raised, focused ? Sakura : Raised, Scale(10));
            DrawLabel(item.Canvas, "", item.Bounds, bigIconFont, Muted, 0x25); // ×
            return;
        }
        bool primaryButton = item.ControlId == Accept;
        uint fill = primaryButton ? (pressed ? AccentPressed : Accent) : (pressed ? Border : Raised);
        uint edge = focused ? Sakura : primaryButton ? fill : Border;
        Rounded(item.Canvas, item.Bounds, fill, edge, Scale(12));
        string label = item.ControlId switch
        {
            Accept => primary!,
            Cancel => secondary!,
            _ => "参照…"
        };
        DrawLabel(item.Canvas, label, item.Bounds, primaryButton ? brandFont : regularFont, primaryButton ? White : Text, 0x25);
    }

    // スイッチ（Windows のチェックボックスの状態・キーボード操作はそのまま使い、見た目だけ描き替える）
    //   左に文字、右に横長のスイッチ（オン＝桜色の濃い方・白い丸が右）
    private static nint DrawSwitch(nint control, uint message, nuint parameter, nint data)
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
                FillRect(canvas, ref bounds, owner.cardBrush);
                bool isChecked = SendMessage(control, GetCheck, 0, 0) == 1;
                bool focused = GetFocus() == control;
                int w = owner.Scale(42), h = owner.Scale(24);
                int top = (bounds.Bottom - h) / 2;
                var pill = new Rect(bounds.Right - w, top, bounds.Right, top + h);
                Rounded(canvas, pill, isChecked ? Accent : FieldEdge, focused ? Sakura : isChecked ? Accent : FieldEdge, h);
                int knob = h - owner.Scale(6);
                int knobLeft = isChecked ? pill.Right - owner.Scale(3) - knob : pill.Left + owner.Scale(3);
                Rounded(canvas, new Rect(knobLeft, top + owner.Scale(3), knobLeft + knob, top + owner.Scale(3) + knob),
                    isChecked ? White : Muted, isChecked ? White : Muted, knob);
                bounds.Right -= w + owner.Scale(12);
                DrawLabel(canvas, owner.SwitchText, bounds, owner.regularFont, Text, 0x24);
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

        nint result = CallWindowProc(owner.originalSwitchProc, control, message, parameter, data);
        if (message is SetCheck or 0x0007 or 0x0008 or 0x0202 or 0x0101)
        {
            GetClientRect(control, out Rect bounds);
            InvalidateRect(control, ref bounds, false);
        }
        return result;
    }

    private static void FillColor(nint canvas, Rect box, uint color)
    {
        nint brush = CreateSolidBrush(color);
        FillRect(canvas, ref box, brush);
        DeleteObject(brush);
    }

    // 角丸の四角。GDI+ でなめらか（アンチエイリアス）に描く。GDI+ が使えないときだけ、前の GDI（ギザギザ）で描く
    //   radius は GDI の RoundRect と同じ「角の円の直径」
    private static void Rounded(nint canvas, Rect box, uint fill, uint edge, int radius)
    {
        if (Gdip.Ready && Gdip.RoundedRect(canvas, box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top, radius, Argb(fill), Argb(edge)))
            return;
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

    private static void CheckMark(nint canvas, float x1, float y1, float x2, float y2, float x3, float y3, float width, uint color)
    {
        if (Gdip.Ready && Gdip.Polyline(canvas, [x1, y1, x2, y2, x3, y3], width, Argb(color)))
            return;
        nint pen = CreatePen(0, (int)MathF.Round(width), color);
        nint previous = SelectObject(canvas, pen);
        MoveToEx(canvas, (int)x1, (int)y1, 0);
        LineTo(canvas, (int)x2, (int)y2);
        LineTo(canvas, (int)x3, (int)y3);
        SelectObject(canvas, previous);
        DeleteObject(pen);
    }

    // COLORREF（0x00BBGGRR）→ GDI+ の ARGB（不透明）
    private static uint Argb(uint color) => 0xFF000000u | ((color & 0xFF) << 16) | (color & 0xFF00) | ((color >> 16) & 0xFF);

    private static void DrawLabel(nint canvas, string text, Rect box, nint font, uint color, uint format)
    {
        nint old = SelectObject(canvas, font);
        SetBkMode(canvas, 1);
        SetTextColor(canvas, color);
        DrawText(canvas, text, text.Length, ref box, format | 0x800);
        SelectObject(canvas, old);
    }

    private static nint MakeFont(int size, int weight, string face = "Yu Gothic UI") =>
        CreateFont(-size, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, face);

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

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint PrivateExtractIcons(string file, int index, int width, int height, nint[] icons, uint[] ids, uint count, uint flags);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(nint canvas, int x, int y, nint icon, int width, int height, uint step, nint background, uint flags);
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
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint canvas);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint canvas, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint canvas);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern bool RoundRect(nint canvas, int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool MoveToEx(nint canvas, int x, int y, nint previous);
    [DllImport("gdi32.dll")] private static extern bool LineTo(nint canvas, int x, int y);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint canvas, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(nint canvas, uint color);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint canvas, int mode);
}
