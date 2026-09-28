// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 ASCOS DbMount contributors
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DbMount
{
    internal enum OpKind { Attach, Detach, Restore }

    internal sealed class DbMountForm : Form
    {
        private static readonly Color Navy = Color.FromArgb(25, 48, 78);
        private static readonly Color Blue = Color.FromArgb(43, 111, 184);
        private static readonly Color Canvas = Color.FromArgb(244, 247, 251);
        private static readonly Color Border = Color.FromArgb(210, 220, 232);
        private static readonly Color Ink = Color.FromArgb(30, 43, 61);
        private static readonly Color Muted = Color.FromArgb(97, 113, 135);
        private static readonly Color Success = Color.FromArgb(105, 213, 164);
        private static readonly Color Danger = Color.FromArgb(217, 83, 79);

        private readonly ComboBox server = new ComboBox();
        private readonly ComboBox auth = new ComboBox();
        private readonly TextBox userBox = new TextBox();
        private readonly TextBox passBox = new TextBox();
        private readonly CheckBox remember = new CheckBox();
        private readonly TextBox folderBox = new TextBox();
        private readonly Button test = new Button();
        private readonly Button browse = new Button();
        private readonly Button pickFiles = new Button();
        private readonly Button pickBackup = new Button();
        private readonly Button refresh = new Button();
        private readonly Button selectAll = new Button();
        private readonly Button clearSelection = new Button();
        private readonly Button attach = new Button();
        private readonly Button detach = new Button();
        private readonly Button restore = new Button();
        private readonly Button guide = new Button();
        private readonly Button openLog = new Button();
        private readonly Button sendLog = new Button();
        private readonly Label connStatus = new Label();
        private readonly Label status = new Label();
        private readonly Label railStatus = new Label();
        private readonly CheckedListBox list = new CheckedListBox();

        private readonly List<DatabaseFile> items = new List<DatabaseFile>();
        private readonly List<DatabaseFile> extraItems = new List<DatabaseFile>();
        private readonly BackgroundWorker testWorker = new BackgroundWorker();
        private readonly BackgroundWorker batchWorker = new BackgroundWorker();
        private readonly BackgroundWorker uploadWorker = new BackgroundWorker();
        private bool busy;
        private bool connected;
        private int batchTotal;
        private string lastRestoreFolder;
#if DEBUG
        private bool selftestActive;
        private bool suppressConfirm;
        private int selftestPhase;
        private readonly string selftestFile = Path.Combine(
            Path.GetTempPath(), "ascos-sqldb-selftest.log");
#endif

        internal DbMountForm()
        {
            Text = ProductInfo.ManagementTitle;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(980, 640);
            MinimumSize = new Size(820, 540);
            BackColor = Canvas;

            ConfigureButton(test, "Bağlantıyı sına", false);
            ConfigureButton(browse, "Klasör seç…", false);
            ConfigureButton(pickFiles, "Dosya seç…", false);
            ConfigureButton(pickBackup, "Yedek seç…", false);
            ConfigureButton(refresh, "Yenile", false);
            ConfigureButton(selectAll, "Tümünü seç", false);
            ConfigureButton(clearSelection, "Seçimi temizle", false);
            ConfigureButton(guide, "Kılavuz", false);
            ConfigureButton(openLog, "Kaydı aç", false);
            ConfigureButton(sendLog, "Hata kaydı gönder", false);
            ConfigureButton(detach, "Çıkar", false);
            ConfigureButton(restore, "Geri yükle", false);
            ConfigureButton(attach, "Ekle", true);

            server.DropDownStyle = ComboBoxStyle.DropDown;
            server.Height = 26;
            server.Width = 210;
            foreach (string instance in SqlOps.DiscoverInstances()) server.Items.Add(instance);

            auth.DropDownStyle = ComboBoxStyle.DropDownList;
            auth.Height = 26;
            auth.Width = 200;
            auth.Items.Add("Windows kimlik doğrulaması");
            auth.Items.Add("SQL Server kimlik doğrulaması");

            userBox.Width = 130;
            userBox.Height = 26;
            passBox.Width = 130;
            passBox.Height = 26;
            passBox.UseSystemPasswordChar = true;
            remember.Text = "Parolayı anımsa";
            remember.AutoSize = true;
            remember.BackColor = Color.White;

            connStatus.Text = "●  Bağlı değil";
            connStatus.ForeColor = Muted;
            connStatus.AutoSize = false;
            connStatus.AutoEllipsis = true;
            connStatus.Width = 300;
            connStatus.Height = 26;
            connStatus.TextAlign = ContentAlignment.MiddleLeft;

            folderBox.ReadOnly = true;
            folderBox.BackColor = Color.White;

            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Color.White;
            list.ForeColor = Ink;
            list.IntegralHeight = false;
            list.CheckOnClick = true;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = 54;
            list.DrawItem += DrawItem;
            list.ItemCheck += delegate { BeginInvoke(new MethodInvoker(UpdateSelectionInfo)); };

            testWorker.WorkerReportsProgress = false;
            testWorker.DoWork += TestWorkerDoWork;
            testWorker.RunWorkerCompleted += TestWorkerCompleted;
            batchWorker.WorkerReportsProgress = true;
            batchWorker.DoWork += BatchWorkerDoWork;
            batchWorker.ProgressChanged += BatchWorkerProgressChanged;
            batchWorker.RunWorkerCompleted += BatchWorkerCompleted;
            uploadWorker.WorkerReportsProgress = false;
            uploadWorker.DoWork += UploadWorkerDoWork;
            uploadWorker.RunWorkerCompleted += UploadWorkerCompleted;

            test.Click += delegate { StartTest(false); };
            browse.Click += delegate { BrowseFolder(); };
            pickFiles.Click += delegate { PickDatabaseFiles(); };
            pickBackup.Click += delegate { PickBackupFiles(); };
            refresh.Click += delegate { RefreshAll(); };
            selectAll.Click += delegate { SetAllChecks(true); };
            clearSelection.Click += delegate { SetAllChecks(false); };
            attach.Click += delegate { StartBatch(OpKind.Attach); };
            detach.Click += delegate { StartBatch(OpKind.Detach); };
            restore.Click += delegate { StartRestore(); };
            openLog.Click += delegate { OpenLogFile(); };
            sendLog.Click += delegate { AskUploadLog(); };
            auth.SelectedIndexChanged += delegate { UpdateAuthVisibility(); };
            guide.Click += delegate {
                try
                {
                    System.Diagnostics.Process.Start(Path.Combine(
                        Application.StartupPath, "USER_GUIDE.html"));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Kullanıcı kılavuzu açılamadı.\n\n" + ex.Message,
                        ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            TableLayoutPanel root = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = Padding.Empty, Padding = Padding.Empty
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildRail(), 0, 0);
            root.Controls.Add(BuildWorkspace(), 1, 0);
            Controls.Add(root);

            FormClosing += delegate { SaveSettings(); };
            Shown += delegate { RestoreAndConnect(); };
        }

        private Control BuildRail()
        {
            Panel rail = new Panel {
                Dock = DockStyle.Fill, BackColor = Navy,
                Padding = new Padding(22, 28, 18, 22)
            };

            PictureBox logo = new PictureBox {
                Size = new Size(52, 52), Location = new Point(20, 25),
                SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent
            };
            string logoPath = Path.Combine(Application.StartupPath, "ASCOS-DbMount.png");
            try
            {
                if (File.Exists(logoPath))
                using (Image loaded = Image.FromFile(logoPath)) logo.Image = new Bitmap(loaded);
            }
            catch { }

            Label logoFallback = new Label {
                Text = "A", ForeColor = Color.White, BackColor = Blue,
                Font = new Font("Segoe UI Semibold", 18F),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(48, 48), Location = new Point(22, 28),
                Visible = logo.Image == null
            };
            Label brand = new Label {
                Text = "ASCOS\nDbMount", ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 15F), AutoSize = true,
                Location = new Point(82, 28)
            };
            Label section = new Label {
                Text = "VERİTABANI YÖNETİMİ", ForeColor = Color.FromArgb(145, 170, 202),
                Font = new Font("Segoe UI Semibold", 8F), AutoSize = true,
                Location = new Point(22, 126)
            };
            Label title = new Label {
                Text = "MSSQL ekle /\nçıkar", ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 17F), AutoSize = true,
                Location = new Point(22, 153)
            };
            Label description = new Label {
                Text = "Klasördeki veritabanı\ndosyalarını sunucuya\nekleyin veya çıkarın.",
                ForeColor = Color.FromArgb(190, 207, 228), AutoSize = true,
                Location = new Point(22, 220)
            };
            Label websiteCaption = new Label {
                Text = "ASCOS HAKKINDA", ForeColor = Color.FromArgb(145, 170, 202),
                Font = new Font("Segoe UI Semibold", 8F), AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            };
            LinkLabel website = new LinkLabel {
                Text = "rotaniz.com  ↗", LinkColor = Color.White,
                ActiveLinkColor = Color.FromArgb(133, 190, 244),
                VisitedLinkColor = Color.White, Font = new Font("Segoe UI Semibold", 10F),
                AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            website.LinkClicked += delegate {
                try { System.Diagnostics.Process.Start("https://rotaniz.com/ascos-araclar/"); }
                catch (Exception ex) {
                    MessageBox.Show("Web sitesi açılamadı.\n\n" + ex.Message,
                        ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            railStatus.Text = "●  Bağlı değil";
            railStatus.ForeColor = Color.FromArgb(190, 207, 228);
            railStatus.AutoSize = true;
            railStatus.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            rail.Controls.AddRange(new Control[] { logo, logoFallback, brand, section, title,
                description, websiteCaption, website, railStatus });
            rail.Resize += delegate {
                railStatus.Top = rail.ClientSize.Height - 48;
                website.Top = railStatus.Top - 46;
                websiteCaption.Top = website.Top - 22;
                railStatus.Left = website.Left = websiteCaption.Left = 22;
            };
            return rail;
        }

        private Control BuildWorkspace()
        {
            TableLayoutPanel workspace = new TableLayoutPanel {
                Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 22),
                ColumnCount = 1, RowCount = 6, BackColor = Canvas
            };
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));

            Panel heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(new Label {
                Text = "Veritabanlarını yönet", ForeColor = Ink,
                Font = new Font("Segoe UI Semibold", 18F), AutoSize = true,
                Location = new Point(0, 0)
            });
            heading.Controls.Add(new Label {
                Text = "Klasördeki MDF dosyalarını sunucuya ekleyin veya çıkarın; dosyalar yerinde kalır.",
                ForeColor = Muted, AutoSize = true, Location = new Point(2, 38)
            });
            workspace.Controls.Add(heading, 0, 0);

            Panel notice = new Panel {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(232, 241, 251),
                Padding = new Padding(14, 10, 14, 8), Margin = new Padding(0, 0, 0, 12)
            };
            notice.Controls.Add(new Label {
                Text = "ⓘ  Ekleme ve çıkarma için sunucuda sysadmin yetkisi gerekir. Dosyalar hiçbir zaman taşınmaz.",
                ForeColor = Color.FromArgb(42, 91, 143), AutoSize = true,
                Location = new Point(14, 12)
            });
            notice.Paint += delegate(object sender, PaintEventArgs e) {
                ControlPaint.DrawBorder(e.Graphics, notice.ClientRectangle,
                    Color.FromArgb(190, 213, 238), ButtonBorderStyle.Solid);
            };
            workspace.Controls.Add(notice, 0, 1);

            workspace.Controls.Add(BuildConnectionCard(), 0, 2);

            Panel listFrame = new Panel {
                Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1),
                Margin = new Padding(0, 0, 0, 14)
            };
            listFrame.Paint += delegate(object sender, PaintEventArgs e) {
                ControlPaint.DrawBorder(e.Graphics, listFrame.ClientRectangle,
                    Border, ButtonBorderStyle.Solid);
            };
            listFrame.Controls.Add(list);
            workspace.Controls.Add(listFrame, 0, 3);

            FlowLayoutPanel toolbar = new FlowLayoutPanel {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
                Padding = new Padding(0, 0, 0, 10), Margin = Padding.Empty
            };
            toolbar.Controls.AddRange(new Control[] { selectAll, clearSelection, refresh,
                guide, openLog, sendLog });
            workspace.Controls.Add(toolbar, 0, 4);

            TableLayoutPanel footer = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
                Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            status.Text = "Klasör seçin";
            status.ForeColor = Muted;
            status.AutoSize = true;
            status.Anchor = AnchorStyles.Left;
            detach.Margin = new Padding(8, 0, 0, 0);
            restore.Margin = new Padding(8, 0, 0, 0);
            attach.Margin = new Padding(8, 0, 0, 0);
            footer.Controls.Add(status, 0, 0);
            footer.Controls.Add(detach, 1, 0);
            footer.Controls.Add(restore, 2, 0);
            footer.Controls.Add(attach, 3, 0);
            workspace.Controls.Add(footer, 0, 5);
            return workspace;
        }

        private Control BuildConnectionCard()
        {
            Panel card = new Panel {
                Dock = DockStyle.Fill, BackColor = Color.White,
                Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0, 0, 0, 14)
            };
            card.Paint += delegate(object sender, PaintEventArgs e) {
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle,
                    Border, ButtonBorderStyle.Solid);
            };

            TableLayoutPanel grid = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
                Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.White
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Label serverCaption = MakeCaption("SQL SERVER");
            Label authCaption = MakeCaption("KULLANICI ADI /\nŞİFRE");
            Label folderCaption = MakeCaption("DOSYA YOLU");

            FlowLayoutPanel serverRow = new FlowLayoutPanel {
                Dock = DockStyle.Fill, WrapContents = false,
                Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 2)
            };
            serverRow.Controls.Add(server);
            auth.Margin = new Padding(8, 0, 0, 0);
            serverRow.Controls.Add(auth);

            FlowLayoutPanel connectRow = new FlowLayoutPanel {
                Dock = DockStyle.Fill, WrapContents = false,
                Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 2)
            };
            connectRow.Controls.Add(userBox);
            passBox.Margin = new Padding(8, 0, 0, 0);
            connectRow.Controls.Add(passBox);
            remember.Margin = new Padding(10, 5, 0, 0);
            connectRow.Controls.Add(remember);
            test.Margin = new Padding(8, 0, 0, 0);
            connectRow.Controls.Add(test);
            connStatus.Margin = new Padding(14, 4, 0, 0);
            connectRow.Controls.Add(connStatus);

            TableLayoutPanel folderRow = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
                Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 2),
                BackColor = Color.White
            };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderBox.Dock = DockStyle.Fill;
            pickFiles.Margin = new Padding(8, 0, 0, 0);
            pickBackup.Margin = new Padding(8, 0, 0, 0);
            browse.Margin = new Padding(8, 0, 0, 0);
            folderRow.Controls.Add(folderBox, 0, 0);
            folderRow.Controls.Add(pickFiles, 1, 0);
            folderRow.Controls.Add(pickBackup, 2, 0);
            folderRow.Controls.Add(browse, 3, 0);

            grid.Controls.Add(serverCaption, 0, 0);
            grid.Controls.Add(serverRow, 1, 0);
            grid.Controls.Add(authCaption, 0, 1);
            grid.Controls.Add(connectRow, 1, 1);
            grid.Controls.Add(folderCaption, 0, 2);
            grid.Controls.Add(folderRow, 1, 2);
            card.Controls.Add(grid);
            return card;
        }

        private static Label MakeCaption(string text)
        {
            return new Label {
                Text = text, ForeColor = Muted,
                Font = new Font("Segoe UI Semibold", 8F), AutoSize = true,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, 8, 4)
            };
        }

        private static void ConfigureButton(Button button, string text, bool primary)
        {
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(0, 34);
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = primary ? Blue : Color.White;
            button.ForeColor = primary ? Color.White : Ink;
            button.Padding = new Padding(primary ? 14 : 10, 3, primary ? 14 : 10, 3);
            button.Margin = new Padding(0, 0, 8, 0);
            button.FlatAppearance.BorderColor = primary ? Blue : Border;
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(35, 96, 162) : Color.FromArgb(249, 251, 253);
        }

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= items.Count) return;
            DatabaseFile item = items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color background = selected ? Color.FromArgb(232, 241, 251) : Color.White;
            using (Brush brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, e.Bounds);
            if (selected)
            using (Brush accent = new SolidBrush(Blue))
                e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top, 4, e.Bounds.Height);

            System.Windows.Forms.VisualStyles.CheckBoxState checkState = list.GetItemChecked(e.Index)
                ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal
                : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
            CheckBoxRenderer.DrawCheckBox(e.Graphics,
                new Point(e.Bounds.Left + 14, e.Bounds.Top + 20), checkState);

            Rectangle nameBounds = new Rectangle(e.Bounds.Left + 40, e.Bounds.Top + 7,
                Math.Max(0, e.Bounds.Width - 56), 20);
            using (Font nameFont = new Font("Segoe UI Semibold", 9.5F))
                TextRenderer.DrawText(e.Graphics, item.Name, nameFont, nameBounds, Ink,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            string stateText;
            Color stateColor;
            if (item.Kind == ItemKind.Bak)
            {
                if (!String.IsNullOrEmpty(item.BackupDatabaseName))
                {
                    stateText = item.Attached
                        ? "● Aynı adlı DB bağlı: " + item.BackupDatabaseName
                        : "● Yedek: " + item.BackupDatabaseName;
                    stateColor = item.Attached ? Danger : Muted;
                }
                else
                {
                    stateText = "● Yedek dosyası";
                    stateColor = Muted;
                }
            }
            else
            {
                stateText = item.Attached
                    ? (String.IsNullOrEmpty(item.AttachedName)
                        ? "● Bağlı" : "● Bağlı: " + item.AttachedName)
                    : "● Bağlı değil";
                stateColor = item.Attached ? Success : Muted;
            }

            int x = e.Bounds.Left + 40;
            int y = e.Bounds.Top + 29;
            Rectangle subBounds = new Rectangle(x, y,
                Math.Max(0, e.Bounds.Right - x - 16), 18);
            TextRenderer.DrawText(e.Graphics, stateText, Font, subBounds, stateColor,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            x += TextRenderer.MeasureText(stateText, Font).Width;

            string tail = "  ·  " + item.FileSummary();
            Rectangle tailBounds = new Rectangle(x, y, Math.Max(0, e.Bounds.Right - x - 16), 18);
            TextRenderer.DrawText(e.Graphics, tail, Font, tailBounds, Muted,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (!String.IsNullOrEmpty(item.LastResult))
            {
                x += TextRenderer.MeasureText(tail, Font).Width;
                string resultText = "  ·  " + item.LastResult;
                Rectangle resultBounds = new Rectangle(x, y,
                    Math.Max(0, e.Bounds.Right - x - 16), 18);
                TextRenderer.DrawText(e.Graphics, resultText, Font, resultBounds,
                    item.LastOk ? Success : Danger,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            using (Pen separator = new Pen(Color.FromArgb(234, 239, 245)))
                e.Graphics.DrawLine(separator, e.Bounds.Left + 12, e.Bounds.Bottom - 1,
                    e.Bounds.Right - 12, e.Bounds.Bottom - 1);
            e.DrawFocusRectangle();
        }

        private void UpdateSelectionInfo()
        {
            int attachCount = 0, detachCount = 0, restoreCount = 0, total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (!list.GetItemChecked(i)) continue;
                total++;
                DatabaseFile item = items[i];
                if (item.Kind == ItemKind.Bak) restoreCount++;
                else if (item.Attached) detachCount++;
                else attachCount++;
            }
            if (total == 0)
            {
                status.Text = "Seçili öğe yok";
            }
            else
            {
                List<string> parts = new List<string>();
                if (attachCount > 0) parts.Add("eklenecek: " + attachCount);
                if (detachCount > 0) parts.Add("çıkarılacak: " + detachCount);
                if (restoreCount > 0) parts.Add("geri yüklenecek: " + restoreCount);
                status.Text = "Seçili: " + total +
                    (parts.Count > 0 ? " — " + String.Join(", ", parts.ToArray()) : "");
            }
            attach.Enabled = !busy && attachCount > 0;
            detach.Enabled = !busy && detachCount > 0;
            restore.Enabled = !busy && restoreCount > 0;
        }

        private void SetAllChecks(bool value)
        {
            for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, value);
            UpdateSelectionInfo();
        }

        private void UpdateAuthVisibility()
        {
            bool sqlAuth = auth.SelectedIndex == 1;
            userBox.Visible = sqlAuth;
            passBox.Visible = sqlAuth;
            remember.Visible = sqlAuth;
        }

        private void BrowseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Veritabanı dosyalarının bulunduğu klasörü seçin";
                if (!String.IsNullOrEmpty(folderBox.Text))
                    dialog.SelectedPath = folderBox.Text;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                folderBox.Text = dialog.SelectedPath;
                SaveSettings();
                LoadFolder();
                if (connected) StartTest(true);
            }
        }

        private void PickDatabaseFiles()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Eklenecek veritabanı dosyalarını seçin (MDF)";
                dialog.Filter = "SQL Server veritabanları (*.mdf)|*.mdf|Tüm dosyalar (*.*)|*.*";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                AddFiles(dialog.FileNames, ItemKind.Mdf);
            }
        }

        private void PickBackupFiles()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Geri yüklenecek yedek dosyalarını seçin (BAK)";
                dialog.Filter = "SQL Server yedekleri (*.bak)|*.bak|Tüm dosyalar (*.*)|*.*";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                AddFiles(dialog.FileNames, ItemKind.Bak);
            }
        }

        private void AddFiles(string[] paths, ItemKind kind)
        {
            int added = 0;
            foreach (string path in paths)
            {
                string full;
                try { full = Path.GetFullPath(path); }
                catch { full = path; }
                bool exists = false;
                foreach (DatabaseFile item in items)
                {
                    if (item.MdfPath.Equals(full, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists) continue;
                DatabaseFile file = new DatabaseFile(full);
                file.Kind = kind;
                if (kind == ItemKind.Mdf)
                {
                    try { MdfHeader.Read(file); } catch { }
                }
                else
                {
                    file.DatabaseName = file.Name;
                }
                items.Add(file);
                extraItems.Add(file);
                list.Items.Add(file.Name);
                added++;
            }
            if (added == 0)
            {
                status.Text = "Seçilen dosyalar zaten listede";
                return;
            }
            for (int i = items.Count - added; i < items.Count; i++)
                list.SetItemChecked(i, true);
            Log.Info("Dosya secildi: " + added + " adet " +
                (kind == ItemKind.Bak ? "BAK" : "MDF") + " eklendi (toplam " +
                items.Count + ")");
            if (connected)
            {
                status.Text = added + " dosya eklendi; durumlar güncelleniyor…";
                StartTest(true);
            }
            else
            {
                status.Text = added + " dosya eklendi — durum için sunucuya bağlanın";
                UpdateSelectionInfo();
            }
        }

        private void RefreshAll()
        {
            LoadFolder();
            StartTest(true);
        }

        private void LoadFolder()
        {
            HashSet<string> checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items.Count; i++)
                if (list.GetItemChecked(i)) checkedPaths.Add(items[i].MdfPath);

            items.Clear();
            items.AddRange(SqlOps.ScanFolder(folderBox.Text));
            foreach (DatabaseFile extra in extraItems)
            {
                bool duplicate = false;
                foreach (DatabaseFile item in items)
                {
                    if (item.MdfPath.Equals(extra.MdfPath, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) items.Add(extra);
            }
            list.BeginUpdate();
            list.Items.Clear();
            foreach (DatabaseFile item in items) list.Items.Add(item.Name);
            list.EndUpdate();
            for (int i = 0; i < items.Count; i++)
                list.SetItemChecked(i, checkedPaths.Contains(items[i].MdfPath));

            if (items.Count == 0 && !String.IsNullOrEmpty(folderBox.Text))
                status.Text = "Klasörde MDF dosyası bulunamadı";
            else if (items.Count == 0)
                status.Text = "Klasör seçin";
            UpdateSelectionInfo();
        }

        private void OpenLogFile()
        {
            try
            {
                string path = Log.LogPath;
                Log.Info("Hata kaydi kullanici tarafindan acildi: " + path);
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, "", Encoding.UTF8);
                }
                System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                Log.Error("Kayit acilamadi", ex);
                MessageBox.Show("Hata kaydı açılamadı.\n\n" + ex.Message,
                    ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AskUploadLog()
        {
            if (busy) return;
            long size = Log.CurrentSize();
            DialogResult result = MessageBox.Show(
                "Hata kaydı (" + DatabaseFile.FormatSize(size) + ") destek sunucusuna " +
                "gönderilecek.\n\nKayıt; uygulama sürümü, Windows sürümü, sunucu adı, " +
                "dosya yolları ve işlem özetlerini içerir. Parola içermez.\n\n" +
                "Gönderilsin mi?", ProductInfo.DisplayName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;
            StartUpload();
        }

        private void StartUpload()
        {
            if (busy) return;
            busy = true;
            SetBusyState();
            status.Text = "Hata kaydı gönderiliyor…";
            Log.Info("Hata kaydi yukleme baslatildi");
            uploadWorker.RunWorkerAsync();
        }

        private void UploadWorkerDoWork(object sender, DoWorkEventArgs e)
        {
            string payload = Log.BuildSupportPayload();
            e.Result = SupportUpload.Upload(payload);
        }

        private void UploadWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            busy = false;
            SetBusyState();
            string result = e.Result as string;
            if (result != null && result.StartsWith("OK"))
            {
                status.Text = "Hata kaydı gönderildi";
                Log.Info("Hata kaydi sunucuya gonderildi: " + result);
                MessageBox.Show("Hata kaydı destek sunucusuna gönderildi. Teşekkürler.",
                    ProductInfo.DisplayName, MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                status.Text = "Hata kaydı gönderilemedi";
                Log.Error("Kayit yukleme sonucu", result ?? "(sonuc yok)");
                MessageBox.Show(
                    "Hata kaydı gönderilemedi. İnternet bağlantınızı denetleyin ve " +
                    "daha sonra yeniden deneyin.\n\nAyrıntı: " + (result ?? "bilinmiyor"),
                    ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshStates(Dictionary<string, string> attachedMap,
            HashSet<string> databaseNames)
        {
            foreach (DatabaseFile item in items)
            {
                if (item.Kind == ItemKind.Bak)
                {
                    if (databaseNames != null && !String.IsNullOrEmpty(item.BackupDatabaseName))
                        item.Attached = databaseNames.Contains(item.BackupDatabaseName);
                    else if (databaseNames != null)
                        item.Attached = false;
                    continue;
                }
                if (attachedMap == null) continue;
                string normalized = SqlOps.NormalizePath(item.MdfPath);
                if (attachedMap.ContainsKey(normalized))
                {
                    item.Attached = true;
                    item.AttachedName = attachedMap[normalized];
                }
                else
                {
                    item.Attached = false;
                    item.AttachedName = null;
                }
            }
            list.Invalidate();
            UpdateSelectionInfo();
        }

        private void SetBusyState()
        {
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
            test.Enabled = !busy;
            browse.Enabled = !busy;
            pickFiles.Enabled = !busy;
            pickBackup.Enabled = !busy;
            refresh.Enabled = !busy;
            sendLog.Enabled = !busy;
            server.Enabled = !busy;
            auth.Enabled = !busy;
            userBox.Enabled = !busy;
            passBox.Enabled = !busy;
            UpdateSelectionInfo();
        }

        private void StartTest(bool silent)
        {
            if (busy) return;
            busy = true;
            SetBusyState();
            connStatus.ForeColor = Muted;
            connStatus.Text = "●  Bağlanılıyor…";
            testWorker.RunWorkerAsync(new object[] {
                server.Text.Trim(), auth.SelectedIndex == 1, userBox.Text, passBox.Text, silent });
        }

        private void TestWorkerDoWork(object sender, DoWorkEventArgs e)
        {
            object[] args = (object[])e.Argument;
            string serverName = (string)args[0];
            bool sqlAuth = (bool)args[1];
            string user = (string)args[2];
            string password = (string)args[3];
            TestResult result = new TestResult();
            result.Silent = (bool)args[4];
            try
            {
                using (System.Data.SqlClient.SqlConnection connection = SqlOps.OpenConnection(
                    serverName, !sqlAuth, user, password))
                {
                    SqlOps.QueryServerInfo(connection, out result.Version,
                        out result.Edition, out result.Host);
                    result.AttachedMap = SqlOps.QueryAttachedFiles(connection);
                    result.DatabaseNames = SqlOps.QueryDatabaseNames(connection);
                    foreach (DatabaseFile item in items)
                    {
                        if (item.Kind != ItemKind.Bak) continue;
                        try
                        {
                            BackupHeader header = SqlOps.QueryBackupHeader(
                                connection, item.MdfPath);
                            item.BackupDatabaseName = header.DatabaseName;
                            item.BackupType = header.Type;
                            item.BackupDate = header.BackupDate;
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Yedek basligi okunamadi: " + item.MdfPath, ex);
                        }
                    }
                    result.Ok = true;
                    Log.Info("Baglanti kuruldu: " + serverName + " — SQL Server " +
                        result.Version + " (" + result.Edition + ") @ " + result.Host);
                }
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Message = SqlOps.TranslateSqlError(ex);
                Log.Error("Baglanti sinamasi basarisiz: " + serverName, ex);
            }
            e.Result = result;
        }

        private void TestWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            busy = false;
            SetBusyState();
            TestResult result = e.Result as TestResult;
            if (result == null) return;
            connected = result.Ok;
            if (result.Ok)
            {
                connStatus.ForeColor = Success;
                string edition = String.IsNullOrEmpty(result.Edition) ? "" : " — " + result.Edition;
                connStatus.Text = "●  Bağlı: SQL Server " + result.Version + edition;
                railStatus.ForeColor = Success;
                railStatus.Text = "●  Bağlı: " + server.Text.Trim();
                SaveSettings();
                RefreshStates(result.AttachedMap, result.DatabaseNames);
                if (!result.Silent)
                    MessageBox.Show("Bağlantı kuruldu.\n\n" + connStatus.Text,
                        ProductInfo.DisplayName, MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
            }
            else
            {
                connStatus.ForeColor = Danger;
                connStatus.Text = "●  " + result.Message;
                railStatus.ForeColor = Color.FromArgb(190, 207, 228);
                railStatus.Text = "●  Bağlı değil";
                if (!result.Silent)
                    MessageBox.Show(result.Message, ProductInfo.DisplayName,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartBatch(OpKind kind)
        {
            if (busy) return;
            List<BatchOp> ops = new List<BatchOp>();
            for (int i = 0; i < items.Count; i++)
            {
                if (!list.GetItemChecked(i)) continue;
                DatabaseFile item = items[i];
                if (kind == OpKind.Attach && (item.Kind != ItemKind.Mdf || item.Attached)) continue;
                if (kind == OpKind.Detach && (item.Kind != ItemKind.Mdf || !item.Attached)) continue;
                BatchOp op = new BatchOp();
                op.Item = item;
                op.Kind = kind;
                ops.Add(op);
            }
            if (ops.Count == 0)
            {
                status.Text = kind == OpKind.Attach
                    ? "Eklenecek seçili öğe yok (zaten bağlı olanlar atlanır)"
                    : "Çıkarılacak seçili öğe yok (yalnızca bağlı olanlar çıkarılabilir)";
                return;
            }
            string password = passBox.Text;
            if (auth.SelectedIndex == 1 && String.IsNullOrEmpty(password))
            {
                using (PasswordForm prompt = new PasswordForm(userBox.Text))
                {
                    if (prompt.ShowDialog(this) != DialogResult.OK) return;
                    password = prompt.Password;
                }
                passBox.Text = password;
            }

            bool statusOk = true;
            List<ConfirmForm.ConfirmItem> confirmItems = new List<ConfirmForm.ConfirmItem>();
            try
            {
                Cursor previous = Cursor;
                Cursor = Cursors.WaitCursor;
                try
                {
                    using (System.Data.SqlClient.SqlConnection connection =
                        SqlOps.OpenConnection(server.Text.Trim(), auth.SelectedIndex != 1,
                            userBox.Text, password, 5))
                    {
                        foreach (BatchOp op in ops)
                        {
                            ConfirmForm.ConfirmItem confirmItem = new ConfirmForm.ConfirmItem();
                            confirmItem.File = op.Item;
                            confirmItem.AttachOp = op.Kind == OpKind.Attach;
                            confirmItem.RestoreOp = op.Kind == OpKind.Restore;
                            confirmItem.StatusChecked = true;
                            if (op.Kind == OpKind.Attach)
                            {
                                confirmItem.FileLocked = SqlOps.AnyFileLocked(op.Item);
                            }
                            else if (op.Kind == OpKind.Detach)
                            {
                                try
                                {
                                    confirmItem.SessionCount = SqlOps.QuerySessionCount(
                                        connection, op.Item.AttachedName);
                                }
                                catch { confirmItem.SessionCount = -1; }
                            }
                            confirmItems.Add(confirmItem);
                        }
                    }
                }
                finally { Cursor = previous; }
            }
            catch (Exception ex)
            {
                statusOk = false;
                Log.Error("On denetim baglantisi basarisiz: " + server.Text.Trim(), ex);
            }

            if (!statusOk)
            {
                confirmItems.Clear();
                foreach (BatchOp op in ops)
                {
                    ConfirmForm.ConfirmItem confirmItem = new ConfirmForm.ConfirmItem();
                    confirmItem.File = op.Item;
                    confirmItem.AttachOp = op.Kind == OpKind.Attach;
                    confirmItem.RestoreOp = op.Kind == OpKind.Restore;
                    confirmItem.StatusChecked = false;
                    confirmItems.Add(confirmItem);
                }
            }
#if DEBUG
            if (selftestActive) SelfTestLogPreflight(confirmItems, statusOk);
#endif
            bool showDialog = true;
#if DEBUG
            showDialog = !suppressConfirm;
#endif
            if (showDialog)
            {
                ConfirmForm.OperationMode mode = ConfirmForm.OperationMode.Attach;
                if (kind == OpKind.Detach) mode = ConfirmForm.OperationMode.Detach;
                if (kind == OpKind.Restore) mode = ConfirmForm.OperationMode.Restore;
                using (ConfirmForm dialog = new ConfirmForm(confirmItems, mode, statusOk))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                }
            }
            batchTotal = ops.Count;
            busy = true;
            SetBusyState();
            status.Text = "İşleniyor 0/" + ops.Count + "…";
            batchWorker.RunWorkerAsync(new object[] {
                server.Text.Trim(), auth.SelectedIndex == 1, userBox.Text, password, ops });
        }

        private void StartRestore()
        {
            if (busy) return;
            List<BatchOp> ops = new List<BatchOp>();
            for (int i = 0; i < items.Count; i++)
            {
                if (!list.GetItemChecked(i)) continue;
                DatabaseFile item = items[i];
                if (item.Kind != ItemKind.Bak) continue;
                BatchOp op = new BatchOp();
                op.Item = item;
                op.Kind = OpKind.Restore;
                ops.Add(op);
            }
            if (ops.Count == 0)
            {
                status.Text = "Geri yüklenecek seçili yedek yok (Yedek seç… ile ekleyin)";
                return;
            }
            string targetFolder = null;
#if DEBUG
            targetFolder = selftestActive ? folderBox.Text : null;
#endif
            if (String.IsNullOrEmpty(targetFolder))
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Geri yüklenen dosyaların konacağı klasörü seçin";
                    if (!String.IsNullOrEmpty(folderBox.Text))
                        dialog.SelectedPath = folderBox.Text;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    targetFolder = dialog.SelectedPath;
                }
            }
            lastRestoreFolder = targetFolder;

            string password = passBox.Text;
            if (auth.SelectedIndex == 1 && String.IsNullOrEmpty(password))
            {
                using (PasswordForm prompt = new PasswordForm(userBox.Text))
                {
                    if (prompt.ShowDialog(this) != DialogResult.OK) return;
                    password = prompt.Password;
                }
                passBox.Text = password;
            }

            bool statusOk = true;
            List<ConfirmForm.ConfirmItem> confirmItems = new List<ConfirmForm.ConfirmItem>();
            try
            {
                Cursor previous = Cursor;
                Cursor = Cursors.WaitCursor;
                try
                {
                    using (System.Data.SqlClient.SqlConnection connection =
                        SqlOps.OpenConnection(server.Text.Trim(), auth.SelectedIndex != 1,
                            userBox.Text, password, 5))
                    {
                        HashSet<string> databaseNames = SqlOps.QueryDatabaseNames(connection);
                        foreach (BatchOp op in ops)
                        {
                            ConfirmForm.ConfirmItem confirmItem = new ConfirmForm.ConfirmItem();
                            confirmItem.File = op.Item;
                            confirmItem.AttachOp = false;
                            confirmItem.RestoreOp = true;
                            confirmItem.RestoreTargetFolder = targetFolder;
                            confirmItem.StatusChecked = true;
                            try
                            {
                                BackupHeader header = SqlOps.QueryBackupHeader(
                                    connection, op.Item.MdfPath);
                                confirmItem.RestoreDbName = header.DatabaseName;
                                confirmItem.RestoreReplace =
                                    databaseNames.Contains(header.DatabaseName);
                                confirmItem.RestoreInvalid =
                                    header.Type != 1 && header.Type != 5;
                                confirmItem.RestoreType = header.Type;
                                if (!confirmItem.RestoreInvalid)
                                {
                                    try
                                    {
                                        foreach (BackupFileEntry entry in
                                            SqlOps.QueryBackupFileList(connection, op.Item.MdfPath))
                                        {
                                            string fileName = Path.GetFileName(
                                                entry.PhysicalName.Trim());
                                            if (!String.IsNullOrEmpty(fileName) &&
                                                File.Exists(Path.Combine(targetFolder, fileName)))
                                            {
                                                confirmItem.RestoreTargetExists = true;
                                                break;
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch (Exception ex)
                            {
                                confirmItem.StatusChecked = false;
                                Log.Error("Yedek basligi okunamadi: " + op.Item.MdfPath, ex);
                            }
                            confirmItem.FileLocked = SqlOps.AnyFileLocked(op.Item.MdfPath);
                            confirmItems.Add(confirmItem);
                        }
                    }
                }
                finally { Cursor = previous; }
            }
            catch (Exception ex)
            {
                statusOk = false;
                Log.Error("On denetim baglantisi basarisiz: " + server.Text.Trim(), ex);
            }

            if (!statusOk)
            {
                confirmItems.Clear();
                foreach (BatchOp op in ops)
                {
                    ConfirmForm.ConfirmItem confirmItem = new ConfirmForm.ConfirmItem();
                    confirmItem.File = op.Item;
                    confirmItem.RestoreOp = true;
                    confirmItem.RestoreTargetFolder = targetFolder;
                    confirmItem.StatusChecked = false;
                    confirmItems.Add(confirmItem);
                }
            }
#if DEBUG
            if (selftestActive) SelfTestLogPreflight(confirmItems, statusOk);
#endif
            bool showDialog = true;
#if DEBUG
            showDialog = !suppressConfirm;
#endif
            if (showDialog)
            {
                using (ConfirmForm dialog = new ConfirmForm(confirmItems,
                    ConfirmForm.OperationMode.Restore, statusOk))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                }
            }
            for (int i = 0; i < ops.Count; i++)
                ops[i].RestoreFolder = targetFolder;
            batchTotal = ops.Count;
            busy = true;
            SetBusyState();
            status.Text = "İşleniyor 0/" + ops.Count + "…";
            batchWorker.RunWorkerAsync(new object[] {
                server.Text.Trim(), auth.SelectedIndex == 1, userBox.Text, password, ops });
        }

        private void BatchWorkerDoWork(object sender, DoWorkEventArgs e)
        {
            object[] args = (object[])e.Argument;
            string serverName = (string)args[0];
            bool sqlAuth = (bool)args[1];
            string user = (string)args[2];
            string password = (string)args[3];
            List<BatchOp> ops = (List<BatchOp>)args[4];
            BatchResult result = new BatchResult();
            Log.Info("Toplu islem baslatildi: " + DescribeKind(ops.Count > 0 ? ops[0].Kind : OpKind.Attach) +
                ", oge sayisi=" + ops.Count + ", sunucu=" + serverName);
            try
            {
                using (System.Data.SqlClient.SqlConnection probe =
                    SqlOps.OpenConnection(serverName, !sqlAuth, user, password))
                {
                    for (int i = 0; i < ops.Count; i++)
                    {
                        BatchOp op = ops[i];
                        try
                        {
                            using (System.Data.SqlClient.SqlConnection connection =
                                SqlOps.OpenConnection(serverName, !sqlAuth, user, password))
                            {
                                if (op.Kind == OpKind.Attach)
                                {
                                    SqlOps.AttachDatabase(connection, op.Item);
                                    op.Message = "Eklendi";
                                }
                                else if (op.Kind == OpKind.Detach)
                                {
                                    SqlOps.DetachDatabase(connection, op.Item.AttachedName);
                                    op.Message = "Çıkarıldı";
                                }
                                else
                                {
                                    BackupHeader header = SqlOps.QueryBackupHeader(
                                        connection, op.Item.MdfPath);
                                    if (header.Type != 1 && header.Type != 5)
                                    {
                                        throw new ApplicationException(
                                            "Bu bir tam veritabanı yedeği değil (tür " +
                                            header.Type + "). Yalnızca tam yedekler " +
                                            "geri yüklenebilir.");
                                    }
                                    HashSet<string> names = SqlOps.QueryDatabaseNames(connection);
                                    bool replace = names.Contains(header.DatabaseName);
                                    SqlOps.RestoreDatabase(connection, op.Item.MdfPath,
                                        header.DatabaseName, op.RestoreFolder, replace);
                                    op.Message = replace
                                        ? "Geri yüklendi (üzerine yazıldı)"
                                        : "Geri yüklendi";
                                }
                            }
                            op.Ok = true;
                            result.Success++;
                            Log.Info("Islem basarili: " + DescribeKind(op.Kind) + " " +
                                op.Item.Name + " (" + op.Item.MdfPath + ")");
                        }
                        catch (Exception ex)
                        {
                            op.Ok = false;
                            op.Message = SqlOps.TranslateSqlError(ex);
                            Log.Error("Islem basarisiz: " + DescribeKind(op.Kind) + " " +
                                op.Item.Name + " (" + op.Item.MdfPath + ")", ex);
                        }
                        batchWorker.ReportProgress(i + 1, op);
                    }
                    try
                    {
                        result.AttachedMap = SqlOps.QueryAttachedFiles(probe);
                        result.DatabaseNames = SqlOps.QueryDatabaseNames(probe);
                    }
                    catch (Exception ex) { Log.Error("Durum yenileme", ex); }
                    result.ConnectionOk = true;
                    Log.Info("Toplu islem bitti: basarili=" + result.Success +
                        ", hatali=" + (ops.Count - result.Success));
                }
            }
            catch (Exception ex)
            {
                result.ConnectionError = SqlOps.TranslateSqlError(ex);
                Log.Error("Toplu islem baglanti hatasi: " + serverName, ex);
            }
            e.Result = result;
        }

        private void BatchWorkerProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            BatchOp op = e.UserState as BatchOp;
            if (op == null) return;
            op.Item.LastResult = op.Message;
            op.Item.LastOk = op.Ok;
            status.Text = "İşleniyor " + e.ProgressPercentage + "/" + batchTotal +
                ": " + op.Item.Name + "…";
            list.Invalidate();
        }

        private void BatchWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            int total = batchTotal;
            batchTotal = 0;
            busy = false;
            SetBusyState();
            BatchResult result = e.Result as BatchResult;
            if (result == null) return;
            if (!result.ConnectionOk)
            {
                connected = false;
                connStatus.ForeColor = Danger;
                connStatus.Text = "●  " + result.ConnectionError;
                railStatus.ForeColor = Color.FromArgb(190, 207, 228);
                railStatus.Text = "●  Bağlı değil";
                status.Text = "İşlem yapılamadı: bağlantı kurulamadı";
                MessageBox.Show(result.ConnectionError, ProductInfo.DisplayName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                connected = true;
                connStatus.ForeColor = Success;
                connStatus.Text = "●  Bağlı: " + server.Text.Trim();
                railStatus.ForeColor = Success;
                railStatus.Text = "●  Bağlı: " + server.Text.Trim();
                if (!String.IsNullOrEmpty(lastRestoreFolder) &&
                    lastRestoreFolder.Equals(folderBox.Text.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    LoadFolder();
                }
                RefreshStates(result.AttachedMap, result.DatabaseNames);
                int failed = Math.Max(0, total - result.Success);
                if (failed == 0)
                {
                    status.Text = total + " işlem tamamlandı";
                }
                else
                {
                    status.Text = result.Success + " başarılı, " + failed + " hatalı";
                    StringBuilder summary = new StringBuilder();
                    summary.AppendLine("Bazı işlemler tamamlanamadı:");
                    int shown = 0;
                    for (int i = 0; i < items.Count && shown < 12; i++)
                    {
                        if (String.IsNullOrEmpty(items[i].LastResult) || items[i].LastOk) continue;
                        summary.AppendLine("");
                        summary.AppendLine(items[i].Name + " — " + items[i].LastResult);
                        shown++;
                    }
                    summary.AppendLine("");
                    summary.AppendLine("Ayrıntılı hata kaydı bu bilgisayarda:");
                    summary.AppendLine(Log.LogPath);
                    bool showUploadPrompt = true;
#if DEBUG
                    showUploadPrompt = !selftestActive;
#endif
                    if (showUploadPrompt)
                    {
                        summary.AppendLine("");
                        summary.AppendLine("Hata kaydı, sorunun çözümü için destek " +
                            "sunucusuna gönderilsin mi?");
                        DialogResult upload = MessageBox.Show(summary.ToString(),
                            ProductInfo.DisplayName, MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        if (upload == DialogResult.Yes) StartUpload();
                    }
                }
#if DEBUG
                if (selftestActive) ContinueSelfTest();
#endif
            }
        }

        private void RestoreAndConnect()
        {
            string savedServer, savedUser, savedPassword, savedFolder;
            bool savedSqlAuth;
            Settings.Load(out savedServer, out savedSqlAuth, out savedUser,
                out savedPassword, out savedFolder);
            if (!String.IsNullOrEmpty(savedServer)) server.Text = savedServer;
            auth.SelectedIndex = savedSqlAuth ? 1 : 0;
            userBox.Text = savedUser;
            passBox.Text = savedPassword;
            UpdateAuthVisibility();
            if (!String.IsNullOrEmpty(savedFolder) && Directory.Exists(savedFolder))
            {
                folderBox.Text = savedFolder;
                LoadFolder();
                StartTest(true);
            }
            else status.Text = "Klasör seçin";
#if DEBUG
            bool hasFlag = false;
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg == "--selftest") hasFlag = true;
            if (hasFlag)
            {
                selftestActive = true;
                suppressConfirm = true;
                File.WriteAllText(selftestFile,
                    "ASCOS DbMount self-test basladi: " + DateTime.Now + Environment.NewLine);
                Timer selftestTimer = new Timer();
                selftestTimer.Interval = 5000;
                selftestTimer.Tick += delegate
                {
                    selftestTimer.Stop();
                    selftestTimer.Dispose();
                    RunSelfTestAttach();
                };
                selftestTimer.Start();
            }
#endif
        }

#if DEBUG
        private void SelfTestLog(string message)
        {
            try
            {
                File.AppendAllText(selftestFile,
                    DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { }
        }

        private void RunSelfTestAttach()
        {
            SelfTestLog("Klasor: " + folderBox.Text);
            SelfTestLog("Liste oge sayisi: " + items.Count);
            foreach (DatabaseFile item in items)
                SelfTestLog("  - " + item.Name + " (db: " + item.DatabaseName +
                    ", veri dosyasi: " + item.DataFiles.Count +
                    ", log: " + item.LogFiles.Count + ")");
            string extraFolder = folderBox.Text.TrimEnd('\\') + "_extra";
            string extraFile = Path.Combine(extraFolder, "SQLdbTestD.mdf");
            if (File.Exists(extraFile))
            {
                AddFiles(new string[] { extraFile }, ItemKind.Mdf);
                SelfTestLog("Ek dosya eklendi (klasor disi): " + extraFile);
                SelfTestLog("Liste oge sayisi (ek sonrasi): " + items.Count);
            }
            if (items.Count == 0)
            {
                SelfTestLog("HATA: liste bos");
                Application.Exit();
                return;
            }
            int wait = 0;
            while (busy && wait < 100)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(100);
                wait++;
            }
            SelfTestLog("Busy bekleme: " + (wait * 100) + " ms");
            SetAllChecks(true);
            selftestPhase = 1;
            SelfTestLog("Asama 1: Ekle baslatiliyor (" + items.Count + " oge)");
            StartBatch(OpKind.Attach);
        }

        private void SelfTestLogPreflight(List<ConfirmForm.ConfirmItem> confirmItems,
            bool statusOk)
        {
            SelfTestLog("On denetim: sunucu durumu=" + (statusOk ? "OK" : "ERISILEMEDI"));
            foreach (ConfirmForm.ConfirmItem item in confirmItems)
            {
                string info = "  - " + item.File.Name + " (" +
                    (item.RestoreOp ? "geri yuklenecek" : (item.AttachOp ? "eklenecek" : "cikarilacak")) + ")";
                if (item.RestoreOp)
                {
                    info += " db=" + (item.RestoreDbName ?? "?") +
                        " uzerineYaz=" + (item.RestoreReplace ? "EVET" : "hayir") +
                        " tur=" + item.RestoreType +
                        " hedefDosya=" + (item.RestoreTargetExists ? "VAR" : "yok") +
                        " kilitli=" + (item.FileLocked ? "EVET" : "hayir");
                }
                else if (item.StatusChecked && !item.AttachOp)
                    info += " oturum=" + item.SessionCount;
                else if (item.StatusChecked && item.AttachOp)
                    info += " kilitli=" + (item.FileLocked ? "EVET" : "hayir");
                SelfTestLog(info);
            }
        }

        private void ContinueSelfTest()
        {
            if (selftestPhase == 1)
            {
                int attached = 0;
                foreach (DatabaseFile item in items)
                {
                    if (String.IsNullOrEmpty(item.LastResult)) continue;
                    SelfTestLog("  Ekle sonucu - " + item.Name + ": " +
                        (item.LastOk ? "OK" : "HATA: " + item.LastResult) +
                        ", durum=" + (item.Attached ? "bagli" : "bagli degil"));
                    if (item.Attached) attached++;
                }
                SelfTestLog("Asama 1 bitti, bagli sayisi: " + attached);
                selftestPhase = 2;
                SelfTestLog("Asama 2: Cikar baslatiliyor");
                StartBatch(OpKind.Detach);
            }
            else if (selftestPhase == 2)
            {
                int attached = 0;
                foreach (DatabaseFile item in items)
                {
                    if (String.IsNullOrEmpty(item.LastResult)) continue;
                    SelfTestLog("  Cikar sonucu - " + item.Name + ": " +
                        (item.LastOk ? "OK" : "HATA: " + item.LastResult) +
                        ", durum=" + (item.Attached ? "bagli" : "bagli degil"));
                    if (item.Attached) attached++;
                }
                SelfTestLog("Asama 2 bitti, bagli kalan: " + attached);
                selftestPhase = 3;
                string extraFolder = folderBox.Text.TrimEnd('\\') + "_extra";
                List<string> baks = new List<string>();
                string backupFile = Path.Combine(extraFolder, "SQLdbTestD.bak");
                if (File.Exists(backupFile)) baks.Add(backupFile);
                string logBackup = Path.Combine(extraFolder, "SQLdbTestD_log.bak");
                if (File.Exists(logBackup)) baks.Add(logBackup);
                if (baks.Count > 0)
                {
                    AddFiles(baks.ToArray(), ItemKind.Bak);
                    int wait = 0;
                    while (busy && wait < 100)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(100);
                        wait++;
                    }
                    SelfTestLog("Asama 3: Geri yukle baslatiliyor (" + baks.Count +
                        " yedek: " + String.Join(", ", baks.ToArray()) + ")");
                    StartRestore();
                }
                else
                {
                    SelfTestLog("Asama 3 atlandi: yedek dosyasi yok");
                    SelfTestLog("SELF TEST TAMAM");
                    Application.Exit();
                }
            }
            else if (selftestPhase == 3)
            {
                foreach (DatabaseFile item in items)
                {
                    if (String.IsNullOrEmpty(item.LastResult)) continue;
                    SelfTestLog("  Geri yukle sonucu - " + item.Name + ": " +
                        (item.LastOk ? "OK" : "HATA: " + item.LastResult));
                }
                bool restoredDbAttached = false;
                foreach (DatabaseFile item in items)
                {
                    if (item.Kind == ItemKind.Mdf && item.Attached &&
                        item.Name.Equals("SQLdbTestD", StringComparison.OrdinalIgnoreCase))
                        restoredDbAttached = true;
                }
                SelfTestLog("Geri yuklenen DB listede bagli: " +
                    (restoredDbAttached ? "EVET" : "HAYIR"));
                SelfTestLog("SELF TEST TAMAM");
                Application.Exit();
            }
        }
#endif

        private void SaveSettings()
        {
            try
            {
                Settings.Save(server.Text.Trim(), auth.SelectedIndex == 1, userBox.Text,
                    passBox.Text, remember.Checked, folderBox.Text);
            }
            catch { }
        }

        private sealed class TestResult
        {
            internal bool Ok;
            internal bool Silent;
            internal string Message = "";
            internal string Version = "";
            internal string Edition = "";
            internal string Host = "";
            internal Dictionary<string, string> AttachedMap;
            internal HashSet<string> DatabaseNames;
        }

        private sealed class BatchResult
        {
            internal bool ConnectionOk;
            internal int Success;
            internal string ConnectionError = "";
            internal Dictionary<string, string> AttachedMap;
            internal HashSet<string> DatabaseNames;
        }

        private sealed class BatchOp
        {
            internal DatabaseFile Item;
            internal OpKind Kind;
            internal string RestoreFolder = "";
            internal bool Ok;
            internal string Message = "";
        }

        private static string DescribeKind(OpKind kind)
        {
            if (kind == OpKind.Attach) return "ekle";
            if (kind == OpKind.Detach) return "cikar";
            return "geri yukle";
        }

        private sealed class PasswordForm : Form
        {
            private readonly TextBox box = new TextBox();
            internal string Password;

            internal PasswordForm(string user)
            {
                Text = "SQL Server parolası";
                Font = new Font("Segoe UI", 9F);
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(420, 190);
                BackColor = Color.FromArgb(244, 247, 251);
                try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

                Label title = new Label {
                    Text = "Kimlik doğrulaması", ForeColor = Color.FromArgb(30, 43, 61),
                    Font = new Font("Segoe UI Semibold", 14F), AutoSize = true,
                    Location = new Point(22, 18)
                };
                Label caption = new Label {
                    Text = "Kullanıcı: " + user, ForeColor = Color.FromArgb(97, 113, 135),
                    AutoSize = true, Location = new Point(22, 48)
                };
                Label label = new Label {
                    Text = "PAROLA", ForeColor = Color.FromArgb(97, 113, 135),
                    Font = new Font("Segoe UI Semibold", 8F), AutoSize = true,
                    Location = new Point(22, 78)
                };
                box.Location = new Point(22, 98);
                box.Width = 376;
                box.Height = 28;
                box.UseSystemPasswordChar = true;

                Button cancel = MakeButton("İptal", false);
                cancel.Location = new Point(214, 146);
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
                Button ok = MakeButton("Tamam", true);
                ok.Location = new Point(302, 146);
                ok.Click += delegate { Password = box.Text; DialogResult = DialogResult.OK; };
                AcceptButton = ok;
                CancelButton = cancel;

                Controls.AddRange(new Control[] { title, caption, label, box, cancel, ok });
            }

            private static Button MakeButton(string text, bool primary)
            {
                Button button = new Button {
                    Text = text, AutoSize = true, MinimumSize = new Size(0, 32),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = primary ? Color.FromArgb(43, 111, 184) : Color.White,
                    ForeColor = primary ? Color.White : Color.FromArgb(30, 43, 61),
                    Padding = new Padding(12, 3, 12, 3)
                };
                button.FlatAppearance.BorderColor = primary
                    ? Color.FromArgb(43, 111, 184) : Color.FromArgb(210, 220, 232);
                return button;
            }
        }
    }
}
