using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using LightCollage.Common;
using LightCollage.Core;
using LightCollage.Shell;
using CoreLayoutEngine = LightCollage.Core.LayoutEngine;

namespace LightCollage.App
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args != null && args.Length > 0)
                {
                    string first = args[0].ToLowerInvariant();
                    if (first == "--register")
                    {
                        return RegisterMenu(args);
                    }
                    if (first == "--unregister")
                    {
                        return UnregisterMenu(args);
                    }
                    if (first == "--compose")
                    {
                        return ComposeCommand(args);
                    }
                    if (first == "--version" || first == "-v")
                    {
                        Console.WriteLine(AppInfo.ProductName + " " + AppInfo.Version);
                        return 0;
                    }
                    if (first == "--help" || first == "-h" || first == "/?")
                    {
                        PrintHelp();
                        return 0;
                    }
                }

                List<string> files = new List<string>();
                if (args != null)
                {
                    foreach (string arg in args)
                    {
                        if (string.IsNullOrEmpty(arg))
                        {
                            continue;
                        }
                        if (arg.StartsWith("--", StringComparison.Ordinal))
                        {
                            MessageBox.Show("未知参数：" + arg, "轻拼", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return 2;
                        }
                        files.Add(arg);
                    }
                }

                SingleInstance instance = new SingleInstance();
                if (!instance.IsPrimary)
                {
                    bool forwarded = false;
                    try
                    {
                        forwarded = instance.TryForward(files, 3500);
                    }
                    catch
                    {
                    }
                    instance.Dispose();
                    if (!forwarded)
                    {
                        MessageBox.Show("轻拼似乎正在启动或关闭，请稍后重试。", "轻拼",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return forwarded ? 0 : 1;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    WriteErrorLog(e.Exception);
                    MessageBox.Show("发生错误：\r\n" + e.Exception.Message, "轻拼", MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    WriteErrorLog(e.ExceptionObject as Exception);
                };

                IncomingFiles inbox = new IncomingFiles();
                instance.FilesReceived += delegate(object sender, FilesReceivedEventArgs e)
                {
                    if (e != null && e.Files != null)
                    {
                        inbox.Enqueue(e.Files);
                    }
                };
                instance.StartServer();

                AppSettings settings = SettingsStore.Load();
                try
                {
                    using (MainForm form = new MainForm(inbox, files.ToArray(), settings))
                    {
                        Application.Run(form);
                    }
                }
                finally
                {
                    instance.Dispose();
                }
                return 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog(ex);
                MessageBox.Show("轻拼启动失败：\r\n" + ex.Message, "轻拼", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RegisterMenu(string[] args)
        {
            bool quiet = Contains(args, "--quiet");
            try
            {
                MenuInstaller.Install();
                if (!quiet)
                {
                    MessageBox.Show("右键菜单已安装。\r\n\r\nWindows 11 请在右键菜单中点击「显示更多选项」，再选择「拼图」。",
                        "轻拼", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    MessageBox.Show("安装右键菜单失败：\r\n" + ex.Message, "轻拼",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return 1;
            }
        }

        private static int UnregisterMenu(string[] args)
        {
            bool quiet = Contains(args, "--quiet");
            try
            {
                MenuInstaller.Uninstall();
                if (!quiet)
                {
                    MessageBox.Show("右键菜单已卸载。", "轻拼", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    MessageBox.Show("卸载右键菜单失败：\r\n" + ex.Message, "轻拼",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return 1;
            }
        }

        private static bool Contains(string[] args, string value)
        {
            if (args == null)
            {
                return false;
            }
            foreach (string arg in args)
            {
                if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static int ComposeCommand(string[] args)
        {
            try
            {
                CollageSettings settings = new CollageSettings();
                string output = null;
                List<string> files = new List<string>();
                bool rowGapSet = false;
                bool columnGapSet = false;
                string mergesText = null;
                for (int i = 1; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg == "--layout" && i + 1 < args.Length)
                    {
                        string value = args[++i].ToLowerInvariant();
                        if (value == "v" || value == "vertical")
                        {
                            settings.Orientation = CollageOrientation.Vertical;
                        }
                        else if (value == "g" || value == "grid")
                        {
                            settings.Orientation = CollageOrientation.Grid;
                        }
                        else
                        {
                            settings.Orientation = CollageOrientation.Horizontal;
                        }
                    }
                    else if (arg == "--gap" && i + 1 < args.Length)
                    {
                        settings.Gap = ParseInt(args[++i], 0, 200, settings.Gap);
                    }
                    else if (arg == "--rows" && i + 1 < args.Length)
                    {
                        settings.GridRows = ParseInt(args[++i], 1, 20, settings.GridRows);
                    }
                    else if (arg == "--cols" && i + 1 < args.Length)
                    {
                        settings.GridColumns = ParseInt(args[++i], 1, 20, settings.GridColumns);
                    }
                    else if (arg == "--row-gap" && i + 1 < args.Length)
                    {
                        settings.RowGap = ParseInt(args[++i], 0, 200, settings.RowGap);
                        rowGapSet = true;
                    }
                    else if (arg == "--col-gap" && i + 1 < args.Length)
                    {
                        settings.ColumnGap = ParseInt(args[++i], 0, 200, settings.ColumnGap);
                        columnGapSet = true;
                    }
                    else if (arg == "--merge" && i + 1 < args.Length)
                    {
                        mergesText = args[++i];
                    }
                    else if (arg == "--margin" && i + 1 < args.Length)
                    {
                        settings.Margin = ParseInt(args[++i], 0, 200, settings.Margin);
                    }
                    else if (arg == "--bg" && i + 1 < args.Length)
                    {
                        settings.BackgroundArgb = ParseColor(args[++i]);
                    }
                    else if (arg == "--base" && i + 1 < args.Length)
                    {
                        string value = args[++i];
                        if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.AutoBaseSize = true;
                            settings.BaseSize = 0;
                        }
                        else
                        {
                            settings.AutoBaseSize = false;
                            settings.BaseSize = ParseInt(value, 1, 20000, 1080);
                        }
                    }
                    else if (arg == "--out-width" && i + 1 < args.Length)
                    {
                        settings.OutputSizeMode = OutputSizeMode.Width;
                        settings.OutputTarget = ParseInt(args[++i], 1, 32000, 2000);
                    }
                    else if (arg == "--out-height" && i + 1 < args.Length)
                    {
                        settings.OutputSizeMode = OutputSizeMode.Height;
                        settings.OutputTarget = ParseInt(args[++i], 1, 32000, 2000);
                    }
                    else if (arg == "--format" && i + 1 < args.Length)
                    {
                        string value = args[++i].ToLowerInvariant();
                        settings.Format = (value == "jpg" || value == "jpeg") ? OutputFormat.Jpeg : OutputFormat.Png;
                    }
                    else if (arg == "--quality" && i + 1 < args.Length)
                    {
                        settings.JpegQuality = ParseInt(args[++i], 1, 100, settings.JpegQuality);
                    }
                    else if (arg == "--out" && i + 1 < args.Length)
                    {
                        output = args[++i];
                    }
                    else if (!arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        files.Add(arg);
                    }
                }
                if (settings.Orientation == CollageOrientation.Grid)
                {
                    // 网格：行/列间距未显式指定时回落到 --gap（再缺省为 12）。
                    if (!rowGapSet)
                    {
                        settings.RowGap = settings.Gap;
                    }
                    if (!columnGapSet)
                    {
                        settings.ColumnGap = settings.Gap;
                    }
                    if (!string.IsNullOrEmpty(mergesText))
                    {
                        settings.Merges = GridLayout.ParseMerges(mergesText, settings.GridRows, settings.GridColumns);
                    }
                }
                if (string.IsNullOrEmpty(output) || files.Count == 0)
                {
                    Console.Error.WriteLine("用法：LightCollage.exe --compose --layout h|v|g [--gap N] [--margin N] [--bg #AARRGGBB] [--base auto|N]");
                    Console.Error.WriteLine("       [--out-width N | --out-height N] [--format png|jpg] [--quality 1-100]");
                    Console.Error.WriteLine("       网格：--rows N --cols N --row-gap N --col-gap N --merge \"1,1,2,2;3,3,3,4\"");
                    Console.Error.WriteLine("       --out FILE image1 [image2 ...]");
                    return 2;
                }

                List<ImageSize> sizes = new List<ImageSize>();
                foreach (string file in files)
                {
                    using (Bitmap bitmap = ImageLoader.LoadFull(file))
                    {
                        sizes.Add(new ImageSize(bitmap.Width, bitmap.Height));
                    }
                }
                CollageLayout layout = CoreLayoutEngine.Compute(sizes, settings);
                string directory = Path.GetDirectoryName(Path.GetFullPath(output));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                Compositor.Save(files, sizes, settings, layout, output, null);
                Console.WriteLine("OK " + layout.CanvasWidth + "x" + layout.CanvasHeight + " -> " + output);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        private static int ParseInt(string value, int min, int max, int fallback)
        {
            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return fallback;
            }
            return Math.Max(min, Math.Min(max, parsed));
        }

        private static int ParseColor(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }
            string hex = value.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal))
            {
                hex = hex.Substring(1);
            }
            if (hex.Length == 6)
            {
                hex = "FF" + hex;
            }
            if (hex.Length != 8)
            {
                throw new ArgumentException("颜色格式应为 #AARRGGBB 或 #RRGGBB");
            }
            uint number;
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number))
            {
                throw new ArgumentException("无法识别颜色：" + value);
            }
            return unchecked((int)number);
        }

        private static void PrintHelp()
        {
            Console.WriteLine(AppInfo.ProductName + " " + AppInfo.ProductNameEn + " " + AppInfo.Version);
            Console.WriteLine("用法：");
            Console.WriteLine("  LightCollage.exe [图片路径 ...]       打开编辑器");
            Console.WriteLine("  LightCollage.exe --register [--quiet]  安装右键菜单");
            Console.WriteLine("  LightCollage.exe --unregister [--quiet] 卸载右键菜单");
            Console.WriteLine("  LightCollage.exe --compose [选项] 图片... 无界面拼接（测试/自动化）");
            Console.WriteLine("      选项：--layout h|v|g --gap N --margin N --bg #AARRGGBB --base auto|N --out-width N | --out-height N");
            Console.WriteLine("            --format png|jpg --quality 1-100 --out FILE");
            Console.WriteLine("      网格：--rows N --cols N --row-gap N --col-gap N --merge \"1,1,2,2;3,3,3,4\"");
            Console.WriteLine("  LightCollage.exe --version / --help");
        }

        private static void WriteErrorLog(Exception ex)
        {
            if (ex == null)
            {
                return;
            }
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "LightCollage-error.log");
                string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex.ToString() + "\r\n\r\n";
                File.AppendAllText(path, text, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}

