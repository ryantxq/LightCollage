using System;
using System.Runtime.InteropServices;

namespace LightCollage.Shell
{
    /// <summary>
    /// Shell 层用到的 Win32 原生方法（仅做右键菜单刷新通知，不引入 COM Shell 扩展）。
    /// </summary>
    internal static class NativeMethods
    {
        // SHChangeNotify 事件与标志（取自 shellapi.h）
        internal const int SHCNE_ASSOCCHANGED = 0x08000000;
        internal const uint SHCNF_IDLIST = 0x0000;
        internal const uint SHCNF_FLUSH = 0x1000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        /// <summary>
        /// 通知资源管理器刷新文件关联与右键菜单缓存。
        /// 注册表已经写入成功时，即使刷新失败也不影响卸载/安装结果，因此这里吞掉异常。
        /// </summary>
        internal static void NotifyAssociationsChanged()
        {
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception)
            {
                // 通知失败忽略（DllImport 缺失/受限环境等）
            }
        }
    }
}
