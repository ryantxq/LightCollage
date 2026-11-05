using System;

namespace LightCollage.Core
{
    /// <summary>
    /// 图片加载/解码失败时抛出的异常。Message 一律为可直接展示给用户的中文提示。
    /// </summary>
    public sealed class ImageLoadException : Exception
    {
        private readonly string _filePath;

        public ImageLoadException(string message)
            : base(message)
        {
            _filePath = null;
        }

        public ImageLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
            _filePath = null;
        }

        public ImageLoadException(string message, string filePath, Exception innerException)
            : base(message, innerException)
        {
            _filePath = filePath;
        }

        /// <summary>出错的文件路径；未知时为 null。</summary>
        public string FilePath
        {
            get { return _filePath; }
        }
    }
}
