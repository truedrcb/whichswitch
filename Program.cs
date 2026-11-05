using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke; // Windows API, generated from NativeMethods.txt by CsWin32

// whichswitch: shows the current keyboard layout (e.g. EN, RU) in the title and icon
// of a taskbar window. Event-driven only: shell hook, no polling, no key hooks.

sealed class MainForm : Form
{
    // The foreground thread switches its layout after the shell message arrives;
    // 80 ms proved too short on Windows 11.
    const int LayoutSettleDelayMs = 400;

    readonly uint shellHookMessage = RegisterWindowMessage("SHELLHOOK");
    readonly Timer layoutSettleTimer = new() { Interval = LayoutSettleDelayMs };
    string shownLayoutCode = "";
    Icon? layoutIcon;

    public MainForm()
    {
        Controls.Add(CreateRepositoryLink());
        layoutSettleTimer.Tick += (_, _) =>
        {
            layoutSettleTimer.Stop();
            ShowForegroundLayout();
        };

        // A normal resizable window with a minimize button (room for future controls).
        ClientSize = new Size(300, 100);
        MinimumSize = new Size(260, 100); // keeps the title bar buttons visible with room to drag
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
    }

    static LinkLabel CreateRepositoryLink()
    {
        const string RepositoryUrl = "https://github.com/truedrcb/whichswitch";
        const int MarginPx = 12;

        var link = new LinkLabel
        {
            Text = "whichswitch",
            Font = SystemFonts.MessageBoxFont, // the standard Windows UI font (same as dialogs)
            AutoSize = true,
            Location = new Point(MarginPx, MarginPx),
        };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        return link;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Without the shell hook the indicator would silently go stale, so fail loudly.
        if (!RegisterShellHookWindow((HWND)Handle))
            throw new InvalidOperationException("Could not register for shell events (RegisterShellHookWindow failed).");

        ShowForegroundLayout();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        DeregisterShellHookWindow((HWND)Handle);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == shellHookMessage)
            HandleShellEvent((uint)m.WParam);

        base.WndProc(ref m);

        // Treat the form's own client area (not child controls) as a title bar: drag by content.
        if (m.Msg == WM_NCHITTEST && m.Result == (nint)HTCLIENT)
            m.Result = (nint)HTCAPTION;
    }

    void HandleShellEvent(uint shellEvent)
    {
        // These two are absent from the Windows metadata, so they are defined by hand.
        const uint HSHELL_UNDOC_LANGUAGE = 0x38; // no official source; observed on Win11 when the layout switches
        const uint HSHELL_RUDEAPPACTIVATED = HSHELL_WINDOWACTIVATED | HSHELL_HIGHBIT;

        switch (shellEvent)
        {
            case HSHELL_LANGUAGE:
            case HSHELL_UNDOC_LANGUAGE:
                ShowForegroundLayoutAfterSettleDelay();
                break;
            case HSHELL_WINDOWACTIVATED:
            case HSHELL_RUDEAPPACTIVATED:
                ShowForegroundLayout();
                break;
        }
    }

    // One-shot, not polling: restarted by each layout event, stopped by its own tick.
    void ShowForegroundLayoutAfterSettleDelay()
    {
        layoutSettleTimer.Stop();
        layoutSettleTimer.Start();
    }

    void ShowForegroundLayout() => ShowLayout(GetForegroundLayout());

    // Returns a null layout when no window is in the foreground, e.g. during a focus change:
    // GetKeyboardLayout(0) would report this app's own layout instead.
    static HKL GetForegroundLayout()
    {
        const uint NoThread = 0;

        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow());
        return foregroundThread == NoThread ? default : GetKeyboardLayout(foregroundThread);
    }

    void ShowLayout(HKL keyboardLayout)
    {
        if (keyboardLayout.IsNull) return;

        var layoutCode = GetLayoutCode(keyboardLayout);
        if (layoutCode == shownLayoutCode) return;

        shownLayoutCode = layoutCode;
        Text = layoutCode;
        ReplaceIcon(layoutCode);
    }

    static string GetLayoutCode(HKL keyboardLayout)
    {
        const int LanguageIdMask = 0xFFFF; // the low word of an HKL is the language ID
        const string UnknownLayoutCode = "??";

        var languageId = (int)((IntPtr)keyboardLayout & LanguageIdMask);
        try { return new CultureInfo(languageId).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch (CultureNotFoundException) { return UnknownLayoutCode; }
    }

    void ReplaceIcon(string layoutCode)
    {
        using var bitmap = DrawIconBitmap(layoutCode);
        var iconHandle = bitmap.GetHicon();
        var newLayoutIcon = (Icon)Icon.FromHandle(iconHandle).Clone();
        Icon = newLayoutIcon;
        layoutIcon?.Dispose(); // only icons we created: the initial Icon belongs to WinForms
        layoutIcon = newLayoutIcon;
        DestroyIcon((HICON)iconHandle);
    }

    static Bitmap DrawIconBitmap(string layoutCode)
    {
        const int IconSizePx = 32;

        var bitmap = new Bitmap(IconSizePx, IconSizePx);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = CreateIconFont();
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(GetIconColor(layoutCode));
        graphics.DrawString(layoutCode, font, Brushes.White, new RectangleF(0, 0, IconSizePx, IconSizePx), centered);
        return bitmap;
    }

    // Colors are compiled in from the IconColor.* items in whichswitch.csproj.
    static Color GetIconColor(string layoutCode)
    {
        const string DefaultColorKey = "Default";

        var colorValue = FindIconColorValue(layoutCode) ?? FindIconColorValue(DefaultColorKey)
            ?? throw new InvalidOperationException("whichswitch.csproj has no IconColor.Default item.");
        return ColorTranslator.FromHtml(colorValue);
    }

    static string? FindIconColorValue(string colorKey)
    {
        const string MetadataKeyPrefix = "IconColor.";

        foreach (var metadata in typeof(MainForm).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            if (metadata.Key == MetadataKeyPrefix + colorKey)
                return metadata.Value;
        return null;
    }

    // The Windows shell font, as the closest match to the taskbar's language indicator.
    // GDI+ silently substitutes a missing font, so compare the resolved name to detect
    // that the Windows 11 font is absent.
    static Font CreateIconFont()
    {
        const string Windows11ShellFontName = "Segoe UI Variable Small";
        const string Windows10ShellFontName = "Segoe UI";

        var font = CreateIconFont(Windows11ShellFontName);
        if (font.Name == Windows11ShellFontName) return font;

        font.Dispose();
        return CreateIconFont(Windows10ShellFontName);
    }

    static Font CreateIconFont(string fontName)
    {
        const int FontSizePx = 15;
        return new Font(fontName, FontSizePx, FontStyle.Regular, GraphicsUnit.Pixel);
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
