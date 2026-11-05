using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using LightCollage.Common;

namespace LightCollage.Core
{
    /// <summary>
    /// ARGB 拼接合成器：
    /// 1) 预览用编辑期缩略图合成（可能带透明，UI 负责棋盘格垫底）；
    /// 2) 保存时逐张按原图分辨率重新解码合成；
    /// 3) PNG 保留 Alpha；JPG 先把背景按自身 Alpha 压到白底，再叠加图片，输出不透明。
    /// 不对图片做任何全局透明度处理，每张图自身 Alpha 完整保留。
    /// </summary>
    public static class Compositor
    {
        private static readonly object EncoderLock = new object();
        private static ImageCodecInfo _pngEncoder;
        private static ImageCodecInfo _jpegEncoder;

        /// <summary>
        /// 用编辑期预览位图合成预览。maxWidth/maxHeight 是预览画布上限（像素），按比例 fit。
        /// settings.Format == Jpeg 时按 JPG 实际效果合成（背景/透明先压白底，输出不透明）。
        /// 空列表（或没有成功解码的图片）返回 null。Layout 输出的是最终像素坐标，Scale = 预览尺寸 / 输出尺寸。
        /// </summary>
        public static PreviewComposition ComposePreview(IList<ImageLoadResult> items, CollageSettings settings,
                                                        int maxWidth, int maxHeight)
        {
            if (settings == null)
            {
                settings = new CollageSettings();
            }
            if (items == null || items.Count == 0)
            {
                return null;
            }

            List<ImageLoadResult> usable = new List<ImageLoadResult>();
            List<ImageSize> sizes = new List<ImageSize>();
            for (int i = 0; i < items.Count; i++)
            {
                ImageLoadResult item = items[i];
                if (item == null)
                {
                    continue;
                }
                if (!item.Success || item.Preview == null)
                {
                    continue;
                }
                if (item.PixelWidth < 1 || item.PixelHeight < 1)
                {
                    continue;
                }
                usable.Add(item);
                sizes.Add(new ImageSize(item.PixelWidth, item.PixelHeight));
            }
            if (usable.Count == 0)
            {
                return null;
            }

            CollageLayout layout = LayoutEngine.Compute(sizes, settings);
            if (layout.CanvasWidth < 1 || layout.CanvasHeight < 1)
            {
                return null;
            }

            if (maxWidth < 1)
            {
                maxWidth = layout.CanvasWidth;
            }
            if (maxHeight < 1)
            {
                maxHeight = layout.CanvasHeight;
            }
            double scale = Math.Min((double)maxWidth / (double)layout.CanvasWidth,
                                    (double)maxHeight / (double)layout.CanvasHeight);
            if (scale > 1.0)
            {
                scale = 1.0;
            }
            if (scale <= 0.0)
            {
                scale = 1.0;
            }

            int previewWidth = Math.Max(1, RoundAway(layout.CanvasWidth * scale));
            int previewHeight = Math.Max(1, RoundAway(layout.CanvasHeight * scale));
            if (previewWidth > layout.CanvasWidth)
            {
                previewWidth = layout.CanvasWidth;
            }
            if (previewHeight > layout.CanvasHeight)
            {
                previewHeight = layout.CanvasHeight;
            }
            double scaleX = (double)previewWidth / (double)layout.CanvasWidth;
            double scaleY = (double)previewHeight / (double)layout.CanvasHeight;
            float actualScale = (float)scaleX;

            Bitmap canvas = null;
            try
            {
                canvas = new Bitmap(previewWidth, previewHeight, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(canvas))
                {
                    ConfigureHighQuality(graphics);
                    Color background = settings.Format == OutputFormat.Jpeg
                        ? FlattenOverWhite(settings.BackgroundColor)
                        : settings.BackgroundColor;
                    FillBackground(canvas, background);
                    using (ImageAttributes attributes = CreateTileFlipAttributes())
                    {
                        if (layout.IsGrid)
                        {
                            // 网格：按 LayoutItem.Index 取图（容量溢出时多余图片不参与预览），
                            // 区域矩形内等比 contain 居中后乘预览缩放比；与保存共用同一 contain 公式。
                            int itemCount = layout.Items.Length;
                            for (int i = 0; i < itemCount; i++)
                            {
                                LayoutItem placement = layout.Items[i];
                                if (placement == null || placement.Index < 0 || placement.Index >= usable.Count)
                                {
                                    continue;
                                }
                                Bitmap preview = usable[placement.Index].Preview;
                                Rectangle region = new Rectangle(placement.X, placement.Y,
                                    Math.Max(1, placement.Width), Math.Max(1, placement.Height));
                                Rectangle contained = CreateContainDestination(region, preview.Width, preview.Height);
                                Rectangle destination = CreatePreviewDestination(
                                    contained, scaleX, scaleY, previewWidth, previewHeight);
                                if (destination.Width < 1 || destination.Height < 1)
                                {
                                    continue;
                                }
                                graphics.DrawImage(preview, destination, 0, 0, preview.Width, preview.Height,
                                    GraphicsUnit.Pixel, attributes);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < usable.Count; i++)
                            {
                                LayoutItem placement = layout.Items[i];
                                // 边界取整：gap=0 时相邻图片在预览里严格相接，不出现 1px 透明缝/重叠
                                Rectangle destination = CreatePreviewDestination(
                                    placement, scaleX, scaleY, previewWidth, previewHeight);
                                if (destination.Width < 1 || destination.Height < 1)
                                {
                                    continue;
                                }
                                Bitmap preview = usable[i].Preview;
                                graphics.DrawImage(preview, destination, 0, 0, preview.Width, preview.Height,
                                    GraphicsUnit.Pixel, attributes);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                if (canvas != null)
                {
                    canvas.Dispose();
                }
                throw;
            }

            PreviewComposition composition = new PreviewComposition();
            composition.Bitmap = canvas;
            composition.Layout = layout;
            composition.Scale = actualScale;
            return composition;
        }

        /// <summary>
        /// 按原始分辨率合成并保存。layout 由调用方用 LayoutEngine.Compute 得到，必须与 sizes 对应。
        /// Png: 保存 ARGB；Jpeg: 先压到白底再以指定质量保存（无 Alpha）。
        /// progress 报告 0..100，可为 null。编码/写入失败抛异常（中文提示），由 UI 捕获。
        /// </summary>
        public static void Save(IList<string> paths, IList<ImageSize> sizes, CollageSettings settings,
                                CollageLayout layout, string outputPath, IProgress<int> progress)
        {
            if (string.IsNullOrEmpty(outputPath))
            {
                throw new ArgumentException("输出路径为空。", "outputPath");
            }
            if (settings == null)
            {
                settings = new CollageSettings();
            }

            string fullOutputPath;
            try
            {
                fullOutputPath = Path.GetFullPath(outputPath);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("输出路径无效：" + outputPath, "outputPath", ex);
            }
            string directory = Path.GetDirectoryName(fullOutputPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("保存目录不存在：" + (directory == null ? outputPath : directory));
            }

            Bitmap composed = Compose(paths, sizes, settings, layout, progress);
            try
            {
                if (settings.Format == OutputFormat.Jpeg)
                {
                    SaveJpeg(composed, fullOutputPath, settings.JpegQuality);
                }
                else
                {
                    SavePng(composed, fullOutputPath);
                }
            }
            catch (Exception ex)
            {
                throw WrapSaveException(fullOutputPath, ex);
            }
            finally
            {
                composed.Dispose();
            }
            if (progress != null)
            {
                progress.Report(100);
            }
        }

        /// <summary>
        /// 供测试/CLI：返回合成后的 Bitmap。
        /// PNG 返回 32bppArgb（保留 Alpha）；JPG 返回已压白底的不透明 24bpp。调用方负责 Dispose。
        /// </summary>
        public static Bitmap Compose(IList<string> paths, IList<ImageSize> sizes, CollageSettings settings,
                                     CollageLayout layout, IProgress<int> progress)
        {
            if (paths == null)
            {
                throw new ArgumentNullException("paths");
            }
            if (sizes == null)
            {
                throw new ArgumentNullException("sizes");
            }
            if (layout == null)
            {
                throw new ArgumentNullException("layout");
            }
            if (paths.Count != sizes.Count)
            {
                throw new ArgumentException("图片数量（" + paths.Count + "）与尺寸数量（" + sizes.Count + "）不一致。", "sizes");
            }
            bool grid = layout.IsGrid;
            if (grid)
            {
                // 网格模式：容量溢出时未放入网格的图片不参与保存，允许 paths.Count >= layout.Count。
                if (paths.Count < layout.Count)
                {
                    throw new ArgumentException("图片数量（" + paths.Count + "）少于布局项数量（" + layout.Count + "）。", "layout");
                }
            }
            else if (paths.Count != layout.Count)
            {
                throw new ArgumentException("图片数量（" + paths.Count + "）与布局项数量（" + layout.Count + "）不一致。", "layout");
            }
            if (paths.Count == 0)
            {
                throw new ArgumentException("没有可拼接的图片。", "paths");
            }
            if (settings == null)
            {
                settings = new CollageSettings();
            }
            if (layout.CanvasWidth < 1 || layout.CanvasHeight < 1)
            {
                throw new ArgumentException("布局画布尺寸无效：" + layout.CanvasWidth + "×" + layout.CanvasHeight + "。", "layout");
            }

            bool jpeg = settings.Format == OutputFormat.Jpeg;
            PixelFormat canvasFormat = jpeg ? PixelFormat.Format24bppRgb : PixelFormat.Format32bppArgb;
            Color background = jpeg ? FlattenOverWhite(settings.BackgroundColor) : settings.BackgroundColor;

            if (progress != null)
            {
                progress.Report(0);
            }

            Bitmap canvas = null;
            try
            {
                try
                {
                    canvas = new Bitmap(layout.CanvasWidth, layout.CanvasHeight, canvasFormat);
                }
                catch (OutOfMemoryException)
                {
                    throw new InvalidOperationException(
                        "内存不足，无法创建拼接画布（" + layout.CanvasWidth + "×" + layout.CanvasHeight
                        + "），请减少图片数量或降低基准尺寸。");
                }

                using (Graphics graphics = Graphics.FromImage(canvas))
                {
                    ConfigureHighQuality(graphics);
                    FillBackground(canvas, background);
                    using (ImageAttributes attributes = CreateTileFlipAttributes())
                    {
                        if (grid)
                        {
                            // 网格：只遍历已放入的区域（layout.Items），用 Index 取图；
                            // 区域矩形内等比 contain 居中（与预览同一公式），大图/小图都完整显示不裁切。
                            int itemCount = layout.Items == null ? 0 : layout.Items.Length;
                            for (int i = 0; i < itemCount; i++)
                            {
                                LayoutItem placement = layout.Items[i];
                                if (placement != null && placement.Index >= 0 && placement.Index < paths.Count)
                                {
                                    using (Bitmap full = ImageLoader.LoadFull(paths[placement.Index]))
                                    {
                                        Rectangle region = new Rectangle(placement.X, placement.Y,
                                            Math.Max(1, placement.Width), Math.Max(1, placement.Height));
                                        Rectangle destination = CreateContainDestination(
                                            region, full.Width, full.Height);
                                        if (destination.Width >= 1 && destination.Height >= 1)
                                        {
                                            graphics.DrawImage(full, destination, 0, 0, full.Width, full.Height,
                                                GraphicsUnit.Pixel, attributes);
                                        }
                                    }
                                }
                                if (progress != null)
                                {
                                    int percent = (int)Math.Round((i + 1) * 100.0 / itemCount,
                                        MidpointRounding.AwayFromZero);
                                    if (percent > 100)
                                    {
                                        percent = 100;
                                    }
                                    progress.Report(percent);
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i < paths.Count; i++)
                            {
                                using (Bitmap full = ImageLoader.LoadFull(paths[i]))
                                {
                                    LayoutItem placement = layout.Items[i];
                                    Rectangle destination = new Rectangle(
                                        placement.X, placement.Y,
                                        Math.Max(1, placement.Width), Math.Max(1, placement.Height));
                                    // TileFlipXY：避免双三次缩放时图片边界采样到透明，产生 gap=0 半透明缝
                                    graphics.DrawImage(full, destination, 0, 0, full.Width, full.Height,
                                        GraphicsUnit.Pixel, attributes);
                                }
                                if (progress != null)
                                {
                                    int percent = (int)Math.Round((i + 1) * 100.0 / paths.Count, MidpointRounding.AwayFromZero);
                                    if (percent > 100)
                                    {
                                        percent = 100;
                                    }
                                    progress.Report(percent);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                if (canvas != null)
                {
                    canvas.Dispose();
                }
                throw;
            }

            if (progress != null)
            {
                progress.Report(100);
            }
            return canvas;
        }

        private static void ConfigureHighQuality(Graphics graphics)
        {
            graphics.CompositingMode = CompositingMode.SourceOver;
            // 注意：不能用 CompositingQuality.HighQuality/GammaCorrected——GDI+ 会做伽马校正混合，
            // 导致半透明像素颜色偏离标准 SourceOver 结果（例如半透明绿压白 ~(187,255,187) 而非 (127,255,127)）。
            // 标准小工具/看图软件的合成语义是 sRGB 直接混合，这里保持 Default + 高质量插值。
            graphics.CompositingQuality = CompositingQuality.Default;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
        }

        /// <summary>
        /// WrapMode.TileFlipXY：GDI+ 缩放绘制时，图片边界外采样使用镜像像素而不是透明，
        /// 避免 gap=0 相邻图片之间出现半透明/背景色 1px 缝（全分辨率合成与预览共用）。
        /// </summary>
        private static ImageAttributes CreateTileFlipAttributes()
        {
            ImageAttributes attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            return attributes;
        }

        /// <summary>
        /// 计算“区域矩形内等比 contain 并居中”的整数目标矩形（不裁切、不变形）。
        /// 预览与保存共用此公式，保证两者视觉效果一致；无法绘制时返回 Empty。
        /// </summary>
        private static Rectangle CreateContainDestination(Rectangle region, int sourceWidth, int sourceHeight)
        {
            if (region.Width < 1 || region.Height < 1 || sourceWidth < 1 || sourceHeight < 1)
            {
                return Rectangle.Empty;
            }
            double scale = Math.Min((double)region.Width / (double)sourceWidth,
                                    (double)region.Height / (double)sourceHeight);
            int width = Math.Max(1, RoundAway((double)sourceWidth * scale));
            int height = Math.Max(1, RoundAway((double)sourceHeight * scale));
            if (width > region.Width)
            {
                width = region.Width;
            }
            if (height > region.Height)
            {
                height = region.Height;
            }
            int x = region.X + (region.Width - width) / 2;
            int y = region.Y + (region.Height - height) / 2;
            return new Rectangle(x, y, width, height);
        }

        /// <summary>
        /// 预览目标矩形：用边界取整，保证 gap=0 时相邻图片在预览里严格相接（无 1px 透明缝/重叠）。
        /// x1=round(X*s), x2=round((X+W)*s), 宽=x2-x1；纵向同理。
        /// </summary>
        private static Rectangle CreatePreviewDestination(LayoutItem placement, double scaleX, double scaleY,
                                                          int canvasWidth, int canvasHeight)
        {
            if (placement == null)
            {
                return Rectangle.Empty;
            }
            return CreatePreviewDestination(
                new Rectangle(placement.X, placement.Y, placement.Width, placement.Height),
                scaleX, scaleY, canvasWidth, canvasHeight);
        }

        /// <summary>把输出坐标矩形按预览缩放比换算到预览位图坐标（边界取整）。</summary>
        private static Rectangle CreatePreviewDestination(Rectangle region, double scaleX, double scaleY,
                                                          int canvasWidth, int canvasHeight)
        {
            if (canvasWidth < 1 || canvasHeight < 1)
            {
                return Rectangle.Empty;
            }
            int x1 = ClampToRange(RoundAway((double)region.X * scaleX), 0, canvasWidth);
            int x2 = ClampToRange(RoundAway(((double)region.X + (double)region.Width) * scaleX), 0, canvasWidth);
            int y1 = ClampToRange(RoundAway((double)region.Y * scaleY), 0, canvasHeight);
            int y2 = ClampToRange(RoundAway(((double)region.Y + (double)region.Height) * scaleY), 0, canvasHeight);
            if (x2 <= x1)
            {
                x2 = Math.Min(canvasWidth, x1 + 1);
            }
            if (y2 <= y1)
            {
                y2 = Math.Min(canvasHeight, y1 + 1);
            }
            if (x2 <= x1 || y2 <= y1)
            {
                return Rectangle.Empty;
            }
            return new Rectangle(x1, y1, x2 - x1, y2 - y1);
        }

        private static int RoundAway(double value)
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

        private static int ClampToRange(int value, int min, int max)
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

        /// <summary>把背景色按自身 Alpha 合成到白色底上，得到 JPG 用的不透明底色。</summary>
        private static Color FlattenOverWhite(Color color)
        {
            int alpha = color.A;
            if (alpha >= 255)
            {
                return Color.FromArgb(255, color.R, color.G, color.B);
            }
            if (alpha <= 0)
            {
                return Color.FromArgb(255, 255, 255, 255);
            }
            double ratio = alpha / 255.0;
            int red = (int)Math.Round(color.R * ratio + 255.0 * (1.0 - ratio), MidpointRounding.AwayFromZero);
            int green = (int)Math.Round(color.G * ratio + 255.0 * (1.0 - ratio), MidpointRounding.AwayFromZero);
            int blue = (int)Math.Round(color.B * ratio + 255.0 * (1.0 - ratio), MidpointRounding.AwayFromZero);
            return Color.FromArgb(255, ClampByte(red), ClampByte(green), ClampByte(blue));
        }

        private static int ClampByte(int value)
        {
            if (value < 0)
            {
                return 0;
            }
            if (value > 255)
            {
                return 255;
            }
            return value;
        }

        /// <summary>
        /// 用背景色填充整张画布（32bppArgb 保留 Alpha，24bppRgb 不透明）。
        /// 直接写像素而不是 Graphics.Clear，确保半透明背景的 Alpha 值精确落盘。
        /// </summary>
        private static void FillBackground(Bitmap bitmap, Color color)
        {
            PixelFormat format = bitmap.PixelFormat;
            int bytesPerPixel;
            if (format == PixelFormat.Format32bppArgb)
            {
                bytesPerPixel = 4;
            }
            else if (format == PixelFormat.Format24bppRgb)
            {
                bytesPerPixel = 3;
            }
            else
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.Clear(color);
                }
                return;
            }

            Rectangle rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, format);
            try
            {
                int stride = data.Stride;
                byte[] row = new byte[stride];
                int rowBytes = bitmap.Width * bytesPerPixel;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int offset = x * bytesPerPixel;
                    row[offset] = color.B;
                    row[offset + 1] = color.G;
                    row[offset + 2] = color.R;
                    if (bytesPerPixel == 4)
                    {
                        row[offset + 3] = color.A;
                    }
                }
                // 行尾对齐填充沿用最后一个像素，避免出现未定义字节
                if (stride > rowBytes)
                {
                    byte lastB = color.B;
                    byte lastG = color.G;
                    byte lastR = color.R;
                    byte lastA = bytesPerPixel == 4 ? color.A : (byte)255;
                    for (int x = rowBytes; x < stride; x += bytesPerPixel)
                    {
                        row[x] = lastB;
                        if (x + 1 < stride)
                        {
                            row[x + 1] = lastG;
                        }
                        if (x + 2 < stride)
                        {
                            row[x + 2] = lastR;
                        }
                        if (bytesPerPixel == 4 && x + 3 < stride)
                        {
                            row[x + 3] = lastA;
                        }
                    }
                }
                for (int y = 0; y < bitmap.Height; y++)
                {
                    IntPtr destination = new IntPtr(data.Scan0.ToInt64() + (long)y * stride);
                    Marshal.Copy(row, 0, destination, stride);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static void SavePng(Bitmap bitmap, string path)
        {
            ImageCodecInfo encoder = GetEncoder(ImageFormat.Png, true);
            if (encoder == null)
            {
                bitmap.Save(path, ImageFormat.Png);
                return;
            }
            bitmap.Save(path, encoder, null);
        }

        private static void SaveJpeg(Bitmap bitmap, string path, int quality)
        {
            if (quality < 1)
            {
                quality = 1;
            }
            if (quality > 100)
            {
                quality = 100;
            }
            ImageCodecInfo encoder = GetEncoder(ImageFormat.Jpeg, false);
            if (encoder == null)
            {
                bitmap.Save(path, ImageFormat.Jpeg);
                return;
            }
            EncoderParameters parameters = new EncoderParameters(1);
            try
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                bitmap.Save(path, encoder, parameters);
            }
            finally
            {
                parameters.Dispose();
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format, bool png)
        {
            lock (EncoderLock)
            {
                if (png)
                {
                    if (_pngEncoder == null)
                    {
                        _pngEncoder = FindEncoder(format);
                    }
                    return _pngEncoder;
                }
                if (_jpegEncoder == null)
                {
                    _jpegEncoder = FindEncoder(format);
                }
                return _jpegEncoder;
            }
        }

        private static ImageCodecInfo FindEncoder(ImageFormat format)
        {
            try
            {
                ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
                for (int i = 0; i < codecs.Length; i++)
                {
                    if (codecs[i].FormatID == format.Guid)
                    {
                        return codecs[i];
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static Exception WrapSaveException(string path, Exception error)
        {
            if (error is ArgumentException || error is DirectoryNotFoundException)
            {
                return error;
            }
            if (error is UnauthorizedAccessException)
            {
                return new InvalidOperationException("没有权限写入文件：" + path, error);
            }
            if (error is OutOfMemoryException)
            {
                return new InvalidOperationException("内存不足，保存失败：" + path, error);
            }
            if (error is IOException)
            {
                return new InvalidOperationException(
                    "保存失败，文件可能被占用或磁盘空间不足：" + path + "（" + error.Message + "）", error);
            }
            return new InvalidOperationException("保存图片失败：" + path + "（" + error.Message + "）", error);
        }
    }
}
