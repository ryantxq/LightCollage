using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using LightCollage.Common;

namespace LightCollage.Shell
{
    /// <summary>
    /// 主实例通过命名管道收到其他实例转交的图片路径。
    /// 事件在后台监听线程上触发，UI 层需要自行 BeginInvoke 回主线程。
    /// </summary>
    public sealed class FilesReceivedEventArgs : EventArgs
    {
        private readonly string[] _files;

        public FilesReceivedEventArgs(string[] files)
        {
            _files = files == null ? new string[0] : files;
        }

        /// <summary>本批收到的图片路径（顺序与另一实例传入顺序一致）。</summary>
        public string[] Files
        {
            get { return _files; }
        }
    }

    /// <summary>
    /// 单实例控制：命名 Mutex 判定主实例，命名管道把后续实例的图片路径转交给主实例。
    ///
    /// 典型用法（入口程序）：
    ///   using (SingleInstance instance = new SingleInstance())
    ///   {
    ///       if (!instance.IsPrimary)
    ///       {
    ///           if (instance.TryForward(files, 2500)) return;
    ///           // 转交失败：提示后退出
    ///       }
    ///       instance.StartServer();          // 必须在创建 UI 之前启动监听
    ///       // 订阅 FilesReceived 后运行主窗口
    ///   }
    ///
    /// 注意：Mutex 所有权属于创建线程，请让 Dispose 与构造函数在同一个线程执行
    /// （入口程序的 using 块天然满足）。若在其他线程 Dispose，所有权会被系统按
    /// “线程退出时放弃”处理，下次启动可通过 AbandonedMutexException 恢复正常。
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        /// <summary>单次转发允许的最大路径数量。</summary>
        public const int MaxFileCount = 500;

        /// <summary>单次转发允许的最大负载字节数（4 MB）。</summary>
        public const int MaxPayloadBytes = 4 * 1024 * 1024;

        private const int ConnectRetryDelayMs = 60;
        private const int JoinTimeoutMs = 1500;

        private readonly object _sync = new object();
        private readonly string _userTag;
        private readonly string _pipeName;
        private readonly string _mutexName;
        private readonly Mutex _mutex;
        private readonly bool _ownsMutex;
        private readonly bool _isPrimary;

        private NamedPipeServerStream _activeServer;
        private Thread _serverThread;
        private bool _serverStarted;
        private volatile bool _disposed;

        /// <summary>尝试成为主实例（命名 Mutex）。构造函数不抛异常。</summary>
        public SingleInstance()
            : this(AppInfo.PipeBaseName)
        {
        }

        /// <summary>
        /// 测试用：自定义命名对象前缀（管道/Mutex 名），避免测试 stub 与正在运行的真实实例互相干扰。
        /// 生产代码使用无参构造函数（前缀固定为 AppInfo.PipeBaseName）。
        /// </summary>
        internal SingleInstance(string baseName)
        {
            string prefix = string.IsNullOrEmpty(baseName) ? AppInfo.PipeBaseName : baseName;
            _userTag = BuildUserTag();
            _pipeName = prefix + ".Pipe." + _userTag;
            _mutexName = @"Local\" + prefix + ".SingleInstance." + _userTag;

            bool isPrimary = true; // 极端情况下（无法创建 Mutex）退化为“直接当主实例”，保证工具仍可用
            bool ownsMutex = false;
            Mutex mutex = null;
            try
            {
                mutex = new Mutex(false, _mutexName);
                try
                {
                    // 立即尝试获取所有权：拿到说明当前没有别的实例持有 -> 主实例。
                    ownsMutex = mutex.WaitOne(0, false);
                }
                catch (AbandonedMutexException)
                {
                    // 上一个实例崩溃/被强杀遗留的 Mutex：系统已把所有权交给当前线程，
                    // 这里接过所有权，正常作为主实例启动。
                    ownsMutex = true;
                }
                isPrimary = ownsMutex;
            }
            catch (Exception)
            {
                // 权限/会话限制导致命名对象不可用：退化为单进程模式，不阻塞用户使用。
                if (mutex != null)
                {
                    try { mutex.Dispose(); } catch (Exception) { }
                }
                mutex = null;
                ownsMutex = false;
                isPrimary = true;
            }

            _mutex = mutex;
            _ownsMutex = ownsMutex;
            _isPrimary = isPrimary;
        }

        /// <summary>是否为本用户的主实例（只有主实例需要 StartServer）。</summary>
        public bool IsPrimary
        {
            get { return _isPrimary; }
        }

        /// <summary>收到其他实例转交的文件列表（后台线程触发）。</summary>
        public event EventHandler<FilesReceivedEventArgs> FilesReceived;

        /// <summary>管道名（含当前用户标识，测试用）。</summary>
        internal string PipeName
        {
            get { return _pipeName; }
        }

        /// <summary>互斥量名（含当前用户标识，测试用）。</summary>
        internal string MutexName
        {
            get { return _mutexName; }
        }

        /// <summary>
        /// 主实例调用：启动后台命名管道监听线程（须在创建 UI 之前调用）。
        /// 非主实例调用时直接忽略；重复调用只启动一次。
        /// </summary>
        public void StartServer()
        {
            lock (_sync)
            {
                if (_disposed || !_isPrimary || _serverStarted)
                {
                    return;
                }
                _serverStarted = true;
                Thread thread = new Thread(ServerLoop);
                thread.IsBackground = true;
                thread.Name = "LightCollage.PipeServer";
                _serverThread = thread;
                thread.Start();
            }
        }

        /// <summary>
        /// 非主实例调用：把文件列表发给主实例。
        /// 在 timeoutMs 内循环重试连接（主实例可能尚未开始监听或正忙）；
        /// 成功写入管道返回 true，超时/失败返回 false。
        /// </summary>
        public bool TryForward(IList<string> files, int timeoutMs)
        {
            if (_disposed || files == null || files.Count == 0)
            {
                return false;
            }

            byte[] payload = BuildPayload(files);
            if (payload.Length <= 4)
            {
                // 只有 count 字段 = 没有有效路径
                return false;
            }

            if (timeoutMs <= 0)
            {
                timeoutMs = 500;
            }

            Stopwatch watch = Stopwatch.StartNew();
            while (!_disposed)
            {
                int elapsed = (int)watch.ElapsedMilliseconds;
                if (elapsed >= timeoutMs)
                {
                    break;
                }

                NamedPipeClientStream client = null;
                try
                {
                    client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.None);
                    int remaining = timeoutMs - elapsed;
                    int connectTimeout = remaining < 250 ? remaining : 250;
                    if (connectTimeout < 1)
                    {
                        connectTimeout = 1;
                    }
                    client.Connect(connectTimeout);
                    client.Write(payload, 0, payload.Length);
                    client.Flush();
                    return true;
                }
                catch (TimeoutException)
                {
                    // 管道实例忙/主实例尚未监听：稍后重试
                }
                catch (IOException)
                {
                    // 主实例已退出或管道已断开：稍后重试
                }
                catch (UnauthorizedAccessException)
                {
                    // 权限不足时重试没有意义
                    return false;
                }
                catch (Exception)
                {
                    // 其他异常（客户端被释放等）：继续重试
                }
                finally
                {
                    if (client != null)
                    {
                        try { client.Dispose(); } catch (Exception) { }
                    }
                }

                if (_disposed)
                {
                    break;
                }
                Thread.Sleep(ConnectRetryDelayMs);
            }

            return false;
        }

        /// <summary>关闭管道监听、释放 Mutex。可重复调用，不抛异常。</summary>
        public void Dispose()
        {
            Thread serverThread;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                serverThread = _serverThread;
                _serverThread = null;
            }

            // 唤醒阻塞在 WaitForConnection 上的监听线程
            if (serverThread != null && serverThread != Thread.CurrentThread && serverThread.IsAlive)
            {
                WakeServer();
                try { serverThread.Join(JoinTimeoutMs); } catch (Exception) { }
            }

            // 兜底：监听线程仍未退出时，直接释放它正在等待的管道实例
            NamedPipeServerStream activeServer;
            lock (_sync)
            {
                activeServer = _activeServer;
                _activeServer = null;
            }
            if (activeServer != null)
            {
                try { activeServer.Dispose(); } catch (Exception) { }
            }
            if (serverThread != null && serverThread != Thread.CurrentThread)
            {
                try
                {
                    if (serverThread.IsAlive)
                    {
                        serverThread.Join(500);
                    }
                }
                catch (Exception) { }
            }

            if (_ownsMutex && _mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // 不在创建线程上释放：忽略，进程退出后系统会按遗弃处理，下次启动仍可恢复
                }
                catch (Exception) { }
            }
            if (_mutex != null)
            {
                try { _mutex.Dispose(); } catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------
        // 监听线程
        // ------------------------------------------------------------------

        private void ServerLoop()
        {
            while (!_disposed)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 4,
                                                       PipeTransmissionMode.Byte, PipeOptions.None);
                    lock (_sync)
                    {
                        if (_disposed)
                        {
                            break;
                        }
                        _activeServer = server;
                    }

                    server.WaitForConnection();
                    if (_disposed)
                    {
                        break;
                    }

                    List<string> files = ReadFiles(server);
                    if (files.Count > 0)
                    {
                        RaiseFilesReceived(files);
                    }
                }
                catch (Exception)
                {
                    if (_disposed)
                    {
                        break;
                    }
                    // 客户端中途断开/管道被异常关闭等：短退避后重建实例继续监听
                    SleepQuietly(120);
                }
                finally
                {
                    lock (_sync)
                    {
                        if (object.ReferenceEquals(_activeServer, server))
                        {
                            _activeServer = null;
                        }
                    }
                    if (server != null)
                    {
                        try { server.Dispose(); } catch (Exception) { }
                    }
                }
            }
        }

        /// <summary>用一个“空连接”唤醒 WaitForConnection（仅用于 Dispose 时退出监听线程）。</summary>
        private void WakeServer()
        {
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.None))
                {
                    client.Connect(300);
                }
            }
            catch (Exception)
            {
                // 管道还没建好/已关闭：交给 Join 超时后的兜底释放处理
            }
        }

        private void RaiseFilesReceived(List<string> files)
        {
            EventHandler<FilesReceivedEventArgs> handler = FilesReceived;
            if (handler == null)
            {
                return;
            }
            try
            {
                handler(this, new FilesReceivedEventArgs(files.ToArray()));
            }
            catch (Exception)
            {
                // UI 订阅方异常不能让监听线程退出
            }
        }

        private static void SleepQuietly(int milliseconds)
        {
            try { Thread.Sleep(milliseconds); } catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // 报文编解码：int32 count + (int32 utf8Length + utf8 bytes) * count
        // ------------------------------------------------------------------

        /// <summary>把文件列表编码成管道负载（最多 500 条、总长 4 MB，超出部分丢弃）。</summary>
        internal static byte[] BuildPayload(IList<string> files)
        {
            List<byte[]> encoded = new List<byte[]>();
            if (files != null)
            {
                long budget = MaxPayloadBytes;
                for (int i = 0; i < files.Count && encoded.Count < MaxFileCount; i++)
                {
                    string file = files[i];
                    if (string.IsNullOrEmpty(file) || file.Trim().Length == 0)
                    {
                        continue;
                    }
                    byte[] bytes = Encoding.UTF8.GetBytes(file);
                    if (bytes.Length > budget - 4)
                    {
                        break; // 超过总长度限制，其余路径丢弃
                    }
                    budget -= bytes.Length + 4;
                    encoded.Add(bytes);
                }
            }

            MemoryStream memory = new MemoryStream();
            try
            {
                BinaryWriter writer = new BinaryWriter(memory, new UTF8Encoding(false));
                writer.Write(encoded.Count);
                for (int i = 0; i < encoded.Count; i++)
                {
                    writer.Write(encoded[i].Length);
                    writer.Write(encoded[i]);
                }
                writer.Flush();
                return memory.ToArray();
            }
            finally
            {
                memory.Dispose();
            }
        }

        /// <summary>从管道读出一批文件路径；负载非法/超限时返回空列表（不抛异常给调用方）。</summary>
        internal static List<string> ReadFiles(Stream stream)
        {
            List<string> result = new List<string>();
            BinaryReader reader = new BinaryReader(stream, new UTF8Encoding(false), true);
            int count = reader.ReadInt32();
            if (count <= 0 || count > MaxFileCount)
            {
                return result;
            }

            long budget = MaxPayloadBytes;
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32();
                if (length < 0 || length > budget)
                {
                    result.Clear();
                    return result;
                }
                budget -= length;
                byte[] bytes = ReadExactly(stream, length);
                if (bytes == null)
                {
                    result.Clear();
                    return result;
                }
                string path = Encoding.UTF8.GetString(bytes);
                if (!string.IsNullOrEmpty(path))
                {
                    result.Add(path);
                }
            }
            return result;
        }

        private static byte[] ReadExactly(Stream stream, int length)
        {
            if (length == 0)
            {
                return new byte[0];
            }
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read <= 0)
                {
                    return null;
                }
                offset += read;
            }
            return buffer;
        }

        // ------------------------------------------------------------------
        // 当前用户标识（用于隔离不同用户的命名对象）
        // ------------------------------------------------------------------

        private static string BuildUserTag()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    if (identity != null && identity.User != null)
                    {
                        string sid = identity.User.Value;
                        StringBuilder builder = new StringBuilder(sid.Length);
                        for (int i = 0; i < sid.Length; i++)
                        {
                            char c = sid[i];
                            if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '-')
                            {
                                builder.Append(c);
                            }
                        }
                        if (builder.Length > 0)
                        {
                            return builder.ToString();
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 拿不到 SID 时退回用户名
            }

            string name = Environment.UserName;
            StringBuilder fallback = new StringBuilder();
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < name.Length; i++)
                {
                    char c = name[i];
                    if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    {
                        fallback.Append(c);
                    }
                }
            }
            if (fallback.Length == 0)
            {
                fallback.Append("default");
            }
            return "user-" + fallback.ToString();
        }
    }
}
