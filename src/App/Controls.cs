using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LightCollage.App
{
    internal sealed class CardPanel : Panel
    {
        public int Radius = 14;
        public Color BorderColor = Theme.CardBorder;
        public Color FillColor = Theme.PanelBg;

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.WindowBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetHighQuality(g);
            using (SolidBrush bg = new SolidBrush(Theme.WindowBg))
            {
                g.FillRectangle(bg, ClientRectangle);
            }
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(g, rect, Radius, FillColor);
            Theme.DrawRounded(g, rect, Radius, BorderColor, 1f);
        }
    }

    internal enum ButtonStyle
    {
        Primary,
        Secondary,
        Ghost,
        Danger
    }

    internal sealed class AccentButton : Control
    {
        private bool _hover;
        private bool _pressed;
        private ButtonStyle _style;

        public AccentButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Font = Theme.FontButton;
            Height = 40;
            Cursor = Cursors.Hand;
            TabStop = true;
            _style = ButtonStyle.Secondary;
            BackColor = Theme.PanelBg;
        }

        public ButtonStyle Style
        {
            get { return _style; }
            set { _style = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Focus();
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetHighQuality(g);
            using (SolidBrush bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, ClientRectangle);
            }

            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color fill;
            Color border;
            Color text;
            float borderWidth = 1f;

            if (!Enabled)
            {
                if (_style == ButtonStyle.Primary)
                {
                    fill = Theme.AccentDim;
                    border = Theme.AccentDim;
                    text = Color.FromArgb(120, Theme.AccentText);
                }
                else
                {
                    fill = Theme.ControlBg;
                    border = Theme.ControlBorderSoft;
                    text = Theme.TextDim;
                }
            }
            else if (_style == ButtonStyle.Primary)
            {
                fill = _pressed ? Theme.Accent : (_hover ? Theme.AccentHover : Theme.Accent);
                border = fill;
                text = Theme.AccentText;
            }
            else if (_style == ButtonStyle.Danger)
            {
                fill = _pressed ? Color.FromArgb(120, 35, 35, 35) : (_hover ? Color.FromArgb(36, 255, 255, 255) : Theme.ControlBg);
                border = Theme.Danger;
                text = Theme.Danger;
            }
            else if (_style == ButtonStyle.Ghost)
            {
                fill = _pressed ? Color.FromArgb(60, 255, 255, 255) : (_hover ? Color.FromArgb(25, 255, 255, 255) : Color.Transparent);
                border = Color.Transparent;
                text = _hover ? Theme.TextPrimary : Theme.TextSecondary;
            }
            else
            {
                fill = _pressed ? Theme.ControlBgDeep : (_hover ? Color.FromArgb(0x30, 0x30, 0x30) : Theme.ControlBg);
                border = (Focused && ShowFocusCues) ? Theme.AccentGlow : Theme.ControlBorder;
                text = Theme.TextPrimary;
                borderWidth = Focused ? 1.5f : 1f;
            }

            if (fill.A > 0)
            {
                Theme.FillRounded(g, rect, 9f, fill);
            }
            if (border.A > 0)
            {
                Theme.DrawRounded(g, rect, 9f, border, borderWidth);
            }

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            Theme.DrawText(g, Text, Font, new Rectangle(8, 0, Width - 16, Height), text, flags);
        }
    }

    internal sealed class SegmentedControl : Control
    {
        private string[] _items = new string[0];
        private int _selectedIndex;
        private int _hoverIndex = -1;

        public event EventHandler SelectedIndexChanged;

        public SegmentedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Font = Theme.FontBase;
            Height = 38;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public string[] Items
        {
            get { return _items; }
            set
            {
                _items = value ?? new string[0];
                if (_selectedIndex >= _items.Length)
                {
                    _selectedIndex = Math.Max(0, _items.Length - 1);
                }
                Invalidate();
            }
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                int next = _items.Length == 0 ? 0 : Math.Max(0, Math.Min(_items.Length - 1, value));
                if (next != _selectedIndex)
                {
                    _selectedIndex = next;
                    Invalidate();
                    if (SelectedIndexChanged != null)
                    {
                        SelectedIndexChanged(this, EventArgs.Empty);
                    }
                }
            }
        }

        private Rectangle[] ItemRects()
        {
            Rectangle[] rects = new Rectangle[_items.Length];
            int inset = 3;
            int gap = 3;
            int total = Width - inset * 2 - gap * Math.Max(0, _items.Length - 1);
            int w = _items.Length == 0 ? 0 : total / _items.Length;
            int x = inset;
            for (int i = 0; i < _items.Length; i++)
            {
                rects[i] = new Rectangle(x, inset, w, Height - inset * 2);
                x += w + gap;
            }
            return rects;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hover = -1;
            Rectangle[] rects = ItemRects();
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i].Contains(e.Location))
                {
                    hover = i;
                    break;
                }
            }
            if (hover != _hoverIndex)
            {
                _hoverIndex = hover;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Rectangle[] rects = ItemRects();
                for (int i = 0; i < rects.Length; i++)
                {
                    if (rects[i].Contains(e.Location))
                    {
                        SelectedIndex = i;
                        Focus();
                        break;
                    }
                }
            }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left)
            {
                SelectedIndex = _selectedIndex - 1;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right)
            {
                SelectedIndex = _selectedIndex + 1;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetHighQuality(g);
            using (SolidBrush bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, ClientRectangle);
            }
            RectangleF container = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(g, container, 10f, Theme.ControlBgDeep);
            Theme.DrawRounded(g, container, 10f, Theme.ControlBorderSoft, 1f);

            Rectangle[] rects = ItemRects();
            for (int i = 0; i < rects.Length; i++)
            {
                RectangleF r = new RectangleF(rects[i].X, rects[i].Y, rects[i].Width, rects[i].Height);
                Color textColor;
                if (i == _selectedIndex)
                {
                    Theme.FillRounded(g, r, 7f, Theme.Accent);
                    textColor = Theme.AccentText;
                }
                else if (i == _hoverIndex)
                {
                    Theme.FillRounded(g, r, 7f, Theme.ControlBg);
                    textColor = Theme.TextPrimary;
                }
                else
                {
                    textColor = Theme.TextSecondary;
                }
                Theme.DrawText(g, _items[i], Font, rects[i], textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    internal sealed class Stepper : Control
    {
        private int _value;
        private int _minimum;
        private int _maximum = 200;
        private int _step = 1;
        private int _hoverZone;
        private readonly TextBox _edit;

        public event EventHandler ValueChanged;

        public Stepper()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Font = Theme.FontNumber;
            Height = 34;
            TabStop = true;

            _edit = new TextBox();
            _edit.BorderStyle = BorderStyle.None;
            _edit.TextAlign = HorizontalAlignment.Center;
            _edit.Font = Theme.FontNumber;
            _edit.BackColor = Theme.ControlBgDeep;
            _edit.ForeColor = Theme.TextPrimary;
            _edit.AutoSize = false;
            _edit.ShortcutsEnabled = true;
            _edit.KeyDown += EditKeyDown;
            _edit.KeyPress += EditKeyPress;
            _edit.LostFocus += delegate { CommitEdit(); };
            _edit.GotFocus += delegate { _edit.SelectAll(); Invalidate(); };
            _edit.MouseWheel += delegate(object sender, MouseEventArgs e) { StepBy(e.Delta > 0 ? 1 : -1); };
            Controls.Add(_edit);
            UpdateEditText();
        }

        public int Minimum
        {
            get { return _minimum; }
            set { _minimum = value; ClampValue(); }
        }

        public int Maximum
        {
            get { return _maximum; }
            set { _maximum = value; ClampValue(); }
        }

        public int Step
        {
            get { return _step; }
            set { _step = Math.Max(1, value); }
        }

        public string Suffix { get; set; }

        public int Value
        {
            get { return _value; }
            set
            {
                int next = Math.Max(_minimum, Math.Min(_maximum, value));
                if (next != _value)
                {
                    _value = next;
                    UpdateEditText();
                    Invalidate();
                    if (ValueChanged != null)
                    {
                        ValueChanged(this, EventArgs.Empty);
                    }
                }
            }
        }

        public void SetValueSilently(int value)
        {
            _value = Math.Max(_minimum, Math.Min(_maximum, value));
            UpdateEditText();
            Invalidate();
        }

        private void ClampValue()
        {
            _value = Math.Max(_minimum, Math.Min(_maximum, _value));
            UpdateEditText();
            Invalidate();
        }

        private void UpdateEditText()
        {
            if (_edit == null)
            {
                return;
            }
            if (!_edit.Focused)
            {
                _edit.Text = _value.ToString();
            }
        }

        private Rectangle MinusRect()
        {
            int zoneWidth = Math.Max(Theme.S(this, 34), Width / 4);
            return new Rectangle(0, 0, zoneWidth, Height);
        }

        private Rectangle PlusRect()
        {
            int zoneWidth = Math.Max(Theme.S(this, 34), Width / 4);
            return new Rectangle(Width - zoneWidth, 0, zoneWidth, Height);
        }

        private void LayoutEdit()
        {
            if (_edit == null)
            {
                return;
            }
            int zoneWidth = Math.Max(Theme.S(this, 34), Width / 4);
            int editHeight = Math.Min(Height - Theme.S(this, 6), _edit.Font.Height + Theme.S(this, 8));
            if (editHeight < Theme.S(this, 18))
            {
                editHeight = Math.Max(Theme.S(this, 18), Height - Theme.S(this, 6));
            }
            int editWidth = Math.Max(Theme.S(this, 30), Width - zoneWidth * 2 - Theme.S(this, 4));
            int x = (Width - editWidth) / 2;
            _edit.SetBounds(x, Math.Max(0, (Height - editHeight) / 2), editWidth, editHeight);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutEdit();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (_edit != null)
            {
                _edit.Enabled = Enabled;
                _edit.BackColor = Theme.ControlBgDeep;
                _edit.ForeColor = Enabled ? Theme.TextPrimary : Theme.TextDim;
            }
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (Enabled && _edit != null)
            {
                _edit.Focus();
                _edit.SelectAll();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int zone = 0;
            if (MinusRect().Contains(e.Location))
            {
                zone = 1;
            }
            else if (PlusRect().Contains(e.Location))
            {
                zone = 2;
            }
            if (zone != _hoverZone)
            {
                _hoverZone = zone;
                Invalidate();
            }
            Cursor = zone == 0 ? Cursors.IBeam : Cursors.Hand;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverZone = 0;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (MinusRect().Contains(e.Location))
                {
                    StepBy(-1);
                }
                else if (PlusRect().Contains(e.Location))
                {
                    StepBy(1);
                }
                else if (_edit != null)
                {
                    _edit.Focus();
                    _edit.SelectAll();
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            StepBy(e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }

        private void StepBy(int direction)
        {
            Value = _value + _step * direction;
        }

        private void EditKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                CommitEdit();
                _edit.SelectAll();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                UpdateEditText();
                _edit.SelectAll();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                StepBy(1);
                _edit.SelectAll();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                StepBy(-1);
                _edit.SelectAll();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void EditKeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        private void CommitEdit()
        {
            if (_edit == null)
            {
                return;
            }
            int parsed;
            if (int.TryParse(_edit.Text.Trim(), out parsed))
            {
                Value = parsed;
            }
            UpdateEditText();
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down)
            {
                StepBy(-1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up)
            {
                StepBy(1);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetHighQuality(g);
            using (SolidBrush bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, ClientRectangle);
            }
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(g, rect, 8f, Theme.ControlBgDeep);
            Color border = _edit != null && _edit.Focused ? Theme.AccentGlow : Theme.ControlBorderSoft;
            Theme.DrawRounded(g, rect, 8f, border, 1f);

            Color minusColor = (!Enabled) ? Theme.TextDim : (_hoverZone == 1 ? Theme.AccentGlow : Theme.TextSecondary);
            Color plusColor = (!Enabled) ? Theme.TextDim : (_hoverZone == 2 ? Theme.AccentGlow : Theme.TextSecondary);
            Rectangle minusRect = MinusRect();
            Rectangle plusRect = PlusRect();
            DrawPlusMinus(g, new Point(minusRect.Left + minusRect.Width / 2, Height / 2), false, minusColor);
            DrawPlusMinus(g, new Point(plusRect.Left + plusRect.Width / 2, Height / 2), true, plusColor);
        }

        private void DrawPlusMinus(Graphics g, Point center, bool plus, Color color)
        {
            float len = Theme.S(this, 5f);
            using (Pen pen = new Pen(color, Theme.S(this, 1.6f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, center.X - len, center.Y, center.X + len, center.Y);
                if (plus)
                {
                    g.DrawLine(pen, center.X, center.Y - len, center.X, center.Y + len);
                }
            }
        }
    }
    internal sealed class AlphaSlider : Control
    {
        private int _value = 255;
        private Color _baseColor = Color.White;
        private bool _dragging;

        public event EventHandler ValueChanged;

        public AlphaSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Height = 26;
            Cursor = Cursors.Hand;
            TabStop = true;
            BackColor = Theme.PanelBg;
        }

        public Color BaseColor
        {
            get { return _baseColor; }
            set { _baseColor = value; Invalidate(); }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int next = Math.Max(0, Math.Min(255, value));
                if (next != _value)
                {
                    _value = next;
                    Invalidate();
                    if (ValueChanged != null)
                    {
                        ValueChanged(this, EventArgs.Empty);
                    }
                }
            }
        }

        public void SetValueSilently(int value)
        {
            _value = Math.Max(0, Math.Min(255, value));
            Invalidate();
        }

        private Rectangle TrackRect()
        {
            int pad = Theme.S(this, 10);
            int y = Height / 2 - Theme.S(this, 1);
            return new Rectangle(pad, y, Math.Max(1, Width - pad * 2), Theme.S(this, 3));
        }

        private int KnobRadius()
        {
            return Theme.S(this, 7);
        }

        private int ValueFromX(int x)
        {
            Rectangle track = TrackRect();
            float t = (x - track.Left) / (float)Math.Max(1, track.Width);
            t = Math.Max(0f, Math.Min(1f, t));
            return (int)Math.Round(t * 255f);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                Capture = true;
                Value = ValueFromX(e.X);
                Focus();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Value = ValueFromX(e.X);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = false;
                Capture = false;
                Value = ValueFromX(e.X);
            }
            base.OnMouseUp(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left)
            {
                Value = _value - 5;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right)
            {
                Value = _value + 5;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetHighQuality(g);
            using (SolidBrush bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, ClientRectangle);
            }
            Rectangle track = TrackRect();
            int knobX = track.Left + (int)Math.Round(track.Width * _value / 255f);
            knobX = Math.Max(track.Left, Math.Min(track.Right, knobX));
            int knobRadius = KnobRadius();

            using (Pen unfilled = new Pen(Theme.ControlBorder, Theme.S(this, 2f)))
            {
                unfilled.StartCap = LineCap.Round;
                unfilled.EndCap = LineCap.Round;
                g.DrawLine(unfilled, track.Left, track.Top + track.Height / 2f, track.Right, track.Top + track.Height / 2f);
            }
            if (knobX > track.Left)
            {
                using (Pen filled = new Pen(Theme.TextPrimary, Theme.S(this, 2f)))
                {
                    filled.StartCap = LineCap.Round;
                    filled.EndCap = LineCap.Round;
                    g.DrawLine(filled, track.Left, track.Top + track.Height / 2f, knobX, track.Top + track.Height / 2f);
                }
            }

            Rectangle knob = new Rectangle(knobX - knobRadius, Height / 2 - knobRadius, knobRadius * 2, knobRadius * 2);
            using (SolidBrush dark = new SolidBrush(Theme.WindowBg))
            {
                g.FillEllipse(dark, knob);
            }
            using (Pen pen = new Pen(Theme.TextPrimary, Theme.S(this, 2f)))
            {
                g.DrawEllipse(pen, knob);
            }
        }
    }
    internal sealed class ColorSwatchButton : Control
    {
        private Color _swatchColor = Color.Transparent;
        private bool _selected;
        private bool _hover;
        private string _caption = string.Empty;
        private bool _showChecker;

        public ColorSwatchButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Height = 52;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public Color SwatchColor
        {
            get { return _swatchColor; }
            set { _swatchColor = value; _showChecker = value.A < 255; Invalidate(); }
        }

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public bool ShowChecker
        {
            get { return _showChecker; }
            set { _showChecker = value; Invalidate(); }
        }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value == null ? string.Empty : value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
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
            int captionHeight = Theme.S(this, 18);
            int boxH = Height - captionHeight;
            int edge = Theme.S(this, 1);
            Rectangle box = new Rectangle(edge, edge, Math.Max(1, Width - edge * 2), Math.Max(1, boxH - edge));
            float radius = Theme.S(this, 6f);
            using (GraphicsPath path = Theme.RoundedPath(box, radius))
            {
                g.SetClip(path);
                if (_showChecker || _swatchColor.A < 255)
                {
                    Theme.DrawCheckerboard(g, box, Theme.S(this, 7),
                        Color.FromArgb(0xD0, 0xD0, 0xD0), Color.FromArgb(0xF2, 0xF2, 0xF2));
                }
                if (_swatchColor.A > 0)
                {
                    using (SolidBrush brush = new SolidBrush(_swatchColor))
                    {
                        g.FillRectangle(brush, box);
                    }
                }
                g.ResetClip();
            }
            Color border = _selected ? Theme.AccentGlow : (_hover ? Theme.TextMuted : Theme.ControlBorder);
            float borderWidth = Theme.S(this, _selected ? 2f : 1f);
            Theme.DrawRounded(g, box, radius, border, borderWidth);

            if (!string.IsNullOrEmpty(_caption))
            {
                Color textColor = _selected ? Theme.AccentGlow : Theme.TextSecondary;
                Theme.DrawText(g, _caption, Theme.FontTiny,
                    new Rectangle(0, boxH, Width, captionHeight), textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    internal sealed class CheckBoxX : Control
    {
        private bool _checked;
        private bool _hover;

        public event EventHandler CheckedChanged;

        public CheckBoxX()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Font = Theme.FontSmall;
            Height = 22;
            Cursor = Cursors.Hand;
            TabStop = true;
            BackColor = Theme.PanelBg;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    Invalidate();
                    if (CheckedChanged != null)
                    {
                        CheckedChanged(this, EventArgs.Empty);
                    }
                }
            }
        }

        public void SetCheckedSilently(bool value)
        {
            _checked = value;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Checked = !Checked;
                Focus();
            }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetHighQuality(g);
            using (SolidBrush bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, ClientRectangle);
            }
            int size = Theme.S(this, 16);
            int edge = Theme.S(this, 1);
            Rectangle box = new Rectangle(edge, (Height - size) / 2, size, size);
            Color fill = _checked ? Theme.Accent : Theme.ControlBgDeep;
            Theme.FillRounded(g, box, Theme.S(this, 4f), fill);
            Theme.DrawRounded(g, box, Theme.S(this, 4f), _checked ? Theme.Accent : (_hover ? Theme.ControlBorder : Theme.ControlBorderSoft), Theme.S(this, 1f));
            if (_checked)
            {
                using (Pen pen = new Pen(Theme.AccentText, Theme.S(this, 1.8f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new Point[]
                    {
                        new Point(box.Left + Theme.S(this, 4), box.Top + Theme.S(this, 8)),
                        new Point(box.Left + Theme.S(this, 7), box.Top + Theme.S(this, 11)),
                        new Point(box.Left + Theme.S(this, 12), box.Top + Theme.S(this, 5))
                    });
                }
            }
            int textLeft = box.Right + Theme.S(this, 7);
            Theme.DrawText(g, Text, Font, new Rectangle(textLeft, 0, Math.Max(1, Width - textLeft), Height),
                Enabled ? Theme.TextSecondary : Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    internal sealed class SeparatorLine : Control
    {
        public SeparatorLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            Height = 1;
            BackColor = Theme.PanelBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(Theme.ControlBorderSoft))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }
    }
}
