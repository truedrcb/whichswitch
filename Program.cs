using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// whichswitch: shows the current keyboard layout (RU/EN) in the title and icon
// of a taskbar window. Event-driven only: shell hook, no polling, no key hooks.

static class Native
{
    [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool DeregisterShellHookWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public int left, top, right, bottom;
    }

    public const int HSHELL_WINDOWACTIVATED = 4;
    public const int HSHELL_LANGUAGE = 8;       // was wrongly 0x19
    public const int HSHELL_UNDOC_LANG = 0x38;  // observed on Win11 when the layout switches
    public const int HSHELL_RUDEFAULT = 0x8004; // RUDE flag | WINDOWACTIVATED (fullscreen/rude app)
}

sealed class MainForm : Form
{
    readonly uint shellMsg = Native.RegisterWindowMessage("SHELLHOOK");
    string current = "";
    readonly System.Windows.Forms.Timer settle = new() { Interval = 400 };

    public MainForm()
    {
        settle.Tick += (_, _) => { settle.Stop(); Update(ForegroundLayout()); };
        Size = new Size(220, 80);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = true; // FixedToolWindow hides it by default; force it via CreateParams
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00040000;  // WS_EX_APPWINDOW: always show on taskbar
            cp.ExStyle &= ~0x00000080; // clear WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RegisterShellHookWindow(Handle);
        Update(Native.GetKeyboardLayout(0));
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Native.DeregisterShellHookWindow(Handle);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == shellMsg)
        {
            int code = m.WParam.ToInt32();            if (code == Native.HSHELL_LANGUAGE || code == Native.HSHELL_UNDOC_LANG)
            {
                // The foreground thread switches its layout slightly AFTER this message,
                // so re-read once after a short delay (one-shot timer, not polling).
                settle.Stop(); settle.Start();
            }
            else if (code == Native.HSHELL_WINDOWACTIVATED || code == Native.HSHELL_RUDEFAULT)
                Update(ForegroundLayout());
        }
        base.WndProc(ref m);
    }

    static IntPtr ForegroundLayout()
    {
        var fg = Native.GetForegroundWindow();
        var thread = Native.GetWindowThreadProcessId(fg, IntPtr.Zero);
        // Prefer the thread that owns the focused control (differs from the top-level
        // window's thread in some apps, e.g. console hosts).
        var gi = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (Native.GetGUIThreadInfo(thread, ref gi) && gi.hwndFocus != IntPtr.Zero)
            thread = Native.GetWindowThreadProcessId(gi.hwndFocus, IntPtr.Zero);
        return Native.GetKeyboardLayout(thread);
    }

    void Update(IntPtr hkl)
    {
        if (hkl == IntPtr.Zero) return;
        var langId = (int)((long)hkl & 0xFFFF);
        string code;
        try { code = new CultureInfo(langId).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch (CultureNotFoundException) { code = "??"; }

        if (code == current) return;
        current = code;
        Text = code;
        SetIcon(code);
    }

    void SetIcon(string code)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(code == "RU" ? Color.FromArgb(200, 40, 40) : Color.FromArgb(40, 90, 200));
            g.DrawString(code, font, Brushes.White, new RectangleF(0, 0, 32, 32), sf);
        }
        var hIcon = bmp.GetHicon();
        var old = Icon;
        Icon = (Icon)Icon.FromHandle(hIcon).Clone();
        old?.Dispose();
        Native.DestroyIcon(hIcon);
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
