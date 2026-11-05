using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LightCollage.Common;

namespace LightCollage.Core
{
    /// <summary>
    /// 网格结构工具（v1.1）：合并区域校验/规范化、容量、区域顺序、合并/拆分、序列化。
    /// 纯计算，不依赖 GDI+/UI，可在任意线程调用。
    /// 坐标约定：内部 0 基、闭区间；序列化（settings.ini / CLI）使用 1 基、闭区间。
    /// </summary>
    public static class GridLayout
    {
        /// <summary>行/列数下限。</summary>
        public const int MinRowsOrColumns = 1;

        /// <summary>行/列数上限。</summary>
        public const int MaxRowsOrColumns = 20;

        /// <summary>合并区域数量上限（防御性）。</summary>
        public const int MaxMerges = 200;

        /// <summary>行间距/列间距/外边距上限。</summary>
        public const int MaxGapOrMargin = 200;

        public static int ClampRows(int rows)
        {
            return Clamp(rows, MinRowsOrColumns, MaxRowsOrColumns);
        }

        public static int ClampColumns(int columns)
        {
            return Clamp(columns, MinRowsOrColumns, MaxRowsOrColumns);
        }

        public static int ClampGap(int gap)
        {
            return Clamp(gap, 0, MaxGapOrMargin);
        }

        /// <summary>
        /// 规范化合并区域：丢掉空/单格/越界/重叠的项，保留最多 MaxMerges 个，并按行优先排序。
        /// 先出现的合法项优先；后续与之重叠的项被丢弃。
        /// </summary>
        public static List<GridRegion> NormalizeMerges(int rows, int columns, IList<GridRegion> merges)
        {
            List<GridRegion> result = new List<GridRegion>();
            rows = ClampRows(rows);
            columns = ClampColumns(columns);
            if (merges == null || merges.Count == 0)
            {
                return result;
            }

            bool[,] occupied = new bool[rows, columns];
            for (int i = 0; i < merges.Count; i++)
            {
                if (result.Count >= MaxMerges)
                {
                    break;
                }
                GridRegion candidate = merges[i];
                if (candidate == null)
                {
                    continue;
                }
                int rowSpan = candidate.RowSpan < 1 ? 1 : candidate.RowSpan;
                int colSpan = candidate.ColSpan < 1 ? 1 : candidate.ColSpan;
                if (rowSpan * colSpan < 2)
                {
                    continue;
                }
                int row = candidate.Row;
                int col = candidate.Col;
                if (row < 0 || col < 0 || row + rowSpan > rows || col + colSpan > columns)
                {
                    continue;
                }

                bool overlap = false;
                for (int r = row; r < row + rowSpan && !overlap; r++)
                {
                    for (int c = col; c < col + colSpan; c++)
                    {
                        if (occupied[r, c])
                        {
                            overlap = true;
                            break;
                        }
                    }
                }
                if (overlap)
                {
                    continue;
                }

                for (int r = row; r < row + rowSpan; r++)
                {
                    for (int c = col; c < col + colSpan; c++)
                    {
                        occupied[r, c] = true;
                    }
                }
                result.Add(new GridRegion(row, col, rowSpan, colSpan));
            }

            result.Sort(CompareRegions);
            return result;
        }

        /// <summary>
        /// 生成按“行优先”排序的区域列表（填图顺序）：每个合并区域在其左上角出现一次，其余格子为单格区域。
        /// </summary>
        public static List<GridRegion> BuildRegions(int rows, int columns, IList<GridRegion> normalizedMerges)
        {
            rows = ClampRows(rows);
            columns = ClampColumns(columns);
            List<GridRegion> regions = new List<GridRegion>(rows * columns);
            Dictionary<int, GridRegion> mergedTopLeft = new Dictionary<int, GridRegion>();
            bool[,] covered = new bool[rows, columns];

            if (normalizedMerges != null)
            {
                for (int i = 0; i < normalizedMerges.Count; i++)
                {
                    GridRegion region = normalizedMerges[i];
                    if (region == null)
                    {
                        continue;
                    }
                    int rowSpan = region.RowSpan < 1 ? 1 : region.RowSpan;
                    int colSpan = region.ColSpan < 1 ? 1 : region.ColSpan;
                    if (rowSpan * colSpan < 2)
                    {
                        continue;
                    }
                    int r0 = region.Row;
                    int c0 = region.Col;
                    if (r0 < 0 || c0 < 0 || r0 + rowSpan > rows || c0 + colSpan > columns)
                    {
                        continue;
                    }
                    bool overlap = false;
                    for (int r = r0; r < r0 + rowSpan && !overlap; r++)
                    {
                        for (int c = c0; c < c0 + colSpan; c++)
                        {
                            if (covered[r, c])
                            {
                                overlap = true;
                                break;
                            }
                        }
                    }
                    if (overlap)
                    {
                        continue;
                    }
                    for (int r = r0; r < r0 + rowSpan; r++)
                    {
                        for (int c = c0; c < c0 + colSpan; c++)
                        {
                            covered[r, c] = true;
                        }
                    }
                    mergedTopLeft[r0 * columns + c0] = new GridRegion(r0, c0, rowSpan, colSpan);
                }
            }

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    if (covered[r, c])
                    {
                        GridRegion merged;
                        if (mergedTopLeft.TryGetValue(r * columns + c, out merged))
                        {
                            regions.Add(merged);
                        }
                        continue;
                    }
                    regions.Add(new GridRegion(r, c, 1, 1));
                }
            }
            return regions;
        }

        /// <summary>网格容量：区域总数（单格 + 合并区域）。</summary>
        public static int Capacity(int rows, int columns, IList<GridRegion> normalizedMerges)
        {
            return BuildRegions(rows, columns, normalizedMerges).Count;
        }

        /// <summary>返回包含指定格子的区域在填图顺序中的下标；非法坐标返回 -1。</summary>
        public static int FindRegionIndex(int rows, int columns, IList<GridRegion> normalizedMerges, int row, int column)
        {
            if (row < 0 || column < 0 || row >= ClampRows(rows) || column >= ClampColumns(columns))
            {
                return -1;
            }
            List<GridRegion> regions = BuildRegions(rows, columns, normalizedMerges);
            for (int i = 0; i < regions.Count; i++)
            {
                GridRegion region = regions[i];
                if (row >= region.Row && row <= region.EndRow && column >= region.Col && column <= region.EndCol)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 尝试把选区 (row,column,rowSpan,columnSpan) 合并成一个区域。
        /// 规则：矩形、至少 2 格、在网格内、不能与已有合并区域部分重叠（完全包含已有区域时按“扩大合并”处理）。
        /// 失败返回 false 且 merged 为规范化后的原集合副本。
        /// </summary>
        public static bool TryMerge(int rows, int columns, IList<GridRegion> merges,
                                    int row, int column, int rowSpan, int columnSpan,
                                    out List<GridRegion> merged)
        {
            merged = NormalizeMerges(rows, columns, merges);
            rows = ClampRows(rows);
            columns = ClampColumns(columns);
            int spanRows = rowSpan < 1 ? 1 : rowSpan;
            int spanCols = columnSpan < 1 ? 1 : columnSpan;
            if (spanRows * spanCols < 2)
            {
                return false;
            }
            if (row < 0 || column < 0 || row + spanRows > rows || column + spanCols > columns)
            {
                return false;
            }

            GridRegion selection = new GridRegion(row, column, spanRows, spanCols);
            List<GridRegion> kept = new List<GridRegion>();
            for (int i = 0; i < merged.Count; i++)
            {
                GridRegion region = merged[i];
                if (Intersects(region, selection))
                {
                    if (!Contains(selection, region))
                    {
                        return false;
                    }
                }
                else
                {
                    kept.Add(region);
                }
            }
            kept.Add(selection);
            merged = NormalizeMerges(rows, columns, kept);
            return true;
        }

        /// <summary>
        /// 拆分：移除“完全包含在选区内”的合并区域（用于「拆分所选」）。
        /// 若无任何区域被移除，返回规范化后的原集合副本。
        /// </summary>
        public static List<GridRegion> RemoveMergesInside(int rows, int columns, IList<GridRegion> merges,
                                                           int row, int column, int rowSpan, int columnSpan)
        {
            List<GridRegion> source = NormalizeMerges(rows, columns, merges);
            int spanRows = rowSpan < 1 ? 1 : rowSpan;
            int spanCols = columnSpan < 1 ? 1 : columnSpan;
            GridRegion selection = new GridRegion(row, column, spanRows, spanCols);
            List<GridRegion> kept = new List<GridRegion>();
            for (int i = 0; i < source.Count; i++)
            {
                if (Contains(selection, source[i]))
                {
                    continue;
                }
                kept.Add(source[i]);
            }
            return kept;
        }

        /// <summary>拆分指定格子所在的合并区域（点击拆分单个大格时使用）。</summary>
        public static List<GridRegion> RemoveMergeAt(int rows, int columns, IList<GridRegion> merges, int row, int column)
        {
            List<GridRegion> source = NormalizeMerges(rows, columns, merges);
            List<GridRegion> kept = new List<GridRegion>();
            for (int i = 0; i < source.Count; i++)
            {
                GridRegion region = source[i];
                if (row >= region.Row && row <= region.EndRow && column >= region.Col && column <= region.EndCol)
                {
                    continue;
                }
                kept.Add(region);
            }
            return kept;
        }

        /// <summary>序列化为设置/CLI 使用的 1 基闭区间格式："1,1,2,2;3,3,3,4"（无合并返回空串）。</summary>
        public static string SerializeMerges(IList<GridRegion> merges)
        {
            if (merges == null || merges.Count == 0)
            {
                return string.Empty;
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < merges.Count; i++)
            {
                GridRegion region = merges[i];
                if (region == null)
                {
                    continue;
                }
                int rowSpan = region.RowSpan < 1 ? 1 : region.RowSpan;
                int colSpan = region.ColSpan < 1 ? 1 : region.ColSpan;
                if (rowSpan * colSpan < 2)
                {
                    continue;
                }
                if (builder.Length > 0)
                {
                    builder.Append(';');
                }
                builder.Append((region.Row + 1).ToString(CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append((region.Col + 1).ToString(CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append((region.Row + rowSpan).ToString(CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append((region.Col + colSpan).ToString(CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        /// <summary>
        /// 解析 1 基闭区间格式（容忍空格、空项、非法项）："1,1,2,2; 3,3,3,4"。
        /// 结果做越界/重叠规范化；非法输入返回空列表而不抛异常。
        /// </summary>
        public static List<GridRegion> ParseMerges(string text, int rows, int columns)
        {
            List<GridRegion> raw = new List<GridRegion>();
            if (string.IsNullOrEmpty(text))
            {
                return new List<GridRegion>();
            }
            string[] items = text.Split(new char[] { ';', '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < items.Length; i++)
            {
                string item = items[i] == null ? string.Empty : items[i].Trim();
                if (item.Length == 0)
                {
                    continue;
                }
                string[] parts = item.Split(new char[] { ',' }, StringSplitOptions.None);
                if (parts.Length != 4)
                {
                    continue;
                }
                int startRow;
                int startCol;
                int endRow;
                int endCol;
                if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out startRow)
                    || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out startCol)
                    || !int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out endRow)
                    || !int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out endCol))
                {
                    continue;
                }
                if (startRow < 1 || startCol < 1 || endRow < startRow || endCol < startCol)
                {
                    continue;
                }
                raw.Add(new GridRegion(startRow - 1, startCol - 1, endRow - startRow + 1, endCol - startCol + 1));
            }
            return NormalizeMerges(rows, columns, raw);
        }

        private static bool Intersects(GridRegion a, GridRegion b)
        {
            return a.Row <= b.EndRow && b.Row <= a.EndRow && a.Col <= b.EndCol && b.Col <= a.EndCol;
        }

        private static bool Contains(GridRegion outer, GridRegion inner)
        {
            return inner.Row >= outer.Row && inner.Col >= outer.Col
                && inner.EndRow <= outer.EndRow && inner.EndCol <= outer.EndCol;
        }

        private static int CompareRegions(GridRegion a, GridRegion b)
        {
            if (a == null && b == null)
            {
                return 0;
            }
            if (a == null)
            {
                return -1;
            }
            if (b == null)
            {
                return 1;
            }
            if (a.Row != b.Row)
            {
                return a.Row.CompareTo(b.Row);
            }
            return a.Col.CompareTo(b.Col);
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
    }
}
