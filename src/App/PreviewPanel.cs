using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LightCollage.App
{
    internal sealed class PreviewPanel : Control
    {
        private Bitmap _image;
        private string _infoText = string.Empty;
        private string _sizeText = string.Empty;
        private bool _loading;
        private string _loadingText = "正在载入图片…";
        private int _loadingPercent;
        private bool _dragOver;

        public event EventHandler AddRequested;

        public PreviewPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.WindowBg;
            Cursor = Cursors.Default;
        }

        public Bitmap Image
        {
            get { return _image; }
        }

        public void SetPreview(Bitmap image, string infoText, string sizeText)
        {
            _image = image;
            _infoText = infoText == null ? string.Empty : infoText;
            _sizeText = sizeText == null ? string.Empty : sizeText;
            Invalidate();
        }

        public void SetLoading(bool loading, string text, int percent)
        {
            _loading = loading;
            _loadingText = text == null ? "正在载入图片…" : text;
            _loadingPercent = Math.Max(0, Math.Min(100, percent));
            Invalidate();
        }

        public void SetDragOver(bool dragOver)
        {
            if (_dragOver != dragOver)
            {
                _dragOver = dragOver;
                Invalidate();
            }
        }

        public Rectangle ContentRect
        {
            get
            {
                int pad = Theme.S(this, 18);
                return new Rectangle(pad, pad, Math.Max(1, Width - pad * 2), Math.Max(1, Height - pad * 2));
            }
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            if (AddRequested != null)
            {
                AddRequested(this, EventArgs.Empty);
            }
            base.OnDoubleClick(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = Cursors.Hand;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Cursor = Cursors.Default;
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetHighQuality(g);
            using (SolidBrush bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, ClientRectangle);
            }

            RectangleF card = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(g, card, 14f, Theme.CardBg);

            Rectangle content = ContentRect;
            using (GraphicsPath clip = Theme.RoundedPath(content, 9f))
            {
                g.SetClip(clip, CombineMode.Intersect);
                Theme.DrawCheckerboard(g, content, Theme.S(this, 16), Theme.CheckerLight, Theme.CheckerDark);

                if (_image != null && _image.Width > 0 && _image.Height > 0)
                {
                    float scale = Math.Min(content.Width / (float)_image.Width, content.Height / (float)_image.Height);
                    if (scale > 1f)
                    {
                        scale = 1f;
                    }
                    int w = Math.Max(1, (int)Math.Round(_image.Width * scale));
                    int h = Math.Max(1, (int)Math.Round(_image.Height * scale));
                    int x = content.Left + (content.Width - w) / 2;
                    int y = content.Top + (content.Height - h) / 2;
                    using (SolidBrush shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                    {
                        g.FillRectangle(shadow, x + Theme.S(this, 4), y + Theme.S(this, 5), w, h);
                    }
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.DrawImage(_image, new Rectangle(x, y, w, h));
                }
                else if (!_loading)
                {
                    DrawEmptyState(g, content);
                }

                if (_loading)
                {
                    using (SolidBrush overlay = new SolidBrush(Color.FromArgb(170, 10, 12, 16)))
                    {
                        g.FillRectangle(overlay, content);
                    }
                    int cx = content.Left + content.Width / 2;
                    int cy = content.Top + content.Height / 2;
                    int ring = Theme.S(this, 26);
                    using (Pen track = new Pen(Color.FromArgb(70, 255, 255, 255), Theme.S(this, 3f)))
                    {
                        g.DrawEllipse(track, cx - ring, cy - ring - Theme.S(this, 8), ring * 2, ring * 2);
                    }
                    using (Pen arc = new Pen(Theme.AccentGlow, Theme.S(this, 3f)))
                    {
                        arc.StartCap = LineCap.Round;
                        arc.EndCap = LineCap.Round;
                        float sweep = _loadingPercent > 0 ? Math.Min(330f, _loadingPercent * 3.3f) : 90f;
                        g.DrawArc(arc, cx - ring, cy - ring - Theme.S(this, 8), ring * 2, ring * 2, -90f, sweep);
                    }
                    Theme.DrawText(g, _loadingText, Theme.FontSmall,
                        new Rectangle(content.Left, cy + ring, content.Width, Theme.S(this, 22)),
                        Theme.TextPrimary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    string pct = _loadingPercent + "%";
                    Theme.DrawText(g, pct, Theme.FontTiny,
                        new Rectangle(content.Left, cy + ring + Theme.S(this, 22), content.Width, Theme.S(this, 18)),
                        Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }

                g.ResetClip();
            }

            Theme.DrawRounded(g, card, 14f, _dragOver ? Theme.AccentGlow : Theme.CardBorder, _dragOver ? 2f : 1f);

            if (_dragOver)
            {
                Rectangle hint = new Rectangle(content.Left, content.Bottom - Theme.S(this, 40), content.Width, Theme.S(this, 30));
                Theme.FillRounded(g, hint, 8f, Color.FromArgb(225, 20, 24, 30));
                Theme.DrawText(g, "松开鼠标即可添加图片", Theme.FontSmall, hint, Theme.AccentGlow,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            if (!string.IsNullOrEmpty(_infoText))
            {
                DrawChip(g, new Point(content.Left + Theme.S(this, 12), content.Top + Theme.S(this, 12)), _infoText, Theme.TextSecondary);
            }
            if (!string.IsNullOrEmpty(_sizeText))
            {
                Size textSize = TextRenderer.MeasureText(_sizeText, Theme.FontTiny);
                int chipW = textSize.Width + Theme.S(this, 22);
                int chipH = Theme.S(this, 28);
                DrawChip(g, new Point(content.Right - chipW - Theme.S(this, 12), content.Top + Theme.S(this, 12)), _sizeText, Theme.AccentGlow);
            }
        }

        private void DrawChip(Graphics g, Point location, string text, Color color)
        {
            Size size = TextRenderer.MeasureText(text, Theme.FontTiny);
            Rectangle rect = new Rectangle(location, new Size(size.Width + Theme.S(this, 22), Theme.S(this, 28)));
            Theme.FillRounded(g, rect, 8f, Color.FromArgb(215, 10, 12, 16));
            Theme.DrawText(g, text, Theme.FontTiny, rect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawEmptyState(Graphics g, Rectangle content)
        {
            int cx = content.Left + content.Width / 2;
            int cy = content.Top + content.Height / 2;
            int icon = Theme.S(this, 64);
            Rectangle photo = new Rectangle(cx - icon / 2, cy - icon - Theme.S(this, 20), icon, icon / 2 + Theme.S(this, 18));
            Theme.FillRounded(g, photo, 8f, Color.FromArgb(40, 255, 255, 255));
            Theme.DrawRounded(g, photo, 8f, Theme.AccentGlow, 1.5f);
            using (Pen pen = new Pen(Theme.AccentGlow, 1.5f))
            {
                g.DrawLine(pen, photo.Left + Theme.S(this, 10), photo.Bottom - Theme.S(this, 14),
                    photo.Left + Theme.S(this, 26), photo.Top + Theme.S(this, 18));
                g.DrawLine(pen, photo.Left + Theme.S(this, 26), photo.Top + Theme.S(this, 18),
                    photo.Left + Theme.S(this, 38), photo.Bottom - Theme.S(this, 22));
                g.DrawLine(pen, photo.Left + Theme.S(this, 38), photo.Bottom - Theme.S(this, 22),
                    photo.Right - Theme.S(this, 10), photo.Top + Theme.S(this, 12));
            }
            using (SolidBrush sun = new SolidBrush(Theme.AccentGlow))
            {
                g.FillEllipse(sun, photo.Right - Theme.S(this, 30), photo.Top + Theme.S(this, 8),
                    Theme.S(this, 8), Theme.S(this, 8));
            }

            Rectangle titleRect = new Rectangle(content.Left, cy, content.Width, Theme.S(this, 28));
            Theme.DrawText(g, "把图片拖进来，或点击这里添加", Theme.FontBase, titleRect, Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            Rectangle subRect = new Rectangle(content.Left, cy + Theme.S(this, 30), content.Width, Theme.S(this, 22));
            Theme.DrawText(g, "支持 JPG / PNG / BMP / GIF / TIFF / WebP / HEIC", Theme.FontTiny, subRect, Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
