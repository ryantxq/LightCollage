using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LightCollage.Common;
using LightCollage.Core;
using LightCollage.Shell;
using CoreLayoutEngine = LightCollage.Core.LayoutEngine;

namespace LightCollage.App
{
    internal sealed class MainForm : Form
    {
        private const int MaxItems = 200;

        private readonly IncomingFiles _incoming;
        private readonly AppSettings _appSettings;
        private readonly List<EditorItem> _items = new List<EditorItem>();
        private readonly Queue<EditorItem> _loadQueue = new Queue<EditorItem>();
        private readonly List<string> _batchErrors = new List<string>();
        private readonly Timer _previewTimer;
        private readonly Timer _incomingTimer;
        private readonly Timer _settingsTimer;

        private PreviewPanel _preview;
        private ThumbnailStrip _strip;
        private CardPanel _sidePanel;
        private Label _sideTitle;
        private Label _sideSubtitle;
        private AccentButton _helpButton;
        private Label _orientationSection;
        private SegmentedControl _orientationSeg;
        private Label _spacingSection;
        private Label _gapLabel;
        private Label _marginLabel;
        private Stepper _gapStep;
        private Stepper _marginStep;
        private Label _gridSection;
        private Label _rowCountLabel;
        private Label _columnCountLabel;
        private Label _rowGapLabel;
        private Label _columnGapLabel;
        private Label _gridMarginLabel;
        private Stepper _rowsStep;
        private Stepper _columnsStep;
        private Stepper _rowGapStep;
        private Stepper _columnGapStep;
        private AccentButton _mergeCellsButton;
        private CheckBoxX _gridGuidesCheck;
        private Label _backgroundSection;
        private ColorSwatchButton _swatchTransparent;
        private ColorSwatchButton _swatchWhite;
        private ColorSwatchButton _swatchBlack;
        private ColorSwatchButton _swatchCustom;
        private Label _alphaTitle;
        private Label _alphaValue;
        private AlphaSlider _alphaSlider;
        private Label _sizeSection;
        private SegmentedControl _sizeModeSeg;
        private Stepper _sizeStep;
        private Label _sizeHint;
        private Label _outputSection;
        private SegmentedControl _formatSeg;
        private Label _formatNote;
        private AccentButton _saveButton;
        private AccentButton _saveAsButton;
        private Label _countLabel;
        private Label _hintLabel;
        private AccentButton _sortButton;
        private AccentButton _clearButton;
        private Label _statusLabel;
        private AccentButton _openFolderButton;
        private ToolTip _toolTip;

        private PreviewComposition _composition;
        private int _selectedIndex = -1;
        private int _loadTotal;
        private int _loadDone;
        private int _queuedLoads;
        private bool _loadingCurrent;
        private string[] _initialFiles;
        private bool _updatingUi;
        private bool _dirty;
        private bool _saving;
        private bool _closing;
        private bool _settingsDirty;
        private string _lastSavedPath;
        private int _previewVersion;

        public MainForm(IncomingFiles incoming, string[] initialFiles, AppSettings settings)
        {
            Theme.InitializeDpi();
            _incoming = incoming ?? new IncomingFiles();
            _initialFiles = initialFiles;
            _appSettings = settings ?? new AppSettings();
            _selectedIndex = -1;

            Text = "轻拼 - 图片拼接";
            BackColor = Theme.WindowBg;
            ForeColor = Theme.TextPrimary;
            Font = Theme.FontBase;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            MinimumSize = new Size(Theme.S(this, 1020), Theme.S(this, 724));
            StartPosition = FormStartPosition.Manual;
            RestoreWindowSettings();

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            BuildUi();
            ApplySettingsToUi();
            LayoutUi();

            _previewTimer = new Timer();
            _previewTimer.Interval = 90;
            _previewTimer.Tick += PreviewTimerTick;

            _incomingTimer = new Timer();
            _incomingTimer.Interval = 140;
            _incomingTimer.Tick += IncomingTimerTick;

            _settingsTimer = new Timer();
            _settingsTimer.Interval = 500;
            _settingsTimer.Tick += SettingsTimerTick;

            Shown += MainFormShown;
            FormClosing += MainFormClosing;
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    return;
                }
                LayoutUi();
                SchedulePreview();
            };
            DragEnter += FormDragEnter;
            DragOver += FormDragOver;
            DragLeave += FormDragLeave;
            DragDrop += FormDragDrop;
            AllowDrop = true;
            AttachDropTarget(_preview);
            AttachDropTarget(_strip);
            AttachDropTarget(_sidePanel);
        }

        private void AttachDropTarget(Control control)
        {
            if (control == null)
            {
                return;
            }
            control.AllowDrop = true;
            control.DragEnter += FormDragEnter;
            control.DragOver += FormDragOver;
            control.DragLeave += FormDragLeave;
            control.DragDrop += FormDragDrop;
        }

        private void BuildUi()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            _preview = new PreviewPanel();
            _preview.AddRequested += delegate { ShowAddDialog(); };
            _preview.SwapRequested += SwapItems;
            _preview.DragStatusChanged += delegate(string text, bool warn) { SetStatus(text, warn); };
            Controls.Add(_preview);

            _strip = new ThumbnailStrip();
            _strip.AddRequested += delegate { ShowAddDialog(); };
            _strip.SelectionChanged += delegate
            {
                _selectedIndex = _strip.SelectedIndex;
            };
            _strip.RemoveRequested += RemoveItemAt;
            _strip.ReorderRequested += ReorderItem;
            Controls.Add(_strip);

            _sidePanel = new CardPanel();
            _sidePanel.Radius = Theme.S(this, 14);
            _sidePanel.Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_sidePanel);

            _sideTitle = NewLabel("拼接设置", Theme.FontTitle, Theme.TextPrimary);
            _sideSubtitle = NewLabel("参数修改会实时刷新预览", Theme.FontTiny, Theme.TextMuted);
            _helpButton = NewButton("?", ButtonStyle.Ghost, 28, 28);
            _helpButton.Font = Theme.FontBase;
            _helpButton.Click += delegate { ShowAbout(); };
            _sidePanel.Controls.Add(_sideTitle);
            _sidePanel.Controls.Add(_sideSubtitle);
            _sidePanel.Controls.Add(_helpButton);

            _orientationSection = NewSection("拼接方式");
            _sidePanel.Controls.Add(_orientationSection);
            _orientationSeg = new SegmentedControl();
            _orientationSeg.Items = new string[] { "横向排列", "纵向排列", "网格" };
            _orientationSeg.AccessibleName = "拼接方式";
            _orientationSeg.Text = "拼接方式选项（横向排列 / 纵向排列 / 网格）";
            _orientationSeg.SelectedIndexChanged += OrientationChanged;
            _sidePanel.Controls.Add(_orientationSeg);

            _spacingSection = NewSection("间距与边距");
            _sidePanel.Controls.Add(_spacingSection);
            _gapLabel = NewControlLabel("图片间距 (px)");
            _marginLabel = NewControlLabel("外边距 (px)");
            _sidePanel.Controls.Add(_gapLabel);
            _sidePanel.Controls.Add(_marginLabel);
            _gapStep = NewStepper(0, 200, 1);
            _marginStep = NewStepper(0, 200, 1);
            _gapStep.AccessibleName = "图片间距";
            _gapStep.Text = "图片间距调节";
            _marginStep.AccessibleName = "外边距";
            _marginStep.Text = "外边距调节";
            _gapStep.ValueChanged += GapChanged;
            _marginStep.ValueChanged += MarginValueChanged;
            _sidePanel.Controls.Add(_gapStep);
            _sidePanel.Controls.Add(_marginStep);

            // ---- v1.1 网格设置区（仅「网格」拼接方式下显示；外边距沿用上面的控件，在网格模式下重排进本区）
            _gridSection = NewSection("网格");
            _gridSection.AccessibleName = "网格设置";
            _sidePanel.Controls.Add(_gridSection);
            _rowCountLabel = NewGridParamLabel("行数");
            _columnCountLabel = NewGridParamLabel("列数");
            _rowGapLabel = NewGridParamLabel("行间距");
            _columnGapLabel = NewGridParamLabel("列间距");
            _rowCountLabel.AccessibleName = "行数标签";
            _columnCountLabel.AccessibleName = "列数标签";
            _rowGapLabel.AccessibleName = "行间距标签";
            _columnGapLabel.AccessibleName = "列间距标签";
            _sidePanel.Controls.Add(_rowCountLabel);
            _sidePanel.Controls.Add(_columnCountLabel);
            _sidePanel.Controls.Add(_rowGapLabel);
            _sidePanel.Controls.Add(_columnGapLabel);
            // 外边距并入网格参数组：网格模式下使用与行/列参数一致的小标题样式（横/纵模式仍用原标签）
            _gridMarginLabel = NewGridParamLabel("外边距");
            _gridMarginLabel.AccessibleName = "外边距标签";
            _sidePanel.Controls.Add(_gridMarginLabel);
            _rowsStep = NewStepper(GridLayout.MinRowsOrColumns, GridLayout.MaxRowsOrColumns, 1);
            _columnsStep = NewStepper(GridLayout.MinRowsOrColumns, GridLayout.MaxRowsOrColumns, 1);
            _rowGapStep = NewStepper(0, GridLayout.MaxGapOrMargin, 1);
            _columnGapStep = NewStepper(0, GridLayout.MaxGapOrMargin, 1);
            _rowsStep.AccessibleName = "行数";
            _rowsStep.Text = "网格行数调节";
            _columnsStep.AccessibleName = "列数";
            _columnsStep.Text = "网格列数调节";
            _rowGapStep.AccessibleName = "行间距";
            _rowGapStep.Text = "行间距调节";
            _columnGapStep.AccessibleName = "列间距";
            _columnGapStep.Text = "列间距调节";
            _rowsStep.ValueChanged += GridRowsChanged;
            _columnsStep.ValueChanged += GridColumnsChanged;
            _rowGapStep.ValueChanged += RowGapChanged;
            _columnGapStep.ValueChanged += ColumnGapChanged;
            _sidePanel.Controls.Add(_rowsStep);
            _sidePanel.Controls.Add(_columnsStep);
            _sidePanel.Controls.Add(_rowGapStep);
            _sidePanel.Controls.Add(_columnGapStep);
            _mergeCellsButton = new AccentButton();
            _mergeCellsButton.Text = "合并单元格…";
            _mergeCellsButton.AccessibleName = "合并单元格";
            _mergeCellsButton.Style = ButtonStyle.Secondary;
            _mergeCellsButton.Font = Theme.FontSmall;
            _mergeCellsButton.Height = Theme.S(this, 30);
            _mergeCellsButton.BackColor = Theme.PanelBg;
            _mergeCellsButton.Click += delegate { OpenGridMergeDialog(); };
            _sidePanel.Controls.Add(_mergeCellsButton);
            _gridGuidesCheck = new CheckBoxX();
            _gridGuidesCheck.Text = "显示网格辅助线";
            _gridGuidesCheck.AccessibleName = "显示网格辅助线";
            _gridGuidesCheck.BackColor = Theme.PanelBg;
            _gridGuidesCheck.CheckedChanged += GridGuidesChanged;
            _sidePanel.Controls.Add(_gridGuidesCheck);
            SetGridControlsVisible(false);

            _backgroundSection = NewSection("背景");
            _sidePanel.Controls.Add(_backgroundSection);
            _swatchTransparent = NewSwatch("透明", Color.Transparent, true);
            _swatchWhite = NewSwatch("白色", Theme.WhiteFlat, false);
            _swatchBlack = NewSwatch("黑色", Color.FromArgb(15, 15, 15), false);
            _swatchCustom = NewSwatch("自定义", Color.White, false);
            _swatchTransparent.AccessibleName = "背景透明";
            _swatchTransparent.Text = "背景透明选项";
            _swatchWhite.AccessibleName = "背景白色";
            _swatchWhite.Text = "背景白色选项";
            _swatchBlack.AccessibleName = "背景黑色";
            _swatchBlack.Text = "背景黑色选项";
            _swatchCustom.AccessibleName = "背景自定义";
            _swatchCustom.Text = "背景自定义选项";
            _swatchTransparent.Click += delegate { SetPresetBackground(Color.Transparent); };
            _swatchWhite.Click += delegate { SetPresetBackground(Theme.WhiteFlat); };
            _swatchBlack.Click += delegate { SetPresetBackground(Color.FromArgb(15, 15, 15)); };
            _swatchCustom.Click += delegate { PickCustomColor(); };
            _sidePanel.Controls.Add(_swatchTransparent);
            _sidePanel.Controls.Add(_swatchWhite);
            _sidePanel.Controls.Add(_swatchBlack);
            _sidePanel.Controls.Add(_swatchCustom);

            _alphaTitle = NewLabel("透明度", Theme.FontTiny, Theme.TextMuted);
            _alphaTitle.AutoSize = false;
            _alphaTitle.TextAlign = ContentAlignment.MiddleLeft;
            _alphaValue = NewLabel("0%", Theme.FontTiny, Theme.TextSecondary);
            _alphaValue.AutoSize = false;
            _alphaValue.TextAlign = ContentAlignment.MiddleRight;
            _sidePanel.Controls.Add(_alphaTitle);
            _sidePanel.Controls.Add(_alphaValue);

            _alphaSlider = new AlphaSlider();
            _alphaSlider.AccessibleName = "背景透明度";
            _alphaSlider.Text = "背景透明度滑块";
            _alphaSlider.ValueChanged += AlphaChanged;
            _sidePanel.Controls.Add(_alphaSlider);

            _sizeSection = NewSection("输出尺寸");
            _sidePanel.Controls.Add(_sizeSection);
            _sizeModeSeg = new SegmentedControl();
            _sizeModeSeg.Items = new string[] { "自动", "指定宽度", "指定高度" };
            _sizeModeSeg.AccessibleName = "输出尺寸模式";
            _sizeModeSeg.Text = "输出尺寸模式选项";
            _sizeModeSeg.SelectedIndexChanged += SizeModeChanged;
            _sidePanel.Controls.Add(_sizeModeSeg);
            _sizeHint = NewLabel("自动：取图片中间尺寸", Theme.FontTiny, Theme.TextMuted);
            _sizeHint.AutoSize = false;
            _sizeHint.TextAlign = ContentAlignment.MiddleLeft;
            _sidePanel.Controls.Add(_sizeHint);
            _sizeStep = NewStepper(1, 32000, 10);
            _sizeStep.AccessibleName = "输出尺寸数值";
            _sizeStep.Text = "输出尺寸输入";
            _sizeStep.ValueChanged += OutputSizeChanged;
            _sidePanel.Controls.Add(_sizeStep);

            _toolTip = new ToolTip();
            _toolTip.SetToolTip(_gapStep, "可直接用键盘输入间距；0–200 px。");
            _toolTip.SetToolTip(_marginStep, "可直接用键盘输入外边距；0–200 px。");
            _toolTip.SetToolTip(_sizeModeSeg,
                "自动：取所有图片高度/宽度的中位数作为拼接基准，避免个别超大或超小图片把整体尺寸带偏。\n" +
                "指定宽度/高度：只设置最终输出的一项，另一项按拼图比例自动计算。");
            _toolTip.SetToolTip(_sizeStep, "点击数字区域可直接键盘输入，回车确认。");
            _toolTip.SetToolTip(_alphaSlider, "背景透明度；0% 为完全透明，100% 为不透明。");
            _toolTip.SetToolTip(_swatchTransparent, "透明背景：只输出图片本身和透明间隙。");
            _toolTip.SetToolTip(_rowsStep, "网格行数：1–20 行，默认 3；图片按行优先顺序填入。");
            _toolTip.SetToolTip(_columnsStep, "网格列数：1–20 列，默认 3。");
            _toolTip.SetToolTip(_rowGapStep, "网格行间距：0–200 px，默认 12；0 时上下相邻格子无缝相接。");
            _toolTip.SetToolTip(_columnGapStep, "网格列间距：0–200 px，默认 12；0 时左右相邻格子无缝相接。");
            _toolTip.SetToolTip(_mergeCellsButton,
                "打开合并编辑器：拖选相邻的矩形区域合并成大格。\n大格只放 1 张图，容量会相应减少；可拆分或清空所有合并。");
            _toolTip.SetToolTip(_gridGuidesCheck, "在预览中叠加淡淡的单元格/合并区域边界线；仅预览，不写入输出图片。");

            _outputSection = NewSection("输出格式");
            _sidePanel.Controls.Add(_outputSection);
            _formatSeg = new SegmentedControl();
            _formatSeg.Items = new string[] { "PNG（透明）", "JPG" };
            _formatSeg.AccessibleName = "输出格式选择";
            _formatSeg.Text = "输出格式选项";
            _formatSeg.SelectedIndexChanged += FormatChanged;
            _sidePanel.Controls.Add(_formatSeg);
            _formatNote = NewLabel("PNG 完整保留每张图的透明通道", Theme.FontTiny, Theme.TextMuted);
            _formatNote.AutoSize = false;
            _sidePanel.Controls.Add(_formatNote);

            _saveButton = NewButton("保存图片  Ctrl+S", ButtonStyle.Primary, 248, 40);
            _saveButton.Click += delegate { SaveDefault(); };
            _sidePanel.Controls.Add(_saveButton);
            _saveAsButton = NewButton("另存为…  Ctrl+Shift+S", ButtonStyle.Secondary, 248, 32);
            _saveAsButton.Font = Theme.FontSmall;
            _saveAsButton.Click += delegate { SaveAs(); };
            _sidePanel.Controls.Add(_saveAsButton);

            _countLabel = NewLabel("共 0 张图片", Theme.FontSmall, Theme.TextSecondary);
            Controls.Add(_countLabel);
            _hintLabel = NewLabel("拖拽缩略图排序 · Delete 删除 · Ctrl+O 添加", Theme.FontTiny, Theme.TextDim);
            Controls.Add(_hintLabel);
            _sortButton = NewButton("按名称排序", ButtonStyle.Secondary, 90, 28);
            _sortButton.Font = Theme.FontTiny;
            _sortButton.Click += delegate { SortByFileName(); };
            Controls.Add(_sortButton);
            _clearButton = NewButton("清空", ButtonStyle.Secondary, 58, 28);
            _clearButton.Font = Theme.FontTiny;
            _clearButton.Click += delegate { ClearAll(); };
            Controls.Add(_clearButton);

            _statusLabel = NewLabel("就绪", Theme.FontTiny, Theme.TextMuted);
            Controls.Add(_statusLabel);
            _openFolderButton = NewButton("打开文件夹", ButtonStyle.Ghost, 90, 26);
            _openFolderButton.Font = Theme.FontTiny;
            _openFolderButton.Visible = false;
            _openFolderButton.Click += delegate { OpenLastFolder(); };
            Controls.Add(_openFolderButton);

            UpdateChrome();
        }

        private Label NewLabel(string text, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoSize = true;
            return label;
        }

        private Label NewControlLabel(string text)
        {
            Label label = NewLabel(text, Theme.FontBase, Theme.TextSecondary);
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private Label NewSection(string text)
        {
            Label label = NewLabel(text, Theme.FontSection, Theme.TextMuted);
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private AccentButton NewButton(string text, ButtonStyle style, int width, int height)
        {
            AccentButton button = new AccentButton();
            button.Text = text;
            button.Style = style;
            button.Size = new Size(Theme.S(this, width), Theme.S(this, height));
            button.BackColor = Theme.PanelBg;
            return button;
        }

        private Stepper NewStepper(int min, int max, int step)
        {
            Stepper stepper = new Stepper();
            stepper.Minimum = min;
            stepper.Maximum = max;
            stepper.Step = step;
            stepper.Suffix = "px";
            stepper.BackColor = Theme.PanelBg;
            return stepper;
        }

        private ColorSwatchButton NewSwatch(string caption, Color color, bool checker)
        {
            ColorSwatchButton swatch = new ColorSwatchButton();
            swatch.Caption = caption;
            swatch.SwatchColor = color;
            swatch.ShowChecker = checker;
            swatch.BackColor = Theme.PanelBg;
            return swatch;
        }

        /// <summary>网格设置区的参数小标题（行数/列数/行间距/列间距，放在步进器上方）。</summary>
        private Label NewGridParamLabel(string text)
        {
            Label label = NewLabel(text, Theme.FontTiny, Theme.TextMuted);
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.BottomLeft;
            return label;
        }

        private void SetGridControlsVisible(bool visible)
        {
            if (_gridSection == null)
            {
                return;
            }
            _gridSection.Visible = visible;
            _rowCountLabel.Visible = visible;
            _columnCountLabel.Visible = visible;
            _rowGapLabel.Visible = visible;
            _columnGapLabel.Visible = visible;
            _gridMarginLabel.Visible = visible;
            _rowsStep.Visible = visible;
            _columnsStep.Visible = visible;
            _rowGapStep.Visible = visible;
            _columnGapStep.Visible = visible;
            _mergeCellsButton.Visible = visible;
            _gridGuidesCheck.Visible = visible;
        }

        private void LayoutUi()
        {
            if (_preview == null)
            {
                return;
            }
            int pad = Theme.S(this, 16);
            int panelW = Theme.S(this, 292);
            int panelX = ClientSize.Width - pad - panelW;
            int leftW = Math.Max(Theme.S(this, 420), panelX - pad - Theme.S(this, 12));
            int toolbarH = Theme.S(this, 28);
            int stripH = Theme.S(this, 104);
            int statusH = Theme.S(this, 26);
            int previewH = ClientSize.Height - pad * 2 - toolbarH - stripH - statusH - Theme.S(this, 20);
            if (previewH < Theme.S(this, 260))
            {
                previewH = Theme.S(this, 260);
            }

            _preview.Bounds = new Rectangle(pad, pad, leftW, previewH);
            int toolbarY = pad + previewH + Theme.S(this, 8);
            _countLabel.Location = new Point(pad, toolbarY + Theme.S(this, 5));
            _hintLabel.Location = new Point(pad + Theme.S(this, 142), toolbarY + Theme.S(this, 7));
            _sortButton.Location = new Point(pad + leftW - _sortButton.Width - _clearButton.Width - Theme.S(this, 8), toolbarY);
            _clearButton.Location = new Point(pad + leftW - _clearButton.Width, toolbarY);
            int stripY = toolbarY + toolbarH + Theme.S(this, 2);
            _strip.Bounds = new Rectangle(pad, stripY, leftW, stripH);
            _statusLabel.Location = new Point(pad + Theme.S(this, 2), stripY + stripH + Theme.S(this, 4));
            _openFolderButton.Location = new Point(pad + leftW - _openFolderButton.Width, stripY + stripH + Theme.S(this, 1));

            _sidePanel.Bounds = new Rectangle(panelX, pad, panelW, ClientSize.Height - pad * 2);
            LayoutSidePanel();
        }

        /// <summary>侧栏布局分派：横向/纵向沿用原有绝对定位布局；网格模式按网格区重新分段定位（并把外边距并入网格区）。</summary>
        private void LayoutSidePanel()
        {
            if (_sidePanel == null || _sidePanel.ClientSize.Width <= 0)
            {
                return;
            }
            if (IsGridMode())
            {
                LayoutSidePanelGrid();
                return;
            }
            LayoutSidePanelLinear();
        }

        /// <summary>横向 / 纵向模式侧栏布局（保持 v1.0.0 原有坐标与观感，不做改动）。</summary>
        private void LayoutSidePanelLinear()
        {
            _formatNote.Visible = true;
            int pad = Theme.S(this, 16);
            int innerW = _sidePanel.ClientSize.Width - pad * 2;
            int y = Theme.S(this, 14);
            _sideTitle.Location = new Point(pad, y);
            _sideTitle.Size = new Size(innerW - Theme.S(this, 34), Theme.S(this, 30));
            _helpButton.Location = new Point(_sidePanel.ClientSize.Width - pad - _helpButton.Width, y);
            _sideSubtitle.Location = new Point(pad, y + Theme.S(this, 30));

            int top = y + Theme.S(this, 52);
            _orientationSection.Location = new Point(pad, top);
            _orientationSection.Size = new Size(innerW, Theme.S(this, 20));
            _orientationSeg.Location = new Point(pad, top + Theme.S(this, 22));
            _orientationSeg.Size = new Size(innerW, Theme.S(this, 34));

            top += Theme.S(this, 64);
            _spacingSection.Location = new Point(pad, top);
            _spacingSection.Size = new Size(innerW, Theme.S(this, 20));
            int row1 = top + Theme.S(this, 24);
            int row2 = row1 + Theme.S(this, 36);
            _gapLabel.Location = new Point(pad, row1 + Theme.S(this, 5));
            _gapLabel.Size = new Size(Theme.S(this, 110), Theme.S(this, 24));
            _gapStep.Location = new Point(pad + innerW - Theme.S(this, 124), row1);
            _gapStep.Size = new Size(Theme.S(this, 124), Theme.S(this, 34));
            _marginLabel.Location = new Point(pad, row2 + Theme.S(this, 5));
            _marginLabel.Size = new Size(Theme.S(this, 110), Theme.S(this, 24));
            _marginStep.Location = new Point(pad + innerW - Theme.S(this, 124), row2);
            _marginStep.Size = new Size(Theme.S(this, 124), Theme.S(this, 34));

            top += Theme.S(this, 96);
            _backgroundSection.Location = new Point(pad, top);
            _backgroundSection.Size = new Size(innerW, Theme.S(this, 20));
            int swY = top + Theme.S(this, 22);
            int swW = Theme.S(this, 58);
            int swGap = Theme.S(this, 7);
            _swatchTransparent.Location = new Point(pad, swY);
            _swatchWhite.Location = new Point(pad + swW + swGap, swY);
            _swatchBlack.Location = new Point(pad + (swW + swGap) * 2, swY);
            _swatchCustom.Location = new Point(pad + (swW + swGap) * 3, swY);
            _swatchTransparent.Size = new Size(swW, Theme.S(this, 52));
            _swatchWhite.Size = new Size(swW, Theme.S(this, 52));
            _swatchBlack.Size = new Size(swW, Theme.S(this, 52));
            _swatchCustom.Size = new Size(swW, Theme.S(this, 52));
            _alphaTitle.Location = new Point(pad, swY + Theme.S(this, 56));
            _alphaTitle.Size = new Size(Theme.S(this, 100), Theme.S(this, 18));
            _alphaValue.Location = new Point(pad + innerW - Theme.S(this, 80), swY + Theme.S(this, 56));
            _alphaValue.Size = new Size(Theme.S(this, 80), Theme.S(this, 18));
            _alphaSlider.Location = new Point(pad, swY + Theme.S(this, 78));
            _alphaSlider.Size = new Size(innerW, Theme.S(this, 24));

            top += Theme.S(this, 122);
            _sizeSection.Location = new Point(pad, top);
            _sizeSection.Size = new Size(innerW, Theme.S(this, 20));
            _sizeModeSeg.Location = new Point(pad, top + Theme.S(this, 22));
            _sizeModeSeg.Size = new Size(innerW, Theme.S(this, 32));
            int sizeRow = top + Theme.S(this, 60);
            _sizeHint.Location = new Point(pad, sizeRow);
            _sizeHint.Size = new Size(innerW - Theme.S(this, 132), Theme.S(this, 34));
            _sizeStep.Location = new Point(pad + innerW - Theme.S(this, 124), sizeRow);
            _sizeStep.Size = new Size(Theme.S(this, 124), Theme.S(this, 34));

            top += Theme.S(this, 98);
            _outputSection.Location = new Point(pad, top);
            _outputSection.Size = new Size(innerW, Theme.S(this, 20));
            _formatSeg.Location = new Point(pad, top + Theme.S(this, 22));
            _formatSeg.Size = new Size(innerW, Theme.S(this, 32));
            _formatNote.Location = new Point(pad, top + Theme.S(this, 60));
            _formatNote.Size = new Size(innerW, Theme.S(this, 32));

            int bottom = _sidePanel.ClientSize.Height - Theme.S(this, 16);
            _saveAsButton.Location = new Point(pad, bottom - _saveAsButton.Height);
            _saveAsButton.Size = new Size(innerW, _saveAsButton.Height);
            _saveButton.Location = new Point(pad, _saveAsButton.Top - Theme.S(this, 44));
            _saveButton.Size = new Size(innerW, Theme.S(this, 38));

            _sideTitle.Font = Theme.FontTitle;
        }

        private bool IsGridMode()
        {
            return _appSettings != null && _appSettings.Collage.Orientation == CollageOrientation.Grid;
        }

        /// <summary>
        /// 网格模式侧栏布局：现有「间距与边距」区替换为「网格」区（行数/列数/行间距/列间距/外边距/合并/辅助线），
        /// 其它分区整体上移。绝对定位没有回流，所以每次切换模式/窗口尺寸变化都重新计算；
        /// 内容与底部保存按钮冲突时自动改用紧凑密度（必要时隐藏格式说明行）。
        /// </summary>
        private void LayoutSidePanelGrid()
        {
            int pad = Theme.S(this, 16);
            int innerW = _sidePanel.ClientSize.Width - pad * 2;
            int y = Theme.S(this, 14);
            _sideTitle.Location = new Point(pad, y);
            _sideTitle.Size = new Size(innerW - Theme.S(this, 34), Theme.S(this, 30));
            _helpButton.Location = new Point(_sidePanel.ClientSize.Width - pad - _helpButton.Width, y);
            _sideSubtitle.Location = new Point(pad, y + Theme.S(this, 30));

            int top = y + Theme.S(this, 52);
            _orientationSection.Location = new Point(pad, top);
            _orientationSection.Size = new Size(innerW, Theme.S(this, 20));
            _orientationSeg.Location = new Point(pad, top + Theme.S(this, 22));
            _orientationSeg.Size = new Size(innerW, Theme.S(this, 34));
            top += Theme.S(this, 64);

            int panelH = _sidePanel.ClientSize.Height;
            int saveAsH = Theme.S(this, 32);
            int saveAsTop = panelH - Theme.S(this, 16) - saveAsH;
            int saveTop = saveAsTop - Theme.S(this, 44);
            int availableBottom = saveTop - Theme.S(this, 6);

            int contentBottom = LayoutGridContent(top, pad, innerW, false);
            if (contentBottom > availableBottom)
            {
                LayoutGridContent(top, pad, innerW, true);
            }

            _saveAsButton.Location = new Point(pad, saveAsTop);
            _saveAsButton.Size = new Size(innerW, saveAsH);
            _saveButton.Location = new Point(pad, saveTop);
            _saveButton.Size = new Size(innerW, Theme.S(this, 38));
        }

        /// <summary>排布网格模式下的各分区，返回内容底边；compact=true 时压缩行距/控件高度。</summary>
        private int LayoutGridContent(int top, int pad, int innerW, bool compact)
        {
            int labelH = Theme.S(this, compact ? 13 : 14);
            int stepH = Theme.S(this, compact ? 27 : 30);
            int rowGap = Theme.S(this, compact ? 3 : 5);
            int sectionGap = Theme.S(this, compact ? 3 : 8);
            int segH = Theme.S(this, compact ? 27 : 30);
            int rowH = Theme.S(this, compact ? 29 : 32);
            int swatchH = Theme.S(this, compact ? 40 : 44);
            int buttonH = Theme.S(this, compact ? 26 : 30);
            int guidesH = Theme.S(this, compact ? 20 : 22);
            int half = Math.Max(Theme.S(this, 60), (innerW - Theme.S(this, 8)) / 2);
            int secondX = pad + innerW - half;

            // ---- 网格区
            _gridSection.Location = new Point(pad, top);
            _gridSection.Size = new Size(innerW, Theme.S(this, 20));
            int y = top + Theme.S(this, 22);

            _rowCountLabel.Location = new Point(pad, y);
            _rowCountLabel.Size = new Size(half, labelH);
            _columnCountLabel.Location = new Point(secondX, y);
            _columnCountLabel.Size = new Size(half, labelH);
            y += labelH;
            _rowsStep.Location = new Point(pad, y);
            _rowsStep.Size = new Size(half, stepH);
            _columnsStep.Location = new Point(secondX, y);
            _columnsStep.Size = new Size(half, stepH);
            y += stepH + rowGap;

            _rowGapLabel.Location = new Point(pad, y);
            _rowGapLabel.Size = new Size(half, labelH);
            _columnGapLabel.Location = new Point(secondX, y);
            _columnGapLabel.Size = new Size(half, labelH);
            y += labelH;
            _rowGapStep.Location = new Point(pad, y);
            _rowGapStep.Size = new Size(half, stepH);
            _columnGapStep.Location = new Point(secondX, y);
            _columnGapStep.Size = new Size(half, stepH);
            y += stepH + rowGap;

            // 外边距与行/列参数同组同款式：小标题在上、同宽步进器在下（网格模式下外边距属于网格参数组）
            _gridMarginLabel.Location = new Point(pad, y);
            _gridMarginLabel.Size = new Size(half, labelH);
            y += labelH;
            _marginStep.Location = new Point(pad, y);
            _marginStep.Size = new Size(half, stepH);
            y += stepH + rowGap;

            _mergeCellsButton.Location = new Point(pad, y);
            _mergeCellsButton.Size = new Size(innerW, buttonH);
            y += buttonH + rowGap;

            _gridGuidesCheck.Location = new Point(pad, y);
            _gridGuidesCheck.Size = new Size(innerW, guidesH);
            y += guidesH + sectionGap;

            // ---- 背景（压缩为：色块行 + 透明度标题/滑块两行）
            _backgroundSection.Location = new Point(pad, y);
            _backgroundSection.Size = new Size(innerW, Theme.S(this, 20));
            int swY = y + Theme.S(this, 22);
            int swW = Theme.S(this, 58);
            int swGap = Theme.S(this, 7);
            _swatchTransparent.Location = new Point(pad, swY);
            _swatchWhite.Location = new Point(pad + swW + swGap, swY);
            _swatchBlack.Location = new Point(pad + (swW + swGap) * 2, swY);
            _swatchCustom.Location = new Point(pad + (swW + swGap) * 3, swY);
            _swatchTransparent.Size = new Size(swW, swatchH);
            _swatchWhite.Size = new Size(swW, swatchH);
            _swatchBlack.Size = new Size(swW, swatchH);
            _swatchCustom.Size = new Size(swW, swatchH);
            int alphaY = swY + swatchH + Theme.S(this, 2);
            _alphaTitle.Location = new Point(pad, alphaY + Theme.S(this, 2));
            _alphaTitle.Size = new Size(Theme.S(this, 100), Theme.S(this, 16));
            _alphaValue.Location = new Point(pad + innerW - Theme.S(this, 80), alphaY + Theme.S(this, 2));
            _alphaValue.Size = new Size(Theme.S(this, 80), Theme.S(this, 16));
            _alphaSlider.Location = new Point(pad, alphaY + Theme.S(this, 19));
            _alphaSlider.Size = new Size(innerW, Theme.S(this, compact ? 20 : 22));
            y = _alphaSlider.Bottom + sectionGap;

            // ---- 输出尺寸
            _sizeSection.Location = new Point(pad, y);
            _sizeSection.Size = new Size(innerW, Theme.S(this, 20));
            _sizeModeSeg.Location = new Point(pad, y + Theme.S(this, 22));
            _sizeModeSeg.Size = new Size(innerW, segH);
            int sizeRow = y + Theme.S(this, 22) + segH + Theme.S(this, 4);
            bool fixedSize = _sizeModeSeg.SelectedIndex != 0;
            _sizeHint.Location = new Point(pad, sizeRow);
            _sizeHint.Size = new Size(fixedSize ? innerW - Theme.S(this, 132) : innerW, rowH);
            _sizeStep.Location = new Point(pad + innerW - Theme.S(this, 124), sizeRow);
            _sizeStep.Size = new Size(Theme.S(this, 124), rowH);
            y = sizeRow + rowH + sectionGap;

            // ---- 输出格式
            _outputSection.Location = new Point(pad, y);
            _outputSection.Size = new Size(innerW, Theme.S(this, 20));
            _formatSeg.Location = new Point(pad, y + Theme.S(this, 22));
            _formatSeg.Size = new Size(innerW, segH);
            y = y + Theme.S(this, 22) + segH + Theme.S(this, 2);
            _formatNote.Location = new Point(pad, y);
            _formatNote.Size = new Size(innerW, Theme.S(this, 18));
            _formatNote.Visible = !compact;
            if (!compact)
            {
                y += Theme.S(this, 18);
            }
            return y;
        }

        private void MainFormShown(object sender, EventArgs e)
        {
            Theme.ApplyDarkTitleBar(this);
            _incomingTimer.Start();
            _statusLabel.Text = "就绪";
            if (_initialFiles != null && _initialFiles.Length > 0)
            {
                string[] files = _initialFiles;
                _initialFiles = null;
                AddFiles(files, true);
            }
            else
            {
                _preview.SetPreview(null, string.Empty, string.Empty);
            }
        }

        private void RestoreWindowSettings()
        {
            int logicalWidth = _appSettings.WindowWidth;
            int logicalHeight = _appSettings.WindowHeight;
            if (logicalWidth < 900 || logicalHeight < 620 || logicalWidth > 4000 || logicalHeight > 3000)
            {
                logicalWidth = 1180;
                logicalHeight = 800;
            }
            int w = Theme.S(this, logicalWidth);
            int h = Theme.S(this, logicalHeight);
            Size = new Size(w, h);
            bool valid = false;
            if (_appSettings.WindowX != int.MinValue && _appSettings.WindowY != int.MinValue)
            {
                Rectangle saved = new Rectangle(_appSettings.WindowX, _appSettings.WindowY, w, h);
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (screen.WorkingArea.IntersectsWith(saved))
                    {
                        valid = true;
                        break;
                    }
                }
            }
            if (valid)
            {
                Location = new Point(_appSettings.WindowX, _appSettings.WindowY);
            }
            else
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(area.Left + Math.Max(0, (area.Width - w) / 2),
                    area.Top + Math.Max(0, (area.Height - h) / 2));
            }
            if (_appSettings.WindowMaximized)
            {
                WindowState = FormWindowState.Maximized;
            }
        }

        private void SaveWindowSettings()
        {
            if (WindowState == FormWindowState.Normal)
            {
                _appSettings.WindowWidth = (int)Math.Round(Width / Theme.ScaleFactor);
                _appSettings.WindowHeight = (int)Math.Round(Height / Theme.ScaleFactor);
                _appSettings.WindowX = Location.X;
                _appSettings.WindowY = Location.Y;
                _appSettings.WindowMaximized = false;
            }
            else
            {
                _appSettings.WindowMaximized = WindowState == FormWindowState.Maximized;
            }
        }

        private void ApplySettingsToUi()
        {
            _updatingUi = true;
            try
            {
                CollageSettings s = _appSettings.Collage;
                s.GridRows = GridLayout.ClampRows(s.GridRows);
                s.GridColumns = GridLayout.ClampColumns(s.GridColumns);
                s.RowGap = GridLayout.ClampGap(s.RowGap);
                s.ColumnGap = GridLayout.ClampGap(s.ColumnGap);
                s.Merges = GridLayout.NormalizeMerges(s.GridRows, s.GridColumns, s.Merges);
                _orientationSeg.SelectedIndex = s.Orientation == CollageOrientation.Grid ? 2
                    : (s.Orientation == CollageOrientation.Vertical ? 1 : 0);
                _gapStep.Value = s.Gap;
                _marginStep.Value = s.Margin;
                _rowsStep.SetValueSilently(s.GridRows);
                _columnsStep.SetValueSilently(s.GridColumns);
                _rowGapStep.SetValueSilently(s.RowGap);
                _columnGapStep.SetValueSilently(s.ColumnGap);
                _gridGuidesCheck.SetCheckedSilently(s.ShowGridGuides);
                _alphaSlider.SetValueSilently(Color.FromArgb(s.BackgroundArgb).A);
                _alphaSlider.BaseColor = Color.FromArgb(Color.FromArgb(s.BackgroundArgb).R,
                    Color.FromArgb(s.BackgroundArgb).G, Color.FromArgb(s.BackgroundArgb).B);
                _formatSeg.SelectedIndex = s.Format == OutputFormat.Png ? 0 : 1;
                _sizeModeSeg.SelectedIndex = s.OutputSizeMode == OutputSizeMode.Width ? 1
                    : (s.OutputSizeMode == OutputSizeMode.Height ? 2 : 0);
                _sizeStep.Minimum = 1;
                _sizeStep.Maximum = 32000;
                _sizeStep.SetValueSilently(s.OutputTarget >= 1 ? s.OutputTarget : 2000);
                RefreshBackgroundUi();
                RefreshOrientationUi();
                RefreshSizeUi();
                RefreshFormatUi();
                UpdateMinimumSizeForOrientation();
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private void RefreshOrientationUi()
        {
            bool grid = IsGridMode();
            if (_gapLabel != null)
            {
                _gapLabel.Text = "图片间距 (px)";
                _marginLabel.Text = "外边距 (px)";
            }
            if (_gridSection != null)
            {
                // 网格模式：显示「网格」区（行列/行距/列距/外边距统一为同一组样式），隐藏「图片间距」；横/纵模式恢复原样。
                _spacingSection.Visible = !grid;
                _gapLabel.Visible = !grid;
                _gapStep.Visible = !grid;
                SetGridControlsVisible(grid);
                _marginLabel.Visible = !grid;
                _gridMarginLabel.Visible = grid;
                _marginStep.Visible = true;
                if (_hintLabel != null)
                {
                    _hintLabel.Visible = !grid;
                }
                LayoutSidePanel();
            }
            RefreshSizeUi();
        }

        /// <summary>网格模式下整行提示文案（行列数/容量/合并/未放入）；横/纵模式保持 v1.0.0 文案。</summary>
        private string BuildCountText()
        {
            CollageSettings s = _appSettings.Collage;
            if (s.Orientation != CollageOrientation.Grid)
            {
                string orientation = s.Orientation == CollageOrientation.Horizontal ? "横向" : "纵向";
                return "共 " + _items.Count + " 张图片 · " + orientation + "拼接";
            }
            int rows = GridLayout.ClampRows(s.GridRows);
            int columns = GridLayout.ClampColumns(s.GridColumns);
            List<GridRegion> merges = GridLayout.NormalizeMerges(rows, columns, s.Merges);
            int capacity = GridLayout.Capacity(rows, columns, merges);
            int unplaced = Math.Max(0, _items.Count - capacity);
            string text = "共 " + _items.Count + " 张图片 · 网格拼接 " + rows + "×" + columns
                + " · 容量 " + capacity + " · 合并 " + merges.Count + " 处";
            if (unplaced > 0)
            {
                text += " · 未放入 " + unplaced + " 张";
            }
            return text;
        }

        /// <summary>当前网格容量（非网格返回 int.MaxValue 表示全部放入）。</summary>
        private int CurrentGridCapacity()
        {
            CollageSettings s = _appSettings.Collage;
            if (s.Orientation != CollageOrientation.Grid)
            {
                return int.MaxValue;
            }
            int rows = GridLayout.ClampRows(s.GridRows);
            int columns = GridLayout.ClampColumns(s.GridColumns);
            List<GridRegion> merges = GridLayout.NormalizeMerges(rows, columns, s.Merges);
            return GridLayout.Capacity(rows, columns, merges);
        }

        /// <summary>按当前模式调整窗口最小高度（网格设置区更高；小屏时以工作区高度为上限）。</summary>
        private void UpdateMinimumSizeForOrientation()
        {
            bool grid = IsGridMode();
            int minWidth = Theme.S(this, 1020);
            int minHeight = Theme.S(this, grid ? 760 : 724);
            int effectiveMinHeight = minHeight;
            if (grid)
            {
                // 屏幕工作区装不下 760 逻辑高度时，允许更小（侧栏会切紧凑密度），避免窗口超出屏幕。
                int workHeight;
                try
                {
                    workHeight = Screen.FromControl(this).WorkingArea.Height;
                }
                catch
                {
                    workHeight = Screen.PrimaryScreen.WorkingArea.Height;
                }
                if (workHeight > 0 && workHeight < minHeight)
                {
                    effectiveMinHeight = Math.Max(Theme.S(this, 620), workHeight);
                }
            }
            MinimumSize = new Size(minWidth, effectiveMinHeight);
            if (grid && Height < effectiveMinHeight)
            {
                Height = effectiveMinHeight;
            }
        }

        /// <summary>规范化网格行列与合并区域；返回合并集合是否被修正。notify 时在状态栏提示被移除的越界合并。</summary>
        private bool NormalizeGridSettings(bool notify)
        {
            CollageSettings s = _appSettings.Collage;
            int rows = GridLayout.ClampRows(s.GridRows);
            int columns = GridLayout.ClampColumns(s.GridColumns);
            int beforeCount = s.Merges == null ? 0 : s.Merges.Count;
            string beforeText = GridLayout.SerializeMerges(s.Merges);
            List<GridRegion> normalized = GridLayout.NormalizeMerges(rows, columns, s.Merges);
            s.GridRows = rows;
            s.GridColumns = columns;
            s.RowGap = GridLayout.ClampGap(s.RowGap);
            s.ColumnGap = GridLayout.ClampGap(s.ColumnGap);
            s.Merges = normalized;
            bool changed = beforeCount != normalized.Count
                || !string.Equals(beforeText, GridLayout.SerializeMerges(normalized), StringComparison.Ordinal);
            if (changed && notify && beforeCount > normalized.Count)
            {
                SetStatus("行列变化：已自动移除 " + (beforeCount - normalized.Count) + " 处越界/非法的合并区域", true);
            }
            return changed;
        }

        private void RefreshSizeUi()
        {
            if (_sizeModeSeg == null || _sizeStep == null || _sizeHint == null)
            {
                return;
            }
            int mode = _sizeModeSeg.SelectedIndex;
            bool fixedSize = mode != 0;
            _sizeStep.Enabled = fixedSize;
            _sizeStep.Visible = fixedSize;
            int innerW = _sidePanel == null ? 0 : _sidePanel.ClientSize.Width - Theme.S(this, 32);
            if (innerW > 0)
            {
                _sizeHint.Width = fixedSize ? innerW - Theme.S(this, 132) : innerW;
            }
            if (mode == 1)
            {
                _sizeHint.Text = "目标宽度 (px)";
            }
            else if (mode == 2)
            {
                _sizeHint.Text = "目标高度 (px)";
            }
            else
            {
                _sizeHint.Text = IsGridMode() ? "自动：单元格取图片尺寸中位数" : "自动：取图片中间尺寸";
            }
        }

        private void RefreshBackgroundUi()
        {
            Color c = Color.FromArgb(_appSettings.Collage.BackgroundArgb);
            _alphaValue.Text = (int)Math.Round(c.A / 2.55) + "%";
            _alphaSlider.BaseColor = Color.FromArgb(c.R, c.G, c.B);
            _alphaSlider.SetValueSilently(c.A);
            UpdateSwatchSelection(c);
        }

        private void UpdateSwatchSelection(Color c)
        {
            bool transparent = c.A == 0;
            bool white = c.A == 255 && c.R == Theme.WhiteFlat.R && c.G == Theme.WhiteFlat.G && c.B == Theme.WhiteFlat.B;
            bool black = c.A == 255 && c.R == 15 && c.G == 15 && c.B == 15;
            bool custom = !transparent && !white && !black;
            _swatchTransparent.Selected = transparent;
            _swatchWhite.Selected = white;
            _swatchBlack.Selected = black;
            _swatchCustom.Selected = custom;
            _swatchCustom.SwatchColor = custom ? c : Color.FromArgb(120, 120, 120);
            _swatchCustom.ShowChecker = custom && c.A < 255;
        }

        private void RefreshFormatUi()
        {
            bool png = _appSettings.Collage.Format == OutputFormat.Png;
            _formatNote.Text = png ? "PNG 完整保留每张图的透明通道" : "JPG 不支持透明，透明区域将填白";
            _formatNote.ForeColor = png ? Theme.TextMuted : Theme.TextSecondary;
            _saveButton.Text = png ? "保存图片  Ctrl+S" : "保存 JPG  Ctrl+S";
        }

        private void MarkChanged()
        {
            _dirty = true;
            _settingsDirty = true;
            SchedulePreview();
        }

        private void SchedulePreview()
        {
            if (_previewTimer != null)
            {
                _previewTimer.Stop();
                _previewTimer.Start();
            }
        }

        private void PreviewTimerTick(object sender, EventArgs e)
        {
            _previewTimer.Stop();
            FlushSettings();
            UpdatePreview();
        }

        private void SettingsTimerTick(object sender, EventArgs e)
        {
            _settingsTimer.Stop();
            FlushSettings();
        }

        private void FlushSettings()
        {
            if (!_settingsDirty)
            {
                return;
            }
            _settingsDirty = false;
            try
            {
                SettingsStore.Save(_appSettings);
            }
            catch
            {
            }
        }

        private void IncomingTimerTick(object sender, EventArgs e)
        {
            List<string[]> batches = _incoming.Drain();
            if (batches == null || batches.Count == 0)
            {
                return;
            }
            List<string> files = new List<string>();
            foreach (string[] batch in batches)
            {
                if (batch != null)
                {
                    files.AddRange(batch);
                }
            }
            if (files.Count == 0)
            {
                return;
            }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Show();
            Activate();
            BringToFront();
            AddFiles(files.ToArray(), true);
        }

        private void AddFiles(string[] paths, bool fromContextMenu)
        {
            if (paths == null || paths.Length == 0)
            {
                return;
            }
            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EditorItem it in _items)
            {
                known.Add(it.FilePath);
            }

            List<string> accepted = new List<string>();
            List<string> unsupported = new List<string>();
            List<string> missing = new List<string>();
            foreach (string raw in paths)
            {
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }
                string path;
                try
                {
                    path = Path.GetFullPath(raw.Trim().Trim('"'));
                }
                catch
                {
                    continue;
                }
                if (!File.Exists(path))
                {
                    if (!missing.Contains(path))
                    {
                        missing.Add(path);
                    }
                    continue;
                }
                if (!ImageLoader.IsSupportedExtension(path))
                {
                    if (!unsupported.Contains(path))
                    {
                        unsupported.Add(path);
                    }
                    continue;
                }
                if (known.Contains(path))
                {
                    continue;
                }
                known.Add(path);
                accepted.Add(path);
            }

            if (accepted.Count == 0)
            {
                string message = "没有可添加的图片。";
                if (unsupported.Count > 0)
                {
                    message = "不支持的文件格式：" + Path.GetFileName(unsupported[0]);
                }
                else if (missing.Count > 0)
                {
                    message = "文件不存在：" + Path.GetFileName(missing[0]);
                }
                SetStatus(message, true);
                return;
            }

            int room = MaxItems - _items.Count;
            if (room <= 0)
            {
                MessageBox.Show(this, "一次最多处理 " + MaxItems + " 张图片，请先移除一些图片。", "轻拼",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (accepted.Count > room)
            {
                accepted.RemoveRange(room, accepted.Count - room);
                SetStatus("图片较多，仅添加前 " + room + " 张", true);
            }

            int startIndex = _items.Count;
            foreach (string path in accepted)
            {
                EditorItem item = new EditorItem();
                item.FilePath = path;
                item.FileName = Path.GetFileName(path);
                item.IsLoading = true;
                _items.Add(item);
                _loadQueue.Enqueue(item);
                _loadTotal++;
                _queuedLoads++;
            }
            _strip.SetItems(_items);
            _strip.SelectedIndex = startIndex;
            _selectedIndex = _strip.SelectedIndex;
            _dirty = true;
            UpdateChrome();
            UpdateCountText();
            if (!_loadingCurrent)
            {
                StartNextLoad();
            }
            if (unsupported.Count > 0)
            {
                SetStatus("跳过 " + unsupported.Count + " 个不支持的文件", true);
            }
        }

        private void StartNextLoad()
        {
            if (_loadingCurrent || _loadQueue.Count == 0)
            {
                return;
            }
            EditorItem item = _loadQueue.Dequeue();
            _loadingCurrent = true;
            _preview.SetLoading(true, "正在载入 " + item.FileName + "…", LoadPercent());
            SetStatus("正在载入图片…", false);
            string path = item.FilePath;
            int thumbSize = Math.Max(160, Theme.S(this, 96) * 2);
            Task<LightCollage.Common.ImageLoadResult> task = Task.Factory.StartNew(
                delegate { return ImageLoader.LoadForEditing(path, 1600, thumbSize); });
            task.ContinueWith(delegate(Task<LightCollage.Common.ImageLoadResult> t)
            {
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action<EditorItem, LightCollage.Common.ImageLoadResult, Exception>(
                            OnItemLoaded), item, t.IsFaulted ? null : t.Result, t.Exception);
                    }
                }
                catch
                {
                }
            });
        }

        private void OnItemLoaded(EditorItem item, LightCollage.Common.ImageLoadResult result, Exception error)
        {
            _loadingCurrent = false;
            _queuedLoads = Math.Max(0, _queuedLoads - 1);
            if (!_items.Contains(item))
            {
                // 图片在解码期间被移除/清空：丢弃结果，避免位图泄漏。
                if (result != null)
                {
                    result.Dispose();
                }
                item.Dispose();
                _loadDone++;
                if (_loadQueue.Count > 0)
                {
                    StartNextLoad();
                    return;
                }
                _preview.SetLoading(false, string.Empty, 0);
                _loadTotal = 0;
                _loadDone = 0;
                SchedulePreview();
                return;
            }
            item.IsLoading = false;
            if (error != null)
            {
                item.LoadError = "载入失败：" + error.GetBaseException().Message;
            }
            else
            {
                item.Data = result;
                if (!item.IsReady)
                {
                    item.LoadError = string.IsNullOrEmpty(result.ErrorMessage) ? "无法读取图片" : result.ErrorMessage;
                }
            }
            _loadDone++;
            if (!item.IsReady)
            {
                // 损坏/不支持的文件直接跳过：不把错误项留在列表里，避免阻塞正常图片的预览和保存。
                string message = item.ErrorText;
                if (!string.IsNullOrEmpty(message) && !_batchErrors.Contains(message))
                {
                    _batchErrors.Add(message);
                }
                int removeIndex = _items.IndexOf(item);
                _items.Remove(item);
                item.Dispose();
                if (removeIndex >= 0)
                {
                    _strip.SetItems(_items);
                    if (_items.Count == 0)
                    {
                        _selectedIndex = -1;
                        _strip.SelectedIndex = -1;
                    }
                    else
                    {
                        int next = Math.Min(removeIndex, _items.Count - 1);
                        _selectedIndex = next;
                        _strip.SelectedIndex = next;
                    }
                }
            }
            else
            {
                _strip.RefreshView();
            }
            UpdateChrome();
            UpdateCountText();

            if (_loadQueue.Count > 0)
            {
                _preview.SetLoading(true, "正在载入下一张…", LoadPercent());
                StartNextLoad();
                return;
            }
            _preview.SetLoading(false, string.Empty, 0);
            _loadTotal = 0;
            _loadDone = 0;
            SchedulePreview();
            if (_batchErrors.Count > 0)
            {
                string details = string.Join("\r\n", _batchErrors.Take(6).ToArray());
                if (_batchErrors.Count > 6)
                {
                    details += "\r\n… 共 " + _batchErrors.Count + " 个错误";
                }
                SetStatus("有 " + _batchErrors.Count + " 张图片无法载入", true);
                MessageBox.Show(this, "以下图片无法载入：\r\n\r\n" + details, "轻拼",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _batchErrors.Clear();
            }
            else
            {
                SetStatus("已载入 " + _items.Count + " 张图片", false);
            }
        }

        private int LoadPercent()
        {
            if (_loadTotal <= 0)
            {
                return 0;
            }
            return Math.Min(99, (int)Math.Round(_loadDone * 100.0 / _loadTotal));
        }

        private void UpdatePreview()
        {
            if (_closing || _preview == null)
            {
                return;
            }
            UpdateChrome();
            bool allReady = _items.Count > 0;
            if (allReady)
            {
                foreach (EditorItem item in _items)
                {
                    if (!item.IsReady)
                    {
                        allReady = false;
                        break;
                    }
                }
            }
            if (!allReady)
            {
                if (_items.Count == 0)
                {
                    DisposeComposition();
                    _preview.SetPreview(null, string.Empty, string.Empty);
                }
                return;
            }

            List<LightCollage.Common.ImageLoadResult> loaded = new List<LightCollage.Common.ImageLoadResult>();
            List<ImageSize> sizes = new List<ImageSize>();
            foreach (EditorItem item in _items)
            {
                loaded.Add(item.Data);
                sizes.Add(item.ToSize());
            }

            CollageSettings snapshot = _appSettings.Collage.Clone();
            Rectangle content = _preview.ContentRect;
            int maxW = Math.Max(64, content.Width);
            int maxH = Math.Max(64, content.Height);
            int version = ++_previewVersion;
            try
            {
                PreviewComposition composition = Compositor.ComposePreview(loaded, snapshot, maxW, maxH);
                if (version != _previewVersion)
                {
                    if (composition != null)
                    {
                        composition.Dispose();
                    }
                    return;
                }
                PreviewComposition old = _composition;
                _composition = composition;
                if (composition != null)
                {
                    CollageLayout layout = composition.Layout;
                    string dir;
                    if (snapshot.Orientation == CollageOrientation.Grid)
                    {
                        dir = "网格拼接";
                    }
                    else
                    {
                        dir = snapshot.Orientation == CollageOrientation.Horizontal ? "横向拼接" : "纵向拼接";
                    }
                    string info = dir + " · " + _items.Count + " 张";
                    string size = string.Format(CultureInfo.CurrentCulture, "输出 {0:N0} × {1:N0} px",
                        layout.CanvasWidth, layout.CanvasHeight);
                    if (layout.WasClamped)
                    {
                        size += " · 已自动缩小";
                    }
                    _preview.SetPreview(composition.Bitmap, info, size);
                    _preview.SetHitInfo(BuildPreviewHitInfo(composition, snapshot));
                    if (snapshot.OutputSizeMode == OutputSizeMode.Width)
                    {
                        _sizeHint.Text = "高度自动 " + layout.CanvasHeight.ToString("N0", CultureInfo.CurrentCulture) + " px";
                    }
                    else if (snapshot.OutputSizeMode == OutputSizeMode.Height)
                    {
                        _sizeHint.Text = "宽度自动 " + layout.CanvasWidth.ToString("N0", CultureInfo.CurrentCulture) + " px";
                    }
                    else
                    {
                        _sizeHint.Text = snapshot.Orientation == CollageOrientation.Grid
                            ? "自动：单元格取图片尺寸中位数"
                            : "自动：取图片中间尺寸";
                    }
                    UpdateChrome();
                }
                else
                {
                    _preview.SetPreview(null, string.Empty, string.Empty);
                    _preview.SetHitInfo(null);
                }
                if (old != null)
                {
                    old.Dispose();
                }
            }
            catch (Exception ex)
            {
                _preview.SetPreview(null, string.Empty, string.Empty);
                _preview.SetHitInfo(null);
                SetStatus("预览生成失败：" + ex.Message, true);
            }
        }

        /// <summary>
        /// 把合成结果中的布局命中信息拷贝成值副本喂给预览面板（面板不持有会被 Dispose 的 PreviewComposition）。
        /// 包括：合成缩放比、输出坐标 item 矩形 + Index、网格元数据、合并区域、辅助线开关。
        /// </summary>
        private PreviewHitInfo BuildPreviewHitInfo(PreviewComposition composition, CollageSettings snapshot)
        {
            if (composition == null || composition.Layout == null)
            {
                return null;
            }
            CollageLayout layout = composition.Layout;
            PreviewHitInfo info = PreviewHitInfo.CreateEmpty();
            info.Scale = composition.Scale <= 0f ? 1f : composition.Scale;
            int count = layout.Items == null ? 0 : layout.Items.Length;
            LayoutItem[] items = new LayoutItem[count];
            for (int i = 0; i < count; i++)
            {
                LayoutItem source = layout.Items[i];
                if (source == null)
                {
                    continue;
                }
                LayoutItem copy = new LayoutItem();
                copy.Index = source.Index;
                copy.X = source.X;
                copy.Y = source.Y;
                copy.Width = source.Width;
                copy.Height = source.Height;
                items[i] = copy;
            }
            info.Items = items;
            info.IsGrid = layout.IsGrid;
            if (layout.IsGrid)
            {
                int rows = GridLayout.ClampRows(layout.GridRows);
                int columns = GridLayout.ClampColumns(layout.GridColumns);
                info.GridRows = rows;
                info.GridColumns = columns;
                info.CellWidth = Math.Max(1, layout.CellWidth);
                info.CellHeight = Math.Max(1, layout.CellHeight);
                info.RowGap = Math.Max(0, layout.RowGap);
                info.ColumnGap = Math.Max(0, layout.ColumnGap);
                info.GridMargin = Math.Max(0, layout.GridMargin);
                List<GridRegion> merges = GridLayout.NormalizeMerges(rows, columns, snapshot.Merges);
                GridRegion[] copies = new GridRegion[merges.Count];
                for (int i = 0; i < merges.Count; i++)
                {
                    copies[i] = merges[i].Clone();
                }
                info.Merges = copies;
                info.ShowGuides = snapshot.ShowGridGuides;
            }
            return info;
        }

        /// <summary>预览拖动对调：交换图片列表顺序 → 缩略图/序号刷新 → 选中跟随 → 状态栏提示。</summary>
        private void SwapItems(int from, int to)
        {
            if (from < 0 || to < 0 || from >= _items.Count || to >= _items.Count || from == to)
            {
                SetStatus("已取消拖动：目标无效", false);
                return;
            }
            if (_saving)
            {
                return;
            }
            EditorItem item = _items[from];
            _items[from] = _items[to];
            _items[to] = item;
            _dirty = true;
            _selectedIndex = to;
            _strip.SetItems(_items);
            _strip.SelectedIndex = to;
            _strip.EnsureVisible(to);
            UpdateChrome();
            MarkChanged();
            SetStatus("已交换 第" + (from + 1) + "张 ↔ 第" + (to + 1) + "张", false);
        }

        private void DisposeComposition()
        {
            if (_composition != null)
            {
                _composition.Dispose();
                _composition = null;
            }
        }

        private void UpdateChrome()
        {
            if (_countLabel == null)
            {
                return;
            }
            _countLabel.Text = BuildCountText();
            if (_strip != null)
            {
                int capacity = CurrentGridCapacity();
                _strip.PlacedCount = capacity == int.MaxValue ? int.MaxValue : Math.Min(_items.Count, capacity);
            }
            _saveButton.Enabled = !_saving && _items.Count > 0 && AllReady();
            _saveAsButton.Enabled = !_saving && _items.Count > 0 && AllReady();
            _clearButton.Enabled = _items.Count > 0 && !_saving;
            _sortButton.Enabled = _items.Count > 1 && !_saving;
        }

        private void UpdateCountText()
        {
            if (_countLabel != null)
            {
                UpdateChrome();
            }
        }

        private bool AllReady()
        {
            if (_items.Count == 0)
            {
                return false;
            }
            foreach (EditorItem item in _items)
            {
                if (!item.IsReady)
                {
                    return false;
                }
            }
            return true;
        }

        private void OrientationChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            int index = _orientationSeg.SelectedIndex;
            CollageOrientation orientation = index == 2 ? CollageOrientation.Grid
                : (index == 1 ? CollageOrientation.Vertical : CollageOrientation.Horizontal);
            bool switchedToGrid = orientation == CollageOrientation.Grid
                && _appSettings.Collage.Orientation != CollageOrientation.Grid;
            _appSettings.Collage.Orientation = orientation;
            if (orientation == CollageOrientation.Grid)
            {
                NormalizeGridSettings(false);
            }
            UpdateMinimumSizeForOrientation();
            RefreshOrientationUi();
            UpdateChrome();
            MarkChanged();
            if (switchedToGrid)
            {
                SetStatus("网格拼接：拖动预览里的图片可对调位置", false);
            }
            else
            {
                SetStatus(orientation == CollageOrientation.Horizontal ? "已切换到横向拼接" : "已切换到纵向拼接", false);
            }
        }

        private void GridRowsChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            CollageSettings s = _appSettings.Collage;
            int beforeCount = s.Merges == null ? 0 : s.Merges.Count;
            s.GridRows = GridLayout.ClampRows(_rowsStep.Value);
            NormalizeGridSettings(false);
            int removed = beforeCount - s.Merges.Count;
            string text = "网格行数：" + s.GridRows + " 行 · 容量 " + CurrentGridCapacity() + " 格";
            if (removed > 0)
            {
                SetStatus("行数变化：已自动移除 " + removed + " 处越界合并；" + text, true);
            }
            else
            {
                SetStatus(text, false);
            }
            UpdateChrome();
            MarkChanged();
        }

        private void GridColumnsChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            CollageSettings s = _appSettings.Collage;
            int beforeCount = s.Merges == null ? 0 : s.Merges.Count;
            s.GridColumns = GridLayout.ClampColumns(_columnsStep.Value);
            NormalizeGridSettings(false);
            int removed = beforeCount - s.Merges.Count;
            string text = "网格列数：" + s.GridColumns + " 列 · 容量 " + CurrentGridCapacity() + " 格";
            if (removed > 0)
            {
                SetStatus("列数变化：已自动移除 " + removed + " 处越界合并；" + text, true);
            }
            else
            {
                SetStatus(text, false);
            }
            UpdateChrome();
            MarkChanged();
        }

        private void RowGapChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            _appSettings.Collage.RowGap = GridLayout.ClampGap(_rowGapStep.Value);
            MarkChanged();
        }

        private void ColumnGapChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            _appSettings.Collage.ColumnGap = GridLayout.ClampGap(_columnGapStep.Value);
            MarkChanged();
        }

        private void GridGuidesChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            _appSettings.Collage.ShowGridGuides = _gridGuidesCheck.Checked;
            MarkChanged();
            SetStatus(_gridGuidesCheck.Checked ? "已显示网格辅助线（仅预览，不写入输出）" : "已隐藏网格辅助线", false);
        }

        private void OpenGridMergeDialog()
        {
            CollageSettings s = _appSettings.Collage;
            NormalizeGridSettings(false);
            int rows = GridLayout.ClampRows(s.GridRows);
            int columns = GridLayout.ClampColumns(s.GridColumns);
            using (GridMergeDialog dialog = new GridMergeDialog(rows, columns, s.Merges, _items.Count))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    s.Merges = dialog.Merges;
                    NormalizeGridSettings(false);
                    UpdateChrome();
                    MarkChanged();
                    SetStatus("已更新合并单元格：合并 " + s.Merges.Count + " 处 · 容量 "
                        + GridLayout.Capacity(rows, columns, s.Merges) + " 格", false);
                }
            }
        }

        private void GapChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            _appSettings.Collage.Gap = _gapStep.Value;
            MarkChanged();
        }

        private void MarginValueChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            _appSettings.Collage.Margin = _marginStep.Value;
            MarkChanged();
        }

        private void AlphaChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            Color c = Color.FromArgb(_alphaSlider.Value, _alphaSlider.BaseColor);
            _appSettings.Collage.BackgroundArgb = c.ToArgb();
            _alphaValue.Text = (int)Math.Round(c.A / 2.55) + "%";
            UpdateSwatchSelection(c);
            MarkChanged();
        }

        private void SetPresetBackground(Color color)
        {
            int alpha = color.A;
            if (color == Color.Transparent)
            {
                alpha = 0;
            }
            else
            {
                alpha = color.A == 0 ? 255 : color.A;
            }
            Color next = color == Color.Transparent ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(alpha, color.R, color.G, color.B);
            _appSettings.Collage.BackgroundArgb = next.ToArgb();
            RefreshBackgroundUi();
            UpdateSwatchSelection(next);
            MarkChanged();
        }

        private void PickCustomColor()
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.FullOpen = true;
                Color current = Color.FromArgb(_appSettings.Collage.BackgroundArgb);
                dialog.Color = Color.FromArgb(current.R, current.G, current.B);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    int alpha = current.A == 0 ? 255 : current.A;
                    if (_swatchCustom.Selected && _alphaSlider.Value > 0)
                    {
                        alpha = _alphaSlider.Value;
                    }
                    Color next = Color.FromArgb(alpha, dialog.Color.R, dialog.Color.G, dialog.Color.B);
                    _appSettings.Collage.BackgroundArgb = next.ToArgb();
                    RefreshBackgroundUi();
                    UpdateSwatchSelection(next);
                    MarkChanged();
                }
            }
        }

        private void FormatChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            _appSettings.Collage.Format = _formatSeg.SelectedIndex == 0 ? OutputFormat.Png : OutputFormat.Jpeg;
            RefreshFormatUi();
            MarkChanged();
        }

        private void SizeModeChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            int mode = _sizeModeSeg.SelectedIndex;
            _appSettings.Collage.OutputSizeMode = mode == 1 ? OutputSizeMode.Width
                : (mode == 2 ? OutputSizeMode.Height : OutputSizeMode.Auto);
            if (mode != 0)
            {
                int current = 0;
                if (_composition != null && _composition.Layout != null)
                {
                    current = mode == 1 ? _composition.Layout.CanvasWidth : _composition.Layout.CanvasHeight;
                }
                if (current > 0)
                {
                    _appSettings.Collage.OutputTarget = current;
                    _updatingUi = true;
                    _sizeStep.SetValueSilently(current);
                    _updatingUi = false;
                }
                _sizeStep.Enabled = true;
            }
            RefreshSizeUi();
            MarkChanged();
        }

        private void OutputSizeChanged(object sender, EventArgs e)
        {
            if (_updatingUi)
            {
                return;
            }
            if (_appSettings.Collage.OutputSizeMode == OutputSizeMode.Auto)
            {
                return;
            }
            _appSettings.Collage.OutputTarget = _sizeStep.Value;
            MarkChanged();
        }

        private void SetStatus(string text, bool warn)
        {
            if (_statusLabel == null)
            {
                return;
            }
            _statusLabel.Text = text;
            _statusLabel.ForeColor = warn ? Theme.TextPrimary : Theme.TextMuted;
        }

        private void ShowAddDialog()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Multiselect = true;
                dialog.Filter = ImageLoader.FileDialogFilter;
                dialog.Title = "添加图片";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddFiles(dialog.FileNames, false);
                }
            }
        }

        private void RemoveItemAt(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                return;
            }
            EditorItem item = _items[index];
            _items.RemoveAt(index);
            item.Dispose();
            _strip.SetItems(_items);
            if (_items.Count == 0)
            {
                _selectedIndex = -1;
                _strip.SelectedIndex = -1;
                DisposeComposition();
                _preview.SetPreview(null, string.Empty, string.Empty);
            }
            else
            {
                int next = Math.Min(index, _items.Count - 1);
                _selectedIndex = next;
                _strip.SelectedIndex = next;
            }
            _dirty = true;
            UpdateChrome();
            SchedulePreview();
        }

        private void ReorderItem(int from, int to)
        {
            if (from < 0 || from >= _items.Count || to < 0 || to >= _items.Count || from == to)
            {
                return;
            }
            EditorItem item = _items[from];
            _items.RemoveAt(from);
            _items.Insert(to, item);
            _strip.SetItems(_items);
            _selectedIndex = to;
            _strip.SelectedIndex = to;
            _strip.EnsureVisible(to);
            _dirty = true;
            MarkChanged();
        }

        private void SortByFileName()
        {
            if (_items.Count < 2)
            {
                return;
            }
            EditorItem selected = _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;
            _items.Sort(delegate(EditorItem a, EditorItem b)
            {
                return NaturalCompare(a.FileName, b.FileName);
            });
            _strip.SetItems(_items);
            int newIndex = selected == null ? 0 : _items.IndexOf(selected);
            _selectedIndex = newIndex;
            _strip.SelectedIndex = newIndex;
            _dirty = true;
            MarkChanged();
        }

        private static int NaturalCompare(string a, string b)
        {
            if (a == null)
            {
                return b == null ? 0 : -1;
            }
            if (b == null)
            {
                return 1;
            }
            int ia = 0;
            int ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                char ca = a[ia];
                char cb = b[ib];
                if (char.IsDigit(ca) && char.IsDigit(cb))
                {
                    int startA = ia;
                    int startB = ib;
                    while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                    while (ib < b.Length && char.IsDigit(b[ib])) ib++;
                    string numA = a.Substring(startA, ia - startA).TrimStart('0');
                    string numB = b.Substring(startB, ib - startB).TrimStart('0');
                    if (numA.Length != numB.Length)
                    {
                        return numA.Length.CompareTo(numB.Length);
                    }
                    int cmp = string.CompareOrdinal(numA, numB);
                    if (cmp != 0)
                    {
                        return cmp;
                    }
                }
                else
                {
                    int cmp = char.ToUpperInvariant(ca).CompareTo(char.ToUpperInvariant(cb));
                    if (cmp != 0)
                    {
                        return cmp;
                    }
                    ia++;
                    ib++;
                }
            }
            return (a.Length - ia).CompareTo(b.Length - ib);
        }

        private void ClearAll()
        {
            if (_items.Count == 0)
            {
                return;
            }
            if (MessageBox.Show(this, "确定清空当前所有图片吗？", "轻拼", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            foreach (EditorItem item in _items)
            {
                item.Dispose();
            }
            _items.Clear();
            _loadQueue.Clear();
            _strip.SetItems(_items);
            _selectedIndex = -1;
            _strip.SelectedIndex = -1;
            DisposeComposition();
            _preview.SetPreview(null, string.Empty, string.Empty);
            _dirty = false;
            UpdateChrome();
            SetStatus("已清空", false);
        }

        private string GetDefaultSavePath()
        {
            string dir = null;
            if (_items.Count > 0)
            {
                dir = Path.GetDirectoryName(_items[0].FilePath);
            }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                dir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }
            string ext = _appSettings.Collage.Format == OutputFormat.Png ? ".png" : ".jpg";
            string baseName = "拼图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(dir, baseName + ext);
            int n = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(dir, baseName + "_" + n + ext);
                n++;
            }
            return path;
        }

        private void SaveDefault()
        {
            if (_saving || _items.Count == 0 || !AllReady())
            {
                return;
            }
            SaveTo(GetDefaultSavePath());
        }

        private void SaveAs()
        {
            if (_saving || _items.Count == 0 || !AllReady())
            {
                return;
            }
            string defaultPath = GetDefaultSavePath();
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "另存拼图";
                dialog.InitialDirectory = Path.GetDirectoryName(defaultPath);
                dialog.FileName = Path.GetFileName(defaultPath);
                if (_appSettings.Collage.Format == OutputFormat.Png)
                {
                    dialog.Filter = "PNG 图片 (*.png)|*.png";
                    dialog.DefaultExt = "png";
                }
                else
                {
                    dialog.Filter = "JPEG 图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg";
                    dialog.DefaultExt = "jpg";
                }
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SaveTo(dialog.FileName);
                }
            }
        }

        private void SaveTo(string path)
        {
            try
            {
                string ext = Path.GetExtension(path);
                OutputFormat format = _appSettings.Collage.Format;
                string desiredExt = format == OutputFormat.Png ? ".png" : ".jpg";
                bool extensionMatches = format == OutputFormat.Png
                    ? string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)
                    : (string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase));
                if (!extensionMatches)
                {
                    path = Path.ChangeExtension(path, desiredExt);
                }

                List<string> filePaths = new List<string>();
                List<ImageSize> sizes = new List<ImageSize>();
                foreach (EditorItem item in _items)
                {
                    filePaths.Add(item.FilePath);
                    sizes.Add(item.ToSize());
                }
                CollageSettings snapshot = _appSettings.Collage.Clone();
                CollageLayout layout = CoreLayoutEngine.Compute(sizes, snapshot);
                _saving = true;
                UpdateChrome();
                _preview.SetLoading(true, "正在保存图片…", 0);
                SetStatus("正在保存…", false);
                Progress<int> progress = new Progress<int>(delegate(int percent)
                {
                    if (!IsDisposed)
                    {
                        _preview.SetLoading(true, "正在保存图片…", percent);
                        SetStatus("正在保存… " + percent + "%", false);
                    }
                });
                string output = path;
                Task.Factory.StartNew(delegate
                {
                    Compositor.Save(filePaths, sizes, snapshot, layout, output, progress);
                }).ContinueWith(delegate(Task t)
                {
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke(new Action<string, Exception>(OnSaveFinished), output,
                                t.IsFaulted ? t.Exception : null);
                        }
                    }
                    catch
                    {
                    }
                });
            }
            catch (Exception ex)
            {
                _saving = false;
                _preview.SetLoading(false, string.Empty, 0);
                UpdateChrome();
                MessageBox.Show(this, "保存失败：\r\n" + ex.Message, "轻拼", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSaveFinished(string path, Exception error)
        {
            _saving = false;
            _preview.SetLoading(false, string.Empty, 0);
            UpdateChrome();
            if (error != null)
            {
                SetStatus("保存失败", true);
                MessageBox.Show(this, "保存失败：\r\n" + error.GetBaseException().Message, "轻拼",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _lastSavedPath = path;
            _dirty = false;
            _openFolderButton.Visible = true;
            SetStatus("已保存：" + path, false);
            if (_appSettings.AutoOpenAfterSave)
            {
                try
                {
                    Process.Start(path);
                }
                catch
                {
                }
            }
        }

        private void OpenLastFolder()
        {
            if (string.IsNullOrEmpty(_lastSavedPath))
            {
                return;
            }
            try
            {
                if (File.Exists(_lastSavedPath))
                {
                    Process.Start("explorer.exe", "/select,\"" + _lastSavedPath + "\"");
                }
                else
                {
                    Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(_lastSavedPath) + "\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开文件夹：\r\n" + ex.Message, "轻拼",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowAbout()
        {
            using (AboutDialog dialog = new AboutDialog(_appSettings))
            {
                dialog.ShowDialog(this);
                _appSettings.AutoOpenAfterSave = dialog.AutoOpenAfterSave;
                _settingsDirty = true;
                FlushSettings();
            }
        }

        private void FormDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                _preview.SetDragOver(true);
            }
        }

        private void FormDragOver(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void FormDragLeave(object sender, EventArgs e)
        {
            _preview.SetDragOver(false);
        }

        private void FormDragDrop(object sender, DragEventArgs e)
        {
            _preview.SetDragOver(false);
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null)
                {
                    AddFiles(files, false);
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (_preview != null && _preview.CancelDrag())
                {
                    return true;
                }
            }
            if (keyData == (Keys.Control | Keys.O))
            {
                ShowAddDialog();
                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveDefault();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.S))
            {
                SaveAs();
                return true;
            }
            if (keyData == Keys.Delete)
            {
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    RemoveItemAt(_selectedIndex);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_closing)
            {
                return;
            }
            if (_saving)
            {
                e.Cancel = true;
                SetStatus("正在保存，请稍候…", true);
                return;
            }
            if (_dirty && _items.Count > 0)
            {
                DialogResult result = MessageBox.Show(this, "还有未保存的拼图，确定关闭吗？", "轻拼",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            _closing = true;
            try
            {
                SaveWindowSettings();
                SettingsStore.Save(_appSettings);
            }
            catch
            {
            }
            if (_incomingTimer != null) _incomingTimer.Stop();
            if (_previewTimer != null) _previewTimer.Stop();
            if (_settingsTimer != null) _settingsTimer.Stop();
            DisposeComposition();
            foreach (EditorItem item in _items)
            {
                item.Dispose();
            }
            _items.Clear();
            if (Icon != null)
            {
                Icon.Dispose();
                Icon = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_incomingTimer != null)
                {
                    _incomingTimer.Dispose();
                }
                if (_previewTimer != null)
                {
                    _previewTimer.Dispose();
                }
                if (_settingsTimer != null)
                {
                    _settingsTimer.Dispose();
                }
                if (_composition != null)
                {
                    _composition.Dispose();
                    _composition = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}

