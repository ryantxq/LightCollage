using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace LightCollage.Core
{
    /// <summary>
    /// 读取 EXIF Orientation（0x0112），用于把手机照片转正。
    /// 支持：GDI+ 位图属性、JPEG APP1、TIFF、WebP EXIF 块、HEIC/AVIF 等容器内的 EXIF/TIFF 数据。
    /// 所有方法均不抛异常，识别不到时返回 1（正常方向）。
    /// </summary>
    internal static class ExifOrientation
    {
        public const int TagOrientation = 0x0112;
        private const int MaxScanWindow = 8 * 1024 * 1024; // 兜底字节扫描只看前 8MB

        /// <summary>从 GDI+ Bitmap 的 EXIF 属性读取；不存在/无效返回 0。</summary>
        public static int TryReadFromBitmap(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return 0;
            }
            try
            {
                int[] ids = bitmap.PropertyIdList;
                if (ids == null)
                {
                    return 0;
                }
                bool has = false;
                for (int i = 0; i < ids.Length; i++)
                {
                    if (ids[i] == TagOrientation)
                    {
                        has = true;
                        break;
                    }
                }
                if (!has)
                {
                    return 0;
                }
                PropertyItem item = bitmap.GetPropertyItem(TagOrientation);
                if (item == null || item.Value == null)
                {
                    return 0;
                }
                int value = ReadOrientationValue(item.Value, item.Len);
                return IsValidOrientation(value) ? value : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>从原始文件字节中解析 EXIF Orientation；识别不到返回 1。</summary>
        public static int ReadFromBytes(byte[] data)
        {
            if (data == null || data.Length < 8)
            {
                return 1;
            }
            try
            {
                // 1) JPEG：扫描 APP1/Exif 段
                if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
                {
                    int jpeg = ReadJpeg(data);
                    if (jpeg > 0)
                    {
                        return jpeg;
                    }
                }

                // 2) TIFF：文件头就是 TIFF 头
                if (IsTiffHeader(data, 0))
                {
                    int tiff = ParseTiff(data, 0);
                    if (tiff > 0)
                    {
                        return tiff;
                    }
                }

                // 3) WebP：RIFF 容器里的 EXIF 块
                if (IsWebpHeader(data))
                {
                    int webp = ReadWebp(data);
                    if (webp > 0)
                    {
                        return webp;
                    }
                }

                // 4) 兜底：搜索 "Exif\0\0" 签名（HEIC/HEIF/AVIF 等容器常见）
                int signature = ScanForExifSignature(data);
                if (signature > 0)
                {
                    return signature;
                }

                // 5) 最后兜底：搜索合法 TIFF 头并校验 IFD0 里确有 Orientation 标签
                return ScanForTiffHeaders(data);
            }
            catch (Exception)
            {
                return 1;
            }
        }

        private static int ReadJpeg(byte[] data)
        {
            int pos = 2;
            int guard = 0;
            while (pos + 4 <= data.Length && guard++ < 100000)
            {
                if (data[pos] != 0xFF)
                {
                    pos++;
                    continue;
                }
                byte marker = data[pos + 1];
                if (marker == 0xFF)
                {
                    pos++;
                    continue;
                }
                if (marker == 0x00 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8))
                {
                    pos += 2;
                    continue;
                }
                if (marker == 0xD9 || marker == 0xDA)
                {
                    break; // 图像数据开始/结束
                }
                int length = (data[pos + 2] << 8) | data[pos + 3];
                if (length < 2)
                {
                    break;
                }
                if (marker == 0xE1 && pos + 10 <= data.Length)
                {
                    if (data[pos + 4] == 'E' && data[pos + 5] == 'x' && data[pos + 6] == 'i'
                        && data[pos + 7] == 'f' && data[pos + 8] == 0 && data[pos + 9] == 0)
                    {
                        int orientation = ParseTiff(data, pos + 10);
                        if (orientation > 0)
                        {
                            return orientation;
                        }
                    }
                }
                pos += 2 + length;
            }
            return -1;
        }

        private static bool IsWebpHeader(byte[] data)
        {
            return data.Length >= 12
                && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
                && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P';
        }

        private static int ReadWebp(byte[] data)
        {
            int pos = 12;
            int guard = 0;
            while (pos + 8 <= data.Length && guard++ < 100000)
            {
                string fourcc = new string(new char[]
                {
                    (char)data[pos], (char)data[pos + 1], (char)data[pos + 2], (char)data[pos + 3]
                });
                long size = ReadU32(data, pos + 4, true);
                long payload = pos + 8L;
                if (size < 0 || payload + size > data.Length)
                {
                    break;
                }
                if (fourcc == "EXIF")
                {
                    int offset = (int)payload;
                    if (offset + 6 <= data.Length
                        && data[offset] == 'E' && data[offset + 1] == 'x' && data[offset + 2] == 'i'
                        && data[offset + 3] == 'f' && data[offset + 4] == 0 && data[offset + 5] == 0)
                    {
                        offset += 6;
                    }
                    int orientation = ParseTiff(data, offset);
                    if (orientation > 0)
                    {
                        return orientation;
                    }
                }
                long next = payload + size + (size & 1L); // RIFF 块按偶数对齐
                if (next <= pos)
                {
                    break;
                }
                pos = (int)next;
            }
            return -1;
        }

        private static int ScanForExifSignature(byte[] data)
        {
            int limit = data.Length < MaxScanWindow ? data.Length : MaxScanWindow;
            int matches = 0;
            for (int i = 0; i + 6 < limit; i++)
            {
                if (data[i] == 'E' && data[i + 1] == 'x' && data[i + 2] == 'i'
                    && data[i + 3] == 'f' && data[i + 4] == 0 && data[i + 5] == 0)
                {
                    matches++;
                    if (matches > 256)
                    {
                        return -1;
                    }
                    int orientation = ParseTiff(data, i + 6);
                    if (orientation > 0)
                    {
                        return orientation;
                    }
                }
            }
            return -1;
        }

        private static int ScanForTiffHeaders(byte[] data)
        {
            int limit = data.Length < MaxScanWindow ? data.Length : MaxScanWindow;
            int matches = 0;
            for (int i = 0; i + 8 < limit; i++)
            {
                if (IsTiffHeader(data, i))
                {
                    matches++;
                    if (matches > 64)
                    {
                        return -1;
                    }
                    int orientation = ParseTiff(data, i);
                    if (orientation > 0)
                    {
                        return orientation;
                    }
                }
            }
            return -1;
        }

        private static bool IsTiffHeader(byte[] data, int offset)
        {
            if (data == null || offset + 8 > data.Length)
            {
                return false;
            }
            bool little = data[offset] == 'I' && data[offset + 1] == 'I';
            bool big = data[offset] == 'M' && data[offset + 1] == 'M';
            if (!little && !big)
            {
                return false;
            }
            int magic = little ? (data[offset + 2] | (data[offset + 3] << 8))
                               : ((data[offset + 2] << 8) | data[offset + 3]);
            return magic == 42;
        }

        /// <summary>解析 TIFF/EXIF 结构中的 IFD0，返回 Orientation（1..8）；失败返回 -1。</summary>
        private static int ParseTiff(byte[] data, int tiffStart)
        {
            if (data == null || tiffStart < 0 || tiffStart + 8 > data.Length)
            {
                return -1;
            }
            bool little;
            if (data[tiffStart] == 'I' && data[tiffStart + 1] == 'I')
            {
                little = true;
            }
            else if (data[tiffStart] == 'M' && data[tiffStart + 1] == 'M')
            {
                little = false;
            }
            else
            {
                return -1;
            }
            if (ReadU16(data, tiffStart + 2, little) != 42)
            {
                return -1;
            }

            long ifdOffset = ReadU32(data, tiffStart + 4, little);
            long ifdStart = tiffStart + ifdOffset;
            if (ifdStart < 0 || ifdStart + 2 > data.Length)
            {
                return -1;
            }
            int count = ReadU16(data, (int)ifdStart, little);
            if (count < 1 || count > 4096)
            {
                return -1;
            }
            long entryStart = ifdStart + 2;
            for (int i = 0; i < count; i++)
            {
                long entry = entryStart + i * 12L;
                if (entry + 12 > data.Length)
                {
                    return -1;
                }
                int tag = ReadU16(data, (int)entry, little);
                int type = ReadU16(data, (int)entry + 2, little);
                long valueCount = ReadU32(data, (int)entry + 4, little);
                int unit = TypeSize(type);
                if (unit < 0 || valueCount < 0 || valueCount > 100000 || valueCount * unit > data.Length)
                {
                    return -1;
                }
                if (tag != TagOrientation)
                {
                    continue;
                }
                if (valueCount < 1)
                {
                    return -1;
                }
                int value;
                if (type == 3) // SHORT
                {
                    value = ReadU16(data, (int)entry + 8, little);
                }
                else if (type == 4) // LONG
                {
                    long raw = ReadU32(data, (int)entry + 8, little);
                    value = raw >= 1 && raw <= 8 ? (int)raw : 0;
                }
                else
                {
                    return -1;
                }
                return IsValidOrientation(value) ? value : -1;
            }
            return -1;
        }

        private static int ReadOrientationValue(byte[] value, int length)
        {
            if (value == null || length < 2 || value.Length < 2)
            {
                return 0;
            }
            int little = value[0] | (value[1] << 8);
            if (IsValidOrientation(little))
            {
                return little;
            }
            int big = (value[0] << 8) | value[1];
            if (IsValidOrientation(big))
            {
                return big;
            }
            if (length >= 4 && value.Length >= 4)
            {
                long raw = (uint)(value[0] | (value[1] << 8) | (value[2] << 16) | (value[3] << 24));
                if (raw >= 1 && raw <= 8)
                {
                    return (int)raw;
                }
            }
            return 0;
        }

        private static bool IsValidOrientation(int value)
        {
            return value >= 1 && value <= 8;
        }

        private static int TypeSize(int type)
        {
            switch (type)
            {
                case 1: return 1; // BYTE
                case 2: return 1; // ASCII
                case 3: return 2; // SHORT
                case 4: return 4; // LONG
                case 5: return 8; // RATIONAL
                case 6: return 1; // SBYTE
                case 7: return 1; // UNDEFINED
                case 8: return 2; // SSHORT
                case 9: return 4; // SLONG
                case 10: return 8; // SRATIONAL
                case 11: return 4; // FLOAT
                case 12: return 8; // DOUBLE
                default: return -1;
            }
        }

        private static int ReadU16(byte[] data, int offset, bool little)
        {
            if (offset < 0 || offset + 2 > data.Length)
            {
                return 0;
            }
            if (little)
            {
                return data[offset] | (data[offset + 1] << 8);
            }
            return (data[offset] << 8) | data[offset + 1];
        }

        private static long ReadU32(byte[] data, int offset, bool little)
        {
            if (offset < 0 || offset + 4 > data.Length)
            {
                return 0;
            }
            if (little)
            {
                return (long)((uint)data[offset]
                    | ((uint)data[offset + 1] << 8)
                    | ((uint)data[offset + 2] << 16)
                    | ((uint)data[offset + 3] << 24));
            }
            return (long)(((uint)data[offset] << 24)
                | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8)
                | (uint)data[offset + 3]);
        }
    }
}
