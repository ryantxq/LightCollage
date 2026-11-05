// LightCollage QA 辅助工具（仅用于验收测试，不参与产品构建）
// 编译：csc /target:exe /platform:anycpu /out:QaTool.exe QaTool.cs /r:...
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

internal static class QaTool
{
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, ref RECT r, uint winini);
    [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int value);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private static string LogPath = null;
    private static int ExitCode = 0;

    private static void Out(string s)
    {
        Console.WriteLine(s);
        if (LogPath != null)
        {
            try { File.AppendAllText(LogPath, s + "\r\n", Encoding.UTF8); } catch { }
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch { try { SetProcessDpiAwareness(2); } catch { } }
        Thread.Sleep(0);
        try
        {
            if (args.Length == 0) { Usage(); return; }
            string cmd = args[0].ToLowerInvariant();
            switch (cmd)
            {
                case "rect": Rect(args); break;
                case "shot": Shot(args); break;
                case "screen": ScreenShot(args); break;
                case "uia": UiaDump(args); break;
                case "wins": Wins(); break;
                case "fg": Fg(args); break;
                case "bring": BringCmd(args); break;
                case "ctx": CtxCmd(args); break;
                case "menutest": MenuTestCmd(args); break;
                case "dumpwin": DumpWinCmd(args); break;
                case "click": MouseClick(args, false); break;
                case "clickseq": ClickSeqCmd(args); break;
                case "rclick": MouseClick(args, true); break;
                case "dclick": DoubleClick(args); break;
                case "move": Move(args); break;
                case "drag": Drag(args); break;
                case "wheel": Wheel(args); break;
                case "keys": KeysCmd(args); break;
                case "keywin": KeyWinCmd(args); break;
                case "paste": Paste(args); break;
                case "close": CloseCmd(args); break;
                case "setpos": SetPos(args); break;
                case "coldstart": ColdStart(args); break;
                case "waitwin": WaitWin(args); break;
                case "waitgone": WaitGone(args); break;
                case "waitfile": WaitFile(args); break;
                case "uiafind": UiaFind(args); break;
                case "clickuia": ClickUia(args); break;
                case "clickname": ClickNameCmd(args); break;
                case "settext": SetTextCmd(args); break;
                case "waittext": WaitTextCmd(args); break;
                case "segwait": SegWaitCmd(args); break;
                case "uistart": UiStartCmd(args); break;
                case "ctrlsave": CtrlSaveCmd(args); break;
                case "clickthumb": ClickThumbCmd(args); break;
                case "step": StepCmd(args); break;
                case "seg": SegCmd(args); break;
                case "slider": SliderCmd(args); break;
                case "dragthumb": DragThumbCmd(args); break;
                case "texts": TextsCmd(args); break;
                case "menudump": MenuDump(args); break;
                case "fgwin": FgInfo(); break;
                case "sleep": Thread.Sleep(int.Parse(args[1])); break;
                default: Usage(); break;
            }
        }
        catch (Exception ex)
        {
            Out("ERROR " + ex.GetType().Name + ": " + ex.Message);
            ExitCode = 1;
        }
        Environment.Exit(ExitCode);
    }

    private static void Usage()
    {
        Out("QaTool commands:");
        Out("  rect <title> | shot <title> <png> | screen <png> | uia <title> <out.txt>");
        Out("  wins | fg <title> | fgwin | click <x> <y> | rclick <x> <y> | dclick <x> <y>");
        Out("  move <x> <y> | drag <x1> <y1> <x2> <y2> [ms] | wheel <x> <y> <delta>");
        Out("  keys <sendkeys> | paste <text> | close <title> | setpos <title> <x> <y> <w> <h>");
        Out("  coldstart <exe> [args...] | waitwin <title> <ms> | waitgone <title> <ms> | waitfile <path> <ms>");
        Out("  uiafind <name> [exact] | clickuia <name> | menudump <out.txt>");
    }

    private static IntPtr WaitWindow(string title, int timeoutMs)
    {
        int waited = 0;
        while (waited <= timeoutMs)
        {
            IntPtr h = FindWindow(null, title);
            if (h != IntPtr.Zero) return h;
            Thread.Sleep(10);
            waited += 10;
        }
        return IntPtr.Zero;
    }

    private static string WinTitle(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string WinClass(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string RectText(RECT r)
    {
        return string.Format("{0},{1},{2},{3} ({4}x{5})", r.Left, r.Top, r.Right, r.Bottom, r.Right - r.Left, r.Bottom - r.Top);
    }

    private static void Rect(string[] args)
    {
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        RECT wr, cr;
        GetWindowRect(h, out wr);
        GetClientRect(h, out cr);
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        Out("title=" + WinTitle(h));
        Out("class=" + WinClass(h));
        Out("hwnd=0x" + h.ToInt64().ToString("X"));
        Out("pid=" + pid);
        Out("window=" + RectText(wr));
        Out("client=" + RectText(cr));
        Out("dpi=" + GetDpiForWindow(h));
        Out("visible=" + IsWindowVisible(h) + " iconic=" + IsIconic(h));
    }

    private static void Shot(string[] args)
    {
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        RECT r;
        GetWindowRect(h, out r);
        int w = r.Right - r.Left, hh = r.Bottom - r.Top;
        using (Bitmap b = new Bitmap(w, hh))
        {
            using (Graphics g = Graphics.FromImage(b))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(h, hdc, 2);
                g.ReleaseHdc(hdc);
            }
            b.Save(args[2], ImageFormat.Png);
        }
        Out("captured " + w + "x" + hh + " -> " + args[2]);
    }

    private static void ScreenShot(string[] args)
    {
        Rectangle vs = SystemInformation.VirtualScreen;
        using (Bitmap b = new Bitmap(vs.Width, vs.Height))
        {
            using (Graphics g = Graphics.FromImage(b))
            {
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0, new Size(vs.Width, vs.Height), CopyPixelOperation.SourceCopy);
            }
            b.Save(args[1], ImageFormat.Png);
        }
        Out("screenshot " + vs.Width + "x" + vs.Height + " -> " + args[1]);
    }

    private static void Wins()
    {
        EnumWindows(delegate(IntPtr h, IntPtr lp)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.Length == 0) return true;
            RECT r;
            GetWindowRect(h, out r);
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            Out(string.Format("hwnd=0x{0} pid={1} class={2} rect={3} title={4}", h.ToInt64().ToString("X"), pid, WinClass(h), RectText(r), sb.ToString()));
            return true;
        }, IntPtr.Zero);
    }

    private static void Fg(string[] args)
    {
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        if (IsIconic(h)) ShowWindow(h, 9);
        bool ok = SetForegroundWindow(h);
        Thread.Sleep(120);
        Out("foreground=" + ok + " fgtitle=" + WinTitle(GetForegroundWindow()));
    }

    private static void BringWindow(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, 9);
        SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
        Thread.Sleep(120);
        SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
        BringWindowToTop(h);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        uint fgPid;
        IntPtr fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, out fgPid);
        uint myThread = GetCurrentThreadId();
        bool attached = false;
        try { attached = AttachThreadInput(myThread, fgThread, true); } catch { }
        try { SetForegroundWindow(h); } catch { }
        if (attached) { try { AttachThreadInput(myThread, fgThread, false); } catch { } }
        Thread.Sleep(150);
    }

    private static void BringCmd(string[] args)
    {
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        BringWindow(h);
        Out("bring hwnd=0x" + h.ToInt64().ToString("X") + " fg=" + WinTitle(GetForegroundWindow()));
    }

    private static void ScreenSave(string png)
    {
        Rectangle vs = SystemInformation.VirtualScreen;
        using (Bitmap b = new Bitmap(vs.Width, vs.Height))
        {
            using (Graphics g = Graphics.FromImage(b))
            {
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0, new Size(vs.Width, vs.Height), CopyPixelOperation.SourceCopy);
            }
            b.Save(png, ImageFormat.Png);
        }
    }

    private static IntPtr FindVisibleWindowByClass(string cls)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr w, IntPtr lp)
        {
            if (!IsWindowVisible(w)) return true;
            if (WinClass(w) != cls) return true;
            RECT r;
            GetWindowRect(w, out r);
            if (r.Right - r.Left < 50 || r.Bottom - r.Top < 50) return true;
            found = w;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static AutomationElement FindElementByNameContains(AutomationElement root, string part)
    {
        AutomationElementCollection all = null;
        try { all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition); } catch { }
        if (all == null) return null;
        for (int i = 0; i < all.Count; i++)
        {
            try
            {
                string n = all[i].Current.Name;
                if (!string.IsNullOrEmpty(n) && n.IndexOf(part, StringComparison.Ordinal) >= 0) return all[i];
            }
            catch { }
        }
        return null;
    }

    private static bool ClickElement(AutomationElement e, string label)
    {
        try
        {
            System.Windows.Rect r = e.Current.BoundingRectangle;
            if (r.Width <= 0 || r.Height <= 0) { Out("ELEMENT_ZERO_RECT " + label); return false; }
            int cx = (int)Math.Round(r.Left + r.Width / 2.0);
            int cy = (int)Math.Round(r.Top + r.Height / 2.0);
            Out(string.Format("{0} name=\"{1}\" rect={2:0},{3:0},{4:0},{5:0} click={6},{7}", label, e.Current.Name, r.Left, r.Top, r.Right, r.Bottom, cx, cy));
            MoveIt(cx, cy);
            Thread.Sleep(150);
            ClickDownUp(false);
            Thread.Sleep(250);
            return true;
        }
        catch (Exception ex) { Out("CLICK_FAIL " + label + ": " + ex.Message); return false; }
    }

    private static void MenuTestCmd(string[] args)
    {
        // menutest <explorerTitle> <fileX> <fileY> <shotPrefix> [--shiftf10]
        string title = args[1];
        int fx = int.Parse(args[2]);
        int fy = int.Parse(args[3]);
        string prefix = args[4];
        bool shiftF10 = args.Length > 5 && args[5] == "--shiftf10";
        IntPtr h = WaitWindow(title, 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        BringWindow(h);
        Out("STEP1 foreground=" + WinTitle(GetForegroundWindow()));
        // 重新建立多选：点选第一项 → Ctrl+A 全选；并记录资源管理器状态栏选择信息
        MoveIt(fx, fy);
        ClickDownUp(false);
        Thread.Sleep(200);
        SendKeys.SendWait("^a");
        Thread.Sleep(400);
        try
        {
            AutomationElement ex = AutomationElement.FromHandle(h);
            AutomationElementCollection all = ex.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            int selectedByStatus = 0;
            for (int i = 0; i < all.Count; i++)
            {
                try
                {
                    string n = all[i].Current.Name;
                    if (!string.IsNullOrEmpty(n) && n.IndexOf("个项目", StringComparison.Ordinal) >= 0)
                    {
                        Out("SELECTION_STATUS \"" + n + "\"");
                        selectedByStatus++;
                    }
                }
                catch { }
            }
            if (selectedByStatus == 0) Out("SELECTION_STATUS not exposed");
        }
        catch (Exception ex) { Out("SELECTION_UIA_ERROR " + ex.Message); }
        ScreenSave(prefix + "-selection.png");

        if (shiftF10)
        {
            SendKeys.SendWait("+{F10}");
            Out("STEP2 sent Shift+F10 (classic menu direct)");
        }
        else
        {
            MoveIt(fx, fy);
            ClickDownUp(true);
            Out("STEP2 right-clicked at " + fx + "," + fy);
        }
        IntPtr popup = IntPtr.Zero;
        for (int i = 0; i < 100; i++)
        {
            popup = FindVisibleWindowByClass("Microsoft.UI.Content.PopupWindowSiteBridge");
            if (popup != IntPtr.Zero) break;
            Thread.Sleep(50);
        }
        if (popup != IntPtr.Zero)
        {
            Thread.Sleep(600);
            RECT pr;
            GetWindowRect(popup, out pr);
            Out("WIN11_MENU hwnd=0x" + popup.ToInt64().ToString("X") + " rect=" + RectText(pr));
            ScreenSave(prefix + "-win11-menu.png");
            try
            {
                AutomationElement pe = AutomationElement.FromHandle(popup);
                using (StreamWriter w = new StreamWriter(prefix + "-win11-menu-uia.txt", false, new UTF8Encoding(false)))
                {
                    DumpTree(pe, w, 8, 400);
                }
                AutomationElement more = FindElementByNameContains(pe, "显示更多选项");
                if (more != null)
                {
                    // 重新取一次实时 rect 并点击
                    ClickElement(more, "WIN11_CLICK_MORE");
                }
                else
                {
                    Out("NO_MORE_OPTIONS_ITEM");
                }
            }
            catch (Exception ex) { Out("WIN11_UIA_ERROR " + ex.Message); }
            Thread.Sleep(700);
        }
        else
        {
            Out("NO_WIN11_POPUP (可能直接打开了经典菜单)");
        }

        // 查找经典菜单窗口 #32768
        IntPtr classic = IntPtr.Zero;
        for (int i = 0; i < 100; i++)
        {
            classic = FindVisibleWindowByClass("#32768");
            if (classic != IntPtr.Zero) break;
            Thread.Sleep(50);
        }
        if (classic == IntPtr.Zero)
        {
            Out("NO_CLASSIC_MENU");
            ScreenSave(prefix + "-classic-missing.png");
            ExitCode = 1;
            return;
        }
        Thread.Sleep(300);
        RECT cr;
        GetWindowRect(classic, out cr);
        Out("CLASSIC_MENU hwnd=0x" + classic.ToInt64().ToString("X") + " rect=" + RectText(cr));
        ScreenSave(prefix + "-classic.png");
        try
        {
            AutomationElement ce = AutomationElement.FromHandle(classic);
            using (StreamWriter w = new StreamWriter(prefix + "-classic-uia.txt", false, new UTF8Encoding(false)))
            {
                DumpTree(ce, w, 8, 600);
            }
            AutomationElement pin = FindElementByNameContains(ce, "拼图");
            if (pin == null)
            {
                Out("NO_PINTU_ITEM_IN_CLASSIC_MENU");
                ExitCode = 2;
                return;
            }
            Out("FOUND_PINTU_ITEM");
            ClickElement(pin, "CLASSIC_CLICK_PINTU");
        }
        catch (Exception ex) { Out("CLASSIC_UIA_ERROR " + ex.Message); ExitCode = 3; return; }
        // 等待编辑器窗口出现
        Stopwatch sw = Stopwatch.StartNew();
        IntPtr app = IntPtr.Zero;
        while (sw.ElapsedMilliseconds < 15000)
        {
            app = FindWindow(null, "轻拼 - 图片拼接");
            if (app != IntPtr.Zero) break;
            Thread.Sleep(50);
        }
        if (app == IntPtr.Zero) { Out("APP_NOT_STARTED"); ExitCode = 4; return; }
        Out("APP_WINDOW_VISIBLE after " + sw.ElapsedMilliseconds + "ms");
        Thread.Sleep(1200);
        ScreenSave(prefix + "-app.png");
        Out("DONE");
    }

    private static void CtxCmd(string[] args)
    {
        // ctx <title> <x> <y> <png> [delayMs]
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        int x = int.Parse(args[2]);
        int y = int.Parse(args[3]);
        string png = args[4];
        int delay = args.Length > 5 ? int.Parse(args[5]) : 900;
        BringWindow(h);
        MoveIt(x, y);
        ClickDownUp(true);
        Thread.Sleep(delay);
        Rectangle vs = SystemInformation.VirtualScreen;
        using (Bitmap b = new Bitmap(vs.Width, vs.Height))
        {
            using (Graphics g = Graphics.FromImage(b))
            {
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0, new Size(vs.Width, vs.Height), CopyPixelOperation.SourceCopy);
            }
            b.Save(png, ImageFormat.Png);
        }
        Out("ctx screenshot -> " + png);
        Out("fg=" + WinTitle(GetForegroundWindow()) + " class=" + WinClass(GetForegroundWindow()));
        EnumWindows(delegate(IntPtr w, IntPtr lp)
        {
            if (!IsWindowVisible(w)) return true;
            RECT r;
            GetWindowRect(w, out r);
            if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return true;
            string cls = WinClass(w);
            string title = WinTitle(w);
            // 只输出可疑菜单/弹窗窗口（非桌面/任务栏/资源管理器主窗）
            if (cls == "CabinetWClass" || cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || title.Length == 0)
            {
                if (cls != "CabinetWClass") return true;
            }
            Out(string.Format("popup hwnd=0x{0} class={1} rect={2} title={3}", w.ToInt64().ToString("X"), cls, RectText(r), title));
            return true;
        }, IntPtr.Zero);
    }

    private static void DumpWinCmd(string[] args)
    {
        string hs = args[1];
        if (hs.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hs = hs.Substring(2);
        long val = long.Parse(hs, System.Globalization.NumberStyles.HexNumber);
        IntPtr h = new IntPtr(val);
        string target = args.Length > 2 ? args[2] : "win.txt";
        AutomationElement root = AutomationElement.FromHandle(h);
        using (StreamWriter w = new StreamWriter(target, false, new UTF8Encoding(false)))
        {
            w.WriteLine("WINDOW: " + ElemLine(root, 0));
            DumpTree(root, w, 12, 3000);
        }
        Out("dumpwin -> " + target);
    }

    private static AutomationElement FindInWindow(string title, string name, bool contains)
    {
        IntPtr h = WaitWindow(title, 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW " + title); return null; }
        BringWindow(h);
        AutomationElement root = AutomationElement.FromHandle(h);
        if (root == null) return null;
        List<AutomationElement> list = FindByName(root, name, !contains);
        if (list.Count == 0) { Out("NOELEMENT \"" + name + "\" in " + title); return null; }
        // 优先非文本标签（同名 Label 与自定义控件同时存在时取可交互控件）
        for (int i = 0; i < list.Count; i++)
        {
            try
            {
                string ct = list[i].Current.ControlType.ProgrammaticName;
                if (ct != "ControlType.Text" && ct != "ControlType.TitleBar" && ct != "ControlType.MenuBar") return list[i];
            }
            catch { }
        }
        return list[0];
    }

    private static void ClickNameCmd(string[] args)
    {
        // clickname <windowTitle> <name> [contains]
        bool contains = args.Length > 3 && args[3] == "contains";
        AutomationElement e = FindInWindow(args[1], args[2], contains);
        if (e == null) { ExitCode = 1; return; }
        if (!ClickElement(e, "CLICKNAME")) ExitCode = 1;
    }

    private static void SetTextCmd(string[] args)
    {
        // settext <windowTitle> <automationIdOrName> <text>
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        BringWindow(h);
        AutomationElement root = AutomationElement.FromHandle(h);
        AutomationElementCollection all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        string key = args[2];
        for (int i = 0; i < all.Count; i++)
        {
            AutomationElement e = all[i];
            try
            {
                string aid = e.Current.AutomationId;
                string nm = e.Current.Name;
                if (string.Equals(aid, key, StringComparison.Ordinal) || (nm != null && nm.IndexOf(key, StringComparison.Ordinal) >= 0))
                {
                    object pattern;
                    if (e.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                    {
                        ((ValuePattern)pattern).SetValue(args[3]);
                        Out("settext ok id=" + aid + " name=" + nm);
                        return;
                    }
                }
            }
            catch { }
        }
        Out("NO_EDITABLE_ELEMENT " + key); ExitCode = 1;
    }

    private static void WaitTextCmd(string[] args)
    {
        // waittext <windowTitle> <substr> <timeoutMs>
        string title = args[1], part = args[2];
        int timeout = int.Parse(args[3]);
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds <= timeout)
        {
            IntPtr h = FindWindow(null, title);
            if (h != IntPtr.Zero)
            {
                try
                {
                    AutomationElement root = AutomationElement.FromHandle(h);
                    if (FindElementByNameContains(root, part) != null)
                    {
                        Out("TEXT_FOUND \"" + part + "\" after " + sw.ElapsedMilliseconds + "ms");
                        return;
                    }
                }
                catch { }
            }
            Thread.Sleep(15);
        }
        Out("TEXT_TIMEOUT \"" + part + "\" after " + timeout + "ms"); ExitCode = 1;
    }

    private static AutomationElement FindStripElement(AutomationElement root)
    {
        AutomationElementCollection all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        AutomationElement best = null;
        double bestW = 0;
        for (int i = 0; i < all.Count; i++)
        {
            try
            {
                AutomationElement e = all[i];
                System.Windows.Rect r = e.Current.BoundingRectangle;
                if (r.Height >= 190 && r.Height <= 230 && r.Width > 800 && r.Width < 2400)
                {
                    if (r.Width > bestW) { bestW = r.Width; best = e; }
                }
            }
            catch { }
        }
        return best;
    }

    private static void ClickThumbCmd(string[] args)
    {
        // clickthumb <windowTitle> <index>
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        AutomationElement root = AutomationElement.FromHandle(h);
        AutomationElement strip = FindStripElement(root);
        if (strip == null) { Out("NO_STRIP"); ExitCode = 1; return; }
        System.Windows.Rect r = strip.Current.BoundingRectangle;
        double dpiScale = GetDpiForWindow(h) / 96.0;
        int idx = int.Parse(args[2]);
        double card = 86 * dpiScale, gap = 10 * dpiScale;
        double px = r.Left + idx * (card + gap) + card / 2;
        double py = r.Top + r.Height / 2;
        Out(string.Format("STRIP rect={0:0},{1:0},{2:0},{3:0} thumb[{4}] click={5:0},{6:0}", r.Left, r.Top, r.Right, r.Bottom, idx, px, py));
        ClickAt(px, py);
    }

    private static void SegWaitCmd(string[] args)
    {
        // segwait <windowTitle> <uiaName> <index> <expectedText> [timeoutMs]
        AutomationElement e = FindInWindow(args[1], args[2], false);
        if (e == null) { ExitCode = 1; return; }
        automation_rect r = GetRect(e);
        int index = int.Parse(args[3]);
        string expected = args[4];
        int timeout = args.Length > 5 ? int.Parse(args[5]) : 3000;
        double px = r.X + r.W / 2.0 * (index + 0.5);
        double py = r.Y + r.H / 2;
        MoveIt((int)Math.Round(px), (int)Math.Round(py));
        ClickDownUp(false);
        Stopwatch sw = Stopwatch.StartNew();
        IntPtr h = WaitWindow(args[1], 1000);
        bool found = false;
        while (sw.ElapsedMilliseconds <= timeout)
        {
            try
            {
                AutomationElement root = AutomationElement.FromHandle(h);
                if (FindElementByNameContains(root, expected) != null) { found = true; break; }
            }
            catch { }
            Thread.Sleep(10);
        }
        sw.Stop();
        Out((found ? "SEGWAIT_OK " : "SEGWAIT_TIMEOUT ") + expected + " after " + sw.ElapsedMilliseconds + "ms (click " + px.ToString("0") + "," + py.ToString("0") + ")");
        if (!found) ExitCode = 1;
    }

    private static void UiStartCmd(string[] args)
    {
        // uistart <exe> <expectedText> <timeoutMs> [args...]
        string exe = args[1];
        string expected = args[2];
        int timeout = int.Parse(args[3]);
        StringBuilder argLine = new StringBuilder();
        for (int i = 4; i < args.Length; i++)
        {
            if (argLine.Length > 0) argLine.Append(' ');
            string a = args[i];
            if (a.IndexOf(' ') >= 0) argLine.Append('"').Append(a).Append('"');
            else argLine.Append(a);
        }
        ProcessStartInfo psi = new ProcessStartInfo(exe, argLine.ToString());
        psi.UseShellExecute = true;
        psi.WorkingDirectory = Path.GetDirectoryName(exe);
        Stopwatch sw = Stopwatch.StartNew();
        Process p = Process.Start(psi);
        IntPtr h = IntPtr.Zero;
        int waited = 0;
        while (waited < 30000)
        {
            h = FindWindow(null, "轻拼 - 图片拼接");
            if (h != IntPtr.Zero) break;
            Thread.Sleep(5);
            waited += 5;
        }
        long visibleMs = sw.ElapsedMilliseconds;
        if (h == IntPtr.Zero) { Out("TIMEOUT no window"); ExitCode = 1; return; }
        bool found = false;
        List<AutomationElement> candidates = new List<AutomationElement>();
        long lastDiscover = -100000;
        while (sw.ElapsedMilliseconds <= timeout)
        {
            try
            {
                if (candidates.Count == 0 || sw.ElapsedMilliseconds - lastDiscover > 300)
                {
                    candidates.Clear();
                    AutomationElement root = AutomationElement.FromHandle(h);
                    AutomationElementCollection all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                    for (int i = 0; i < all.Count; i++)
                    {
                        try
                        {
                            string n = all[i].Current.Name;
                            if (string.IsNullOrEmpty(n)) continue;
                            if (n.StartsWith("共 ") || n.IndexOf("载入", StringComparison.Ordinal) >= 0 ||
                                n.IndexOf("保存", StringComparison.Ordinal) >= 0 || n == "就绪")
                            {
                                candidates.Add(all[i]);
                            }
                        }
                        catch { }
                    }
                    lastDiscover = sw.ElapsedMilliseconds;
                }
                for (int i = 0; i < candidates.Count; i++)
                {
                    try
                    {
                        if (candidates[i].Current.Name.IndexOf(expected, StringComparison.Ordinal) >= 0)
                        {
                            found = true;
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            if (found) break;
            Thread.Sleep(10);
        }
        sw.Stop();
        RECT wr, cr;
        GetWindowRect(h, out wr);
        GetClientRect(h, out cr);
        Out(string.Format("UISTART window_visible_ms={0} text_ready_ms={1} found={2} pid={3} window={4}x{5} client={6}x{7} dpi={8}",
            visibleMs, sw.ElapsedMilliseconds, found, p.Id, wr.Right - wr.Left, wr.Bottom - wr.Top, cr.Right - cr.Left, cr.Bottom - cr.Top, GetDpiForWindow(h)));
    }

    private static void FocusPreview(IntPtr h)
    {
        RECT wr, cr;
        GetWindowRect(h, out wr);
        GetClientRect(h, out cr);
        int borderX = (wr.Right - wr.Left - cr.Right) / 2;
        int px = wr.Left + borderX + cr.Right / 4;
        int py = wr.Top + (wr.Bottom - wr.Top - cr.Bottom) / 2 + cr.Bottom / 4;
        MoveIt(px, py);
        Thread.Sleep(150);
        ClickDownUp(false);
        Thread.Sleep(200);
    }

    private static string PollNewFile(string dir, HashSet<string> before, Stopwatch sw, int timeoutMs)
    {
        while (sw.ElapsedMilliseconds <= timeoutMs)
        {
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    string name = Path.GetFileName(f);
                    if (!before.Contains(name) && new FileInfo(f).Length > 0)
                    {
                        long last = -1;
                        for (int i = 0; i < 50; i++)
                        {
                            long len = new FileInfo(f).Length;
                            if (len == last) return f;
                            last = len;
                            Thread.Sleep(20);
                        }
                        return f;
                    }
                }
            }
            Thread.Sleep(10);
        }
        return null;
    }

    private static void CtrlSaveCmd(string[] args)
    {
        // ctrlsave <windowTitle> <watchDir>
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        string dir = args[2];
        HashSet<string> before = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(dir)) foreach (string f in Directory.GetFiles(dir)) before.Add(Path.GetFileName(f));
        BringWindow(h);
        FocusPreview(h);
        Stopwatch sw = Stopwatch.StartNew();
        SendKeys.SendWait("^s");
        string created = PollNewFile(dir, before, sw, 15000);
        if (created == null)
        {
            Out("SAVE_CTRL_S_TIMEOUT retry via save button");
            // 兜底：点击自绘保存按钮（名称以「保存」开头，控件类型非文本）
            AutomationElement root = AutomationElement.FromHandle(h);
            AutomationElementCollection all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            AutomationElement saveBtn = null;
            for (int i = 0; i < all.Count; i++)
            {
                try
                {
                    string n = all[i].Current.Name;
                    string ct = all[i].Current.ControlType.ProgrammaticName;
                    if (n != null && n.StartsWith("保存") && ct == "ControlType.Pane")
                    {
                        saveBtn = all[i];
                        break;
                    }
                }
                catch { }
            }
            if (saveBtn != null)
            {
                ClickElement(saveBtn, "SAVE_BUTTON");
                created = PollNewFile(dir, before, sw, 20000);
            }
        }
        sw.Stop();
        if (created == null) { Out("SAVE_TIMEOUT"); ExitCode = 1; return; }
        Out("SAVE_OK ms=" + sw.ElapsedMilliseconds + " file=" + created + " size=" + new FileInfo(created).Length);
    }

    private static void TextsCmd(string[] args)
    {
        // texts <windowTitle> <out>
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        AutomationElement root = AutomationElement.FromHandle(h);
        AutomationElementCollection all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        using (StreamWriter w = new StreamWriter(args[2], false, new UTF8Encoding(false)))
        {
            for (int i = 0; i < all.Count; i++)
            {
                try
                {
                    AutomationElement e = all[i];
                    string ct = e.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
                    string n = e.Current.Name;
                    if (string.IsNullOrEmpty(n)) continue;
                    System.Windows.Rect r = e.Current.BoundingRectangle;
                    w.WriteLine(string.Format("{0}\t{1}\trect={2:0},{3:0},{4:0},{5:0}\toff={6}", ct, n, r.Left, r.Top, r.Right, r.Bottom, e.Current.IsOffscreen));
                }
                catch { }
            }
        }
        Out("texts -> " + args[2]);
    }

    private static automation_rect GetRect(AutomationElement e)
    {
        System.Windows.Rect r = e.Current.BoundingRectangle;
        return new automation_rect { X = r.Left, Y = r.Top, W = r.Width, H = r.Height };
    }

    private struct automation_rect { public double X, Y, W, H; }

    private static void ClickAt(double x, double y)
    {
        MoveIt((int)Math.Round(x), (int)Math.Round(y));
        Thread.Sleep(150);
        ClickDownUp(false);
        Thread.Sleep(120);
    }

    private static void KeyWinCmd(string[] args)
    {
        // keywin <windowTitle> <sendkeys>
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        BringWindow(h);
        // 点击预览区空白点，确保窗口已激活（单击无副作用；预览仅双击添加）
        RECT wr, cr;
        GetWindowRect(h, out wr);
        GetClientRect(h, out cr);
        int borderX = (wr.Right - wr.Left - cr.Right) / 2;
        int borderY = (wr.Bottom - wr.Top - cr.Bottom) - (wr.Bottom - wr.Top - cr.Bottom) / 4;
        // 预览区中心（client 左上角 + 预览面板中心附近）
        int px = wr.Left + borderX + cr.Right / 4;
        int py = wr.Top + (wr.Bottom - wr.Top - cr.Bottom) / 2 + cr.Bottom / 4;
        MoveIt(px, py);
        Thread.Sleep(120);
        ClickDownUp(false);
        Thread.Sleep(200);
        SendKeys.SendWait(string.Join(" ", args, 2, args.Length - 2));
        Thread.Sleep(150);
        Out("keywin sent to \"" + WinTitle(GetForegroundWindow()) + "\"");
    }

    private static void StepCmd(string[] args)
    {
        // step <windowTitle> <uiaName> <+|-> [times]
        AutomationElement e = FindInWindow(args[1], args[2], false);
        if (e == null) { ExitCode = 1; return; }
        automation_rect r = GetRect(e);
        bool plus = args[3].StartsWith("+");
        int times = args.Length > 4 ? int.Parse(args[4]) : 1;
        // Stepper: minus rect = (4,4,28,H-8)，plus rect = (W-32,4,28,H-8)，均为逻辑像素 → 200% DPI 下 x2
        double dpiScale = GetDpiForWindow(WaitWindow(args[1], 1000)) / 96.0;
        double px = plus ? r.X + (r.W - 32 * dpiScale / 2 - 14 * dpiScale) : r.X + (4 * dpiScale / 2 + 14 * dpiScale);
        // 简化：plus 取右端 16 逻辑像素处；minus 取左端 18 逻辑像素处
        px = plus ? r.X + r.W - 16 * dpiScale : r.X + 18 * dpiScale;
        double py = r.Y + r.H / 2;
        Out(string.Format("STEP {0} name={1} rect={2:0},{3:0},{4:0},{5:0} -> click {6:0},{7:0} x{8}",
            args[3], e.Current.Name, r.X, r.Y, r.W, r.H, px, py, times));
        for (int i = 0; i < times; i++) ClickAt(px, py);
    }

    private static void SegCmd(string[] args)
    {
        // seg <windowTitle> <uiaName> <index>
        AutomationElement e = FindInWindow(args[1], args[2], false);
        if (e == null) { ExitCode = 1; return; }
        automation_rect r = GetRect(e);
        int index = int.Parse(args[3]);
        int count = 2;
        double segW = r.W / count;
        double px = r.X + segW * (index + 0.5);
        double py = r.Y + r.H / 2;
        Out(string.Format("SEG index={0} rect={1:0},{2:0},{3:0},{4:0} -> click {5:0},{6:0}", index, r.X, r.Y, r.W, r.H, px, py));
        ClickAt(px, py);
    }

    private static void SliderCmd(string[] args)
    {
        // slider <windowTitle> <uiaName> <percent>
        AutomationElement e = FindInWindow(args[1], args[2], false);
        if (e == null) { ExitCode = 1; return; }
        automation_rect r = GetRect(e);
        double pct = double.Parse(args[3]);
        double dpiScale = GetDpiForWindow(WaitWindow(args[1], 1000)) / 96.0;
        double pad = 8 * dpiScale;
        double px = r.X + pad + (r.W - pad * 2) * pct / 100.0;
        double py = r.Y + r.H / 2;
        Out(string.Format("SLIDER pct={0} rect={1:0},{2:0},{3:0},{4:0} -> click {5:0},{6:0}", pct, r.X, r.Y, r.W, r.H, px, py));
        ClickAt(px, py);
    }

    private static void DragThumbCmd(string[] args)
    {
        // dragthumb <windowTitle> <fromIndex> <toIndex>
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        AutomationElement root = AutomationElement.FromHandle(h);
        // 缩略图条：Pane 名称空、位于窗口左下；通过 UIA 查找 class 中含 WindowsForms 且高度约 208px 的 Pane 不可靠。
        // 使用固定布局推算：主题 ScaleFactor=2；strip 位置由 MainForm.LayoutUi 决定：
        // pad=16*2=32; panelW=292*2=584; panelX=clientW-32-584; stripX=32; stripW=panelX-32-12*2=...
        // 直接按卡片尺寸推算：card=86*2=172, gap=10*2=20, strip 中心 y = 窗口 client 高度 - 26*2 - 104*2 - ... 复杂。
        // 改用窗口 UIA 中 Pane 的空名元素找 strip：高度=104*2=208 且 x=32。
        double dpiScale = GetDpiForWindow(h) / 96.0;
        RECT wr, cr;
        GetWindowRect(h, out wr);
        GetClientRect(h, out cr);
        // client 原点相对窗口：取窗口 rect + 边框
        int borderX = (wr.Right - wr.Left - cr.Right) / 2;
        int borderY = (wr.Bottom - wr.Top - cr.Bottom);
        // 重新依据 MainForm 布局计算
        int pad = (int)(16 * dpiScale);
        int panelW = (int)(292 * dpiScale);
        int clientW = cr.Right;
        int panelX = clientW - pad - panelW;
        int leftW = Math.Max((int)(420 * dpiScale), panelX - pad - (int)(12 * dpiScale));
        int clientH = cr.Bottom;
        int toolbarH = (int)(28 * dpiScale);
        int stripH = (int)(104 * dpiScale);
        int statusH = (int)(26 * dpiScale);
        int previewH = clientH - pad * 2 - toolbarH - stripH - statusH - (int)(20 * dpiScale);
        if (previewH < (int)(260 * dpiScale)) previewH = (int)(260 * dpiScale);
        int toolbarY = pad + previewH + (int)(8 * dpiScale);
        int stripY = toolbarY + toolbarH + (int)(2 * dpiScale);
        int stripX = pad;
        int card = (int)(86 * dpiScale);
        int gap = (int)(10 * dpiScale);
        int cy = wr.Top + borderY + stripY + stripH / 2;
        int fromX = wr.Left + borderX + stripX + int.Parse(args[2]) * (card + gap) + card / 2;
        int toX = wr.Left + borderX + stripX + int.Parse(args[3]) * (card + gap) + card / 2;
        Out("DRAGTHUMB from=(" + fromX + "," + cy + ") to=(" + toX + "," + cy + ")");
        MoveIt(fromX, cy);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(120);
        int steps = 20;
        for (int i = 1; i <= steps; i++)
        {
            int x = fromX + (toX - fromX) * i / steps;
            SetCursorPos(x, cy);
            Thread.Sleep(25);
        }
        Thread.Sleep(150);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(300);
        Out("dragthumb done");
    }

    private static void FgInfo()
    {
        IntPtr h = GetForegroundWindow();
        RECT r;
        GetWindowRect(h, out r);
        Out("fg hwnd=0x" + h.ToInt64().ToString("X") + " class=" + WinClass(h) + " title=" + WinTitle(h) + " rect=" + RectText(r));
    }

    private static void MoveIt(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(30);
    }

    private static void Move(string[] args)
    {
        int x = int.Parse(args[1]);
        int y = int.Parse(args[2]);
        MoveIt(x, y);
        Out("moved to " + x + "," + y);
    }

    private static void ClickDownUp(bool right)
    {
        uint down = right ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
        uint up = right ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;
        mouse_event(down, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        mouse_event(up, 0, 0, 0, UIntPtr.Zero);
    }

    private static void ClickSeqCmd(string[] args)
    {
        // clickseq <windowTitle> <x> <y> <n> [intervalMs]
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        int x = int.Parse(args[2]);
        int y = int.Parse(args[3]);
        int n = int.Parse(args[4]);
        int interval = args.Length > 5 ? int.Parse(args[5]) : 800;
        BringWindow(h);
        Out("clickseq fg_after_bring=" + WinTitle(GetForegroundWindow()) + " target=" + WinTitle(h));
        for (int i = 0; i < n; i++)
        {
            MoveIt(x, y);
            Thread.Sleep(150);
            Out("iter " + i + " fg_before_click=" + WinTitle(GetForegroundWindow()) + " cursor=" + Cursor.Position.X + "," + Cursor.Position.Y);
            ClickDownUp(false);
            Thread.Sleep(interval);
            Out("iter " + i + " clicked; fg_after=" + WinTitle(GetForegroundWindow()));
        }
    }

    private static void MouseClick(string[] args, bool right)
    {
        string body = "";
        for (int i = 0; i < args.Length; i++) body += args[i] + " ";
        if (body.StartsWith("click ") || body.StartsWith("rclick ")) body = body.Substring(body.IndexOf(' ') + 1);
        string[] parts = body.Trim().Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        int x = int.Parse(parts[0]);
        int y = int.Parse(parts[1]);
        WaitWindow("", 0);
        MoveIt(x, y);
        Thread.Sleep(150);
        ClickDownUp(right);
        Thread.Sleep(150);
        Out("clicked " + (right ? "right" : "left") + " at " + x + "," + y);
    }

    private static void DoubleClick(string[] args)
    {
        int x = int.Parse(args[1]);
        int y = int.Parse(args[2]);
        MoveIt(x, y);
        ClickDownUp(false);
        Thread.Sleep(60);
        ClickDownUp(false);
        Thread.Sleep(150);
        Out("double-clicked at " + x + "," + y);
    }

    private static void Drag(string[] args)
    {
        int x1 = int.Parse(args[1]);
        int y1 = int.Parse(args[2]);
        int x2 = int.Parse(args[3]);
        int y2 = int.Parse(args[4]);
        int ms = args.Length > 5 ? int.Parse(args[5]) : 600;
        MoveIt(x1, y1);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(120);
        int steps = Math.Max(10, ms / 30);
        for (int i = 1; i <= steps; i++)
        {
            int x = x1 + (x2 - x1) * i / steps;
            int y = y1 + (y2 - y1) * i / steps;
            SetCursorPos(x, y);
            Thread.Sleep(Math.Max(10, ms / steps));
        }
        Thread.Sleep(150);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(200);
        Out("dragged " + x1 + "," + y1 + " -> " + x2 + "," + y2);
    }

    private static void Wheel(string[] args)
    {
        int x = int.Parse(args[1]);
        int y = int.Parse(args[2]);
        int delta = int.Parse(args[3]);
        MoveIt(x, y);
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
        Thread.Sleep(150);
        Out("wheel " + delta + " at " + x + "," + y);
    }

    private static void KeysCmd(string[] args)
    {
        string keys = string.Join(" ", args, 1, args.Length - 1);
        SendKeys.SendWait(keys);
        Thread.Sleep(120);
        Out("keys " + keys);
    }

    private static void Paste(string[] args)
    {
        string text = string.Join(" ", args, 1, args.Length - 1);
        // args 里 \\n 转义为换行
        text = text.Replace("\\n", "\n");
        Clipboard.SetText(text);
        Thread.Sleep(80);
        SendKeys.SendWait("^v");
        Thread.Sleep(150);
        Out("pasted " + text.Length + " chars");
    }

    private static void CloseCmd(string[] args)
    {
        IntPtr h = FindWindow(null, args[1]);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);
        Out("WM_CLOSE sent");
    }

    private static void SetPos(string[] args)
    {
        IntPtr h = WaitWindow(args[1], 3000);
        if (h == IntPtr.Zero) { Out("NOWINDOW"); ExitCode = 1; return; }
        int x = int.Parse(args[2]), y = int.Parse(args[3]), w = int.Parse(args[4]), hh = int.Parse(args[5]);
        SetWindowPos(h, IntPtr.Zero, x, y, w, hh, 0x0004 | 0x0010);
        Thread.Sleep(300);
        Out("setpos done");
    }

    private static void ColdStart(string[] args)
    {
        string exe = args[1];
        StringBuilder argLine = new StringBuilder();
        for (int i = 2; i < args.Length; i++)
        {
            if (argLine.Length > 0) argLine.Append(' ');
            string a = args[i];
            if (a.IndexOf(' ') >= 0) argLine.Append('"').Append(a).Append('"');
            else argLine.Append(a);
        }
        ProcessStartInfo psi = new ProcessStartInfo(exe, argLine.ToString());
        psi.UseShellExecute = true;
        psi.WorkingDirectory = Path.GetDirectoryName(exe);
        Stopwatch sw = Stopwatch.StartNew();
        Process p = Process.Start(psi);
        IntPtr h = IntPtr.Zero;
        int waited = 0;
        while (waited < 30000)
        {
            h = FindWindow(null, "轻拼 - 图片拼接");
            if (h != IntPtr.Zero) break;
            Thread.Sleep(5);
            waited += 5;
        }
        sw.Stop();
        if (h == IntPtr.Zero) { Out("TIMEOUT no window in 30000ms"); ExitCode = 1; return; }
        RECT wr, cr;
        GetWindowRect(h, out wr);
        GetClientRect(h, out cr);
        Out(string.Format("window_visible_ms={0} pid={1} hwnd=0x{2} window={3}x{4} client={5}x{6} dpi={7}",
            sw.ElapsedMilliseconds, p.Id, h.ToInt64().ToString("X"),
            wr.Right - wr.Left, wr.Bottom - wr.Top, cr.Right - cr.Left, cr.Bottom - cr.Top, GetDpiForWindow(h)));
    }

    private static void WaitWin(string[] args)
    {
        int ms = int.Parse(args[2]);
        Stopwatch sw = Stopwatch.StartNew();
        IntPtr h = WaitWindow(args[1], ms);
        sw.Stop();
        if (h == IntPtr.Zero) { Out("TIMEOUT " + sw.ElapsedMilliseconds + "ms"); ExitCode = 1; return; }
        Out("FOUND after " + sw.ElapsedMilliseconds + "ms hwnd=0x" + h.ToInt64().ToString("X"));
    }

    private static void WaitGone(string[] args)
    {
        int ms = int.Parse(args[2]);
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds <= ms)
        {
            if (FindWindow(null, args[1]) == IntPtr.Zero) { Out("GONE after " + sw.ElapsedMilliseconds + "ms"); return; }
            Thread.Sleep(20);
        }
        Out("STILL THERE after " + ms + "ms"); ExitCode = 1;
    }

    private static void WaitFile(string[] args)
    {
        int ms = int.Parse(args[2]);
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds <= ms)
        {
            if (File.Exists(args[1]))
            {
                // 等待文件大小稳定（保存可能尚未写完）
                long last = -1;
                for (int i = 0; i < 100; i++)
                {
                    long len = new FileInfo(args[1]).Length;
                    if (len > 0 && len == last) { Out("FILE_READY after " + sw.ElapsedMilliseconds + "ms size=" + len); return; }
                    last = len;
                    Thread.Sleep(20);
                }
                Out("FILE_EXISTS after " + sw.ElapsedMilliseconds + "ms (size not stable)");
                return;
            }
            Thread.Sleep(10);
        }
        Out("TIMEOUT " + ms + "ms"); ExitCode = 1;
    }

    private static AutomationElement FindWindowElement(string title)
    {
        IntPtr h = WaitWindow(title, 3000);
        if (h == IntPtr.Zero) return null;
        try { return AutomationElement.FromHandle(h); }
        catch { return null; }
    }

    private static string ElemLine(AutomationElement e, int depth)
    {
        string name = "";
        string aid = "";
        string cls = "";
        string ctype = "";
        try { name = e.Current.Name; } catch { }
        try { aid = e.Current.AutomationId; } catch { }
        try { cls = e.Current.ClassName; } catch { }
        try { ctype = e.Current.ControlType.ProgrammaticName; } catch { }
        bool off = false, en = false;
        try { off = e.Current.IsOffscreen; } catch { }
        try { en = e.Current.IsEnabled; } catch { }
        string rect = "";
        try
        {
            System.Windows.Rect r = e.Current.BoundingRectangle;
            rect = string.Format("{0:0},{1:0},{2:0},{3:0}", r.Left, r.Top, r.Right, r.Bottom);
        }
        catch { }
        return new string(' ', depth * 2) + ctype.Replace("ControlType.", "") + " name=\"" + name + "\" id=\"" + aid + "\" class=\"" + cls + "\" rect=" + rect + (off ? " OFFSCREEN" : "") + (en ? "" : " DISABLED");
    }

    private static void DumpTree(AutomationElement root, TextWriter w, int maxDepth, int maxCount)
    {
        int count = 0;
        Stack<KeyValuePair<AutomationElement, int>> stack = new Stack<KeyValuePair<AutomationElement, int>>();
        stack.Push(new KeyValuePair<AutomationElement, int>(root, 0));
        while (stack.Count > 0 && count < maxCount)
        {
            KeyValuePair<AutomationElement, int> cur = stack.Pop();
            w.WriteLine(ElemLine(cur.Key, cur.Value));
            count++;
            if (cur.Value >= maxDepth) continue;
            AutomationElementCollection kids = null;
            try { kids = cur.Key.FindAll(TreeScope.Children, Condition.TrueCondition); } catch { }
            if (kids == null) continue;
            for (int i = kids.Count - 1; i >= 0; i--)
            {
                try { stack.Push(new KeyValuePair<AutomationElement, int>(kids[i], cur.Value + 1)); } catch { }
            }
        }
        Out("uia_dump count=" + count);
    }

    private static void UiaDump(string[] args)
    {
        AutomationElement root = FindWindowElement(args[1]);
        if (root == null) { Out("NOWINDOW"); ExitCode = 1; return; }
        string target = args.Length > 2 ? args[2] : "uia.txt";
        using (StreamWriter w = new StreamWriter(target, false, new UTF8Encoding(false)))
        {
            w.WriteLine("WINDOW: " + ElemLine(root, 0));
            DumpTree(root, w, 14, 5000);
        }
        Out("saved UIA -> " + target);
    }

    private static bool ElementVisible(AutomationElement e)
    {
        try { return !e.Current.IsOffscreen; } catch { return false; }
    }

    private static List<AutomationElement> FindByName(AutomationElement root, string name, bool exact)
    {
        List<AutomationElement> found = new List<AutomationElement>();
        AutomationElementCollection all = null;
        try { all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition); } catch { }
        if (all == null) return found;
        for (int i = 0; i < all.Count; i++)
        {
            AutomationElement e = all[i];
            try
            {
                string n = e.Current.Name;
                if (string.IsNullOrEmpty(n)) continue;
                bool match = exact ? string.Equals(n, name, StringComparison.Ordinal)
                                   : n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
                if (match) found.Add(e);
            }
            catch { }
        }
        return found;
    }

    private static void PrintElements(List<AutomationElement> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            AutomationElement e = list[i];
            try
            {
                System.Windows.Rect r = e.Current.BoundingRectangle;
                Out(string.Format("MATCH[{0}] type={1} name=\"{2}\" class=\"{3}\" rect={4:0},{5:0},{6:0},{7:0} off={8}",
                    i, e.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""), e.Current.Name,
                    e.Current.ClassName, r.Left, r.Top, r.Right, r.Bottom, e.Current.IsOffscreen));
            }
            catch (Exception ex) { Out("MATCH[" + i + "] <error " + ex.Message + ">"); }
        }
    }

    private static void UiaFind(string[] args)
    {
        bool exact = args.Length > 2 && string.Equals(args[2], "exact", StringComparison.OrdinalIgnoreCase);
        AutomationElement root = AutomationElement.RootElement;
        List<AutomationElement> found = FindByName(root, args[1], exact);
        Out("uiafind \"" + args[1] + "\" matches=" + found.Count);
        PrintElements(found);
    }

    private static void ClickUia(string[] args)
    {
        bool exact = args.Length > 2 && string.Equals(args[2], "exact", StringComparison.OrdinalIgnoreCase);
        AutomationElement root = AutomationElement.RootElement;
        List<AutomationElement> found = FindByName(root, args[1], exact);
        Out("clickuia matches=" + found.Count);
        PrintElements(found);
        for (int i = 0; i < found.Count; i++)
        {
            AutomationElement e = found[i];
            try
            {
                if (e.Current.IsOffscreen) continue;
                // 优先 InvokePattern
                object pattern;
                if (e.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                {
                    ((InvokePattern)pattern).Invoke();
                    Out("invoked match[" + i + "]");
                    Thread.Sleep(300);
                    return;
                }
                System.Windows.Rect r = e.Current.BoundingRectangle;
                if (r.Width > 0 && r.Height > 0)
                {
                    int cx = (int)Math.Round(r.Left + r.Width / 2.0);
                    int cy = (int)Math.Round(r.Top + r.Height / 2.0);
                    MoveIt(cx, cy);
                    ClickDownUp(false);
                    Out("clicked match[" + i + "] at " + cx + "," + cy);
                    Thread.Sleep(300);
                    return;
                }
            }
            catch (Exception ex) { Out("match[" + i + "] failed: " + ex.Message); }
        }
        ExitCode = 1;
    }

    private static void MenuDump(string[] args)
    {
        string target = args.Length > 1 ? args[1] : "menu.txt";
        using (StreamWriter w = new StreamWriter(target, false, new UTF8Encoding(false)))
        {
            AutomationElement root = AutomationElement.RootElement;
            AutomationElementCollection all = null;
            try { all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition); }
            catch (Exception ex) { w.WriteLine("ERROR " + ex.Message); Out("menudump error: " + ex.Message); ExitCode = 1; return; }
            int count = 0, printed = 0;
            for (int i = 0; i < all.Count; i++)
            {
                count++;
                AutomationElement e = all[i];
                try
                {
                    string ctype = e.Current.ControlType.ProgrammaticName;
                    if (ctype != "ControlType.MenuItem" && ctype != "ControlType.Menu" && ctype != "ControlType.MenuBar") continue;
                    bool off = e.Current.IsOffscreen;
                    string name = e.Current.Name;
                    if (off && string.IsNullOrEmpty(name)) continue;
                    System.Windows.Rect r = e.Current.BoundingRectangle;
                    w.WriteLine(string.Format("type={0} name=\"{1}\" class=\"{2}\" rect={3:0},{4:0},{5:0},{6:0} off={7}",
                        ctype.Replace("ControlType.", ""), name, e.Current.ClassName, r.Left, r.Top, r.Right, r.Bottom, off));
                    printed++;
                }
                catch { }
            }
            w.WriteLine("TOTAL_SCANNED=" + count + " MENU_ELEMS=" + printed);
            Out("menudump scanned=" + count + " menuElems=" + printed + " -> " + target);
        }
    }
}
