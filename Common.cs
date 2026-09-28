// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 ASCOS DbMount contributors
using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace DbMount
{
    internal static class ProductInfo
    {
        internal const string DisplayName = "ASCOS DbMount";
        internal const string ManagementTitle = "ASCOS DbMount Yönetimi";
        internal const string Version = "1.2.1";
    }

    internal static class Common
    {
        internal static string UploadUrl =
            "https://rotaniz.com/ascos-araclar/dbmount/log-upload.php";
    }

    internal static class Settings
    {
        private const string SettingsPath = @"SOFTWARE\ASCOS\DbMount";
        private const string LegacySettingsPath = @"SOFTWARE\ASCOS\SQLdb";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ASCOS.SqlDb.Credentials.v1");

        internal static void Save(string server, bool sqlAuth, string user, string password,
            bool remember, string folder)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsPath))
            {
                key.SetValue("Server", server ?? "", RegistryValueKind.String);
                key.SetValue("SqlAuth", sqlAuth ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("User", user ?? "", RegistryValueKind.String);
                key.SetValue("Folder", folder ?? "", RegistryValueKind.String);
                if (remember && sqlAuth && !String.IsNullOrEmpty(password))
                {
                    byte[] plain = Encoding.UTF8.GetBytes(password);
                    try
                    {
                        key.SetValue("Secret", ProtectedData.Protect(plain, Entropy,
                            DataProtectionScope.CurrentUser), RegistryValueKind.Binary);
                    }
                    finally { Array.Clear(plain, 0, plain.Length); }
                }
                else key.DeleteValue("Secret", false);
            }
        }

        internal static void Load(out string server, out bool sqlAuth, out string user,
            out string password, out string folder)
        {
            server = "localhost";
            sqlAuth = false;
            user = "";
            password = "";
            folder = "";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsPath, false))
                {
                    if (key != null)
                    {
                        ReadValues(key, ref server, ref sqlAuth, ref user, ref password, ref folder);
                        return;
                    }
                }
                using (RegistryKey legacy = Registry.CurrentUser.OpenSubKey(LegacySettingsPath, false))
                {
                    if (legacy != null)
                        ReadValues(legacy, ref server, ref sqlAuth, ref user, ref password, ref folder);
                }
            }
            catch { }
        }

        private static void ReadValues(RegistryKey key, ref string server, ref bool sqlAuth,
            ref string user, ref string password, ref string folder)
        {
            string savedServer = key.GetValue("Server") as string;
            if (!String.IsNullOrEmpty(savedServer)) server = savedServer;
            sqlAuth = ((key.GetValue("SqlAuth") as int?) ?? 0) == 1;
            user = key.GetValue("User") as string ?? "";
            folder = key.GetValue("Folder") as string ?? "";
            byte[] encrypted = key.GetValue("Secret") as byte[];
            if (encrypted != null)
            {
                byte[] plain = ProtectedData.Unprotect(encrypted, Entropy,
                    DataProtectionScope.CurrentUser);
                try { password = Encoding.UTF8.GetString(plain); }
                finally { Array.Clear(plain, 0, plain.Length); }
            }
        }
    }
}
