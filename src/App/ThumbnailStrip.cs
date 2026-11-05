using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LightCollage.App
{
    internal sealed class ThumbnailStrip : Control
    {
        private IList<EditorItem> _items = new List<EditorItem>();
        private int _selectedIndex = -1;
        private int _hoverIndex = -1;
        private bool _hoverAdd;
        private bool _hoverRemove;
        private int _dragFrom = -1;
        private int _dragTo = -1;
        private bool _dragging;
        private Point _dragStart;
        private int _scrollX;

        public event EventHandler AddRequested;
        public event Action<int> RemoveRequested;
        public event Action<int, int> ReorderRequested;
        public event EventHandler SelectionChanged;

        public ThumbnailStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.WindowBg;
            Height = 118;
            AllowDrop = false;
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                int next = -1;
                if (_items.Count > 0)
                {
                    next = Math.Max(0, Math.Min(_items.Count - 1, value));
                }
                if (next != _selectedIndex)
                {
                    _selectedIndex = next;
                    Invalidate();
                    if (SelectionChanged != null)
                    {
                        SelectionChanged(this, EventArgs.Empty);
                    }
                }
            }
        }

        public void SetItems(IList<EditorItem> items)
        {
            _items = items ?? new List<EditorItem>();
            if (_items.Count == 0)
            {
                _selectedIndex = -1;
            }
            else if (_selectedIndex >= _items.Count)
            {
                _selectedIndex = _items.Count - 1;
            }
            ClampScroll();
            Invalidate();
        }

        public void RefreshView()
        {
            ClampScroll();
            Invalidate();
        }

        public void EnsureVisible(int index)
        {
            if (index < 0)
            {
                return;
            }
            int cardW = CardWidth();
            int left = index * (cardW + Gap());
            int right = left + cardW;
            if (left < _scrollX)
            {
                _scrollX = left;
            }
            else if (right > _scrollX + ClientSize.Width)
            {
                _scrollX = right - ClientSize.Width;
            }
            ClampScroll();
            Invalidate();
        }

        private int CardSize()
        {
            return Theme.S(this, 86);
        }

        private int Gap()
        {
            return Theme.S(this, 10);
        }

        private int CardWidth()
        {
            return CardSize();
        }

        private int TotalContentWidth()
        {
            return _items.Count * (CardWidth() + Gap()) + CardWidth() + Gap();
        }

        private void ClampScroll()
        {
            int max = Math.Max(0, TotalContentWidth() - ClientSize.Width);
            if (_scrollX > max)
            {
                _scrollX = max;
            }
            if (_scrollX < 0)
            {
                _scrollX = 0;
            }
        }

        private int CardX(int index)
        {
            return index * (CardWidth() + Gap()) - _scrollX;
        }

        private Rectangle CardRect(int index)
        {
            int size = CardSize();
            return new Rectangle(CardX(index), (Height - size) / 2, size, size);
        }

        private Rectangle AddRect()
        {
            int size = CardSize();
            return new Rectangle(_items.Count * (CardWidth() + Gap()) - _scrollX, (Height - size) / 2, size, size);
        }

        private int HitIndex(Point p)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (CardRect(i).Contains(p))
                {
                    return i;
                }
            }
            return -1;
        }

        private Rectangle RemoveRect(int index)
        {
            Rectangle card = CardRect(index);
            int size = Theme.S(this, 18);
            return new Rectangle(card.Right - size - Theme.S(this, 5), card.Top + Theme.S(this, 5), size, size);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hit = HitIndex(e.Location);
            bool hoverAdd = AddRect().Contains(e.Location);
            bool hoverRemove = hit >= 0 && RemoveRect(hit).Contains(e.Location);

            if (_dragging && _dragFrom >= 0)
            {
                int to = InsertionIndex(e.X);
                if (to != _dragTo)
                {
                    _dragTo = to;
                    Invalidate();
                }
                AutoScrollForDrag(e.X);
                Cursor = Cursors.SizeWE;
            }
            else if (_dragFrom >= 0 && Math.Abs(e.X - _dragStart.X) > Theme.S(this, 5))
            {
                _dragging = true;
                _dragTo = InsertionIndex(e.X);
                Cursor = Cursors.SizeWE;
                Invalidate();
            }
            else if (hoverRemove)
            {
                Cursor = Cursors.Hand;
            }
            else if (hoverAdd)
            {
                Cursor = Cursors.Hand;
            }
            else if (hit >= 0)
            {
                Cursor = Cursors.Hand;
            }
            else
            {
                Cursor = Cursors.Default;
            }

            if (hit != _hoverIndex || hoverAdd != _hoverAdd || hoverRemove != _hoverRemove)
            {
                _hoverIndex = hit;
                _hoverAdd = hoverAdd;
                _hoverRemove = hoverRemove;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverIndex = -1;
            _hoverAdd = false;
            _hoverRemove = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                int hit = HitIndex(e.Location);
                if (AddRect().Contains(e.Location))
                {
                    if (AddRequested != null)
                    {
                        AddRequested(this, EventArgs.Empty);
                    }
                    return;
                }
                if (hit >= 0 && RemoveRect(hit).Contains(e.Location))
                {
                    if (RemoveRequested != null)
                    {
                        RemoveRequested(hit);
                    }
                    return;
                }
                if (hit >= 0)
                {
                    SelectedIndex = hit;
                    _dragFrom = hit;
                    _dragTo = hit;
                    _dragStart = e.Location;
                    _dragging = false;
                    Capture = true;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_dragging && _dragFrom >= 0 && _dragTo >= 0)
                {
                    int target = _dragTo;
                    if (target > _dragFrom)
                    {
                        target--;
                    }
                    target = Math.Max(0, Math.Min(_items.Count - 1, target));
                    if (target != _dragFrom && ReorderRequested != null)
                    {
                        ReorderRequested(_dragFrom, target);
                    }
                }
                _dragging = false;
                _dragFrom = -1;
                _dragTo = -1;
                Capture = false;
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        private void AutoScrollForDrag(int x)
        {
            int edge = Theme.S(this, 28);
            if (x < edge && _scrollX > 0)
            {
                _scrollX = Math.Max(0, _scrollX - Theme.S(this, 16));
                Invalidate();
            }
            else if (x > Width - edge)
            {
                int max = Math.Max(0, TotalContentWidth() - ClientSize.Width);
                if (_scrollX < max)
                {
                    _scrollX = Math.Min(max, _scrollX + Theme.S(this, 16));
                    Invalidate();
                }
            }
        }

        private int InsertionIndex(int x)
        {
            int contentX = x + _scrollX;
            int stride = CardWidth() + Gap();
            int index = (int)Math.Round(contentX / (double)stride);
            return Math.Max(0, Math.Min(_items.Count, index));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int max = Math.Max(0, TotalContentWidth() - ClientSize.Width);
            if (max > 0)
            {
                _scrollX -= e.Delta > 0 ? Theme.S(this, 48) : -Theme.S(this, 48);
                ClampScroll();
                Invalidate();
            }
            base.OnMouseWheel(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Left)
            {
                SelectedIndex = _selectedIndex - 1;
                EnsureVisible(_selectedIndex);
                return true;
            }
            if (keyData == Keys.Right)
            {
                if (_selectedIndex < 0)
                {
                    SelectedIndex = 0;
                }
                else
                {
                    SelectedIndex = _selectedIndex + 1;
                }
                EnsureVisible(_selectedIndex);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetHighQuality(g);
            using (SolidBrush bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, ClientRectangle);
            }

            for (int i = 0; i < _items.Count; i++)
            {
                DrawCard(g, i, CardRect(i));
            }
            DrawAddCard(g, AddRect());

            if (_dragging && _dragTo >= 0)
            {
                int stride = CardWidth() + Gap();
                int x = _dragTo * stride - _scrollX - Gap() / 2;
                x = Math.Max(0, Math.Min(Width - Theme.S(this, 2), x));
                using (Pen pen = new Pen(Theme.AccentGlow, Theme.S(this, 3f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, x, Theme.S(this, 8), x, Height - Theme.S(this, 8));
                }
            }
        }

        private void DrawCard(Graphics g, int index, Rectangle rect)
        {
            EditorItem item = _items[index];
            bool selected = index == _selectedIndex;
            bool hover = index == _hoverIndex;

            Theme.FillRounded(g, rect, 9f, selected ? Color.FromArgb(0x2A, 0x2A, 0x2A) : Theme.PanelBg2);
            Theme.DrawRounded(g, rect, 9f, selected ? Theme.AccentGlow : (hover ? Theme.ControlBorder : Theme.ControlBorderSoft),
                selected ? 2f : 1f);

            Rectangle imageRect = new Rectangle(rect.Left + 5, rect.Top + 5, rect.Width - 10, rect.Height - 10);
            using (GraphicsPath clip = Theme.RoundedPath(imageRect, 6f))
            {
                g.SetClip(clip, CombineMode.Intersect);
                Theme.DrawCheckerboard(g, imageRect, Theme.S(this, 8), Color.FromArgb(0x30, 0x30, 0x30), Color.FromArgb(0x24, 0x24, 0x24));
                if (item.IsLoading)
                {
                    Theme.DrawText(g, "载入中", Theme.FontTiny, imageRect, Theme.TextMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
                else if (!item.IsReady)
                {
                    Theme.DrawText(g, "无法载入", Theme.FontTiny, imageRect, Theme.Danger,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
                else
                {
                    Bitmap thumb = item.Thumbnail;
                    if (thumb != null)
                    {
                        float scale = Math.Min(imageRect.Width / (float)thumb.Width, imageRect.Height / (float)thumb.Height);
                        int w = Math.Max(1, (int)Math.Round(thumb.Width * scale));
                        int h = Math.Max(1, (int)Math.Round(thumb.Height * scale));
                        int x = imageRect.Left + (imageRect.Width - w) / 2;
                        int y = imageRect.Top + (imageRect.Height - h) / 2;
                        g.DrawImage(thumb, new Rectangle(x, y, w, h));
                    }
                }
                g.ResetClip();
            }

            int badge = Theme.S(this, 18);
            Rectangle badgeRect = new Rectangle(rect.Left + Theme.S(this, 4), rect.Top + Theme.S(this, 4), badge, badge);
            Theme.FillRounded(g, badgeRect, 5f, Color.FromArgb(215, 8, 10, 14));
            Theme.DrawText(g, (index + 1).ToString(), Theme.FontTiny, badgeRect, Theme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            if (!item.IsLoading)
            {
                Rectangle xRect = RemoveRect(index);
                Theme.FillRounded(g, xRect, xRect.Width / 2f, Color.FromArgb(hover && _hoverRemove ? 230 : 170, 8, 10, 14));
                Theme.DrawText(g, "×", Theme.FontSmall, xRect, Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            if (!item.IsReady && !item.IsLoading && !string.IsNullOrEmpty(item.ErrorText))
            {
                Theme.DrawRounded(g, rect, 9f, Color.FromArgb(120, Theme.Danger), 1.5f);
            }
        }

        private void DrawAddCard(Graphics g, Rectangle rect)
        {
            Color fill = _hoverAdd ? Color.FromArgb(0x28, 0x28, 0x28) : Theme.PanelBg2;
            Theme.FillRounded(g, rect, 9f, fill);
            using (Pen pen = new Pen(_hoverAdd ? Theme.AccentGlow : Theme.ControlBorder, 1.2f))
            {
                pen.DashStyle = DashStyle.Dash;
                using (GraphicsPath path = Theme.RoundedPath(new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1f, rect.Height - 1f), 9f))
                {
                    g.DrawPath(pen, path);
                }
            }
            int cx = rect.Left + rect.Width / 2;
            int cy = rect.Top + rect.Height / 2 - Theme.S(this, 6);
            using (Pen pen = new Pen(_hoverAdd ? Theme.AccentGlow : Theme.TextSecondary, Theme.S(this, 1.6f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int len = Theme.S(this, 9);
                g.DrawLine(pen, cx - len, cy, cx + len, cy);
                g.DrawLine(pen, cx, cy - len, cx, cy + len);
            }
            Theme.DrawText(g, "添加图片", Theme.FontTiny,
                new Rectangle(rect.Left, cy + Theme.S(this, 15), rect.Width, Theme.S(this, 18)),
                _hoverAdd ? Theme.AccentGlow : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
