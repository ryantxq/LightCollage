using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using LightCollage.Common;
using Microsoft.Win32;

namespace LightCollage.Shell
{
    /// <summary>
    /// 右键菜单安装/卸载（只写 HKCU\Software\Classes，不需要管理员权限）。
    ///
    /// 注册结构（经典 Shell 菜单，Win11 位于「显示更多选项」内）：
    ///   HKCU\Software\Classes\SystemFileAssociations\&lt;扩展名&gt;\shell\LightCollage
    ///       (默认) = 拼图
    ///       MUIVerb = 拼图
    ///       Icon = "&lt;exe&gt;",0
    ///       MultiSelectModel = Player
    ///       command\(默认) = "&lt;exe&gt;" "%1"
    ///   HKCU\Software\Classes\SystemFileAssociations\image\shell\LightCollage  (图片感知类型，覆盖混合格式多选)
    /// </summary>
    public static class MenuInstaller
    {
        internal const string ClassesRootPath = @"Software\Classes";
        internal const string AssociationsRootPath = @"SystemFileAssociations";
        internal const string ImageTypeName = "image";

        /// <summary>
        /// 注册右键菜单的扩展名列表（小写带点）。
        /// 与 ImageLoader.SupportedExtensions 保持一致；Shell 不依赖 Core，故在此独立维护。
        /// </summary>
        internal static readonly string[] SupportedExtensions = new string[]
        {
            ".jpg", ".jpeg", ".jpe", ".png", ".bmp", ".gif", ".tif", ".tiff",
            ".webp", ".heic", ".heif", ".avif", ".jfif"
        };

        /// <summary>安装当前 exe 的右键菜单（只写 HKCU）。失败抛中文异常。</summary>
        public static void Install()
        {
            Install(Application.ExecutablePath);
        }

        /// <summary>卸载本工具写入的右键菜单项；菜单不存在时静默成功。失败抛中文异常。</summary>
        public static void Uninstall()
        {
            try
            {
                using (RegistryKey classesRoot = Registry.CurrentUser.OpenSubKey(ClassesRootPath, true))
                {
                    if (classesRoot != null)
                    {
                        Uninstall(classesRoot, false);
                    }
                }
                NativeMethods.NotifyAssociationsChanged();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("卸载右键菜单失败：" + ex.Message, ex);
            }
        }

        /// <summary>检查本用户注册表的右键菜单是否至少有一个扩展名的命令指向当前 exe。</summary>
        public static bool IsInstalled()
        {
            try
            {
                using (RegistryKey classesRoot = Registry.CurrentUser.OpenSubKey(ClassesRootPath, false))
                {
                    if (classesRoot == null)
                    {
                        return false;
                    }
                    return IsInstalled(classesRoot, Application.ExecutablePath);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        // 可注入 RegistryKey 的内部重载（单元测试用临时键，避免污染真实右键菜单）
        // ------------------------------------------------------------------

        /// <summary>安装到指定的 Classes 根键（测试用）。</summary>
        internal static void Install(string executablePath)
        {
            using (RegistryKey classesRoot = Registry.CurrentUser.CreateSubKey(ClassesRootPath))
            {
                if (classesRoot == null)
                {
                    throw new InvalidOperationException("无法打开 HKCU\\Software\\Classes，右键菜单安装失败。");
                }
                Install(classesRoot, executablePath, true);
            }
        }

        internal static void Install(RegistryKey classesRoot, string executablePath, bool notify)
        {
            if (classesRoot == null)
            {
                throw new InvalidOperationException("注册表根键为空，右键菜单安装失败。");
            }
            if (string.IsNullOrEmpty(executablePath))
            {
                throw new InvalidOperationException("无法确定程序路径，右键菜单安装失败。");
            }
            if (!File.Exists(executablePath))
            {
                throw new InvalidOperationException("程序文件不存在，右键菜单安装失败：" + executablePath);
            }

            string quotedExe = "\"" + executablePath + "\"";
            try
            {
                for (int i = 0; i < SupportedExtensions.Length; i++)
                {
                    WriteVerb(classesRoot, GetVerbKeyPath(SupportedExtensions[i]), quotedExe);
                }
                // 图片感知类型：覆盖「同时选中 jpg+png+webp 等混合格式」的情况
                WriteVerb(classesRoot, GetImageVerbKeyPath(), quotedExe);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("写入右键菜单注册表失败：" + ex.Message, ex);
            }

            if (notify)
            {
                NativeMethods.NotifyAssociationsChanged();
            }
        }

        /// <summary>从指定 Classes 根键卸载（测试用，不抛不存在错误）。</summary>
        internal static void Uninstall(RegistryKey classesRoot, bool notify)
        {
            if (classesRoot == null)
            {
                return;
            }

            try
            {
                string[] paths = GetVerbKeyPaths();
                for (int i = 0; i < paths.Length; i++)
                {
                    DeleteVerb(classesRoot, paths[i]);
                }
            }
            catch (Exception)
            {
                // 卸载尽量彻底，单个键删除失败不阻断其他键
            }

            if (notify)
            {
                NativeMethods.NotifyAssociationsChanged();
            }
        }

        /// <summary>指定 Classes 根键时检查是否已安装到指定 exe（测试用）。</summary>
        internal static bool IsInstalled(RegistryKey classesRoot, string executablePath)
        {
            if (classesRoot == null || string.IsNullOrEmpty(executablePath))
            {
                return false;
            }

            string quotedExe = "\"" + executablePath + "\"";
            string[] paths = GetVerbKeyPaths();
            for (int i = 0; i < paths.Length; i++)
            {
                try
                {
                    using (RegistryKey commandKey = classesRoot.OpenSubKey(paths[i] + "\\command", false))
                    {
                        if (commandKey == null)
                        {
                            continue;
                        }
                        string command = commandKey.GetValue(null) as string;
                        if (CommandMatches(command, quotedExe))
                        {
                            return true;
                        }
                    }
                }
                catch (Exception)
                {
                    // 单个键读取失败不影响其他键
                }
            }
            return false;
        }

        /// <summary>全部菜单动词键路径（相对 Classes 根键），测试与卸载共用。</summary>
        internal static string[] GetVerbKeyPaths()
        {
            string[] paths = new string[SupportedExtensions.Length + 1];
            for (int i = 0; i < SupportedExtensions.Length; i++)
            {
                paths[i] = GetVerbKeyPath(SupportedExtensions[i]);
            }
            paths[SupportedExtensions.Length] = GetImageVerbKeyPath();
            return paths;
        }

        internal static string GetVerbKeyPath(string extension)
        {
            return AssociationsRootPath + "\\" + extension + "\\shell\\" + AppInfo.RegistryVerb;
        }

        internal static string GetImageVerbKeyPath()
        {
            return AssociationsRootPath + "\\" + ImageTypeName + "\\shell\\" + AppInfo.RegistryVerb;
        }

        // ------------------------------------------------------------------
        // 注册表读写细节
        // ------------------------------------------------------------------

        private static void WriteVerb(RegistryKey classesRoot, string verbKeyPath, string quotedExe)
        {
            using (RegistryKey verbKey = classesRoot.CreateSubKey(verbKeyPath))
            {
                if (verbKey == null)
                {
                    throw new InvalidOperationException("无法创建注册表键：" + verbKeyPath);
                }
                // MUIVerb 是菜单显示文本；同时写默认值兼容少数只认默认值的旧 Shell 场景
                verbKey.SetValue(null, AppInfo.MenuText, RegistryValueKind.String);
                verbKey.SetValue("MUIVerb", AppInfo.MenuText, RegistryValueKind.String);
                verbKey.SetValue("Icon", quotedExe + ",0", RegistryValueKind.String);
                // Player：允许多选（静态注册表菜单在多选时仍可能被系统多次调用，
                // 由 SingleInstance 汇总成一次操作）
                verbKey.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);

                using (RegistryKey commandKey = verbKey.CreateSubKey("command"))
                {
                    if (commandKey == null)
                    {
                        throw new InvalidOperationException("无法创建注册表键：" + verbKeyPath + "\\command");
                    }
                    commandKey.SetValue(null, quotedExe + " \"%1\"", RegistryValueKind.String);
                }
            }
        }

        private static void DeleteVerb(RegistryKey classesRoot, string verbKeyPath)
        {
            bool exists;
            using (RegistryKey probe = classesRoot.OpenSubKey(verbKeyPath, false))
            {
                exists = probe != null;
            }
            if (!exists)
            {
                return;
            }
            // 只删本工具的动词键，保留父级扩展名键（可能还有其他软件/系统菜单项）
            classesRoot.DeleteSubKeyTree(verbKeyPath, false);
        }

        private static bool CommandMatches(string command, string quotedExe)
        {
            if (string.IsNullOrEmpty(command))
            {
                return false;
            }
            string trimmed = command.Trim();
            if (string.Equals(trimmed, quotedExe + " \"%1\"", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(trimmed, quotedExe, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }
    }
}
