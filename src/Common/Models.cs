using System;
using System.Drawing;

namespace LightCollage.Common
{
    public enum CollageOrientation
    {
        Horizontal = 0,
        Vertical = 1
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
            return copy;
        }
    }

    public sealed class LayoutItem
    {
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
        public const string Version = "1.0.0";
        public const string PipeBaseName = "LightCollage";
        public const string RegistryVerb = "LightCollage";
        public const string MenuText = "拼图";
    }
}
