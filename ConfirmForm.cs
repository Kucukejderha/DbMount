// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 ASCOS DbMount contributors
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DbMount
{
    internal sealed class ConfirmForm : Form
    {
        private static readonly Color Blue = Color.FromArgb(43, 111, 184);
        private static readonly Color Canvas = Color.FromArgb(244, 247, 251);
        private static readonly Color Border = Color.FromArgb(210, 220, 232);
        private static readonly Color Ink = Color.FromArgb(30, 43, 61);
        private static readonly Color Muted = Color.FromArgb(97, 113, 135);
        private static readonly Color Danger = Color.FromArgb(217, 83, 79);
        private static readonly Color InfoBack = Color.FromArgb(232, 241, 251);
        private static readonly Color InfoInk = Color.FromArgb(42, 91, 143);
        private static readonly Color InfoBorder = Color.FromArgb(190, 213, 238);
        private static readonly Font WarningFont = new Font("Segoe UI", 8F);

        internal sealed class ConfirmItem
        {
            internal DatabaseFile File;
            internal bool AttachOp;
            internal int SessionCount = -1;
            internal bool FileLocked;
            internal bool StatusChecked;
        }

        private readonly List<ConfirmItem> items;
        private readonly bool attachMode;
        private readonly bool statusChecked;
        private readonly ListBox list = new ListBox();

        internal ConfirmForm(List<ConfirmItem> confirmItems, bool attachOperation, bool statusOk)
        {
            items = confirmItems;
            attachMode = attachOperation;
            statusChecked = statusOk;

            Text = (attachMode ? "Ekleme onayı" : "Çıkarma onayı") + " — " + ProductInfo.DisplayName;
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(620, 480);
            BackColor = Canvas;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = new Label {
                Text = items.Count + " veritabanı " +
                    (attachMode ? "eklenecek" : "çıkarılacak"),
                ForeColor = Ink, Font = new Font("Segoe UI Semibold", 14F),
                AutoSize = true, Location = new Point(22, 18)
            };
            Label subtitle = new Label {
                Text = "İşleme başlamadan önce listeyi gözden geçirin.",
                ForeColor = Muted, AutoSize = true, Location = new Point(22, 46)
            };

            Panel listFrame = new Panel {
                BackColor = Color.White, Padding = new Padding(1),
                Location = new Point(22, 78),
                Size = new Size(576, 288),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            listFrame.Paint += delegate(object sender, PaintEventArgs e) {
                ControlPaint.DrawBorder(e.Graphics, listFrame.ClientRectangle,
                    Border, ButtonBorderStyle.Solid);
            };

            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Color.White;
            list.ForeColor = Ink;
            list.IntegralHeight = false;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = 64;
            list.DrawItem += DrawConfirmItem;
            foreach (ConfirmItem item in items) list.Items.Add(item);
            listFrame.Controls.Add(list);

            Panel notice = new Panel {
                BackColor = statusChecked ? InfoBack : Color.FromArgb(253, 241, 240),
                Location = new Point(22, 374),
                Size = new Size(576, 48),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            notice.Paint += delegate(object sender, PaintEventArgs e) {
                ControlPaint.DrawBorder(e.Graphics, notice.ClientRectangle,
                    statusChecked ? InfoBorder : Color.FromArgb(240, 196, 192),
                    ButtonBorderStyle.Solid);
            };
            Label noticeText = new Label {
                AutoSize = true, Location = new Point(14, 15), MaximumSize = new Size(548, 0),
                ForeColor = statusChecked ? InfoInk : Danger,
                Font = new Font("Segoe UI", 8.5F)
            };
            if (!statusChecked)
                noticeText.Text = "⚠  Sunucu durumu denetlenemedi; bağlantı ayarlarınızı kontrol edin. " +
                    "Yine de devam edebilirsiniz, hatalar işlem sırasında bildirilir.";
            else if (attachMode)
                noticeText.Text = "ⓘ  İşlem dosyaları taşımaz; veritabanları bulundukları klasörde yerinde kalır.";
            else
                noticeText.Text = "ⓘ  Dosyalar silinmez; klasörde kalır. Etkin bağlantılar otomatik olarak kapatılır.";
            notice.Controls.Add(noticeText);

            Button cancel = MakeButton("İptal", false);
            cancel.Location = new Point(400, 436);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Button proceed = MakeButton("Devam et", true);
            proceed.Location = new Point(494, 436);
            proceed.Click += delegate { DialogResult = DialogResult.OK; };
            AcceptButton = proceed;
            CancelButton = cancel;

            Controls.AddRange(new Control[] { title, subtitle, listFrame, notice, cancel, proceed });
        }

        private static Button MakeButton(string text, bool primary)
        {
            Button button = new Button {
                Text = text, AutoSize = true, MinimumSize = new Size(0, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Blue : Color.White,
                ForeColor = primary ? Color.White : Ink,
                Padding = new Padding(14, 3, 14, 3)
            };
            button.FlatAppearance.BorderColor = primary ? Blue : Border;
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(35, 96, 162) : Color.FromArgb(249, 251, 253);
            return button;
        }

        private void DrawConfirmItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= items.Count) return;
            ConfirmItem item = items[e.Index];
            using (Brush brush = new SolidBrush(Color.White))
                e.Graphics.FillRectangle(brush, e.Bounds);

            Rectangle nameBounds = new Rectangle(e.Bounds.Left + 16, e.Bounds.Top + 6,
                Math.Max(0, e.Bounds.Width - 32), 20);
            using (Font nameFont = new Font("Segoe UI Semibold", 9.5F))
                TextRenderer.DrawText(e.Graphics, item.File.Name, nameFont, nameBounds, Ink,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            string action = item.AttachOp
                ? "Sunucuya eklenecek"
                : "Sunucudan çıkarılacak — dosyalar silinmez";
            int x = e.Bounds.Left + 16;
            int y = e.Bounds.Top + 27;
            Rectangle actionBounds = new Rectangle(x, y, e.Bounds.Width - 32, 17);
            TextRenderer.DrawText(e.Graphics, action, Font, actionBounds, Ink,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            x += TextRenderer.MeasureText(action, Font).Width;

            string tail = "  ·  " + item.File.FileSummary();
            Rectangle tailBounds = new Rectangle(x, y,
                Math.Max(0, e.Bounds.Right - x - 16), 17);
            TextRenderer.DrawText(e.Graphics, tail, Font, tailBounds, Muted,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            string warning = null;
            if (item.StatusChecked && !item.AttachOp && item.SessionCount > 0)
                warning = "⚠  " + item.SessionCount + " etkin bağlantı var — Logo/Netsis gibi " +
                    "bir uygulama bu veritabanını kullanıyor olabilir; işlem bağlantıları kapatacak.";
            else if (item.StatusChecked && item.AttachOp && item.FileLocked)
                warning = "⚠  Dosya başka bir uygulama veya SQL Server örneği tarafından kullanılıyor olabilir.";
            else if (!item.StatusChecked)
                warning = "Durum denetlenemedi";

            if (warning != null)
            {
                Rectangle warningBounds = new Rectangle(e.Bounds.Left + 16,
                    e.Bounds.Top + 45, Math.Max(0, e.Bounds.Width - 32), 15);
                TextRenderer.DrawText(e.Graphics, warning, WarningFont,
                    warningBounds, Danger,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            using (Pen separator = new Pen(Color.FromArgb(234, 239, 245)))
                e.Graphics.DrawLine(separator, e.Bounds.Left + 12, e.Bounds.Bottom - 1,
                    e.Bounds.Right - 12, e.Bounds.Bottom - 1);
        }
    }
}
