using System;
using System.Collections.Generic;
using System.Drawing;
using LightCollage.Common;

namespace LightCollage.App
{
    internal sealed class EditorItem : IDisposable
    {
        public string FilePath;
        public string FileName;
        public ImageLoadResult Data;
        public bool IsLoading;
        public string LoadError;

        public bool IsReady
        {
            get { return Data != null && Data.Success; }
        }

        public int PixelWidth
        {
            get { return Data == null ? 0 : Data.PixelWidth; }
        }

        public int PixelHeight
        {
            get { return Data == null ? 0 : Data.PixelHeight; }
        }

        public ImageSize ToSize()
        {
            return new ImageSize(PixelWidth, PixelHeight);
        }

        public Bitmap Thumbnail
        {
            get { return Data == null ? null : Data.Thumbnail; }
        }

        public Bitmap Preview
        {
            get { return Data == null ? null : Data.Preview; }
        }

        public string ErrorText
        {
            get
            {
                if (!string.IsNullOrEmpty(LoadError))
                {
                    return LoadError;
                }
                if (Data != null && !string.IsNullOrEmpty(Data.ErrorMessage))
                {
                    return Data.ErrorMessage;
                }
                return string.Empty;
            }
        }

        public void Dispose()
        {
            if (Data != null)
            {
                Data.Dispose();
                Data = null;
            }
        }
    }

    internal sealed class IncomingFiles
    {
        private readonly object _sync = new object();
        private readonly List<string[]> _batches = new List<string[]>();

        public void Enqueue(string[] files)
        {
            if (files == null || files.Length == 0)
            {
                return;
            }
            lock (_sync)
            {
                _batches.Add(files);
            }
        }

        public List<string[]> Drain()
        {
            lock (_sync)
            {
                if (_batches.Count == 0)
                {
                    return null;
                }
                List<string[]> copy = new List<string[]>(_batches);
                _batches.Clear();
                return copy;
            }
        }
    }
}
