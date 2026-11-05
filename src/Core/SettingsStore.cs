using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LightCollage.Common;

namespace LightCollage.Core
{
    /// <summary>
    /// 设置持久化：%APPDATA%\LightCollage\settings.ini。
    /// Load 永不抛异常（缺失/损坏返回默认值并做 clamp）；Save 静默失败（不影响主流程）。
    /// </summary>
    public static class SettingsStore
    {
        /// <summary>设置目录名。</summary>
        public const string SettingsDirectoryName = "LightCollage";

        /// <summary>设置文件名。</summary>
        public const string SettingsFileName = "settings.ini";

        /// <summary>仅用于自动化测试隔离的目录覆盖环境变量；正常使用时不设置。</summary>
        public const string SettingsDirectoryOverrideVariable = "LIGHTCOLLAGE_SETTINGS_DIR";

        private const int MinWindowWidth = 640;
        private const int MaxWindowWidth = 10000;
        private const int MinWindowHeight = 480;
        private const int MaxWindowHeight = 10000;
        private const int MaxBaseSize = 100000;
        private const int MaxWindowCoordinate = 100000;
        private const int MinOutputTarget = 1;
        private const int MaxOutputTarget = 32000;

        /// <summary>设置目录（%APPDATA%\LightCollage）。</summary>
        public static string SettingsDirectory
        {
            get
            {
                string overrideDirectory = null;
                try
                {
                    overrideDirectory = Environment.GetEnvironmentVariable(SettingsDirectoryOverrideVariable);
                }
                catch (Exception)
                {
                }
                if (!string.IsNullOrEmpty(overrideDirectory))
                {
                    return overrideDirectory;
                }

                string appData = null;
                try
                {
                    appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                }
                catch (Exception)
                {
                }
                if (string.IsNullOrEmpty(appData))
                {
                    try
                    {
                        appData = Environment.GetEnvironmentVariable("APPDATA");
                    }
                    catch (Exception)
                    {
                    }
                }
                if (string.IsNullOrEmpty(appData))
                {
                    appData = Path.GetTempPath();
                }
                return Path.Combine(appData, SettingsDirectoryName);
            }
        }

        /// <summary>设置文件完整路径。</summary>
        public static string SettingsPath
        {
            get { return Path.Combine(SettingsDirectory, SettingsFileName); }
        }

        /// <summary>读取设置；文件缺失/损坏/字段非法时返回默认值或做 clamp，永不抛异常。</summary>
        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            try
            {
                string path = SettingsPath;
                if (!File.Exists(path))
                {
                    return settings;
                }
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                Dictionary<string, string> values = ParseIni(lines);

                settings.Collage.Orientation = ParseOrientation(
                    GetValue(values, "collage", "orientation"), CollageOrientation.Horizontal);
                settings.Collage.Gap = Clamp(ParseInt(GetValue(values, "collage", "gap"), 12), 0, 200);
                settings.Collage.Margin = Clamp(ParseInt(GetValue(values, "collage", "margin"), 16), 0, 200);
                settings.Collage.BackgroundArgb = ParseArgb(
                    GetValue(values, "collage", "backgroundargb"), unchecked((int)0x00000000));
                settings.Collage.AutoBaseSize = ParseBool(
                    GetValue(values, "collage", "autobasesize"), true);
                settings.Collage.BaseSize = Clamp(
                    ParseInt(GetValue(values, "collage", "basesize"), 0), 0, MaxBaseSize);
                settings.Collage.Format = ParseFormat(
                    GetValue(values, "collage", "format"), OutputFormat.Png);
                settings.Collage.JpegQuality = Clamp(
                    ParseInt(GetValue(values, "collage", "jpegquality"), 92), 1, 100);
                // 第 2 轮新增：输出尺寸模式；旧配置缺失时默认自动 / 2000
                settings.Collage.OutputSizeMode = ParseOutputSizeMode(
                    GetValue(values, "collage", "outputsizemode"), OutputSizeMode.Auto);
                settings.Collage.OutputTarget = Clamp(
                    ParseInt(GetValue(values, "collage", "outputtarget"), 2000), MinOutputTarget, MaxOutputTarget);

                // v1.1 网格拼接：旧配置缺键时用默认值；非法值 clamp，合并结构先 clamp 行列再解析。
                settings.Collage.GridRows = GridLayout.ClampRows(
                    ParseInt(GetValue(values, "collage", "gridrows"), 3));
                settings.Collage.GridColumns = GridLayout.ClampColumns(
                    ParseInt(GetValue(values, "collage", "gridcolumns"), 3));
                settings.Collage.RowGap = GridLayout.ClampGap(
                    ParseInt(GetValue(values, "collage", "rowgap"), 12));
                settings.Collage.ColumnGap = GridLayout.ClampGap(
                    ParseInt(GetValue(values, "collage", "columngap"), 12));
                settings.Collage.Merges = GridLayout.ParseMerges(
                    GetValue(values, "collage", "gridmerges"),
                    settings.Collage.GridRows, settings.Collage.GridColumns);
                settings.Collage.ShowGridGuides = ParseBool(
                    GetValue(values, "collage", "showgridguides"), true);

                settings.WindowWidth = Clamp(
                    ParseInt(GetValue(values, "window", "width"), 1180), MinWindowWidth, MaxWindowWidth);
                settings.WindowHeight = Clamp(
                    ParseInt(GetValue(values, "window", "height"), 780), MinWindowHeight, MaxWindowHeight);
                settings.WindowX = ParseCoordinate(GetValue(values, "window", "x"), int.MinValue);
                settings.WindowY = ParseCoordinate(GetValue(values, "window", "y"), int.MinValue);
                settings.WindowMaximized = ParseBool(GetValue(values, "window", "maximized"), false);
                settings.AutoOpenAfterSave = ParseBool(
                    GetValue(values, "general", "autoopenaftersave"), false);
                return settings;
            }
            catch (Exception)
            {
                return new AppSettings();
            }
        }

        /// <summary>保存设置；自动建目录，写失败静默忽略。</summary>
        public static void Save(AppSettings settings)
        {
            if (settings == null)
            {
                return;
            }
            try
            {
                CollageSettings collage = settings.Collage;
                if (collage == null)
                {
                    collage = new CollageSettings();
                    settings.Collage = collage;
                }

                StringBuilder builder = new StringBuilder();
                builder.AppendLine("; 轻拼 LightCollage 设置文件（UTF-8）");
                builder.AppendLine("; 可删除本文件以恢复默认设置");
                builder.AppendLine("[collage]");
                builder.AppendLine("Orientation=" + collage.Orientation.ToString());
                builder.AppendLine("Gap=" + collage.Gap.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("Margin=" + collage.Margin.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("BackgroundArgb=#" + collage.BackgroundArgb.ToString("X8", CultureInfo.InvariantCulture));
                builder.AppendLine("AutoBaseSize=" + (collage.AutoBaseSize ? "1" : "0"));
                builder.AppendLine("BaseSize=" + collage.BaseSize.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("Format=" + collage.Format.ToString());
                builder.AppendLine("JpegQuality=" + collage.JpegQuality.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("OutputSizeMode=" + collage.OutputSizeMode.ToString());
                builder.AppendLine("OutputTarget=" + collage.OutputTarget.ToString(CultureInfo.InvariantCulture));
                // v1.1 网格拼接：行列/间距写入前 clamp；合并结构规范化后用 1 基闭区间格式序列化。
                int gridRows = GridLayout.ClampRows(collage.GridRows);
                int gridColumns = GridLayout.ClampColumns(collage.GridColumns);
                builder.AppendLine("GridRows=" + gridRows.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("GridColumns=" + gridColumns.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("RowGap=" + GridLayout.ClampGap(collage.RowGap).ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("ColumnGap=" + GridLayout.ClampGap(collage.ColumnGap).ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("GridMerges=" + GridLayout.SerializeMerges(
                    GridLayout.NormalizeMerges(gridRows, gridColumns, collage.Merges)));
                builder.AppendLine("ShowGridGuides=" + (collage.ShowGridGuides ? "1" : "0"));
                builder.AppendLine("[window]");
                builder.AppendLine("Width=" + settings.WindowWidth.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("Height=" + settings.WindowHeight.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("X=" + settings.WindowX.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("Y=" + settings.WindowY.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("Maximized=" + (settings.WindowMaximized ? "1" : "0"));
                builder.AppendLine("[general]");
                builder.AppendLine("AutoOpenAfterSave=" + (settings.AutoOpenAfterSave ? "1" : "0"));
                builder.AppendLine();

                string directory = SettingsDirectory;
                Directory.CreateDirectory(directory);
                string path = SettingsPath;
                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, builder.ToString(), new UTF8Encoding(false));
                try
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Replace(tempPath, path, null);
                        }
                        catch (Exception)
                        {
                            File.Copy(tempPath, path, true);
                            TryDelete(tempPath);
                        }
                    }
                    else
                    {
                        File.Move(tempPath, path);
                    }
                }
                catch (Exception)
                {
                    TryDelete(tempPath);
                }
            }
            catch (Exception)
            {
                // 设置保存失败不影响主流程
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }

        private static Dictionary<string, string> ParseIni(string[] lines)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (lines == null)
            {
                return values;
            }
            string section = string.Empty;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line == null)
                {
                    continue;
                }
                line = line.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                {
                    continue;
                }
                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    if (end > 1)
                    {
                        section = line.Substring(1, end - 1).Trim().ToLowerInvariant();
                    }
                    continue;
                }
                int equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }
                string key = line.Substring(0, equals).Trim().ToLowerInvariant();
                string value = line.Substring(equals + 1).Trim();
                if (key.Length == 0)
                {
                    continue;
                }
                values[section + "." + key] = value;
            }
            return values;
        }

        private static string GetValue(Dictionary<string, string> values, string section, string key)
        {
            string value;
            if (values.TryGetValue(section + "." + key, out value))
            {
                return value;
            }
            return null;
        }

        private static int ParseInt(string raw, int fallback)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return fallback;
            }
            int value;
            if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return fallback;
        }

        private static int ParseCoordinate(string raw, int fallback)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return fallback;
            }
            int value;
            if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return fallback;
            }
            if (value == int.MinValue)
            {
                return fallback;
            }
            return Clamp(value, -MaxWindowCoordinate, MaxWindowCoordinate);
        }

        private static bool ParseBool(string raw, bool fallback)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return fallback;
            }
            string value = raw.Trim().ToLowerInvariant();
            if (value == "1" || value == "true" || value == "yes" || value == "on")
            {
                return true;
            }
            if (value == "0" || value == "false" || value == "no" || value == "off")
            {
                return false;
            }
            return fallback;
        }

        private static CollageOrientation ParseOrientation(string raw, CollageOrientation fallback)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return fallback;
            }
            string value = raw.Trim();
            if (string.Equals(value, "Grid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "G", StringComparison.OrdinalIgnoreCase)
                || value == "2")
            {
                return CollageOrientation.Grid;
            }
            if (string.Equals(value, "Vertical", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "V", StringComparison.OrdinalIgnoreCase)
                || value == "1")
            {
                return CollageOrientation.Vertical;
            }
            if (string.Equals(value, "Horizontal", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "H", StringComparison.OrdinalIgnoreCase)
                || value == "0")
            {
                return CollageOrientation.Horizontal;
            }
            return fallback;
        }

        private static OutputFormat ParseFormat(string raw, OutputFormat fallback)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return fallback;
            }
            string value = raw.Trim();
            if (string.Equals(value, "Jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Jpg", StringComparison.OrdinalIgnoreCase)
                || value == "1")
            {
                return OutputFormat.Jpeg;
            }
            if (string.Equals(value, "Png", StringComparison.OrdinalIgnoreCase) || value == "0")
            {
                return OutputFormat.Png;
            }
            return fallback;
        }

        private static OutputSizeMode ParseOutputSizeMode(string raw, OutputSizeMode fallback)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return fallback;
            }
            string value = raw.Trim();
            if (string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "A", StringComparison.OrdinalIgnoreCase)
                || value == "0")
            {
                return OutputSizeMode.Auto;
            }
            if (string.Equals(value, "Width", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "W", StringComparison.OrdinalIgnoreCase)
                || value == "1")
            {
                return OutputSizeMode.Width;
            }
            if (string.Equals(value, "Height", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "H", StringComparison.OrdinalIgnoreCase)
                || value == "2")
            {
                return OutputSizeMode.Height;
            }
            return fallback;
        }

        /// <summary>支持 "#AARRGGBB"、"#RRGGBB"、"AARRGGBB"、十进制整数（可为负）。</summary>
        private static int ParseArgb(string raw, int fallback)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return fallback;
            }
            string value = raw.Trim();
            if (value.Length == 0)
            {
                return fallback;
            }

            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            string hex = value;
            if (hex[0] == '#')
            {
                hex = hex.Substring(1);
            }
            if (hex.Length == 8)
            {
                uint unsigned;
                if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out unsigned))
                {
                    return unchecked((int)unsigned);
                }
            }
            else if (hex.Length == 6)
            {
                uint unsigned;
                if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out unsigned))
                {
                    return unchecked((int)(0xFF000000u | unsigned));
                }
            }
            return fallback;
        }

        private static int Clamp(int value, int min, int max)
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
    }
}
