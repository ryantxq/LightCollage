using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LightCollage.Common;
using LightCollage.Core;

namespace LightCollage.App
{
    /// <summary>
    /// 预览命中信息副本（v1.1 网格拼接）：
    /// 由 MainForm 从一次性的 PreviewComposition 拷贝而来，PreviewPanel 只持有这些值/数组副本，
    /// 不持有会被 Dispose 的 Bitmap / PreviewComposition，避免生命周期问题。
    /// 坐标约定：Items 与网格元数据都使用“输出像素坐标”；Scale = 预览位图尺寸 / 输出尺寸。
    /// </summary>
    internal sealed class PreviewHitInfo
    {
        /// <summary>合成缩放比：预览位图像素 / 输出像素（来自 PreviewComposition.Scale）。</summary>
        public float Scale = 1f;

        /// <summary>输出坐标下的图片区域矩形（LayoutItem.Index = 图片列表下标）。</summary>
        public LayoutItem[] Items = new LayoutItem[0];

        /// <summary>是否为网格布局。</summary>
        public bool IsGrid;

        public int GridRows;
        public int GridColumns;
        public int CellWidth;
        public int CellHeight;
        public int RowGap;
        public int ColumnGap;
        public int GridMargin;

        /// <summary>合并区域副本（0 基坐标、闭区间）。</summary>
        public GridRegion[] Merges = new GridRegion[0];

        /// <summary>是否叠加网格辅助线（仅预览，不写入输出）。</summary>
        public bool ShowGuides;

        public static PreviewHitInfo CreateEmpty()
        {
            return new PreviewHitInfo();
        }
    }

    /// <summary>
    /// 预览面板：棋盘格 + 合成图 + 信息胶囊 + 加载遮罩；
    /// v1.1 增加：网格辅助线叠加、鼠标拖动两张图对调（SwapRequested）、拖动高亮与状态提示、Esc 取消。
    /// </summary>
    internal sealed class PreviewPanel : Control
    {
        /// <summary>拖动判定阈值（逻辑像素）：按下后移动超过该距离才算拖动，避免误触。</summary>
        private const int DragThreshold = 5;

        private Bitmap _image;
        private string _infoText = string.Empty;
        private string _sizeText = string.Empty;
        private bool _loading;
        private string _loadingText = "正在载入图片…";
        private int _loadingPercent;
        private bool _dragOver;

        private PreviewHitInfo _hitInfo;
        private bool _dragCandidate;
        private bool _dragging;
        private int _dragFrom = -1;
        private int _dragTo = -1;
        private Point _dragStart;
        private string _lastDragStatus = string.Empty;

        /// <summary>双击空白处添加图片。</summary>
        public event EventHandler AddRequested;

        /// <summary>拖动两张图片后松开：from/to 均为图片列表下标（0 基）。</summary>
        public event Action<int, int> SwapRequested;

        /// <summary>拖动过程/取消时希望显示在状态栏的提示。warn=true 表示需要强调。</summary>
        public event Action<string, bool> DragStatusChanged;

        public PreviewPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = Theme.WindowBg;
            Cursor = Cursors.Default;
            AccessibleName = "预览区";
            Text = "预览区";
        }

        public Bitmap Image
        {
            get { return _image; }
        }

        public bool IsDragging
        {
            get { return _dragging; }
        }

        public bool IsDragCandidate
        {
            get { return _dragCandidate; }
        }

        public void SetPreview(Bitmap image, string infoText, string sizeText)
        {
            _image = image;
            _infoText = infoText == null ? string.Empty : infoText;
            _sizeText = sizeText == null ? string.Empty : sizeText;
            if (image == null)
            {
                _hitInfo = null;
            }
            if (_dragCandidate || _dragging)
            {
                ResetDragState();
            }
            Invalidate();
        }

        /// <summary>喂入“命中信息副本”（合成缩放 + 输出坐标矩形 + 网格元数据 + 合并区域 + 辅助线开关）。</summary>
        public void SetHitInfo(PreviewHitInfo info)
        {
            _hitInfo = info;
            if (_dragCandidate || _dragging)
            {
                ResetDragState();
            }
            Invalidate();
        }

        public void SetLoading(bool loading, string text, int percent)
        {
            _loading = loading;
            _loadingText = text == null ? "正在载入图片…" : text;
            _loadingPercent = Math.Max(0, Math.Min(100, percent));
            if (loading && (_dragCandidate || _dragging))
            {
                ResetDragState();
            }
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

        /// <summary>取消当前拖动/拖动候选；返回是否确实取消了拖动（供 Esc 判断是否消费按键）。</summary>
        public bool CancelDrag()
        {
            if (!_dragCandidate && !_dragging)
            {
                return false;
            }
            ResetDragState();
            RaiseDragStatus("已取消拖动", false);
            return true;
        }

        public Rectangle ContentRect
        {
            get
            {
                int pad = Theme.S(this, 18);
                return new Rectangle(pad, pad, Math.Max(1, Width - pad * 2), Math.Max(1, Height - pad * 2));
            }
        }

        // ------------------------------------------------------------------ 坐标换算

        /// <summary>预览位图在面板上的显示矩形（与 OnPaint 中绘制逻辑一致，最大 1:1）。</summary>
        private Rectangle ImageDisplayRect()
        {
            if (_image == null || _image.Width <= 0 || _image.Height <= 0)
            {
                return Rectangle.Empty;
            }
            Rectangle content = ContentRect;
            float scale = Math.Min(content.Width / (float)_image.Width, content.Height / (float)_image.Height);
            if (scale > 1f)
            {
                scale = 1f;
            }
            int w = Math.Max(1, (int)Math.Round(_image.Width * scale));
            int h = Math.Max(1, (int)Math.Round(_image.Height * scale));
            return new Rectangle(content.Left + (content.Width - w) / 2, content.Top + (content.Height - h) / 2, w, h);
        }

        /// <summary>输出像素坐标 → 面板客户区坐标的缩放系数。</summary>
        private bool OutputToClientScale(out float factor)
        {
            factor = 0f;
            if (_image == null || _hitInfo == null || _hitInfo.Scale <= 0.00001f)
            {
                return false;
            }
            Rectangle display = ImageDisplayRect();
            if (display.Width <= 0 || display.Height <= 0)
            {
                return false;
            }
            factor = _hitInfo.Scale * (float)display.Width / (float)_image.Width;
            return factor > 0.00001f;
        }

        /// <summary>把输出坐标下的图片区域转换为面板客户区矩形（用于高亮）。</summary>
        private bool TryGetItemClientRect(int imageIndex, out Rectangle rect)
        {
            rect = Rectangle.Empty;
            if (_hitInfo == null || _hitInfo.Items == null)
            {
                return false;
            }
            for (int i = 0; i < _hitInfo.Items.Length; i++)
            {
                LayoutItem item = _hitInfo.Items[i];
                if (item == null || item.Index != imageIndex)
                {
                    continue;
                }
                float factor;
                if (!OutputToClientScale(out factor))
                {
                    return false;
                }
                Rectangle display = ImageDisplayRect();
                float x1 = display.Left + item.X * factor;
                float y1 = display.Top + item.Y * factor;
                float x2 = display.Left + (item.X + item.Width) * factor;
                float y2 = display.Top + (item.Y + item.Height) * factor;
                rect = Rectangle.FromLTRB((int)Math.Round(x1), (int)Math.Round(y1),
                    Math.Max((int)Math.Round(x1) + 1, (int)Math.Round(x2)),
                    Math.Max((int)Math.Round(y1) + 1, (int)Math.Round(y2)));
                return true;
            }
            return false;
        }

        /// <summary>网格区域（0 基、闭区间）→ 输出像素坐标矩形。</summary>
        private RectangleF RegionOutputRect(GridRegion region)
        {
            PreviewHitInfo info = _hitInfo;
            float cw = info.CellWidth;
            float ch = info.CellHeight;
            float cg = info.ColumnGap;
            float rg = info.RowGap;
            float m = info.GridMargin;
            float x = m + region.Col * (cw + cg);
            float y = m + region.Row * (ch + rg);
            int colSpan = region.ColSpan < 1 ? 1 : region.ColSpan;
            int rowSpan = region.RowSpan < 1 ? 1 : region.RowSpan;
            float w = colSpan * cw + (colSpan - 1) * cg;
            float h = rowSpan * ch + (rowSpan - 1) * rg;
            return new RectangleF(x, y, Math.Max(1f, w), Math.Max(1f, h));
        }

        /// <summary>命中测试：返回鼠标所在图片的图片下标（0 基），空白处返回 -1。</summary>
        private int HitTestItemIndex(Point point)
        {
            if (_hitInfo == null || _image == null)
            {
                return -1;
            }
            Rectangle display = ImageDisplayRect();
            if (display.Width <= 0 || display.Height <= 0 || !display.Contains(point))
            {
                return -1;
            }
            if (_hitInfo.Scale <= 0.00001f)
            {
                return -1;
            }
            float outX = (point.X - display.Left) * (float)_image.Width / (float)display.Width / _hitInfo.Scale;
            float outY = (point.Y - display.Top) * (float)_image.Height / (float)display.Height / _hitInfo.Scale;
            LayoutItem[] items = _hitInfo.Items;
            if (items == null)
            {
                return -1;
            }
            for (int i = 0; i < items.Length; i++)
            {
                LayoutItem item = items[i];
                if (item == null)
                {
                    continue;
                }
                if (outX >= item.X && outX < item.X + item.Width && outY >= item.Y && outY < item.Y + item.Height)
                {
                    return item.Index;
                }
            }
            return -1;
        }

        // ------------------------------------------------------------------ 拖动状态

        private void RaiseDragStatus(string text, bool warn)
        {
            if (text == _lastDragStatus)
            {
                return;
            }
            _lastDragStatus = text;
            if (DragStatusChanged != null)
            {
                DragStatusChanged(text, warn);
            }
        }

        private void ResetDragState()
        {
            _dragCandidate = false;
            _dragging = false;
            _dragFrom = -1;
            _dragTo = -1;
            _lastDragStatus = string.Empty;
            Capture = false;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            if (AddRequested != null)
            {
                AddRequested(this, EventArgs.Empty);
            }
            base.OnDoubleClick(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                if (_dragCandidate || _dragging)
                {
                    CancelDrag();
                }
                return;
            }
            if (e.Button != MouseButtons.Left || _loading || _image == null || _hitInfo == null)
            {
                return;
            }
            if (_dragCandidate || _dragging)
            {
                return;
            }
            int hit = HitTestItemIndex(e.Location);
            if (hit < 0)
            {
                return;
            }
            _dragCandidate = true;
            _dragging = false;
            _dragFrom = hit;
            _dragTo = hit;
            _dragStart = e.Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragCandidate && _dragFrom >= 0)
            {
                if (!_dragging)
                {
                    int dx = Math.Abs(e.X - _dragStart.X);
                    int dy = Math.Abs(e.Y - _dragStart.Y);
                    if (dx + dy >= Theme.S(this, DragThreshold))
                    {
                        _dragging = true;
                        RaiseDragStatus("拖动中：拖到另一张图片上松开对调，Esc 取消", false);
                    }
                }
                if (_dragging)
                {
                    if (!ClientRectangle.Contains(e.Location))
                    {
                        CancelDrag();
                        base.OnMouseMove(e);
                        return;
                    }
                    int target = HitTestItemIndex(e.Location);
                    if (target != _dragTo)
                    {
                        _dragTo = target;
                        Invalidate();
                    }
                    if (target >= 0 && target != _dragFrom)
                    {
                        RaiseDragStatus("松开鼠标交换 第" + (_dragFrom + 1) + "张 ↔ 第" + (target + 1) + "张", false);
                        Cursor = Cursors.Hand;
                    }
                    else
                    {
                        RaiseDragStatus("松开取消：请拖到另一张图片上", false);
                        Cursor = Cursors.No;
                    }
                    base.OnMouseMove(e);
                    return;
                }
                Cursor = Cursors.Hand;
                base.OnMouseMove(e);
                return;
            }

            if (_loading)
            {
                Cursor = Cursors.Default;
            }
            else if (_image == null || _hitInfo == null)
            {
                Cursor = Cursors.Hand;
            }
            else
            {
                Cursor = HitTestItemIndex(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_dragging)
            {
                CancelDrag();
            }
            else if (_dragCandidate)
            {
                ResetDragState();
            }
            Cursor = Cursors.Default;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && (_dragCandidate || _dragging))
            {
                bool wasDragging = _dragging;
                int from = _dragFrom;
                int to = _dragTo;
                ResetDragState();
                if (wasDragging)
                {
                    if (from >= 0 && to >= 0 && from != to)
                    {
                        if (SwapRequested != null)
                        {
                            SwapRequested(from, to);
                        }
                    }
                    else
                    {
                        RaiseDragStatus("已取消拖动：请拖到另一张图片上", false);
                    }
                }
            }
            base.OnMouseUp(e);
        }

        // ------------------------------------------------------------------ 绘制

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

                Rectangle display = ImageDisplayRect();
                if (_image != null && display.Width > 0 && display.Height > 0)
                {
                    using (SolidBrush shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                    {
                        g.FillRectangle(shadow, display.X + Theme.S(this, 4), display.Y + Theme.S(this, 5),
                            display.Width, display.Height);
                    }
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.DrawImage(_image, display);
                    if (!_loading && _hitInfo != null && _hitInfo.IsGrid && _hitInfo.ShowGuides)
                    {
                        DrawGridGuides(g, display);
                    }
                }
                else if (!_loading)
                {
                    DrawEmptyState(g, content);
                }

                if (!_loading && _image != null && (_dragCandidate || _dragging))
                {
                    DrawDragFeedback(g);
                }

                if (_loading)
                {
                    DrawLoadingOverlay(g, content);
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
                DrawChip(g, new Point(content.Right - chipW - Theme.S(this, 12), content.Top + Theme.S(this, 12)), _sizeText, Theme.AccentGlow);
            }

            if (!_loading && _image != null && _hitInfo != null && _hitInfo.IsGrid && _hitInfo.Items != null
                && _hitInfo.Items.Length > 0)
            {
                string hintText = _dragging ? "拖到另一张图片上松开 → 两张图片对调；Esc 取消" : "拖动图片到另一张图上：两张图片对调位置";
                Size hintSize = TextRenderer.MeasureText(hintText, Theme.FontTiny);
                Rectangle hintRect = new Rectangle(content.Left + Theme.S(this, 12),
                    content.Bottom - Theme.S(this, 36),
                    Math.Min(content.Width - Theme.S(this, 24), hintSize.Width + Theme.S(this, 22)),
                    Theme.S(this, 26));
                Theme.FillRounded(g, hintRect, 8f, Color.FromArgb(205, 10, 12, 16));
                Theme.DrawText(g, hintText, Theme.FontTiny, hintRect,
                    _dragging ? Theme.AccentGlow : Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        /// <summary>网格辅助线：淡淡的区域边界（仅预览，不写入输出；合并区域用更亮的虚线）。</summary>
        private void DrawGridGuides(Graphics g, Rectangle display)
        {
            PreviewHitInfo info = _hitInfo;
            if (info == null || info.GridRows < 1 || info.GridColumns < 1)
            {
                return;
            }
            float factor;
            if (!OutputToClientScale(out factor))
            {
                return;
            }
            List<GridRegion> regions = GridLayout.BuildRegions(info.GridRows, info.GridColumns, info.Merges);
            LayoutItem[] items = info.Items;
            for (int i = 0; i < regions.Count; i++)
            {
                GridRegion region = regions[i];
                // 已放入图片的区域优先用 layout.Items 的输出矩形（指定宽/高缩放时元数据逐字段取整可能有几像素偏差）；
                // 空位区域没有 item，用网格元数据近似即可。
                RectangleF output;
                if (items != null && i < items.Length && items[i] != null)
                {
                    LayoutItem item = items[i];
                    output = new RectangleF(item.X, item.Y, Math.Max(1, item.Width), Math.Max(1, item.Height));
                }
                else
                {
                    output = RegionOutputRect(region);
                }
                RectangleF rect = new RectangleF(display.Left + output.X * factor, display.Top + output.Y * factor,
                    Math.Max(1f, output.Width * factor), Math.Max(1f, output.Height * factor));
                bool merged = region.Area > 1;
                Color color = merged ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(50, 255, 255, 255);
                float width = merged ? Theme.S(this, 1.6f) : Theme.S(this, 1f);
                using (Pen pen = new Pen(color, width))
                {
                    pen.DashStyle = DashStyle.Dash;
                    pen.DashPattern = new float[] { 4f, 4f };
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }
        }

        private void DrawLoadingOverlay(Graphics g, Rectangle content)
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

        /// <summary>拖动反馈：源图压暗 + 目标格高亮；无效目标提示明显不亮。</summary>
        private void DrawDragFeedback(Graphics g)
        {
            Rectangle sourceRect;
            if (_dragFrom >= 0 && TryGetItemClientRect(_dragFrom, out sourceRect))
            {
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(140, 8, 10, 14)))
                {
                    g.FillRectangle(dim, sourceRect);
                }
                using (Pen pen = new Pen(Color.FromArgb(220, 255, 255, 255), Theme.S(this, 1.6f)))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(pen, sourceRect.X, sourceRect.Y, sourceRect.Width - 1, sourceRect.Height - 1);
                }
            }
            if (!_dragging)
            {
                return;
            }
            if (_dragTo >= 0 && _dragTo != _dragFrom)
            {
                Rectangle targetRect;
                if (TryGetItemClientRect(_dragTo, out targetRect))
                {
                    using (SolidBrush highlight = new SolidBrush(Color.FromArgb(52, 255, 255, 255)))
                    {
                        g.FillRectangle(highlight, targetRect);
                    }
                    Theme.DrawRounded(g, new RectangleF(targetRect.X, targetRect.Y, targetRect.Width - 1, targetRect.Height - 1),
                        3f, Theme.AccentGlow, Theme.S(this, 2.4f));
                    Size tagSize = TextRenderer.MeasureText("交换", Theme.FontTiny);
                    Rectangle tag = new Rectangle(targetRect.Left + (targetRect.Width - tagSize.Width - Theme.S(this, 14)) / 2,
                        targetRect.Top + (targetRect.Height - Theme.S(this, 22)) / 2,
                        tagSize.Width + Theme.S(this, 14), Theme.S(this, 22));
                    if (tag.Width > 0 && tag.Height > 0 && tag.Left >= targetRect.Left && tag.Right <= targetRect.Right)
                    {
                        Theme.FillRounded(g, tag, 6f, Color.FromArgb(235, 12, 14, 18));
                        Theme.DrawText(g, "交换", Theme.FontTiny, tag, Theme.AccentGlow,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                }
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
