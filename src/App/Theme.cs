using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LightCollage.App
{
    internal static class Theme
    {
        public static readonly Color WindowBg = Color.FromArgb(0x0A, 0x0A, 0x0A);
        public static readonly Color PanelBg = Color.FromArgb(0x16, 0x16, 0x16);
        public static readonly Color PanelBg2 = Color.FromArgb(0x1E, 0x1E, 0x1E);
        public static readonly Color CardBg = Color.FromArgb(0x10, 0x10, 0x10);
        public static readonly Color CardBorder = Color.FromArgb(0x2E, 0x2E, 0x2E);
        public static readonly Color ControlBg = Color.FromArgb(0x24, 0x24, 0x24);
        public static readonly Color ControlBgDeep = Color.FromArgb(0x14, 0x14, 0x14);
        public static readonly Color ControlBorder = Color.FromArgb(0x3A, 0x3A, 0x3A);
        public static readonly Color ControlBorderSoft = Color.FromArgb(0x2A, 0x2A, 0x2A);
        public static readonly Color TextPrimary = Color.FromArgb(0xF2, 0xF2, 0xF2);
        public static readonly Color TextSecondary = Color.FromArgb(0xBD, 0xBD, 0xBD);
        public static readonly Color TextMuted = Color.FromArgb(0x8A, 0x8A, 0x8A);
        public static readonly Color TextDim = Color.FromArgb(0x61, 0x61, 0x61);
        public static readonly Color Accent = Color.FromArgb(0xE6, 0xE6, 0xE6);
        public static readonly Color AccentHover = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static readonly Color AccentText = Color.FromArgb(0x11, 0x11, 0x11);
        public static readonly Color AccentGlow = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static readonly Color AccentDim = Color.FromArgb(0x3A, 0x3A, 0x3A);
        public static readonly Color Danger = Color.FromArgb(0xD8, 0xD8, 0xD8);
        public static readonly Color CheckerLight = Color.FromArgb(0x26, 0x26, 0x26);
        public static readonly Color CheckerDark = Color.FromArgb(0x1B, 0x1B, 0x1B);
        public static readonly Color WhiteFlat = Color.FromArgb(255, 255, 255, 255);

        public static readonly Font FontBase;
        public static readonly Font FontSmall;
        public static readonly Font FontTiny;
        public static readonly Font FontTitle;
        public static readonly Font FontSection;
        public static readonly Font FontButton;
        public static readonly Font FontNumber;
        public static readonly Font FontBigNumber;

        private static int _systemDpi = 96;

        static Theme()
        {
            InitializeDpi();
            string family = "Microsoft YaHei UI";
            try
            {
                FontFamily test = new FontFamily(family);
                test.Dispose();
            }
            catch
            {
                family = "Microsoft YaHei";
            }

            FontBase = new Font(family, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            FontSmall = new Font(family, 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            FontTiny = new Font(family, 7.8f, FontStyle.Regular, GraphicsUnit.Point);
            FontTitle = new Font(family, 15f, FontStyle.Bold, GraphicsUnit.Point);
            FontSection = new Font(family, 9f, FontStyle.Bold, GraphicsUnit.Point);
            FontButton = new Font(family, 9.8f, FontStyle.Regular, GraphicsUnit.Point);
            FontNumber = new Font(family, 10f, FontStyle.Regular, GraphicsUnit.Point);
            FontBigNumber = new Font(family, 11f, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static int SystemDpi
        {
            get { return _systemDpi; }
        }

        public static float ScaleFactor
        {
            get { return _systemDpi / 96f; }
        }

        /// <summary>系统 DPI 感知 + 程序内手动缩放：在创建任何窗口前初始化一次。</summary>
        public static void InitializeDpi()
        {
            int dpi = 96;
            try
            {
                dpi = (int)GetDpiForSystem();
            }
            catch
            {
                try
                {
                    using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                    {
                        dpi = (int)Math.Round(graphics.DpiX);
                    }
                }
                catch
                {
                    dpi = 96;
                }
            }
            if (dpi < 96)
            {
                dpi = 96;
            }
            _systemDpi = dpi;
        }

        public static int Dpi(Control c)
        {
            return _systemDpi;
        }

        public static int S(Control c, int value)
        {
            return (int)Math.Round(value * _systemDpi / 96.0);
        }

        public static float S(Control c, float value)
        {
            return (float)(value * _systemDpi / 96.0);
        }

        public static void EnableDoubleBuffer(Control c)
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(c, true, null);
            }
            catch
            {
            }
        }

        public static GraphicsPath RoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0.1f)
            {
                path.AddRectangle(rect);
                return path;
            }
            float d = radius * 2f;
            if (d > rect.Width)
            {
                d = rect.Width;
            }
            if (d > rect.Height)
            {
                d = rect.Height;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, RectangleF rect, float radius, Color color)
        {
            using (GraphicsPath path = RoundedPath(rect, radius))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRounded(Graphics g, RectangleF rect, float radius, Color color, float width)
        {
            using (GraphicsPath path = RoundedPath(rect, radius))
            using (Pen pen = new Pen(color, width))
            {
                g.DrawPath(pen, path);
            }
        }

        public static void SetHighQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static void DrawCheckerboard(Graphics g, Rectangle rect, int cell, Color light, Color dark)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }
            using (SolidBrush b1 = new SolidBrush(light))
            using (SolidBrush b2 = new SolidBrush(dark))
            {
                for (int y = rect.Top, row = 0; y < rect.Bottom; y += cell, row++)
                {
                    for (int x = rect.Left, col = 0; x < rect.Right; x += cell, col++)
                    {
                        Brush brush = (((row + col) & 1) == 0) ? b1 : b2;
                        int w = Math.Min(cell, rect.Right - x);
                        int h = Math.Min(cell, rect.Bottom - y);
                        g.FillRectangle(brush, x, y, w, h);
                    }
                }
            }
        }

        public static void DrawText(Graphics g, string text, Font font, Rectangle rect, Color color,
            TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, rect, color, flags | TextFormatFlags.NoPrefix);
        }

        public static void DrawText(Graphics g, string text, Font font, Point point, Color color)
        {
            TextRenderer.DrawText(g, text, font, point, color, TextFormatFlags.NoPrefix);
        }

        public static Color WithAlpha(Color c, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
        }

        public static Color Blend(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static string ToHexArgb(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B);
        }

        public static bool IsLight(Color c)
        {
            return (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 150;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        public static void ApplyDarkTitleBar(Form form)
        {
            try
            {
                int value = 1;
                int result = DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int));
                if (result != 0)
                {
                    DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
                }
            }
            catch
            {
            }
        }
    }
}
