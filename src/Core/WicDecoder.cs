using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;
using MediaPixelFormats = System.Windows.Media.PixelFormats;

namespace LightCollage.Core
{
    /// <summary>WIC 解码结果（Bitmap 为 32bppArgb；Orientation 为 WIC 元数据里的 EXIF 方向，未知为 1）。</summary>
    internal sealed class WicDecodeResult
    {
        public Bitmap Bitmap;
        public int Orientation;
    }

    /// <summary>
    /// 用 WIC（WPF 的 BitmapDecoder）解码 GDI+ 不支持的格式（WebP/HEIC/HEIF/AVIF 等），
    /// 然后把像素复制到 System.Drawing.Bitmap。WIC 不会自动应用 EXIF 方向，方向由调用方处理。
    /// </summary>
    internal static class WicDecoder
    {
        private const int DecodeTimeoutMs = 120000;
        private const int MaxPixels = 200000000; // 2 亿像素保护

        /// <summary>
        /// 解码首帧。返回的 Bitmap 为 32bppArgb（alpha 保留）。
        /// 解码失败抛 ImageLoadException 或原始异常（由 ImageLoader 组装中文提示）。
        /// </summary>
        public static WicDecodeResult Decode(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                throw new ImageLoadException("图片数据为空。");
            }

            WicDecodeResult result = null;
            Exception failure = null;

            // WIC 部分解码器对线程套间敏感，统一放到独立 STA 线程执行，避免 UI 线程/线程池线程差异。
            Thread worker = new Thread(delegate()
            {
                try
                {
                    result = DecodeCore(data);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            worker.IsBackground = true;
            worker.Name = "LightCollage-WIC-Decode";
            try
            {
                worker.SetApartmentState(ApartmentState.STA);
            }
            catch (Exception)
            {
                // 某些运行环境不允许设置套间状态，退化为默认线程。
            }
            worker.Start();

            bool finished = worker.Join(DecodeTimeoutMs);
            if (!finished)
            {
                throw new ImageLoadException("解码超时（超过 " + (DecodeTimeoutMs / 1000) + " 秒），文件可能过大或已损坏。");
            }
            if (failure != null)
            {
                throw failure;
            }
            if (result == null || result.Bitmap == null)
            {
                throw new ImageLoadException("图片不包含可解码的图像帧。");
            }
            return result;
        }

        private static WicDecodeResult DecodeCore(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data, false))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames == null || decoder.Frames.Count == 0)
                {
                    throw new ImageLoadException("图片不包含可解码的图像帧。");
                }

                BitmapSource frame = decoder.Frames[0];
                if (frame == null)
                {
                    throw new ImageLoadException("图片不包含可解码的图像帧。");
                }

                int width = frame.PixelWidth;
                int height = frame.PixelHeight;
                GuardPixelCount(width, height);

                int orientation = TryReadMetadataOrientation(frame);

                BitmapSource source = frame;
                FormatConvertedBitmap converted = null;
                bool needsAlphaFill = false;
                if (frame.Format != MediaPixelFormats.Bgra32 && frame.Format != MediaPixelFormats.Bgr32)
                {
                    converted = new FormatConvertedBitmap(frame, MediaPixelFormats.Bgra32, null, 0);
                    source = converted;
                }
                else if (frame.Format == MediaPixelFormats.Bgr32)
                {
                    needsAlphaFill = true;
                }

                int stride = checked(width * 4);
                long totalBytes = (long)stride * height;
                if (totalBytes > int.MaxValue)
                {
                    throw new ImageLoadException("图片尺寸过大，无法解码：" + width + "×" + height + "。");
                }
                byte[] pixels = new byte[(int)totalBytes];
                source.CopyPixels(pixels, stride, 0);
                if (converted != null)
                {
                    converted = null; // 不持有托管包装，尽快释放
                }

                if (needsAlphaFill)
                {
                    for (int i = 3; i < pixels.Length; i += 4)
                    {
                        pixels[i] = 255;
                    }
                }

                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                BitmapData bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr destination = new IntPtr(bitmapData.Scan0.ToInt64() + (long)y * bitmapData.Stride);
                        Marshal.Copy(pixels, y * stride, destination, stride);
                    }
                }
                catch (Exception)
                {
                    bitmap.Dispose();
                    throw;
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                }

                WicDecodeResult result = new WicDecodeResult();
                result.Bitmap = bitmap;
                result.Orientation = orientation;
                return result;
            }
        }

        private static int TryReadMetadataOrientation(BitmapSource frame)
        {
            try
            {
                BitmapMetadata metadata = frame.Metadata as BitmapMetadata;
                if (metadata == null)
                {
                    return 1;
                }
                string[] queries = new string[]
                {
                    "/app1/ifd/{ushort=274}",
                    "/ifd/{ushort=274}",
                    "/{ushort=274}"
                };
                for (int i = 0; i < queries.Length; i++)
                {
                    try
                    {
                        object value = metadata.GetQuery(queries[i]);
                        int orientation = ConvertToInt(value);
                        if (orientation >= 1 && orientation <= 8)
                        {
                            return orientation;
                        }
                    }
                    catch (Exception)
                    {
                        // 该查询路径不适用于当前格式，继续尝试下一个
                    }
                }
            }
            catch (Exception)
            {
                // 元数据不可用
            }
            return 1;
        }

        private static int ConvertToInt(object value)
        {
            if (value == null)
            {
                return 0;
            }
            if (value is ushort)
            {
                return (ushort)value;
            }
            if (value is short)
            {
                return (short)value;
            }
            if (value is uint)
            {
                uint raw = (uint)value;
                return raw <= int.MaxValue ? (int)raw : 0;
            }
            if (value is int)
            {
                return (int)value;
            }
            if (value is long)
            {
                long raw = (long)value;
                return raw >= int.MinValue && raw <= int.MaxValue ? (int)raw : 0;
            }
            if (value is byte)
            {
                return (byte)value;
            }
            try
            {
                return Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static void GuardPixelCount(int width, int height)
        {
            if (width < 1 || height < 1)
            {
                throw new ImageLoadException("图片尺寸无效：" + width + "×" + height + "。");
            }
            long pixels = (long)width * (long)height;
            if (pixels > MaxPixels)
            {
                throw new ImageLoadException(
                    "图片像素过大（" + width + "×" + height + "，超过 2 亿像素上限），无法处理。");
            }
        }
    }
}
