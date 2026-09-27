// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 ASCOS DbMount contributors
using System;
using System.Windows.Forms;

namespace DbMount
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += delegate(object sender,
                System.Threading.ThreadExceptionEventArgs e) {
                Log.Error("Beklenmeyen arayuz hatasi", e.Exception);
                MessageBox.Show(
                    "Beklenmeyen bir hata oluştu. Ayrıntılar hata kaydına yazıldı:\n\n" +
                    Log.LogPath + "\n\n" + e.Exception.Message,
                    ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender,
                UnhandledExceptionEventArgs e) {
                try
                {
                    Exception ex = e.ExceptionObject as Exception;
                    if (ex != null) Log.Error("Beklenmeyen islem hatasi", ex);
                    else Log.Error("Beklenmeyen islem hatasi",
                        e.ExceptionObject == null ? "(ayrinti yok)" : e.ExceptionObject.ToString());
                }
                catch { }
            };

            Log.Info("Uygulama baslatildi: " + ProductInfo.DisplayName + " " +
                ProductInfo.Version);
            Log.Info("Isletim sistemi: " + Environment.OSVersion + " (64 bit: " +
                (Environment.Is64BitOperatingSystem ? "evet" : "hayir") + "), CLR: " +
                Environment.Version);

            Application.Run(new DbMountForm());
        }
    }
}
