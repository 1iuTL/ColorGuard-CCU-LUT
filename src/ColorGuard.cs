using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

static class Nat
{
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr d);
    [DllImport("gdi32.dll")] public static extern bool GetDeviceGammaRamp(IntPtr d, ushort[] r);
    [DllImport("gdi32.dll")] public static extern bool SetDeviceGammaRamp(IntPtr d, ushort[] r);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegOpenKeyEx(IntPtr hKey, string sub, uint opt, int sam, out IntPtr phk);
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int RegNotifyChangeKeyValue(IntPtr hKey, bool sub, int filter, IntPtr ev, bool async);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr CreateEvent(IntPtr a, bool m, bool i, string n);
    [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
    public static readonly IntPtr HKLM = new IntPtr(unchecked((int)0x80000002));
}

static class Guard
{
    public static readonly ushort[] DEFAULT = BuildIdentity();   // no machine-specific data: capture your own curve at first run
    public static readonly ushort[] IDENTITY = BuildIdentity();
    public static ushort[] RampDefault;
    public static ushort[] RampSrgb;
    public static string LogPath = "", ProfDefaultPath = "", ProfSrgbPath = "";
    public static bool CapturedDefault = false, CapturedSrgb = false;

    static ushort[] BuildIdentity()
    {
        var r = new ushort[768];
        for (int i = 0; i < 256; i++) { ushort v = (ushort)Math.Round(i * 65535.0 / 255.0); r[i] = v; r[256 + i] = v; r[512 + i] = v; }
        return r;
    }
    public static void Log(string m)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + m + "\r\n"); } catch { }
    }
    public static ushort[] Current()
    {
        var r = new ushort[768];
        IntPtr h = Nat.GetDC(IntPtr.Zero); Nat.GetDeviceGammaRamp(h, r); Nat.ReleaseDC(IntPtr.Zero, h);
        return r;
    }
    public static int Sum(ushort[] a) { int s = 0; for (int i = 0; i < 768; i++) s += a[i]; return s; }
    public static bool Same(ushort[] a, ushort[] b)
    {
        for (int i = 0; i < 768; i++) if (Math.Abs((int)a[i] - (int)b[i]) > 12) return false;
        return true;
    }
    public static bool Apply(ushort[] r)
    {
        IntPtr h = Nat.GetDC(IntPtr.Zero); bool ok = Nat.SetDeviceGammaRamp(h, r); Nat.ReleaseDC(IntPtr.Zero, h);
        return ok;
    }
    public static string ReadMode()
    {
        string gcu = "";
        try
        {
            using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = bk.OpenSubKey(@"SOFTWARE\OEM\GamingCenter2\MySetting"))
                if (k != null) { object v = k.GetValue("GCUUtil"); if (v != null) gcu = v.ToString(); }
        }
        catch { }
        if (gcu.IndexOf("sRGB.icm", StringComparison.OrdinalIgnoreCase) >= 0) return "sRGB";
        if (gcu.IndexOf("Default.icm", StringComparison.OrdinalIgnoreCase) >= 0) return "原色域";
        return "未知";
    }
    public static ushort[] TryLoad(string p)
    {
        try
        {
            if (!File.Exists(p)) return null;
            var parts = File.ReadAllText(p).Split(new char[] { ' ', ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 768) return null;
            var r = new ushort[768];
            for (int i = 0; i < 768; i++) r[i] = ushort.Parse(parts[i]);
            return r;
        }
        catch { return null; }
    }
    public static void LoadProfiles()
    {
        var d = TryLoad(ProfDefaultPath);
        CapturedDefault = (d != null);
        RampDefault = d != null ? d : DEFAULT;
        var s = TryLoad(ProfSrgbPath);
        CapturedSrgb = (s != null);
        RampSrgb = s != null ? s : IDENTITY;
    }
    public static string SourceText()
    {
        return "曲线来源：原色域=" + (CapturedDefault ? "已捕获" : "未捕获(恒等)") + "，sRGB=" + (CapturedSrgb ? "已捕获" : "未捕获(恒等)");
    }
    static string JoinNums(ushort[] a)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 768; i++) { if (i > 0) sb.Append(' '); sb.Append(a[i]); }
        return sb.ToString();
    }
    public static string CaptureCurrent()
    {
        string mode = ReadMode();
        if (mode != "sRGB" && mode != "原色域") return "无法捕获：当前控制中心档案未知";
        var cur = Current();
        string path;
        if (mode == "sRGB") { path = ProfSrgbPath; File.WriteAllText(path, JoinNums(cur)); RampSrgb = cur; CapturedSrgb = true; }
        else { path = ProfDefaultPath; File.WriteAllText(path, JoinNums(cur)); RampDefault = cur; CapturedDefault = true; }
        Log("captured current ramp as " + mode + " -> " + path + " (sum=" + Sum(cur) + ")");
        return "已捕获" + mode + "曲线（总和 " + Sum(cur) + "）→ " + Path.GetFileName(path);
    }
    public static string CheckOnce()
    {
        string mode = ReadMode();
        if (mode != "sRGB" && mode != "原色域") return "skip";
        ushort[] want = (mode == "sRGB") ? RampSrgb : RampDefault;
        if (Same(Current(), want)) return "ok";
        bool ok = Apply(want);
        Log("mode=" + mode + "  corrected display LUT (set=" + ok + ")");
        return "corrected";
    }
    public static string LutDesc()
    {
        int s = Sum(Current());
        string tag = s == 25165440 ? "恒等" : (s == 22835221 ? "原厂曲线" : "其他");
        return s.ToString("N0") + "（" + tag + "）";
    }
}

class MainForm : Form
{
    static volatile bool running = false;
    static Thread worker;
    Label lblState, lblMode, lblLut, lblProfiles, lblHint;
    Button btnOn, btnOff, btnFix, btnCap, btnLog;

    public MainForm()
    {
        Text = "ColorGuard - 显示色彩守护（手动开启）";
        Width = 500; Height = 330; StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        var lblTitle = new Label();
        lblTitle.Text = "控制中心切换色域后自动校正显示器曲线";
        lblTitle.SetBounds(14, 12, 460, 20);
        lblTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        Controls.Add(lblTitle);

        lblState = new Label(); lblState.SetBounds(14, 42, 460, 20); Controls.Add(lblState);
        lblMode = new Label(); lblMode.SetBounds(14, 66, 460, 20); Controls.Add(lblMode);
        lblLut = new Label(); lblLut.SetBounds(14, 90, 460, 20); Controls.Add(lblLut);
        lblProfiles = new Label(); lblProfiles.SetBounds(14, 114, 460, 20); Controls.Add(lblProfiles);

        btnOn = new Button(); btnOn.Text = "开启守护"; btnOn.SetBounds(14, 146, 100, 30);
        btnOn.Click += delegate { StartGuard(); };
        Controls.Add(btnOn);

        btnOff = new Button(); btnOff.Text = "关闭守护"; btnOff.SetBounds(122, 146, 100, 30);
        btnOff.Click += delegate { StopGuard(); };
        Controls.Add(btnOff);

        btnFix = new Button(); btnFix.Text = "立即校正一次"; btnFix.SetBounds(230, 146, 110, 30);
        btnFix.Click += delegate { Guard.CheckOnce(); RefreshUi(); };
        Controls.Add(btnFix);

        btnLog = new Button(); btnLog.Text = "打开日志"; btnLog.SetBounds(348, 146, 95, 30);
        btnLog.Click += delegate { try { System.Diagnostics.Process.Start("notepad.exe", Guard.LogPath); } catch { } };
        Controls.Add(btnLog);

        btnCap = new Button(); btnCap.Text = "捕获当前曲线（做成通用版用）"; btnCap.SetBounds(14, 186, 220, 30);
        btnCap.Click += delegate { var m = Guard.CaptureCurrent(); lblHint.Text = m; RefreshUi(); };
        Controls.Add(btnCap);

        lblHint = new Label();
        lblHint.Text = "本程序不会自启动；点【开启守护】才生效。切换色域后到目标状态点【捕获当前曲线】，即可把该机曲线保存进 profile-default.ramp / profile-srgb.ramp（换机也能用）。";
        lblHint.SetBounds(14, 224, 460, 56);
        lblHint.ForeColor = Color.DimGray;
        Controls.Add(lblHint);

        Load += delegate
        {
            string ed = Path.GetDirectoryName(Application.ExecutablePath);
            Guard.LogPath = Path.Combine(ed, "ColorGuard.log");
            Guard.ProfDefaultPath = Path.Combine(ed, "profile-default.ramp");
            Guard.ProfSrgbPath = Path.Combine(ed, "profile-srgb.ramp");
            Guard.LoadProfiles();
            RefreshUi();
        };
        FormClosing += delegate { StopGuard(); };
    }

    void StartGuard()
    {
        if (running) return;
        running = true;
        Guard.Log("guard enabled by user");
        worker = new Thread(Worker); worker.IsBackground = true; worker.Start();
        RefreshUi();
    }
    void StopGuard()
    {
        if (!running) { RefreshUi(); return; }
        running = false;
        Guard.Log("guard disabled by user");
        RefreshUi();
    }
    void Worker()
    {
        IntPtr hKey; int rc = Nat.RegOpenKeyEx(Nat.HKLM, @"SOFTWARE\OEM\GamingCenter2", 0, 0x10 | 0x100, out hKey);
        IntPtr ev = Nat.CreateEvent(IntPtr.Zero, false, false, null);
        Guard.CheckOnce();
        Ui(RefreshUi);
        while (running)
        {
            if (rc == 0 && hKey != IntPtr.Zero && ev != IntPtr.Zero)
            {
                Nat.RegNotifyChangeKeyValue(hKey, true, 4, ev, true);
                Nat.WaitForSingleObject(ev, 1000);
            }
            else Thread.Sleep(1000);
            if (!running) break;
            Guard.CheckOnce();
            for (int i = 0; i < 4 && running; i++) { Thread.Sleep(120); Guard.CheckOnce(); }
            Ui(RefreshUi);
        }
    }
    void Ui(Action a) { try { if (!IsDisposed) BeginInvoke(a); } catch { } }
    void RefreshUi()
    {
        try
        {
            lblState.Text = "守护状态：" + (running ? "运行中（自动跟随色域切换）" : "已停止");
            lblState.ForeColor = running ? Color.SeaGreen : Color.DimGray;
            lblMode.Text = "控制中心当前档案：" + Guard.ReadMode();
            lblLut.Text = "显示器曲线总和：" + Guard.LutDesc();
            lblProfiles.Text = Guard.SourceText();
            btnOn.Enabled = !running; btnOff.Enabled = running;
        }
        catch { }
    }
}

class Program
{
    [STAThread]
    static void Main()
    {
        bool created;
        var mtx = new Mutex(true, "Local\\ColorGuardGui", out created);
        if (!created) { MessageBox.Show("ColorGuard 已经在运行了（看任务栏）。", "ColorGuard", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        Application.EnableVisualStyles();
        Application.Run(new MainForm());
    }
}

