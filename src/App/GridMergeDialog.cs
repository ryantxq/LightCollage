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
    /// 合并编辑器左侧的矩阵控件：自绘 R×C 格子，支持鼠标拖选相邻矩形，已合并区域高亮显示。
    /// 只负责显示与选区，合并/拆分逻辑由 GridMergeDialog 调用 Core/GridLayout 完成。
    /// </summary>
    internal sealed class GridMatrixControl : Control
    {
        private int _rows = 3;
        private int _columns = 3;
        private List<GridRegion> _merges = new List<GridRegion>();

        private bool _selecting;
        private bool _hasSelection;
        private int _startRow = -1;
        private int _startColumn = -1;
        private int _endRow = -1;
        private int _endColumn = -1;
        private int _hoverRow = -1;
        private int _hoverColumn = -1;

        public event EventHandler SelectionChanged;

        public GridMatrixControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = Theme.PanelBg;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleName = "网格矩阵";
            Text = "网格合并矩阵，拖动框选相邻格子";
        }

        public int Rows
        {
            get { return _rows; }
        }

        public int Columns
        {
            get { return _columns; }
        }

        public bool HasSelection
        {
            get { return _hasSelection; }
        }

        public int HoverRow
        {
            get { return _hoverRow; }
        }

        public int HoverColumn
        {
            get { return _hoverColumn; }
        }

        public void SetGrid(int rows, int columns, IList<GridRegion> merges)
        {
            _rows = GridLayout.ClampRows(rows);
            _columns = GridLayout.ClampColumns(columns);
            _merges = GridLayout.NormalizeMerges(_rows, _columns, merges);
            ClearSelection();
        }

        public void SetMerges(IList<GridRegion> merges)
        {
            _merges = GridLayout.NormalizeMerges(_rows, _columns, merges);
            Invalidate();
        }

        public void ClearSelection()
        {
            _hasSelection = false;
            _selecting = false;
            _startRow = -1;
            _startColumn = -1;
            _endRow = -1;
            _endColumn = -1;
            Invalidate();
            if (SelectionChanged != null)
            {
                SelectionChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>取当前选区（0 基、闭区间，转换为左上角 + 跨度）。无选区返回 false。</summary>
        public bool TryGetSelection(out int row, out int column, out int rowSpan, out int columnSpan)
        {
            row = 0;
            column = 0;
            rowSpan = 0;
            columnSpan = 0;
            if (!_hasSelection)
            {
                return false;
            }
            int r0 = Math.Min(_startRow, _endRow);
            int c0 = Math.Min(_startColumn, _endColumn);
            row = r0;
            column = c0;
            rowSpan = Math.Abs(_endRow - _startRow) + 1;
            columnSpan = Math.Abs(_endColumn - _startColumn) + 1;
            return true;
        }

        /// <summary>选区是否可合并（矩形、至少 2 格、不与已有合并区域部分重叠）。</summary>
        public bool IsSelectionMergeable()
        {
            int row;
            int column;
            int rowSpan;
            int columnSpan;
            if (!TryGetSelection(out row, out column, out rowSpan, out columnSpan))
            {
                return false;
            }
            if (rowSpan * columnSpan < 2)
            {
                return false;
            }
            List<GridRegion> ignored;
            return GridLayout.TryMerge(_rows, _columns, _merges, row, column, rowSpan, columnSpan, out ignored);
        }

        // ------------------------------------------------------------------ 几何

        private int CellGap()
        {
            return Theme.S(this, 6);
        }

        private void ComputeGeometry(out int cell, out int originX, out int originY)
        {
            int gap = CellGap();
            int pad = Theme.S(this, 8);
            int availW = Math.Max(1, Width - pad * 2);
            int availH = Math.Max(1, Height - pad * 2);
            int cellByWidth = (availW - gap * Math.Max(0, _columns - 1)) / Math.Max(1, _columns);
            int cellByHeight = (availH - gap * Math.Max(0, _rows - 1)) / Math.Max(1, _rows);
            cell = Math.Min(cellByWidth, cellByHeight);
            int maxCell = Theme.S(this, 100);
            if (cell > maxCell)
            {
                cell = maxCell;
            }
            if (cell < 4)
            {
                cell = 4;
            }
            int gridW = _columns * cell + gap * Math.Max(0, _columns - 1);
            int gridH = _rows * cell + gap * Math.Max(0, _rows - 1);
            originX = (Width - gridW) / 2;
            originY = (Height - gridH) / 2;
        }

        private Rectangle CellRect(int row, int column)
        {
            int cell;
            int originX;
            int originY;
            ComputeGeometry(out cell, out originX, out originY);
            int gap = CellGap();
            return new Rectangle(originX + column * (cell + gap), originY + row * (cell + gap), cell, cell);
        }

        private Rectangle RegionRect(GridRegion region)
        {
            int cell;
            int originX;
            int originY;
            ComputeGeometry(out cell, out originX, out originY);
            int gap = CellGap();
            int colSpan = region.ColSpan < 1 ? 1 : region.ColSpan;
            int rowSpan = region.RowSpan < 1 ? 1 : region.RowSpan;
            return new Rectangle(originX + region.Col * (cell + gap), originY + region.Row * (cell + gap),
                colSpan * cell + (colSpan - 1) * gap, rowSpan * cell + (rowSpan - 1) * gap);
        }

        private bool HitCell(Point point, out int row, out int column)
        {
            row = -1;
            column = -1;
            if (_rows < 1 || _columns < 1)
            {
                return false;
            }
            int cell;
            int originX;
            int originY;
            ComputeGeometry(out cell, out originX, out originY);
            int gap = CellGap();
            int stride = cell + gap;
            if (point.X < originX || point.Y < originY || stride < 1)
            {
                return false;
            }
            int c = (point.X - originX) / stride;
            int r = (point.Y - originY) / stride;
            if (r < 0 || r >= _rows || c < 0 || c >= _columns)
            {
                return false;
            }
            if (!CellRect(r, c).Contains(point))
            {
                return false;
            }
            row = r;
            column = c;
            return true;
        }

        private void HitCellClamped(Point point, out int row, out int column)
        {
            int cell;
            int originX;
            int originY;
            ComputeGeometry(out cell, out originX, out originY);
            int gap = CellGap();
            int stride = cell + gap;
            int r = (int)Math.Floor((point.Y - originY) / (double)stride);
            int c = (int)Math.Floor((point.X - originX) / (double)stride);
            row = Math.Max(0, Math.Min(_rows - 1, r));
            column = Math.Max(0, Math.Min(_columns - 1, c));
        }

        // ------------------------------------------------------------------ 鼠标交互

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            int row;
            int column;
            if (!HitCell(e.Location, out row, out column))
            {
                return;
            }
            Focus();
            _selecting = true;
            _hasSelection = true;
            _startRow = row;
            _startColumn = column;
            _endRow = row;
            _endColumn = column;
            Capture = true;
            Invalidate();
            RaiseSelectionChanged();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hoverRow;
            int hoverColumn;
            if (HitCell(e.Location, out hoverRow, out hoverColumn))
            {
                if (hoverRow != _hoverRow || hoverColumn != _hoverColumn)
                {
                    _hoverRow = hoverRow;
                    _hoverColumn = hoverColumn;
                    Invalidate();
                }
            }
            else if (_hoverRow != -1 || _hoverColumn != -1)
            {
                _hoverRow = -1;
                _hoverColumn = -1;
                Invalidate();
            }

            if (_selecting)
            {
                int row;
                int column;
                HitCellClamped(e.Location, out row, out column);
                if (row != _endRow || column != _endColumn)
                {
                    _endRow = row;
                    _endColumn = column;
                    Invalidate();
                    RaiseSelectionChanged();
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _selecting)
            {
                _selecting = false;
                Capture = false;
                Invalidate();
                RaiseSelectionChanged();
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hoverRow != -1 || _hoverColumn != -1)
            {
                _hoverRow = -1;
                _hoverColumn = -1;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        private void RaiseSelectionChanged()
        {
            if (SelectionChanged != null)
            {
                SelectionChanged(this, EventArgs.Empty);
            }
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

            if (_rows < 1 || _columns < 1)
            {
                return;
            }

            int cell;
            int originX;
            int originY;
            ComputeGeometry(out cell, out originX, out originY);

            List<GridRegion> regions = GridLayout.BuildRegions(_rows, _columns, _merges);
            bool mergeable = IsSelectionMergeable();
            int selRow;
            int selColumn;
            int selRowSpan;
            int selColumnSpan;
            bool hasSelection = TryGetSelection(out selRow, out selColumn, out selRowSpan, out selColumnSpan);

            for (int i = 0; i < regions.Count; i++)
            {
                GridRegion region = regions[i];
                Rectangle rect = RegionRect(region);
                bool merged = region.Area > 1;
                bool hovered = !_selecting && _hoverRow >= region.Row && _hoverRow <= region.EndRow
                    && _hoverColumn >= region.Col && _hoverColumn <= region.EndCol;

                Color fill;
                Color border;
                float borderWidth;
                if (merged)
                {
                    fill = Color.FromArgb(0x30, 0x30, 0x30);
                    border = Theme.AccentGlow;
                    borderWidth = Theme.S(this, 1.4f);
                }
                else if (hovered)
                {
                    fill = Theme.ControlBg;
                    border = Theme.ControlBorder;
                    borderWidth = Theme.S(this, 1f);
                }
                else
                {
                    fill = Theme.PanelBg2;
                    border = Theme.ControlBorderSoft;
                    borderWidth = Theme.S(this, 1f);
                }
                Theme.FillRounded(g, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), Theme.S(this, 5f), fill);
                if (merged)
                {
                    using (Pen pen = new Pen(border, borderWidth))
                    {
                        pen.DashStyle = DashStyle.Dash;
                        pen.DashPattern = new float[] { 4f, 3f };
                        using (GraphicsPath path = Theme.RoundedPath(
                            new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1f, rect.Height - 1f), Theme.S(this, 5f)))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                }
                else
                {
                    Theme.DrawRounded(g, new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1f, rect.Height - 1f),
                        Theme.S(this, 5f), border, borderWidth);
                }

                int textPad = Math.Max(2, cell / 8);
                if (merged)
                {
                    if (rect.Width >= Theme.S(this, 72) && rect.Height >= Theme.S(this, 34))
                    {
                        Theme.DrawText(g, "大格 " + (i + 1), Theme.FontTiny,
                            new Rectangle(rect.Left + textPad, rect.Top + textPad, rect.Width - textPad * 2, Theme.S(this, 16)),
                            Theme.TextPrimary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                        string spanText = region.RowSpan + " × " + region.ColSpan + " · 已合并";
                        Theme.DrawText(g, spanText, Theme.FontTiny,
                            new Rectangle(rect.Left + textPad, rect.Top + textPad + Theme.S(this, 16),
                                rect.Width - textPad * 2, Theme.S(this, 16)),
                            Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                    else
                    {
                        Theme.DrawText(g, "+", Theme.FontSmall,
                            new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height),
                            Theme.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                }
                else if (cell >= Theme.S(this, 22))
                {
                    Theme.DrawText(g, (i + 1).ToString(), Theme.FontTiny,
                        new Rectangle(rect.Left + textPad, rect.Top + textPad, Math.Max(1, rect.Width - textPad * 2), Theme.S(this, 16)),
                        Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine);
                }
            }

            if (hasSelection)
            {
                int r0 = Math.Min(selRow, selRow + selRowSpan - 1);
                int c0 = selColumn;
                Rectangle first = CellRect(r0, c0);
                Rectangle last = CellRect(r0 + selRowSpan - 1, c0 + selColumnSpan - 1);
                Rectangle selectionRect = Rectangle.FromLTRB(first.Left, first.Top, last.Right, last.Bottom);
                using (SolidBrush overlay = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                {
                    g.FillRectangle(overlay, selectionRect);
                }
                Color border = mergeable || selRowSpan * selColumnSpan < 2
                    ? Theme.AccentGlow : Color.FromArgb(0xE8, 0xB4, 0x80);
                float width = Theme.S(this, 2f);
                using (Pen pen = new Pen(border, width))
                {
                    if (selRowSpan * selColumnSpan >= 2 && !mergeable)
                    {
                        pen.DashStyle = DashStyle.Dash;
                        pen.DashPattern = new float[] { 3f, 3f };
                    }
                    g.DrawRectangle(pen, selectionRect.X + 1, selectionRect.Y + 1,
                        Math.Max(1, selectionRect.Width - 2), Math.Max(1, selectionRect.Height - 2));
                }

                if (selRowSpan * selColumnSpan >= 2 && selectionRect.Width >= Theme.S(this, 52))
                {
                    string tagText = selRowSpan + " × " + selColumnSpan;
                    Size tagSize = TextRenderer.MeasureText(tagText, Theme.FontTiny);
                    Rectangle tag = new Rectangle(selectionRect.Left + (selectionRect.Width - tagSize.Width - Theme.S(this, 12)) / 2,
                        selectionRect.Bottom - Theme.S(this, 24), tagSize.Width + Theme.S(this, 12), Theme.S(this, 20));
                    if (tag.Height > 0 && tag.Top >= selectionRect.Top)
                    {
                        Theme.FillRounded(g, tag, 5f, Color.FromArgb(235, 12, 14, 18));
                        Theme.DrawText(g, tagText, Theme.FontTiny, tag, border,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                }
            }
        }
    }

    /// <summary>合并编辑器图例：单个格子 / 已合并区域 / 拖选区域。</summary>
    internal sealed class MergeLegendControl : Control
    {
        private readonly string[] _labels = new string[] { "单个格子", "已合并区域", "拖选区域" };

        public MergeLegendControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.PanelBg;
            Height = 24;
            AccessibleName = "合并编辑器图例";
            Text = "图例";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetHighQuality(g);
            using (SolidBrush bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, ClientRectangle);
            }
            int swatch = Theme.S(this, 13);
            int gap = Theme.S(this, 7);
            int itemGap = Theme.S(this, 16);
            int x = 0;
            int y = (Height - swatch) / 2;
            for (int i = 0; i < _labels.Length; i++)
            {
                Rectangle box = new Rectangle(x, y, swatch, swatch);
                if (i == 0)
                {
                    Theme.FillRounded(g, box, 3f, Theme.PanelBg2);
                    Theme.DrawRounded(g, box, 3f, Theme.ControlBorderSoft, 1f);
                }
                else if (i == 1)
                {
                    Theme.FillRounded(g, box, 3f, Color.FromArgb(0x30, 0x30, 0x30));
                    Theme.DrawRounded(g, box, 3f, Theme.AccentGlow, 1f);
                }
                else
                {
                    Theme.DrawRounded(g, box, 3f, Theme.AccentGlow, Theme.S(this, 1.6f));
                }
                Size textSize = TextRenderer.MeasureText(_labels[i], Theme.FontTiny);
                Rectangle textRect = new Rectangle(box.Right + gap, 0, textSize.Width + 2, Height);
                Theme.DrawText(g, _labels[i], Theme.FontTiny, textRect, Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                x = textRect.Right + itemGap;
                if (x > Width)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 编辑网格合并的模态弹窗（深色主题）：R×C 矩阵拖选矩形，「合并所选 / 拆分所选 / 清空所有合并 /
    /// 取消 / 确定」，实时显示容量/合并数/图片数；所有结构变更都调用 Core/GridLayout 校验。
    /// 只编辑“网格结构”，不影响图片顺序；取消不修改现有结构。
    /// </summary>
    internal sealed class GridMergeDialog : Form
    {
        private readonly int _rows;
        private readonly int _columns;
        private readonly int _imageCount;
        private List<GridRegion> _merges;

        private GridMatrixControl _matrix;
        private MergeLegendControl _legend;
        private Label _matrixCaption;
        private Label _selectionTitle;
        private Label _selectionLabel;
        private Label _warningLabel;
        private Label _statsLabel;
        private Label _helpHint;
        private Label _footerHint;
        private AccentButton _mergeButton;
        private AccentButton _splitButton;
        private AccentButton _clearButton;
        private AccentButton _cancelButton;
        private AccentButton _okButton;

        private string _actionMessage = string.Empty;
        private bool _actionWarn;
        private bool _applying;

        public GridMergeDialog(int rows, int columns, IList<GridRegion> merges, int imageCount)
        {
            Theme.InitializeDpi();
            _rows = GridLayout.ClampRows(rows);
            _columns = GridLayout.ClampColumns(columns);
            _merges = GridLayout.NormalizeMerges(_rows, _columns, merges);
            _imageCount = imageCount < 0 ? 0 : imageCount;

            Text = "编辑网格合并";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.WindowBg;
            ForeColor = Theme.TextPrimary;
            Font = Theme.FontBase;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            ClientSize = new Size(S(880), S(620));
            AccessibleName = "编辑网格合并";
            Text = "编辑网格合并";

            BuildUi();
            RefreshState();

            Shown += delegate { Theme.ApplyDarkTitleBar(this); };
        }

        /// <summary>确定后的合并区域（0 基、闭区间、已规范化）。取消时调用方忽略。</summary>
        public List<GridRegion> Merges
        {
            get
            {
                List<GridRegion> copy = new List<GridRegion>();
                for (int i = 0; i < _merges.Count; i++)
                {
                    copy.Add(_merges[i].Clone());
                }
                return copy;
            }
        }

        private int S(int value)
        {
            return Theme.S(this, value);
        }

        private float S(float value)
        {
            return Theme.S(this, value);
        }

        private void BuildUi()
        {
            Label title = MakeLabel("编辑网格合并", 24, 16, 520, 32, Theme.FontTitle, Theme.TextPrimary);
            title.AccessibleName = "编辑网格合并标题";
            Label subtitle = MakeLabel("拖选相邻的矩形区域后点「合并所选」；大格只放 1 张图，取消不修改现有结构。",
                24, 50, 640, 20, Theme.FontTiny, Theme.TextMuted);
            Controls.Add(title);
            Controls.Add(subtitle);

            _matrixCaption = MakeLabel(string.Empty, 40, 96, 468, 18, Theme.FontTiny, Theme.TextMuted);
            _matrixCaption.Text = "网格 " + _rows + " × " + _columns + " · 按住左键拖动可框选相邻格子";
            Controls.Add(_matrixCaption);

            _matrix = new GridMatrixControl();
            _matrix.Location = new Point(S(40), S(118));
            _matrix.Size = new Size(S(468), S(380));
            _matrix.BackColor = Theme.PanelBg;
            _matrix.SetGrid(_rows, _columns, _merges);
            _matrix.SelectionChanged += delegate
            {
                if (_applying)
                {
                    return;
                }
                _actionMessage = string.Empty;
                RefreshState();
            };
            Controls.Add(_matrix);

            // 右侧：操作说明 + 当前选区 + 统计 + 图例
            _helpHint = MakeLabel(
                "操作提示：\r\n" +
                "1. 按住鼠标左键，拖选相邻的矩形区域；\r\n" +
                "2. 点「合并所选」生成大格（容量会减少）；\r\n" +
                "3. 已合并的大格可选中后点「拆分所选」。",
                566, 96, 292, 92, Theme.FontTiny, Theme.TextSecondary);
            _helpHint.AutoSize = false;
            Controls.Add(_helpHint);

            _selectionTitle = MakeLabel("当前选区", 566, 200, 292, 16, Theme.FontTiny, Theme.TextMuted);
            Controls.Add(_selectionTitle);
            _selectionLabel = MakeLabel(string.Empty, 566, 218, 292, 22, Theme.FontSmall, Theme.TextPrimary);
            _selectionLabel.AutoSize = false;
            Controls.Add(_selectionLabel);
            _warningLabel = MakeLabel(string.Empty, 566, 244, 292, 44, Theme.FontTiny, Theme.TextMuted);
            _warningLabel.AutoSize = false;
            Controls.Add(_warningLabel);
            _statsLabel = MakeLabel(string.Empty, 566, 298, 292, 44, Theme.FontSmall, Theme.TextSecondary);
            _statsLabel.AutoSize = false;
            Controls.Add(_statsLabel);

            _legend = new MergeLegendControl();
            _legend.Location = new Point(S(566), S(356));
            _legend.Size = new Size(S(292), S(24));
            _legend.BackColor = Theme.PanelBg;
            Controls.Add(_legend);

            _footerHint = MakeLabel("Esc 取消 · Enter 确定", 566, 388, 292, 18, Theme.FontTiny, Theme.TextDim);
            Controls.Add(_footerHint);

            int bottomY = S(620) - S(24) - S(40);
            _mergeButton = NewDialogButton("合并所选", ButtonStyle.Secondary, 24, 556, 116, 40);
            _mergeButton.Click += delegate { ApplyMerge(); };
            _splitButton = NewDialogButton("拆分所选", ButtonStyle.Secondary, 148, 556, 116, 40);
            _splitButton.Click += delegate { ApplySplit(); };
            _clearButton = NewDialogButton("清空所有合并", ButtonStyle.Ghost, 272, 556, 132, 40);
            _clearButton.Click += delegate { ApplyClearAll(); };
            _cancelButton = NewDialogButton("取消", ButtonStyle.Secondary, 636, 556, 100, 40);
            _cancelButton.Click += delegate { CloseWithResult(DialogResult.Cancel); };
            _okButton = NewDialogButton("确定", ButtonStyle.Primary, 748, 556, 108, 40);
            _okButton.Click += delegate { CloseWithResult(DialogResult.OK); };
            Controls.Add(_mergeButton);
            Controls.Add(_splitButton);
            Controls.Add(_clearButton);
            Controls.Add(_cancelButton);
            Controls.Add(_okButton);

            // 背景卡片（在控件之下绘制，见 OnPaint）
            Paint += PaintCards;
        }

        private AccentButton NewDialogButton(string text, ButtonStyle style, int x, int y, int width, int height)
        {
            AccentButton button = new AccentButton();
            button.Text = text;
            button.AccessibleName = text;
            button.Style = style;
            button.Location = new Point(S(x), S(y));
            button.Size = new Size(S(width), S(height));
            button.BackColor = Theme.PanelBg;
            button.Font = Theme.FontButton;
            return button;
        }

        private Label MakeLabel(string text, int x, int y, int width, int height, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(S(x), S(y));
            label.Size = new Size(S(width), S(height));
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoSize = false;
            return label;
        }

        private void PaintCards(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetHighQuality(g);
            // 矩阵卡片
            RectangleF matrixCard = new RectangleF(S(24) + 0.5f, S(84) + 0.5f, S(500) - 1f, S(436) - 1f);
            Theme.FillRounded(g, matrixCard, S(12f), Theme.PanelBg);
            Theme.DrawRounded(g, matrixCard, S(12f), Theme.CardBorder, 1f);
            // 说明卡片
            RectangleF helpCard = new RectangleF(S(548) + 0.5f, S(84) + 0.5f, S(308) - 1f, S(104) - 1f);
            Theme.FillRounded(g, helpCard, S(10f), Theme.PanelBg);
            Theme.DrawRounded(g, helpCard, S(10f), Theme.CardBorder, 1f);
            // 选区/统计卡片
            RectangleF infoCard = new RectangleF(S(548) + 0.5f, S(188) + 0.5f, S(308) - 1f, S(160) - 1f);
            Theme.FillRounded(g, infoCard, S(10f), Theme.PanelBg);
            Theme.DrawRounded(g, infoCard, S(10f), Theme.CardBorder, 1f);
            // 底部按钮区上方的分隔线
            using (Pen pen = new Pen(Theme.CardBorder, 1f))
            {
                g.DrawLine(pen, S(24), S(540), Width - S(24), S(540));
            }
        }

        private void CloseWithResult(DialogResult result)
        {
            if (_applying)
            {
                return;
            }
            DialogResult = result;
            Close();
        }

        // ------------------------------------------------------------------ 状态刷新

        private void RefreshState()
        {
            int capacity = GridLayout.Capacity(_rows, _columns, _merges);
            int row;
            int column;
            int rowSpan;
            int columnSpan;
            bool hasSelection = _matrix.TryGetSelection(out row, out column, out rowSpan, out columnSpan);
            bool mergeable = hasSelection && _matrix.IsSelectionMergeable();
            int selectionArea = hasSelection ? rowSpan * columnSpan : 0;

            if (!hasSelection)
            {
                _selectionLabel.Text = "未选择（拖选相邻格子）";
                _selectionLabel.ForeColor = Theme.TextMuted;
            }
            else if (rowSpan == 1 && columnSpan == 1)
            {
                _selectionLabel.Text = "第 " + (row + 1) + " 行 · 第 " + (column + 1) + " 列（单个格子）";
                _selectionLabel.ForeColor = Theme.TextPrimary;
            }
            else
            {
                string text = "第 " + (row + 1) + (rowSpan > 1 ? "–" + (row + rowSpan) : "") + " 行 · 第 "
                    + (column + 1) + (columnSpan > 1 ? "–" + (column + columnSpan) : "") + " 列";
                string size = rowSpan + " × " + columnSpan;
                _selectionLabel.Text = text + "（" + size + "）";
                _selectionLabel.ForeColor = Theme.TextPrimary;
            }

            if (!string.IsNullOrEmpty(_actionMessage))
            {
                _warningLabel.Text = _actionMessage;
                _warningLabel.ForeColor = _actionWarn ? Color.FromArgb(0xE8, 0xB4, 0x80) : Theme.AccentGlow;
            }
            else if (!hasSelection)
            {
                _warningLabel.Text = "提示：拖选至少 2 个相邻格子才能合并；大格或单个格子可用「拆分所选」。";
                _warningLabel.ForeColor = Theme.TextMuted;
            }
            else if (selectionArea < 2)
            {
                _warningLabel.Text = "当前只选中了 1 个格子；请拖选至少 2 个相邻格子再合并。";
                _warningLabel.ForeColor = Theme.TextMuted;
            }
            else if (!mergeable)
            {
                _warningLabel.Text = "选区与已有合并区域部分重叠，无法合并；可先拆分重叠的大格。";
                _warningLabel.ForeColor = Color.FromArgb(0xE8, 0xB4, 0x80);
            }
            else
            {
                List<GridRegion> merged;
                GridLayout.TryMerge(_rows, _columns, _merges, row, column, rowSpan, columnSpan, out merged);
                int afterCapacity = GridLayout.Capacity(_rows, _columns, merged);
                string text = "合并后容量：" + capacity + " → " + afterCapacity + " 格（减少 " + (capacity - afterCapacity) + " 格）";
                if (_imageCount > afterCapacity)
                {
                    text += "；将有 " + (_imageCount - afterCapacity) + " 张图片未放入";
                }
                _warningLabel.Text = text;
                _warningLabel.ForeColor = Theme.AccentGlow;
            }

            string stats = "容量 " + capacity + " 格 · 合并 " + _merges.Count + " 处";
            string images = "图片 " + _imageCount + " 张";
            if (_imageCount > capacity)
            {
                images += "（" + (_imageCount - capacity) + " 张超出容量未放入）";
            }
            _statsLabel.Text = stats + "\r\n" + images;

            _mergeButton.Enabled = mergeable;
            _clearButton.Enabled = _merges.Count > 0;

            bool canSplit = false;
            if (hasSelection)
            {
                int before = GridLayout.NormalizeMerges(_rows, _columns, _merges).Count;
                List<GridRegion> kept = GridLayout.RemoveMergesInside(_rows, _columns, _merges, row, column, rowSpan, columnSpan);
                if (GridLayout.NormalizeMerges(_rows, _columns, kept).Count < before)
                {
                    canSplit = true;
                }
                else if (rowSpan == 1 && columnSpan == 1)
                {
                    List<GridRegion> after = GridLayout.RemoveMergeAt(_rows, _columns, _merges, row, column);
                    if (GridLayout.NormalizeMerges(_rows, _columns, after).Count < before)
                    {
                        canSplit = true;
                    }
                }
            }
            _splitButton.Enabled = canSplit;

            _matrixCaption.Text = "网格 " + _rows + " × " + _columns + " · 按住左键拖动可框选相邻格子 · 容量 " + capacity + " 格";
        }

        // ------------------------------------------------------------------ 操作

        private void ApplyMerge()
        {
            int row;
            int column;
            int rowSpan;
            int columnSpan;
            if (!_matrix.TryGetSelection(out row, out column, out rowSpan, out columnSpan))
            {
                return;
            }
            List<GridRegion> merged;
            if (!GridLayout.TryMerge(_rows, _columns, _merges, row, column, rowSpan, columnSpan, out merged))
            {
                _actionMessage = "合并失败：选区与已有合并区域部分重叠或超出网格范围。";
                _actionWarn = true;
                RefreshState();
                return;
            }
            int before = _merges.Count;
            _merges = merged;
            _applying = true;
            _matrix.SetMerges(_merges);
            _matrix.ClearSelection();
            _applying = false;
            int added = _merges.Count - before;
            if (added <= 0)
            {
                _actionMessage = "已合并所选区域（合并数 " + _merges.Count + " 处）。";
            }
            else
            {
                _actionMessage = "已合并所选区域（新增 1 处，共 " + _merges.Count + " 处合并）。";
            }
            _actionWarn = false;
            RefreshState();
        }

        private void ApplySplit()
        {
            int row;
            int column;
            int rowSpan;
            int columnSpan;
            if (!_matrix.TryGetSelection(out row, out column, out rowSpan, out columnSpan))
            {
                return;
            }
            int before = GridLayout.NormalizeMerges(_rows, _columns, _merges).Count;
            List<GridRegion> kept = GridLayout.RemoveMergesInside(_rows, _columns, _merges, row, column, rowSpan, columnSpan);
            int after = GridLayout.NormalizeMerges(_rows, _columns, kept).Count;
            if (after == before && rowSpan == 1 && columnSpan == 1)
            {
                kept = GridLayout.RemoveMergeAt(_rows, _columns, _merges, row, column);
                after = GridLayout.NormalizeMerges(_rows, _columns, kept).Count;
            }
            if (after >= before)
            {
                _actionMessage = "选区内没有可拆分的合并区域；请选中大格（或大格中的任意格子）再拆分。";
                _actionWarn = true;
                RefreshState();
                return;
            }
            _merges = kept;
            _applying = true;
            _matrix.SetMerges(_merges);
            _matrix.ClearSelection();
            _applying = false;
            _actionMessage = "已拆分 " + (before - after) + " 处合并区域（剩余 " + _merges.Count + " 处）。";
            _actionWarn = false;
            RefreshState();
        }

        private void ApplyClearAll()
        {
            if (_merges.Count == 0)
            {
                return;
            }
            int removed = _merges.Count;
            _merges = new List<GridRegion>();
            _applying = true;
            _matrix.SetMerges(_merges);
            _matrix.ClearSelection();
            _applying = false;
            _actionMessage = "已清空 " + removed + " 处合并，恢复为 " + (_rows * _columns)
                + " 个单格（容量 " + (_rows * _columns) + " 格）。";
            _actionWarn = false;
            RefreshState();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                CloseWithResult(DialogResult.Cancel);
                return true;
            }
            if (keyData == Keys.Enter && !(ActiveControl is AccentButton))
            {
                CloseWithResult(DialogResult.OK);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
