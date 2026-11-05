using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using LightCollage.Common;
using LightCollage.Core;
using LightCollage.Shell;

namespace LightCollage.App
{
    internal sealed class AboutDialog : Form
    {
        private readonly CheckBoxX _autoOpen;
        private readonly Label _menuStatus;
        private readonly AccentButton _repairButton;

        public AboutDialog(AppSettings settings)
        {
            Theme.InitializeDpi();
            Text = "关于轻拼";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.WindowBg;
            ForeColor = Theme.TextPrimary;
            Font = Theme.FontBase;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(500), S(460));

            Label title = MakeLabel("轻拼 LightCollage", S(118), S(28), S(340), S(30), Theme.FontTitle, Theme.TextPrimary);
            Label subtitle = MakeLabel("右键拼图 · 轻量、快速、保留透明", S(118), S(60), S(340), S(22), Theme.FontSmall, Theme.TextMuted);
            Label version = MakeLabel("版本 " + AppInfo.Version, S(118), S(86), S(340), S(20), Theme.FontTiny, Theme.TextDim);
            Label author = MakeLabel("作者：@孜然鸡丁", S(118), S(108), S(340), S(20), Theme.FontTiny, Theme.TextDim);

            Label features = MakeLabel(
                "· 选中一张或多张图片，右键「拼图」即可编辑\n" +
                "· 横向 / 纵向 / 网格三种拼接方式，实时预览\n" +
                "· 网格：行×列、行/列间距可调，按顺序填充、空格留空\n" +
                "· 合并单元格：相邻格子合并成大格，只放 1 张图\n" +
                "· 预览中拖动图片可对调顺序，底部缩略图同步\n" +
                "· 间距、外边距、背景色与透明度可调，设置自动记忆\n" +
                "· 输出尺寸自动，或指定宽度/高度；支持 PNG / JPG\n" +
                "· 支持 JPG / PNG / BMP / GIF / TIFF / WebP / HEIC",
                S(28), S(136), S(446), S(144), Theme.FontSmall, Theme.TextSecondary);
            features.AutoSize = false;

            Label tip = MakeLabel("Windows 11：右键菜单里的「拼图」位于「显示更多选项」中。",
                S(28), S(290), S(446), S(22), Theme.FontTiny, Theme.TextMuted);
            tip.AutoSize = false;

            _menuStatus = MakeLabel(string.Empty, S(28), S(318), S(250), S(22), Theme.FontTiny, Theme.TextMuted);
            _menuStatus.AutoSize = false;
            RefreshMenuStatus();

            _autoOpen = new CheckBoxX();
            _autoOpen.Text = "保存后自动打开图片";
            _autoOpen.Checked = settings.AutoOpenAfterSave;
            _autoOpen.Location = new Point(S(28), S(348));
            _autoOpen.Size = new Size(S(260), S(24));
            _autoOpen.BackColor = Theme.WindowBg;

            _repairButton = new AccentButton();
            _repairButton.Text = "修复 / 安装右键菜单";
            _repairButton.Style = ButtonStyle.Secondary;
            _repairButton.Location = new Point(S(28), S(392));
            _repairButton.Size = new Size(S(170), S(38));
            _repairButton.Click += RepairMenu;

            AccentButton openSettings = new AccentButton();
            openSettings.Text = "打开设置目录";
            openSettings.Style = ButtonStyle.Secondary;
            openSettings.Location = new Point(S(208), S(392));
            openSettings.Size = new Size(S(130), S(38));
            openSettings.Click += OpenSettingsDirectory;

            AccentButton close = new AccentButton();
            close.Text = "关闭";
            close.Style = ButtonStyle.Primary;
            close.Location = new Point(S(356), S(392));
            close.Size = new Size(S(112), S(38));
            close.Click += delegate { Close(); };

            Controls.Add(title);
            Controls.Add(subtitle);
            Controls.Add(version);
            Controls.Add(author);
            Controls.Add(features);
            Controls.Add(tip);
            Controls.Add(_menuStatus);
            Controls.Add(_autoOpen);
            Controls.Add(_repairButton);
            Controls.Add(openSettings);
            Controls.Add(close);

            Paint += PaintLogo;
            Shown += delegate { Theme.ApplyDarkTitleBar(this); };
        }

        public bool AutoOpenAfterSave
        {
            get { return _autoOpen.Checked; }
        }

        private int S(int value)
        {
            return Theme.S(this, value);
        }

        private Label MakeLabel(string text, int x, int y, int width, int height, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, height);
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoSize = false;
            return label;
        }

        private void PaintLogo(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.SetHighQuality(g);
            Rectangle box = new Rectangle(S(28), S(26), S(72), S(72));
            Theme.FillRounded(g, box, Theme.S(this, 16f), Theme.Accent);
            using (Pen pen = new Pen(Color.FromArgb(220, 26, 26, 26), Theme.S(this, 3f)))
            {
                g.DrawRectangle(pen, box.Left + S(17), box.Top + S(17), S(26), S(24));
                g.DrawRectangle(pen, box.Left + S(29), box.Top + S(29), S(26), S(24));
            }
        }

        private void RefreshMenuStatus()
        {
            bool installed = false;
            try
            {
                installed = MenuInstaller.IsInstalled();
            }
            catch
            {
            }
            _menuStatus.Text = installed ? "当前程序副本的右键菜单：已安装" : "当前程序副本的右键菜单：未安装";
            _menuStatus.ForeColor = installed ? Theme.AccentGlow : Theme.TextMuted;
        }

        private void RepairMenu(object sender, EventArgs e)
        {
            try
            {
                MenuInstaller.Install();
                RefreshMenuStatus();
                MessageBox.Show(this,
                    "右键菜单已安装。\r\n\r\nWindows 11 请在右键菜单中点击「显示更多选项」，再选择「拼图」。",
                    "轻拼", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "安装右键菜单失败：\r\n" + ex.Message, "轻拼",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenSettingsDirectory(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.SettingsDirectory);
                Process.Start("explorer.exe", "\"" + SettingsStore.SettingsDirectory + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开目录：\r\n" + ex.Message, "轻拼",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
