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
    /// 图片解码：常见格式走 GDI+，WebP/HEIC/HEIF/AVIF 走 WIC，解码失败再互相兜底。
    /// 负责 EXIF 方向转正、预览/缩略图生成、不锁定源文件。
    /// </summary>
    public static class ImageLoader
    {
        /// <summary>解码像素上限（2 亿像素），超过则跳过并给出提示。</summary>
        public const int MaxDecodePixels = 200000000;

        /// <summary>允许读取的最大文件字节数（1GB），超过视为异常大文件。</summary>
        public const long MaxFileBytes = 1024L * 1024L * 1024L;

        private static readonly string[] ExtensionArray = new string[]
        {
            ".jpg", ".jpeg", ".jpe", ".png", ".bmp", ".gif", ".tif", ".tiff",
            ".webp", ".heic", ".heif", ".avif", ".jfif"
        };

        private static readonly HashSet<string> ExtensionSet = CreateExtensionSet();

        private static readonly string[] WicOnlyExtensions = new string[]
        {
            ".webp", ".heic", ".heif", ".avif"
        };

        /// <summary>支持的图片扩展名（小写、带点）。</summary>
        public static string[] SupportedExtensions
        {
            get { return (string[])ExtensionArray.Clone(); }
        }

        /// <summary>OpenFileDialog 用的过滤器字符串。</summary>
        public static string FileDialogFilter
        {
            get
            {
                return "图片文件|*.jpg;*.jpeg;*.jpe;*.png;*.bmp;*.gif;*.tif;*.tiff;"
                     + "*.webp;*.heic;*.heif;*.avif;*.jfif|所有文件|*.*";
            }
        }

        /// <summary>扩展名是否在支持列表内（不区分大小写）。</summary>
        public static bool IsSupportedExtension(string path)
        {
            string extension = GetExtensionLower(path);
            return extension.Length > 0 && ExtensionSet.Contains(extension);
        }

        /// <summary>扩展名支持且文件存在。</summary>
        public static bool IsSupportedFile(string path)
        {
            if (!IsSupportedExtension(path))
            {
                return false;
            }
            try
            {
                return File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 编辑期加载：解码 + EXIF 转正，并生成预览图与缩略图。永不抛异常。
        /// 失败时返回 Success=false 且 ErrorMessage 为中文提示，位图为 null。
        /// </summary>
        public static ImageLoadResult LoadForEditing(string path, int previewMaxSide, int thumbMaxSide)
        {
            ImageLoadResult result = new ImageLoadResult();
            result.FilePath = path == null ? string.Empty : path;
            result.FileName = SafeFileName(path);
            result.ErrorMessage = null;
            result.PixelWidth = 0;
            result.PixelHeight = 0;
            result.Preview = null;
            result.Thumbnail = null;

            Bitmap full = null;
            try
            {
                string fullPath = ValidatePath(path);
                byte[] data = ReadAllBytes(fullPath);
                full = DecodeOriented(fullPath, data);
                result.PixelWidth = full.Width;
                result.PixelHeight = full.Height;
                if (previewMaxSide > 0)
                {
                    result.Preview = ScaleToMaxSide(full, previewMaxSide);
                }
                if (thumbMaxSide > 0)
                {
                    result.Thumbnail = ScaleToMaxSide(full, thumbMaxSide);
                }
                return result;
            }
            catch (Exception ex)
            {
                if (result.Preview != null)
                {
                    result.Preview.Dispose();
                    result.Preview = null;
                }
                if (result.Thumbnail != null)
                {
                    result.Thumbnail.Dispose();
                    result.Thumbnail = null;
                }
                result.PixelWidth = 0;
                result.PixelHeight = 0;
                result.ErrorMessage = BuildEditingErrorMessage(path, ex);
                return result;
            }
            finally
            {
                if (full != null)
                {
                    full.Dispose();
                }
            }
        }

        /// <summary>
        /// 保存期按原图分辨率加载并应用 EXIF 转正。失败抛 ImageLoadException（中文 Message）。
        /// 返回的位图固定为 32bppArgb。
        /// </summary>
        public static Bitmap LoadFull(string path)
        {
            string fullPath = ValidatePath(path);
            byte[] data = ReadAllBytes(fullPath);
            try
            {
                return DecodeOriented(fullPath, data);
            }
            catch (ImageLoadException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ImageLoadException(BuildEditingErrorMessage(path, ex), fullPath, ex);
            }
        }

        private static HashSet<string> CreateExtensionSet()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ExtensionArray.Length; i++)
            {
                set.Add(ExtensionArray[i]);
            }
            return set;
        }

        private static Bitmap DecodeOriented(string path, byte[] data)
        {
            string extension = GetExtensionLower(path);
            bool wicFirst = IsWicOnlyExtension(extension);

            Bitmap bitmap = null;
            Exception gdiError = null;
            int gdiOrientation = 0;

            if (!wicFirst)
            {
                try
                {
                    bitmap = DecodeGdi(data);
                    gdiOrientation = ExifOrientation.TryReadFromBitmap(bitmap);
                }
                catch (Exception ex)
                {
                    gdiError = ex;
                    if (bitmap != null)
                    {
                        bitmap.Dispose();
                        bitmap = null;
                    }
                }
            }

            if (bitmap == null)
            {
                WicDecodeResult wic = null;
                Exception wicError = null;
                try
                {
                    wic = WicDecoder.Decode(data);
                }
                catch (Exception ex)
                {
                    wicError = ex;
                }

                if (wic == null || wic.Bitmap == null)
                {
                    throw CreateDecodeFailure(extension, gdiError, wicError);
                }

                bitmap = wic.Bitmap;
                int orientation = NormalizeOrientation(wic.Orientation);
                if (orientation == 1)
                {
                    orientation = ExifOrientation.ReadFromBytes(data);
                }
                ApplyOrientationInPlace(bitmap, orientation);
                return bitmap;
            }

            int finalOrientation = NormalizeOrientation(gdiOrientation);
            if (finalOrientation == 1)
            {
                finalOrientation = ExifOrientation.ReadFromBytes(data);
            }
            ApplyOrientationInPlace(bitmap, finalOrientation);
            return bitmap;
        }

        /// <summary>GDI+ 解码首帧并规范化成 32bppArgb（保留 Alpha）。</summary>
        private static Bitmap DecodeGdi(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data, false))
            {
                using (Image image = Image.FromStream(stream, false, true))
                {
                    int width = image.Width;
                    int height = image.Height;
                    GuardPixelCount(width, height);

                    Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    try
                    {
                        using (Graphics graphics = Graphics.FromImage(bitmap))
                        {
                            // SourceCopy：直接把源像素（含 Alpha）复制到 32bpp 画布，避免半透明像素被二次合成。
                            graphics.CompositingMode = CompositingMode.SourceCopy;
                            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                            graphics.PixelOffsetMode = PixelOffsetMode.Half;
                            graphics.SmoothingMode = SmoothingMode.None;
                            graphics.Clear(Color.Transparent);
                            graphics.DrawImage(image,
                                new Rectangle(0, 0, width, height), 0, 0, width, height, GraphicsUnit.Pixel);
                        }
                    }
                    catch (Exception)
                    {
                        bitmap.Dispose();
                        throw;
                    }
                    return bitmap;
                }
            }
        }

        private static Bitmap ScaleToMaxSide(Bitmap source, int maxSide)
        {
            if (maxSide < 1)
            {
                maxSide = 1;
            }
            int width = source.Width;
            int height = source.Height;
            int longSide = Math.Max(width, height);
            if (longSide <= maxSide)
            {
                return CopyBitmap(source);
            }

            double scale = (double)maxSide / (double)longSide;
            int newWidth = (int)Math.Round(width * scale, MidpointRounding.AwayFromZero);
            int newHeight = (int)Math.Round(height * scale, MidpointRounding.AwayFromZero);
            if (newWidth < 1)
            {
                newWidth = 1;
            }
            if (newHeight < 1)
            {
                newHeight = 1;
            }
            if (newWidth > maxSide)
            {
                newWidth = maxSide;
            }
            if (newHeight > maxSide)
            {
                newHeight = maxSide;
            }
            if (newWidth == width && newHeight == height)
            {
                return CopyBitmap(source);
            }

            Bitmap result = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(result))
                {
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    // 与 Compositor 一致：用标准 sRGB 混合（GammaCorrected/HighQuality 会偏色）
                    graphics.CompositingQuality = CompositingQuality.Default;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.Clear(Color.Transparent);
                    // TileFlipXY：缩放时图片边界采样镜像像素，避免预览缩略图边缘出现半透明毛边
                    // （否则 gap=0 拼接时预览相邻图之间会透出背景）。
                    using (ImageAttributes attributes = new ImageAttributes())
                    {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        graphics.DrawImage(source,
                            new Rectangle(0, 0, newWidth, newHeight), 0, 0, width, height,
                            GraphicsUnit.Pixel, attributes);
                    }
                }
            }
            catch (Exception)
            {
                result.Dispose();
                throw;
            }
            return result;
        }

        /// <summary>逐行精确复制 32bppArgb 位图（不缩放、不混合）。</summary>
        private static Bitmap CopyBitmap(Bitmap source)
        {
            int width = source.Width;
            int height = source.Height;
            if (source.PixelFormat != PixelFormat.Format32bppArgb)
            {
                Bitmap converted = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics graphics = Graphics.FromImage(converted))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.Clear(Color.Transparent);
                        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
                    }
                }
                catch (Exception)
                {
                    converted.Dispose();
                    throw;
                }
                return converted;
            }

            Bitmap copy = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            Rectangle rectangle = new Rectangle(0, 0, width, height);
            BitmapData sourceData = null;
            BitmapData copyData = null;
            try
            {
                sourceData = source.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                copyData = copy.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                int rowBytes = width * 4;
                byte[] row = new byte[rowBytes];
                for (int y = 0; y < height; y++)
                {
                    IntPtr sourceRow = new IntPtr(sourceData.Scan0.ToInt64() + (long)y * sourceData.Stride);
                    Marshal.Copy(sourceRow, row, 0, rowBytes);
                    IntPtr copyRow = new IntPtr(copyData.Scan0.ToInt64() + (long)y * copyData.Stride);
                    Marshal.Copy(row, 0, copyRow, rowBytes);
                }
            }
            catch (Exception)
            {
                copy.Dispose();
                throw;
            }
            finally
            {
                if (sourceData != null)
                {
                    source.UnlockBits(sourceData);
                }
                if (copyData != null)
                {
                    try
                    {
                        copy.UnlockBits(copyData);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            return copy;
        }

        private static void ApplyOrientationInPlace(Bitmap bitmap, int orientation)
        {
            if (bitmap == null)
            {
                return;
            }
            RotateFlipType rotateFlip;
            switch (orientation)
            {
                case 2:
                    rotateFlip = RotateFlipType.RotateNoneFlipX;
                    break;
                case 3:
                    rotateFlip = RotateFlipType.Rotate180FlipNone;
                    break;
                case 4:
                    rotateFlip = RotateFlipType.RotateNoneFlipY;
                    break;
                case 5:
                    rotateFlip = RotateFlipType.Rotate90FlipX;
                    break;
                case 6:
                    rotateFlip = RotateFlipType.Rotate90FlipNone;
                    break;
                case 7:
                    rotateFlip = RotateFlipType.Rotate270FlipX;
                    break;
                case 8:
                    rotateFlip = RotateFlipType.Rotate270FlipNone;
                    break;
                default:
                    return;
            }
            bitmap.RotateFlip(rotateFlip);
        }

        private static int NormalizeOrientation(int orientation)
        {
            return orientation >= 1 && orientation <= 8 ? orientation : 1;
        }

        private static string ValidatePath(string path)
        {
            if (path == null || path.Trim().Length == 0)
            {
                throw new ImageLoadException("文件路径为空。");
            }
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                throw new ImageLoadException("文件路径无效：" + path, ex);
            }
            if (Directory.Exists(fullPath))
            {
                throw new ImageLoadException("这是一个文件夹，不是图片文件：" + path);
            }
            if (!File.Exists(fullPath))
            {
                throw new ImageLoadException("文件不存在：" + path);
            }
            long length;
            try
            {
                length = new FileInfo(fullPath).Length;
            }
            catch (Exception ex)
            {
                throw new ImageLoadException("无法读取文件信息：" + path, ex);
            }
            if (length <= 0)
            {
                throw new ImageLoadException("文件为空：" + path);
            }
            if (length > MaxFileBytes)
            {
                throw new ImageLoadException(
                    "文件过大（" + FormatSize(length) + "，上限 " + FormatSize(MaxFileBytes) + "）：" + path);
            }
            return fullPath;
        }

        private static byte[] ReadAllBytes(string fullPath)
        {
            try
            {
                // 一次性读入内存再解码：解码完成后立即释放文件句柄，不长期锁定源文件。
                return File.ReadAllBytes(fullPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new ImageLoadException("没有权限读取文件：" + fullPath, ex);
            }
            catch (FileNotFoundException ex)
            {
                throw new ImageLoadException("文件不存在：" + fullPath, ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new ImageLoadException("文件所在目录不存在：" + fullPath, ex);
            }
            catch (IOException ex)
            {
                throw new ImageLoadException("读取文件失败（可能被占用）：" + fullPath + "（" + BriefError(ex) + "）", ex);
            }
            catch (Exception ex)
            {
                throw new ImageLoadException("读取文件失败：" + fullPath + "（" + BriefError(ex) + "）", ex);
            }
        }

        private static ImageLoadException CreateDecodeFailure(string extension, Exception gdiError, Exception wicError)
        {
            if (gdiError == null && wicError is ImageLoadException)
            {
                // 例如像素过大、解码超时等，保留更具体的信息
                return (ImageLoadException)wicError;
            }

            bool wicOnly = IsWicOnlyExtension(extension);
            if (wicOnly && LooksLikeMissingCodec(wicError))
            {
                return new ImageLoadException(MissingCodecMessage(extension), wicError);
            }

            string detail = BriefError(wicError != null ? wicError : gdiError);
            string message = "图片损坏或格式不受支持";
            if (wicOnly)
            {
                message += "（该格式依赖系统图像解码器：" + CodecName(extension) + "）";
            }
            if (detail.Length > 0)
            {
                message += "；技术细节：" + detail;
            }
            return new ImageLoadException(message, wicError != null ? wicError : gdiError);
        }

        private static string BuildEditingErrorMessage(string path, Exception ex)
        {
            string fileName = SafeFileName(path);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = path == null ? "（未知文件）" : path;
            }
            ImageLoadException loadError = ex as ImageLoadException;
            if (loadError != null)
            {
                string message = loadError.Message;
                if (message.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return message;
                }
                return "无法打开「" + fileName + "」：" + message;
            }
            if (ex is OutOfMemoryException)
            {
                return "内存不足，图片可能过大，已跳过「" + fileName + "」。";
            }
            if (ex is UnauthorizedAccessException)
            {
                return "没有权限读取「" + fileName + "」。";
            }
            return "无法打开「" + fileName + "」：" + BriefError(ex);
        }

        private static bool LooksLikeMissingCodec(Exception error)
        {
            Exception current = error;
            int guard = 0;
            while (current != null && guard++ < 16)
            {
                if (current is NotSupportedException)
                {
                    return true;
                }
                COMException comException = current as COMException;
                if (comException != null && comException.HResult == unchecked((int)0x88982F50))
                {
                    return true;
                }
                string message = current.Message ?? string.Empty;
                if (message.IndexOf("imaging component", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                if (message.IndexOf("0x88982F50", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                current = current.InnerException;
            }
            return false;
        }

        private static string MissingCodecMessage(string extension)
        {
            if (extension == ".webp")
            {
                return "无法解码 WebP 文件：系统可能未安装“WebP 图像扩展”，或文件已损坏。请安装后重试。";
            }
            if (extension == ".heic" || extension == ".heif")
            {
                return "无法解码 HEIC/HEIF 文件：系统可能未安装“HEIF 图像扩展”（部分机器还需“HEVC 视频扩展”），或文件已损坏。";
            }
            if (extension == ".avif")
            {
                return "无法解码 AVIF 文件：系统可能未安装“AV1 视频扩展”（或“AVIF 图像扩展”），或文件已损坏。";
            }
            return "无法解码该图片：系统可能未安装对应的图像扩展，或文件已损坏。";
        }

        private static string CodecName(string extension)
        {
            if (extension == ".webp")
            {
                return "“WebP 图像扩展”";
            }
            if (extension == ".heic" || extension == ".heif")
            {
                return "“HEIF 图像扩展”";
            }
            if (extension == ".avif")
            {
                return "“AV1 视频扩展”或“AVIF 图像扩展”";
            }
            return "对应的系统图像扩展";
        }

        private static string BriefError(Exception error)
        {
            if (error == null)
            {
                return string.Empty;
            }
            Exception current = error;
            int guard = 0;
            while (current.InnerException != null && guard++ < 8)
            {
                current = current.InnerException;
            }
            string message = current.Message ?? string.Empty;
            message = message.Replace("\r", " ").Replace("\n", " ").Trim();
            if (message.Length > 200)
            {
                message = message.Substring(0, 200) + "…";
            }
            return message;
        }

        private static string GetExtensionLower(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }
            try
            {
                return Path.GetExtension(path).ToLowerInvariant();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool IsWicOnlyExtension(string extension)
        {
            for (int i = 0; i < WicOnlyExtensions.Length; i++)
            {
                if (WicOnlyExtensions[i] == extension)
                {
                    return true;
                }
            }
            return false;
        }

        private static string SafeFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }
            try
            {
                return Path.GetFileName(path);
            }
            catch (Exception)
            {
                return path;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return (((double)bytes) / (1024.0 * 1024.0 * 1024.0)).ToString("0.0") + " GB";
            }
            if (bytes >= 1024L * 1024L)
            {
                return (((double)bytes) / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            }
            if (bytes >= 1024L)
            {
                return (((double)bytes) / 1024.0).ToString("0.0") + " KB";
            }
            return bytes.ToString() + " B";
        }

        private static void GuardPixelCount(int width, int height)
        {
            if (width < 1 || height < 1)
            {
                throw new ImageLoadException("图片尺寸无效：" + width + "×" + height + "。");
            }
            long pixels = (long)width * (long)height;
            if (pixels > MaxDecodePixels)
            {
                throw new ImageLoadException(
                    "图片像素过大（" + width + "×" + height + "，超过 2 亿像素上限），已跳过。");
            }
        }
    }
}
