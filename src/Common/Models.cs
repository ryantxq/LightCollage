using System;
using System.Collections.Generic;
using System.Drawing;

namespace LightCollage.Common
{
    public enum CollageOrientation
    {
        Horizontal = 0,
        Vertical = 1,

        /// <summary>网格拼接（v1.1 新增）。行列、行/列间距、合并区域见 CollageSettings。</summary>
        Grid = 2
    }

    public enum OutputFormat
    {
        Png = 0,
        Jpeg = 1
    }

    /// <summary>输出尺寸模式：自动（中位数基准）/ 指定最终宽度 / 指定最终高度。</summary>
    public enum OutputSizeMode
    {
        Auto = 0,
        Width = 1,
        Height = 2
    }

    public sealed class ImageSize
    {
        public ImageSize()
        {
        }

        public ImageSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; set; }
        public int Height { get; set; }
    }

    /// <summary>
    /// 网格合并区域：0 基坐标、闭区间（Row..EndRow / Col..EndCol）。
    /// RowSpan/ColSpan 至少为 1；有效合并区域的面积至少为 2（单个格子不算合并）。
    /// </summary>
    public sealed class GridRegion
    {
        public GridRegion()
        {
            RowSpan = 1;
            ColSpan = 1;
        }

        public GridRegion(int row, int col, int rowSpan, int colSpan)
        {
            Row = row;
            Col = col;
            RowSpan = rowSpan < 1 ? 1 : rowSpan;
            ColSpan = colSpan < 1 ? 1 : colSpan;
        }

        public int Row { get; set; }
        public int Col { get; set; }
        public int RowSpan { get; set; }
        public int ColSpan { get; set; }

        public int EndRow
        {
            get { return Row + (RowSpan < 1 ? 1 : RowSpan) - 1; }
        }

        public int EndCol
        {
            get { return Col + (ColSpan < 1 ? 1 : ColSpan) - 1; }
        }

        public int Area
        {
            get { return (RowSpan < 1 ? 1 : RowSpan) * (ColSpan < 1 ? 1 : ColSpan); }
        }

        public GridRegion Clone()
        {
            GridRegion copy = new GridRegion();
            copy.Row = Row;
            copy.Col = Col;
            copy.RowSpan = RowSpan;
            copy.ColSpan = ColSpan;
            return copy;
        }

        public override string ToString()
        {
            return Row + "," + Col + "," + RowSpan + "," + ColSpan;
        }
    }

    public sealed class CollageSettings
    {
        public CollageSettings()
        {
            Orientation = CollageOrientation.Horizontal;
            Gap = 12;
            Margin = 16;
            BackgroundArgb = unchecked((int)0x00000000);
            AutoBaseSize = true;
            BaseSize = 0;
            Format = OutputFormat.Png;
            JpegQuality = 92;
            OutputSizeMode = OutputSizeMode.Auto;
            OutputTarget = 2000;
            GridRows = 3;
            GridColumns = 3;
            RowGap = 12;
            ColumnGap = 12;
            Merges = new List<GridRegion>();
            ShowGridGuides = true;
        }

        public CollageOrientation Orientation { get; set; }
        public int Gap { get; set; }
        public int Margin { get; set; }
        public int BackgroundArgb { get; set; }
        public bool AutoBaseSize { get; set; }
        public int BaseSize { get; set; }
        public OutputFormat Format { get; set; }
        public int JpegQuality { get; set; }
        public OutputSizeMode OutputSizeMode { get; set; }
        public int OutputTarget { get; set; }

        // ---- v1.1 网格拼接 ----
        /// <summary>网格行数（1..20），默认 3。</summary>
        public int GridRows { get; set; }

        /// <summary>网格列数（1..20），默认 3。</summary>
        public int GridColumns { get; set; }

        /// <summary>行间距（0..200），默认 12。</summary>
        public int RowGap { get; set; }

        /// <summary>列间距（0..200），默认 12。</summary>
        public int ColumnGap { get; set; }

        /// <summary>合并区域（0 基坐标）；为空表示没有合并。写入设置时需做校验/规范化。</summary>
        public List<GridRegion> Merges { get; set; }

        /// <summary>是否在预览中显示网格辅助线（仅预览，不写入输出）。</summary>
        public bool ShowGridGuides { get; set; }

        public Color BackgroundColor
        {
            get { return Color.FromArgb(BackgroundArgb); }
            set { BackgroundArgb = value.ToArgb(); }
        }

        public CollageSettings Clone()
        {
            CollageSettings copy = new CollageSettings();
            copy.Orientation = Orientation;
            copy.Gap = Gap;
            copy.Margin = Margin;
            copy.BackgroundArgb = BackgroundArgb;
            copy.AutoBaseSize = AutoBaseSize;
            copy.BaseSize = BaseSize;
            copy.Format = Format;
            copy.JpegQuality = JpegQuality;
            copy.OutputSizeMode = OutputSizeMode;
            copy.OutputTarget = OutputTarget;
            copy.GridRows = GridRows;
            copy.GridColumns = GridColumns;
            copy.RowGap = RowGap;
            copy.ColumnGap = ColumnGap;
            copy.ShowGridGuides = ShowGridGuides;
            copy.Merges = new List<GridRegion>();
            if (Merges != null)
            {
                for (int i = 0; i < Merges.Count; i++)
                {
                    GridRegion region = Merges[i];
                    if (region != null)
                    {
                        copy.Merges.Add(region.Clone());
                    }
                }
            }
            return copy;
        }
    }

    public sealed class LayoutItem
    {
        /// <summary>对应输入图片列表中的下标（网格模式下等于“落位顺序”）。</summary>
        public int Index;
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    public sealed class CollageLayout
    {
        public int CanvasWidth;
        public int CanvasHeight;
        public int BaseSize;
        public LayoutItem[] Items;
        public bool WasClamped;

        // ---- v1.1 网格元数据（非网格模式为默认 0/false） ----
        /// <summary>是否为网格布局。</summary>
        public bool IsGrid;
        public int GridRows;
        public int GridColumns;
        /// <summary>缩放后的单元格宽/高（输出像素）。</summary>
        public int CellWidth;
        public int CellHeight;
        /// <summary>缩放后的行/列间距（输出像素）。</summary>
        public int RowGap;
        public int ColumnGap;
        /// <summary>网格外边距（输出像素）。</summary>
        public int GridMargin;
        /// <summary>网格容量（区域数）；非网格为 0。</summary>
        public int Capacity;

        public int Count
        {
            get { return Items == null ? 0 : Items.Length; }
        }
    }

    public sealed class ImageLoadResult : IDisposable
    {
        public string FilePath;
        public string FileName;
        public int PixelWidth;
        public int PixelHeight;
        public Bitmap Preview;
        public Bitmap Thumbnail;
        public string ErrorMessage;

        public bool Success
        {
            get { return string.IsNullOrEmpty(ErrorMessage); }
        }

        public void Dispose()
        {
            if (Preview != null)
            {
                Preview.Dispose();
                Preview = null;
            }
            if (Thumbnail != null)
            {
                Thumbnail.Dispose();
                Thumbnail = null;
            }
        }
    }

    public sealed class PreviewComposition : IDisposable
    {
        public Bitmap Bitmap;
        public CollageLayout Layout;
        public float Scale;

        public void Dispose()
        {
            if (Bitmap != null)
            {
                Bitmap.Dispose();
                Bitmap = null;
            }
        }
    }

    public sealed class AppSettings
    {
        public AppSettings()
        {
            Collage = new CollageSettings();
            WindowWidth = 1180;
            WindowHeight = 780;
            WindowX = int.MinValue;
            WindowY = int.MinValue;
            WindowMaximized = false;
            AutoOpenAfterSave = false;
        }

        public CollageSettings Collage { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int WindowX { get; set; }
        public int WindowY { get; set; }
        public bool WindowMaximized { get; set; }
        public bool AutoOpenAfterSave { get; set; }
    }

    public static class AppInfo
    {
        public const string ProductName = "轻拼";
        public const string ProductNameEn = "LightCollage";
        public const string Version = "1.1.0";
        public const string PipeBaseName = "LightCollage";
        public const string RegistryVerb = "LightCollage";
        public const string MenuText = "拼图";
    }
}
