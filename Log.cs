// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 ASCOS DbMount contributors
using System;
using System.IO;
using System.Net;
using System.Text;

namespace DbMount
{
    internal static class Log
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DbMount");
        private static readonly string LogFile = Path.Combine(LogDirectory, "DbMount.log");
        private static readonly string PreviousLogFile = Path.Combine(LogDirectory, "DbMount.previous.log");
        private const long MaxLogBytes = 2L * 1024 * 1024;
        private static readonly object Sync = new object();

        internal static string LogPath { get { return LogFile; } }

        internal static void Info(string message)
        {
            Write("INFO", message);
        }

        internal static void Error(string context, string message)
        {
            Write("HATA", context + " :: " + message);
        }

        internal static void Error(string context, Exception ex)
        {
            Write("HATA", context + " :: " + Format(ex));
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    FileInfo info = new FileInfo(LogFile);
                    if (info.Exists && info.Length > MaxLogBytes)
                    {
                        try { File.Copy(LogFile, PreviousLogFile, true); } catch { }
                        File.Delete(LogFile);
                    }
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + level + "] " +
                        message + Environment.NewLine;
                    File.AppendAllText(LogFile, line, Encoding.UTF8);
                }
            }
            catch { }
        }

        internal static string Format(Exception ex)
        {
            if (ex == null) return "";
            StringBuilder builder = new StringBuilder();
            Exception current = ex;
            int depth = 0;
            while (current != null && depth < 8)
            {
                if (builder.Length > 0) builder.AppendLine("  -- neden: --");
                builder.AppendLine(current.GetType().FullName + ": " + current.Message);
                System.Data.SqlClient.SqlException sqlEx = current as System.Data.SqlClient.SqlException;
                if (sqlEx != null)
                {
                    foreach (System.Data.SqlClient.SqlError error in sqlEx.Errors)
                    {
                        builder.AppendLine("    SQL hata " + error.Number +
                            " (sinif " + error.Class + ", durum " + error.State +
                            ", satir " + error.LineNumber + ", yordam " +
                            (error.Procedure ?? "") + "): " + error.Message);
                    }
                }
                if (current.StackTrace != null) builder.AppendLine(current.StackTrace);
                current = current.InnerException;
                depth++;
            }
            return builder.ToString().Trim();
        }

        internal static string ReadAll()
        {
            try
            {
                lock (Sync)
                {
                    if (File.Exists(LogFile)) return File.ReadAllText(LogFile, Encoding.UTF8);
                }
            }
            catch { }
            return "";
        }

        internal static long CurrentSize()
        {
            try
            {
                lock (Sync)
                {
                    FileInfo info = new FileInfo(LogFile);
                    return info.Exists ? info.Length : 0;
                }
            }
            catch { return 0; }
        }

        internal static string BuildSupportPayload()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("== ASCOS DbMount destek kaydi ==");
            builder.AppendLine("Urun: " + ProductInfo.DisplayName + " " + ProductInfo.Version);
            builder.AppendLine("Tarih: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Isletim sistemi: " + Environment.OSVersion +
                " (64 bit: " + (Environment.Is64BitOperatingSystem ? "evet" : "hayir") + ")");
            builder.AppendLine("CLR: " + Environment.Version);
            builder.AppendLine("Makine: " + Environment.MachineName);
            builder.AppendLine("");
            builder.AppendLine("== Uygulama gunlugu ==");
            builder.Append(ReadAll());
            return builder.ToString();
        }
    }

    internal static class SupportUpload
    {
        internal static void ConfigureTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }
        }

        internal static string Upload(string payload)
        {
            ConfigureTls();
            string url = Common.UploadUrl +
                "?product=" + Uri.EscapeDataString(ProductInfo.DisplayName) +
                "&version=" + Uri.EscapeDataString(ProductInfo.Version) +
                "&machine=" + Uri.EscapeDataString(Environment.MachineName);
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "text/plain; charset=utf-8";
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                byte[] data = Encoding.UTF8.GetBytes(payload);
                request.ContentLength = data.Length;
                using (Stream stream = request.GetRequestStream())
                    stream.Write(data, 0, data.Length);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
                        return "OK";
                    return "HTTP " + (int)response.StatusCode + " " + response.StatusDescription;
                }
            }
            catch (WebException ex)
            {
                Log.Error("Kayit yukleme", ex);
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    if ((int)response.StatusCode == 404)
                        return "404";
                    return "HTTP " + (int)response.StatusCode + " " + response.StatusDescription;
                }
                return "AG: " + ex.Message;
            }
            catch (Exception ex)
            {
                Log.Error("Kayit yukleme", ex);
                return "HATA: " + ex.Message;
            }
        }
    }
}
