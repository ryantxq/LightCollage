using System;
using System.Collections.Generic;
using LightCollage.Common;

namespace LightCollage.Core
{
    /// <summary>
    /// 拼接布局计算：横向统一高度、纵向统一宽度；等比缩放、不裁切、居中排列。
    /// 支持输出尺寸模式：
    ///   Auto   = 现有中位数/自定义基准逻辑；
    ///   Width  = 先算自然布局，再整体等比缩放到 CanvasWidth 精确等于目标宽度；
    ///   Height = 对称（CanvasHeight 精确等于目标高度）。
    /// 缩放采用“边界取整”（x2-x1），保证 gap=0 时相邻图片坐标严格相接、无 1px 缝隙。
    /// 纯计算，不依赖任何 GDI+/UI 对象，可在任意线程调用。
    /// </summary>
    public static class LayoutEngine
    {
        /// <summary>默认输出总像素上限（8000 万）。</summary>
        public const int DefaultMaxPixels = 80000000;

        /// <summary>默认输出单边像素上限（GDI+ 安全边界内）。</summary>
        public const int DefaultMaxSide = 32000;

        private const int MaxGapOrMargin = 200;

        /// <summary>
        /// 计算自动基准尺寸：横向取所有图片高度的中位数，纵向取宽度的中位数；
        /// 偶数个取中间两个的平均值后四舍五入；结果至少为 1。空列表返回 0。
        /// </summary>
        public static int GetAutoBaseSize(IList<ImageSize> sizes, CollageOrientation orientation)
        {
            if (sizes == null || sizes.Count == 0)
            {
                return 0;
            }

            bool vertical = orientation == CollageOrientation.Vertical;
            List<int> values = new List<int>(sizes.Count);
            for (int i = 0; i < sizes.Count; i++)
            {
                ImageSize size = sizes[i];
                int value;
                if (size == null)
                {
                    value = 1;
                }
                else
                {
                    value = vertical ? size.Width : size.Height;
                }
                if (value < 1)
                {
                    value = 1;
                }
                values.Add(value);
            }

            values.Sort();
            return RoundMedian(values);
        }

        /// <summary>
        /// 网格模式的自动单元格尺寸：宽、高分别取所有图片宽、高的中位数（偶数取中间两个平均、
        /// 四舍五入、至少 1）。空列表返回 0×0。网格模式不使用 AutoBaseSize/BaseSize。
        /// </summary>
        public static ImageSize GetAutoGridCellSize(IList<ImageSize> sizes)
        {
            if (sizes == null || sizes.Count == 0)
            {
                return new ImageSize(0, 0);
            }

            List<int> widths = new List<int>(sizes.Count);
            List<int> heights = new List<int>(sizes.Count);
            for (int i = 0; i < sizes.Count; i++)
            {
                ImageSize size = sizes[i];
                int width = size == null ? 1 : size.Width;
                int height = size == null ? 1 : size.Height;
                if (width < 1)
                {
                    width = 1;
                }
                if (height < 1)
                {
                    height = 1;
                }
                widths.Add(width);
                heights.Add(height);
            }

            widths.Sort();
            heights.Sort();
            return new ImageSize(RoundMedian(widths), RoundMedian(heights));
        }

        /// <summary>已排序列表的中位数：奇数取中间值，偶数取中间两个平均值后四舍五入，至少 1。</summary>
        private static int RoundMedian(List<int> sortedValues)
        {
            int n = sortedValues.Count;
            double median;
            if ((n & 1) == 1)
            {
                median = sortedValues[n / 2];
            }
            else
            {
                median = ((double)sortedValues[n / 2 - 1] + (double)sortedValues[n / 2]) / 2.0;
            }

            int rounded = (int)Math.Round(median, MidpointRounding.AwayFromZero);
            return rounded < 1 ? 1 : rounded;
        }

        /// <summary>用默认上限（8000 万像素 / 单边 32000px）计算布局。空列表返回 0x0 布局。</summary>
        public static CollageLayout Compute(IList<ImageSize> sizes, CollageSettings settings)
        {
            return Compute(sizes, settings, DefaultMaxPixels, DefaultMaxSide);
        }

        /// <summary>
        /// 计算布局。maxPixels / maxSide 为保护上限（&lt;1 时退回默认值）。
        /// Auto 模式超限时自动等比缩小基准尺寸；Width/Height 模式超限时自动缩小目标尺寸；
        /// 两种情况都设置 WasClamped。若缩到 1 仍超限则接受最大可能值，不抛异常。
        /// </summary>
        public static CollageLayout Compute(IList<ImageSize> sizes, CollageSettings settings, int maxPixels, int maxSide)
        {
            if (sizes == null || sizes.Count == 0)
            {
                return CreateEmptyLayout();
            }
            if (settings == null)
            {
                settings = new CollageSettings();
            }
            if (maxPixels < 1)
            {
                maxPixels = DefaultMaxPixels;
            }
            if (maxSide < 1)
            {
                maxSide = DefaultMaxSide;
            }

            // v1.1：网格模式走独立分支；横向/纵向路径保持 v1.0 原实现不变。
            if (settings.Orientation == CollageOrientation.Grid)
            {
                return ComputeGrid(sizes, settings, maxPixels, maxSide);
            }

            CollageOrientation orientation = settings.Orientation == CollageOrientation.Vertical
                ? CollageOrientation.Vertical
                : CollageOrientation.Horizontal;
            int gap = Clamp(settings.Gap, 0, MaxGapOrMargin);
            int margin = Clamp(settings.Margin, 0, MaxGapOrMargin);
            int baseSize = ResolveBaseSize(sizes, settings, orientation);

            OutputSizeMode sizeMode = NormalizeSizeMode(settings.OutputSizeMode);
            if (sizeMode != OutputSizeMode.Auto && settings.OutputTarget >= 1)
            {
                return ComputeWithOutputTarget(sizes, orientation, baseSize, gap, margin, sizeMode,
                    settings.OutputTarget, maxPixels, maxSide);
            }

            return ComputeAuto(sizes, orientation, baseSize, gap, margin, maxPixels, maxSide);
        }

        private static int ResolveBaseSize(IList<ImageSize> sizes, CollageSettings settings,
                                           CollageOrientation orientation)
        {
            // 自动基准为 0 或未开启自动时：使用 settings.BaseSize（若 <1 则退回自动值）。
            int autoBase = GetAutoBaseSize(sizes, orientation);
            int baseSize = autoBase;
            if (!settings.AutoBaseSize && settings.BaseSize >= 1)
            {
                baseSize = settings.BaseSize;
            }
            if (baseSize < 1)
            {
                baseSize = 1;
            }
            return baseSize;
        }

        // ---------------------------------------------------------------- 网格模式（v1.1）

        /// <summary>
        /// 网格布局入口：行列/间距 clamp、合并规范化、行优先区域顺序、容量截断
        /// （只产出 placed = min(图片数, 容量) 个 LayoutItem，Index = 图片下标）。
        /// </summary>
        private static CollageLayout ComputeGrid(IList<ImageSize> sizes, CollageSettings settings,
                                                 int maxPixels, int maxSide)
        {
            int rows = GridLayout.ClampRows(settings.GridRows);
            int columns = GridLayout.ClampColumns(settings.GridColumns);
            List<GridRegion> merges = GridLayout.NormalizeMerges(rows, columns, settings.Merges);
            List<GridRegion> regions = GridLayout.BuildRegions(rows, columns, merges);
            int capacity = regions.Count;
            int placed = Math.Min(sizes.Count, capacity);
            int margin = Clamp(settings.Margin, 0, MaxGapOrMargin);
            int rowGap = GridLayout.ClampGap(settings.RowGap);
            int columnGap = GridLayout.ClampGap(settings.ColumnGap);

            ImageSize autoCell = GetAutoGridCellSize(sizes);
            int cellWidth = autoCell.Width < 1 ? 1 : autoCell.Width;
            int cellHeight = autoCell.Height < 1 ? 1 : autoCell.Height;

            OutputSizeMode sizeMode = NormalizeSizeMode(settings.OutputSizeMode);
            if (sizeMode != OutputSizeMode.Auto && settings.OutputTarget >= 1)
            {
                return ComputeGridWithOutputTarget(regions, placed, rows, columns,
                    cellWidth, cellHeight, rowGap, columnGap, margin,
                    sizeMode, settings.OutputTarget, maxPixels, maxSide);
            }
            return ComputeGridAuto(regions, placed, rows, columns,
                cellWidth, cellHeight, rowGap, columnGap, margin, maxPixels, maxSide);
        }

        /// <summary>网格 Auto 模式：自然尺寸超限时循环缩小 cellW/cellH（间距/边距不缩）。</summary>
        private static CollageLayout ComputeGridAuto(List<GridRegion> regions, int placed,
                                                     int rows, int columns,
                                                     int cellWidth, int cellHeight,
                                                     int rowGap, int columnGap, int margin,
                                                     int maxPixels, int maxSide)
        {
            CollageLayout layout = BuildGrid(regions, placed, rows, columns,
                cellWidth, cellHeight, rowGap, columnGap, margin);
            if (IsWithinLimits(layout, maxPixels, maxSide))
            {
                return layout;
            }

            int currentWidth = cellWidth;
            int currentHeight = cellHeight;
            for (int iteration = 0; iteration < 64; iteration++)
            {
                if (currentWidth <= 1 && currentHeight <= 1)
                {
                    // 已经缩到最小仍超限（间距/边距本身就超限）：接受最大可能值，不抛异常。
                    break;
                }

                double ratio = ComputeShrinkRatio(layout, maxPixels, maxSide);
                int nextWidth = ShrinkDimension(currentWidth, ratio);
                int nextHeight = ShrinkDimension(currentHeight, ratio);
                if (nextWidth == currentWidth && nextHeight == currentHeight)
                {
                    break;
                }
                currentWidth = nextWidth;
                currentHeight = nextHeight;

                layout = BuildGrid(regions, placed, rows, columns,
                    currentWidth, currentHeight, rowGap, columnGap, margin);
                if (IsWithinLimits(layout, maxPixels, maxSide))
                {
                    layout.WasClamped = true;
                    return layout;
                }
            }

            layout.WasClamped = true;
            return layout;
        }

        /// <summary>网格 Width/Height 模式：先算自然布局，再整体缩放到目标主尺寸（复用 ScaleLayout）。</summary>
        private static CollageLayout ComputeGridWithOutputTarget(List<GridRegion> regions, int placed,
                                                                 int rows, int columns,
                                                                 int cellWidth, int cellHeight,
                                                                 int rowGap, int columnGap, int margin,
                                                                 OutputSizeMode sizeMode, int target,
                                                                 int maxPixels, int maxSide)
        {
            bool widthMode = sizeMode == OutputSizeMode.Width;
            CollageLayout natural = BuildGrid(regions, placed, rows, columns,
                cellWidth, cellHeight, rowGap, columnGap, margin);
            if (natural.CanvasWidth < 1 || natural.CanvasHeight < 1)
            {
                // 极端无效输入：安全退回 Auto 逻辑
                return ComputeGridAuto(regions, placed, rows, columns,
                    cellWidth, cellHeight, rowGap, columnGap, margin, maxPixels, maxSide);
            }

            int desired = target < 1 ? 1 : target;
            int current = desired;
            for (int iteration = 0; iteration < 64; iteration++)
            {
                CollageLayout scaled = ScaleLayout(natural, current, widthMode);
                if (IsWithinLimits(scaled, maxPixels, maxSide))
                {
                    scaled.WasClamped = current != desired;
                    return scaled;
                }
                if (current <= 1)
                {
                    scaled.WasClamped = true;
                    return scaled;
                }

                double ratio = ComputeShrinkRatio(scaled, maxPixels, maxSide);
                int next = (int)Math.Floor(current * ratio);
                if (next >= current)
                {
                    next = current - 1;
                }
                if (next < 1)
                {
                    next = 1;
                }
                current = next;
            }

            CollageLayout fallback = ScaleLayout(natural, current, widthMode);
            fallback.WasClamped = true;
            return fallback;
        }

        /// <summary>
        /// 按网格几何构建布局（整数像素）：x(c) = margin + c*(cellW+colGap)，y(r) 同理；
        /// 区域矩形包含其跨越的内部行/列间距。Items 只包含前 placed 个区域。
        /// </summary>
        private static CollageLayout BuildGrid(List<GridRegion> regions, int placed,
                                               int rows, int columns,
                                               int cellWidth, int cellHeight,
                                               int rowGap, int columnGap, int margin)
        {
            if (cellWidth < 1)
            {
                cellWidth = 1;
            }
            if (cellHeight < 1)
            {
                cellHeight = 1;
            }
            if (rowGap < 0)
            {
                rowGap = 0;
            }
            if (columnGap < 0)
            {
                columnGap = 0;
            }
            if (margin < 0)
            {
                margin = 0;
            }

            long canvasWidth = 2L * margin + (long)columns * cellWidth
                + (long)(columns - 1) * columnGap;
            long canvasHeight = 2L * margin + (long)rows * cellHeight
                + (long)(rows - 1) * rowGap;

            LayoutItem[] items = new LayoutItem[placed];
            for (int i = 0; i < placed; i++)
            {
                GridRegion region = regions[i];
                long spanColumns = region.ColSpan < 1 ? 1 : region.ColSpan;
                long spanRows = region.RowSpan < 1 ? 1 : region.RowSpan;

                LayoutItem item = new LayoutItem();
                item.Index = i;
                item.X = ClampToInt((long)margin + (long)region.Col * ((long)cellWidth + columnGap));
                item.Y = ClampToInt((long)margin + (long)region.Row * ((long)cellHeight + rowGap));
                item.Width = ClampToInt(spanColumns * cellWidth + (spanColumns - 1) * columnGap);
                item.Height = ClampToInt(spanRows * cellHeight + (spanRows - 1) * rowGap);
                items[i] = item;
            }

            CollageLayout layout = new CollageLayout();
            layout.Items = items;
            layout.BaseSize = cellWidth;
            layout.WasClamped = false;
            layout.CanvasWidth = ClampToInt(canvasWidth);
            layout.CanvasHeight = ClampToInt(canvasHeight);
            layout.IsGrid = true;
            layout.GridRows = rows;
            layout.GridColumns = columns;
            layout.CellWidth = cellWidth;
            layout.CellHeight = cellHeight;
            layout.RowGap = rowGap;
            layout.ColumnGap = columnGap;
            layout.GridMargin = margin;
            layout.Capacity = regions.Count;
            return layout;
        }

        /// <summary>按比例缩小一个单元格维度：至少减 1，最小 1。</summary>
        private static int ShrinkDimension(int current, double ratio)
        {
            if (current <= 1)
            {
                return 1;
            }
            int next = (int)Math.Floor((double)current * ratio);
            if (next >= current)
            {
                next = current - 1;
            }
            if (next < 1)
            {
                next = 1;
            }
            return next;
        }

        // ---------------------------------------------------------------- Auto 模式

        private static CollageLayout ComputeAuto(IList<ImageSize> sizes, CollageOrientation orientation,
                                                 int baseSize, int gap, int margin, int maxPixels, int maxSide)
        {
            CollageLayout layout = Build(sizes, orientation, baseSize, gap, margin);
            if (IsWithinLimits(layout, maxPixels, maxSide))
            {
                return layout;
            }

            // 超限：逐步等比缩小基准尺寸，直到满足上限或基准已到 1。
            int current = baseSize;
            for (int iteration = 0; iteration < 64; iteration++)
            {
                CollageLayout candidate = Build(sizes, orientation, current, gap, margin);
                if (IsWithinLimits(candidate, maxPixels, maxSide))
                {
                    candidate.WasClamped = true;
                    return candidate;
                }
                if (current <= 1)
                {
                    // 已经缩到最小仍超限：接受最大可能值，不抛异常。
                    candidate.WasClamped = true;
                    return candidate;
                }

                double ratio = ComputeShrinkRatio(candidate, maxPixels, maxSide);
                int next = (int)Math.Floor(current * ratio);
                if (next >= current)
                {
                    next = current - 1;
                }
                if (next < 1)
                {
                    next = 1;
                }
                current = next;
            }

            CollageLayout fallback = Build(sizes, orientation, current, gap, margin);
            fallback.WasClamped = true;
            return fallback;
        }

        // ---------------------------------------------------------------- Width/Height 模式

        private static CollageLayout ComputeWithOutputTarget(IList<ImageSize> sizes, CollageOrientation orientation,
                                                             int baseSize, int gap, int margin,
                                                             OutputSizeMode sizeMode, int target,
                                                             int maxPixels, int maxSide)
        {
            bool widthMode = sizeMode == OutputSizeMode.Width;
            CollageLayout natural = Build(sizes, orientation, baseSize, gap, margin);
            if (natural.CanvasWidth < 1 || natural.CanvasHeight < 1)
            {
                // 极端无效输入：安全退回 Auto 逻辑
                return ComputeAuto(sizes, orientation, baseSize, gap, margin, maxPixels, maxSide);
            }

            int desired = target < 1 ? 1 : target;
            int current = desired;
            for (int iteration = 0; iteration < 64; iteration++)
            {
                CollageLayout scaled = ScaleLayout(natural, current, widthMode);
                if (IsWithinLimits(scaled, maxPixels, maxSide))
                {
                    scaled.WasClamped = current != desired;
                    return scaled;
                }
                if (current <= 1)
                {
                    scaled.WasClamped = true;
                    return scaled;
                }

                double ratio = ComputeShrinkRatio(scaled, maxPixels, maxSide);
                int next = (int)Math.Floor(current * ratio);
                if (next >= current)
                {
                    next = current - 1;
                }
                if (next < 1)
                {
                    next = 1;
                }
                current = next;
            }

            CollageLayout fallback = ScaleLayout(natural, current, widthMode);
            fallback.WasClamped = true;
            return fallback;
        }

        /// <summary>
        /// 把自然布局整体等比缩放到目标尺寸（Width 模式目标=宽，Height 模式目标=高）。
        /// 每张图的矩形用“边界取整”计算：x1=round(X*f), x2=round((X+W)*f), 宽=x2-x1，
        /// 这样 gap=0 时相邻图片的边界值完全一致，不会出现 1px 缝隙或重叠。
        /// </summary>
        private static CollageLayout ScaleLayout(CollageLayout natural, int target, bool widthMode)
        {
            int naturalMain = widthMode ? natural.CanvasWidth : natural.CanvasHeight;
            if (naturalMain < 1)
            {
                naturalMain = 1;
            }
            int targetMain = target < 1 ? 1 : target;
            double factor = (double)targetMain / (double)naturalMain;

            CollageLayout result = new CollageLayout();
            result.WasClamped = false;
            if (widthMode)
            {
                result.CanvasWidth = targetMain;
                result.CanvasHeight = Math.Max(1, RoundToInt(natural.CanvasHeight * factor));
            }
            else
            {
                result.CanvasWidth = Math.Max(1, RoundToInt(natural.CanvasWidth * factor));
                result.CanvasHeight = targetMain;
            }
            result.BaseSize = Math.Max(1, RoundToInt(natural.BaseSize * factor));

            int count = natural.Items == null ? 0 : natural.Items.Length;
            LayoutItem[] items = new LayoutItem[count];
            for (int i = 0; i < count; i++)
            {
                LayoutItem source = natural.Items[i];
                LayoutItem item = new LayoutItem();
                item.Index = i;
                item.Width = 1;
                item.Height = 1;
                if (source != null)
                {
                    int x1 = RoundToInt((double)source.X * factor);
                    int x2 = RoundToInt(((double)source.X + (double)source.Width) * factor);
                    int y1 = RoundToInt((double)source.Y * factor);
                    int y2 = RoundToInt(((double)source.Y + (double)source.Height) * factor);

                    // 极端小目标：保证每张图至少 1px，且不越出画布。
                    if (x2 <= x1)
                    {
                        x2 = x1 + 1;
                    }
                    if (y2 <= y1)
                    {
                        y2 = y1 + 1;
                    }
                    if (x1 > result.CanvasWidth)
                    {
                        x1 = result.CanvasWidth;
                    }
                    if (y1 > result.CanvasHeight)
                    {
                        y1 = result.CanvasHeight;
                    }
                    if (x2 > result.CanvasWidth)
                    {
                        x2 = result.CanvasWidth;
                    }
                    if (y2 > result.CanvasHeight)
                    {
                        y2 = result.CanvasHeight;
                    }
                    if (x2 > x1 && y2 > y1)
                    {
                        item.Index = source.Index >= 0 ? source.Index : i;
                        item.X = x1;
                        item.Y = y1;
                        item.Width = x2 - x1;
                        item.Height = y2 - y1;
                    }
                }
                items[i] = item;
            }
            result.Items = items;

            // 网格元数据同步缩放（v1.1）：间距/边距/单元格按同一比例缩放并取整，
            // 主尺寸仍精确等于目标（由 CanvasWidth/CanvasHeight 保证）。
            if (natural.IsGrid)
            {
                result.IsGrid = true;
                result.GridRows = natural.GridRows;
                result.GridColumns = natural.GridColumns;
                result.Capacity = natural.Capacity;
                result.GridMargin = Math.Max(0, RoundToInt((double)natural.GridMargin * factor));
                result.RowGap = Math.Max(0, RoundToInt((double)natural.RowGap * factor));
                result.ColumnGap = Math.Max(0, RoundToInt((double)natural.ColumnGap * factor));
                result.CellWidth = Math.Max(1, RoundToInt((double)natural.CellWidth * factor));
                result.CellHeight = Math.Max(1, RoundToInt((double)natural.CellHeight * factor));
                result.BaseSize = result.CellWidth;
            }
            return result;
        }

        // ---------------------------------------------------------------- 通用构建

        private static CollageLayout CreateEmptyLayout()
        {
            CollageLayout layout = new CollageLayout();
            layout.CanvasWidth = 0;
            layout.CanvasHeight = 0;
            layout.BaseSize = 0;
            layout.Items = new LayoutItem[0];
            layout.WasClamped = false;
            return layout;
        }

        private static CollageLayout Build(IList<ImageSize> sizes, CollageOrientation orientation,
                                           int baseSize, int gap, int margin)
        {
            int n = sizes.Count;
            LayoutItem[] items = new LayoutItem[n];
            bool vertical = orientation == CollageOrientation.Vertical;

            // 统一基准：横向=统一高度；纵向=统一宽度。
            int unified = baseSize < 1 ? 1 : baseSize;
            long crossTotal = 0; // 横向=所有图宽度之和；纵向=所有图高度之和

            for (int i = 0; i < n; i++)
            {
                ImageSize size = sizes[i];
                int sourceWidth = size == null ? 1 : size.Width;
                int sourceHeight = size == null ? 1 : size.Height;
                if (sourceWidth < 1)
                {
                    sourceWidth = 1;
                }
                if (sourceHeight < 1)
                {
                    sourceHeight = 1;
                }

                int scaledWidth;
                int scaledHeight;
                if (vertical)
                {
                    scaledWidth = unified;
                    scaledHeight = ScaleDimension(sourceHeight, unified, sourceWidth);
                }
                else
                {
                    scaledWidth = ScaleDimension(sourceWidth, unified, sourceHeight);
                    scaledHeight = unified;
                }

                LayoutItem item = new LayoutItem();
                item.Index = i;
                item.Width = scaledWidth;
                item.Height = scaledHeight;
                long crossOffset = crossTotal + (long)gap * i;
                if (vertical)
                {
                    // 纵向：统一宽度，沿 Y 轴依次向下排列
                    item.X = margin;
                    item.Y = ClampToInt(margin + crossOffset);
                }
                else
                {
                    // 横向：统一高度，沿 X 轴依次向右排列
                    item.X = ClampToInt(margin + crossOffset);
                    item.Y = margin;
                }
                items[i] = item;

                crossTotal += vertical ? scaledHeight : scaledWidth;
            }

            long crossGaps = gap * (long)Math.Max(0, n - 1);
            long canvasCross = 2L * margin + crossTotal + crossGaps;
            long canvasMain = 2L * margin + unified;

            CollageLayout layout = new CollageLayout();
            layout.Items = items;
            layout.BaseSize = unified;
            layout.WasClamped = false;
            if (vertical)
            {
                layout.CanvasWidth = ClampToInt(canvasMain);
                layout.CanvasHeight = ClampToInt(canvasCross);
            }
            else
            {
                layout.CanvasWidth = ClampToInt(canvasCross);
                layout.CanvasHeight = ClampToInt(canvasMain);
            }
            return layout;
        }

        /// <summary>result = max(1, round(sourceDimension * unified / otherDimension))</summary>
        private static int ScaleDimension(int sourceDimension, int unified, int otherDimension)
        {
            if (otherDimension < 1)
            {
                otherDimension = 1;
            }
            double scaled = (double)sourceDimension * (double)unified / (double)otherDimension;
            int rounded = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
            if (rounded < 1)
            {
                rounded = 1;
            }
            return rounded;
        }

        private static double ComputeShrinkRatio(CollageLayout layout, int maxPixels, int maxSide)
        {
            double ratio = 1.0;
            long pixels = (long)layout.CanvasWidth * (long)layout.CanvasHeight;
            if (pixels > maxPixels)
            {
                double pixelRatio = Math.Sqrt((double)maxPixels / (double)pixels);
                if (pixelRatio < ratio)
                {
                    ratio = pixelRatio;
                }
            }
            if (layout.CanvasWidth > maxSide)
            {
                double r = (double)maxSide / (double)layout.CanvasWidth;
                if (r < ratio)
                {
                    ratio = r;
                }
            }
            if (layout.CanvasHeight > maxSide)
            {
                double r = (double)maxSide / (double)layout.CanvasHeight;
                if (r < ratio)
                {
                    ratio = r;
                }
            }
            return ratio;
        }

        private static bool IsWithinLimits(CollageLayout layout, int maxPixels, int maxSide)
        {
            if (layout.CanvasWidth < 1 || layout.CanvasHeight < 1)
            {
                return false;
            }
            if (layout.CanvasWidth > maxSide || layout.CanvasHeight > maxSide)
            {
                return false;
            }
            long pixels = (long)layout.CanvasWidth * (long)layout.CanvasHeight;
            return pixels <= maxPixels;
        }

        private static OutputSizeMode NormalizeSizeMode(OutputSizeMode mode)
        {
            if (mode == OutputSizeMode.Width || mode == OutputSizeMode.Height)
            {
                return mode;
            }
            return OutputSizeMode.Auto;
        }

        /// <summary>四舍五入并限制到 int 范围（防止超大目标尺寸导致 double→int 溢出）。</summary>
        private static int RoundToInt(double value)
        {
            if (double.IsNaN(value) || value <= 0.0)
            {
                return 0;
            }
            if (value >= 2147483647.0)
            {
                return int.MaxValue;
            }
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        private static int ClampToInt(long value)
        {
            if (value < 0)
            {
                return 0;
            }
            if (value > int.MaxValue)
            {
                return int.MaxValue;
            }
            return (int)value;
        }
    }
}
