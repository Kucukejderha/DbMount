// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 ASCOS DbMount contributors
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace DbMount
{
    internal enum ItemKind { Mdf, Bak }

    internal sealed class DatabaseFile
    {
        internal readonly string MdfPath;
        internal readonly string Name;
        internal string DatabaseName;
        internal readonly List<string> DataFiles = new List<string>();
        internal readonly List<string> LogFiles = new List<string>();
        internal long MdfSize;
        internal bool Attached;
        internal string AttachedName;
        internal string LastResult;
        internal bool LastOk;
        internal ItemKind Kind = ItemKind.Mdf;
        internal string BackupDatabaseName;
        internal int BackupType;
        internal DateTime BackupDate;

        internal DatabaseFile(string mdfPath)
        {
            MdfPath = mdfPath;
            Name = Path.GetFileNameWithoutExtension(mdfPath);
            DatabaseName = Name;
            try { MdfSize = new FileInfo(mdfPath).Length; } catch { }
        }

        internal string FileSummary()
        {
            StringBuilder builder = new StringBuilder();
            if (Kind == ItemKind.Bak)
            {
                builder.Append("bak " + FormatSize(MdfSize));
                return builder.ToString();
            }
            builder.Append("mdf " + FormatSize(MdfSize));
            string folder = Path.GetDirectoryName(MdfPath);
            string ownName = Path.GetFileName(MdfPath);
            foreach (string file in DataFiles)
            {
                string name = Path.GetFileName(file);
                if (name.Equals(ownName, StringComparison.OrdinalIgnoreCase)) continue;
                long size = TryFileSize(Path.Combine(folder, name));
                if (size >= 0) builder.Append(" · ndf " + FormatSize(size));
            }
            foreach (string file in LogFiles)
            {
                long size = TryFileSize(Path.Combine(folder, Path.GetFileName(file)));
                if (size >= 0) builder.Append(" · ldf " + FormatSize(size));
            }
            return builder.ToString();
        }

        private static long TryFileSize(string path)
        {
            try
            {
                if (File.Exists(path)) return new FileInfo(path).Length;
            }
            catch { }
            return -1;
        }

        internal static string FormatSize(long bytes)
        {
            if (bytes < 0) return "?";
            double value = bytes;
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(unit == 0 ? "0" : "0.#") + " " + units[unit];
        }
    }

    internal sealed class BackupHeader
    {
        internal string DatabaseName = "";
        internal int Type;
        internal DateTime BackupDate;
    }

    internal sealed class BackupFileEntry
    {
        internal string LogicalName = "";
        internal string PhysicalName = "";
        internal string Type = "";
    }

    internal static class SqlOps
    {
        internal static List<string> DiscoverInstances()
        {
            List<string> instances = new List<string>();
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL", false))
                {
                    if (key != null)
                    {
                        string[] names = key.GetValueNames();
                        Array.Sort(names, StringComparer.OrdinalIgnoreCase);
                        foreach (string name in names)
                        {
                            string server = name.Equals("MSSQLSERVER",
                                StringComparison.OrdinalIgnoreCase)
                                ? "localhost" : "localhost\\" + name;
                            if (!instances.Contains(server)) instances.Add(server);
                        }
                    }
                }
            }
            catch { }
            if (instances.Count == 0) instances.Add("localhost");
            return instances;
        }

        internal static SqlConnection OpenConnection(string server, bool windowsAuth,
            string user, string password)
        {
            return OpenConnection(server, windowsAuth, user, password, 15);
        }

        internal static SqlConnection OpenConnection(string server, bool windowsAuth,
            string user, string password, int connectTimeout)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            builder.DataSource = String.IsNullOrWhiteSpace(server) ? "localhost" : server.Trim();
            builder.InitialCatalog = "master";
            builder.ConnectTimeout = connectTimeout;
            builder.ApplicationName = "ASCOS DbMount";
            if (windowsAuth) builder.IntegratedSecurity = true;
            else
            {
                builder.UserID = user ?? "";
                builder.Password = password ?? "";
            }
            SqlConnection connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }

        internal static void QueryServerInfo(SqlConnection connection, out string version,
            out string edition, out string host)
        {
            version = edition = host = "";
            using (SqlCommand command = new SqlCommand(
                "SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(30)), " +
                "CAST(SERVERPROPERTY('Edition') AS varchar(60)), " +
                "CAST(SERVERPROPERTY('MachineName') AS varchar(128))", connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    version = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                    edition = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                    host = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                }
            }
        }

        internal static Dictionary<string, string> QueryAttachedFiles(SqlConnection connection)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            using (SqlCommand command = new SqlCommand(
                "SELECT d.name, mf.physical_name FROM sys.databases d " +
                "INNER JOIN sys.master_files mf ON mf.database_id = d.database_id " +
                "WHERE d.database_id > 4", connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string name = reader.GetString(0);
                    string physical = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    if (String.IsNullOrEmpty(physical)) continue;
                    string normalized = NormalizePath(physical);
                    if (!map.ContainsKey(normalized)) map[normalized] = name;
                }
            }
            return map;
        }

        internal static string NormalizePath(string path)
        {
            if (String.IsNullOrEmpty(path)) return "";
            try { return Path.GetFullPath(path.Trim()); }
            catch { return path.Trim(); }
        }

        internal static int QuerySessionCount(System.Data.SqlClient.SqlConnection connection,
            string databaseName)
        {
            using (SqlCommand command = new SqlCommand(
                "SELECT COUNT(*) FROM sys.dm_exec_sessions " +
                "WHERE database_id = DB_ID(@name) AND is_user_process = 1", connection))
            {
                command.Parameters.AddWithValue("@name", databaseName);
                return (int)command.ExecuteScalar();
            }
        }

        internal static List<string> ResolveExistingFiles(DatabaseFile item)
        {
            string folder = Path.GetDirectoryName(item.MdfPath);
            List<string> resolved = new List<string>();
            if (File.Exists(item.MdfPath) && !resolved.Contains(item.MdfPath))
                resolved.Add(item.MdfPath);
            foreach (string file in item.DataFiles)
            {
                string candidate = ResolveCandidate(folder, file);
                if (candidate != null && !resolved.Contains(candidate)) resolved.Add(candidate);
            }
            foreach (string file in item.LogFiles)
            {
                string candidate = ResolveCandidate(folder, file);
                if (candidate != null && !resolved.Contains(candidate)) resolved.Add(candidate);
            }
            return resolved;
        }

        internal static bool AnyFileLocked(DatabaseFile item)
        {
            foreach (string path in ResolveExistingFiles(item))
            {
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open,
                        FileAccess.Read, FileShare.None)) { }
                }
                catch { return true; }
            }
            return false;
        }

        internal static bool AnyFileLocked(string path)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.None)) { }
                return false;
            }
            catch { return true; }
        }

        internal static BackupHeader QueryBackupHeader(SqlConnection connection,
            string backupPath)
        {
            using (SqlCommand command = new SqlCommand(
                "RESTORE HEADERONLY FROM DISK = '" + EscapeSql(backupPath) + "'", connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                BackupHeader header = new BackupHeader();
                if (reader.Read())
                {
                    header.DatabaseName = reader.GetString(reader.GetOrdinal("DatabaseName"));
                    header.Type = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("BackupType")));
                    header.BackupDate = Convert.ToDateTime(
                        reader.GetValue(reader.GetOrdinal("BackupFinishDate")));
                }
                return header;
            }
        }

        internal static List<BackupFileEntry> QueryBackupFileList(SqlConnection connection,
            string backupPath)
        {
            List<BackupFileEntry> entries = new List<BackupFileEntry>();
            using (SqlCommand command = new SqlCommand(
                "RESTORE FILELISTONLY FROM DISK = '" + EscapeSql(backupPath) + "'", connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                int logicalIndex = reader.GetOrdinal("LogicalName");
                int physicalIndex = reader.GetOrdinal("PhysicalName");
                int typeIndex = reader.GetOrdinal("Type");
                while (reader.Read())
                {
                    BackupFileEntry entry = new BackupFileEntry();
                    entry.LogicalName = reader.GetString(logicalIndex);
                    entry.PhysicalName = reader.IsDBNull(physicalIndex) ? "" : reader.GetString(physicalIndex);
                    entry.Type = reader.GetString(typeIndex);
                    entries.Add(entry);
                }
            }
            return entries;
        }

        internal static HashSet<string> QueryDatabaseNames(SqlConnection connection)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (SqlCommand command = new SqlCommand(
                "SELECT name FROM sys.databases WHERE database_id > 4", connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read()) names.Add(reader.GetString(0));
            }
            return names;
        }

        internal static void RestoreDatabase(SqlConnection connection, string backupPath,
            string databaseName, string targetFolder, bool replace)
        {
            List<BackupFileEntry> entries = QueryBackupFileList(connection, backupPath);
            if (entries.Count == 0)
                throw new ApplicationException("Yedek dosyasında veritabanı dosya listesi okunamadı.");
            foreach (BackupFileEntry entry in entries)
            {
                if (entry.Type.Equals("S", StringComparison.OrdinalIgnoreCase))
                    throw new ApplicationException(
                        "FILESTREAM içeren yedekler desteklenmiyor. Bu yedeği SQL Server " +
                        "Management Studio ile geri yükleyin.");
            }
            StringBuilder sql = new StringBuilder();
            sql.Append("RESTORE DATABASE [");
            sql.Append(databaseName.Replace("]", "]]"));
            sql.Append("] FROM DISK = '");
            sql.Append(EscapeSql(backupPath));
            sql.Append("' WITH ");
            List<string> moves = new List<string>();
            foreach (BackupFileEntry entry in entries)
            {
                string fileName = Path.GetFileName(entry.PhysicalName.Trim());
                if (String.IsNullOrEmpty(fileName))
                    fileName = entry.LogicalName +
                        (entry.Type.Equals("L", StringComparison.OrdinalIgnoreCase) ? ".ldf" : ".mdf");
                string target = Path.Combine(targetFolder, fileName);
                moves.Add("MOVE '" + EscapeSql(entry.LogicalName) + "' TO '" +
                    EscapeSql(target) + "'");
            }
            sql.Append(String.Join(", ", moves.ToArray()));
            if (replace) sql.Append(", REPLACE");
            sql.Append(", RECOVERY");
            using (SqlCommand command = new SqlCommand(sql.ToString(), connection))
                command.ExecuteNonQuery();
        }

        internal static string EscapeSql(string value)
        {
            return value == null ? "" : value.Replace("'", "''");
        }

        internal static List<DatabaseFile> ScanFolder(string folder)
        {
            List<DatabaseFile> items = new List<DatabaseFile>();
            if (String.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return items;
            string[] files = Directory.GetFiles(folder, "*.mdf", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string mdf in files)
            {
                DatabaseFile item = new DatabaseFile(mdf);
                try { MdfHeader.Read(item); } catch { }
                items.Add(item);
            }
            return items;
        }

        internal static void AttachDatabase(SqlConnection connection, DatabaseFile item)
        {
            string folder = Path.GetDirectoryName(item.MdfPath);

            int headerDataCount = item.DataFiles.Count;
            List<string> foundData = new List<string>();
            foreach (string file in item.DataFiles)
            {
                string candidate = ResolveCandidate(folder, file);
                if (candidate != null && !foundData.Contains(candidate)) foundData.Add(candidate);
            }
            if (!foundData.Contains(item.MdfPath)) foundData.Insert(0, item.MdfPath);

            int expectedOthers = Math.Max(0, headerDataCount - 1);
            int foundOthers = Math.Max(0, foundData.Count - 1);
            if (foundOthers < expectedOthers)
            {
                throw new ApplicationException(
                    "Veritabanına ait veri dosyalarının bir kısmı klasörde bulunamadı. " +
                    "Tüm MDF/NDF dosyaları aynı klasörde olmalıdır.");
            }

            List<string> foundLogs = new List<string>();
            foreach (string file in item.LogFiles)
            {
                string candidate = ResolveCandidate(folder, file);
                if (candidate != null && !foundLogs.Contains(candidate)) foundLogs.Add(candidate);
            }

            if (item.LogFiles.Count > 0 && foundLogs.Count == item.LogFiles.Count)
            {
                List<string> all = new List<string>(foundData);
                all.AddRange(foundLogs);
                ExecuteAttachDb(connection, item.DatabaseName, all);
                return;
            }

            try
            {
                ExecuteAttachSingleFile(connection, item.DatabaseName, item.MdfPath);
            }
            catch (SqlException ex)
            {
                bool mismatch = ex.Message.IndexOf("does not match the primary file",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ex.Message.IndexOf("do not match the primary file",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                if (!mismatch || foundLogs.Count == 0) throw;
                List<string> all = new List<string>(foundData);
                all.AddRange(foundLogs);
                ExecuteAttachDb(connection, item.DatabaseName, all);
            }
        }

        private static string ResolveCandidate(string folder, string headerPhysical)
        {
            if (String.IsNullOrEmpty(headerPhysical)) return null;
            string name = Path.GetFileName(headerPhysical.Trim());
            if (String.IsNullOrEmpty(name)) return null;
            string candidate = Path.Combine(folder, name);
            return File.Exists(candidate) ? candidate : null;
        }

        private static void ExecuteAttachDb(SqlConnection connection, string databaseName,
            List<string> files)
        {
            using (SqlCommand command = new SqlCommand("sp_attach_db", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@dbname", databaseName);
                int count = Math.Min(files.Count, 16);
                for (int i = 0; i < count; i++)
                    command.Parameters.AddWithValue("@filename" + (i + 1), files[i]);
                command.ExecuteNonQuery();
            }
        }

        private static void ExecuteAttachSingleFile(SqlConnection connection,
            string databaseName, string mdfPath)
        {
            using (SqlCommand command = new SqlCommand("sp_attach_single_file_db", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@dbname", databaseName);
                command.Parameters.AddWithValue("@physname", mdfPath);
                command.ExecuteNonQuery();
            }
        }

        internal static void DetachDatabase(SqlConnection connection, string databaseName)
        {
            SetSingleUser(connection, databaseName);
            try
            {
                ExecuteDetach(connection, databaseName);
            }
            catch
            {
                RestoreMultiUser(connection, databaseName);
                throw;
            }
        }

        private static void ExecuteDetach(SqlConnection connection, string databaseName)
        {
            using (SqlCommand command = new SqlCommand("sp_detach_db", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@dbname", databaseName);
                command.ExecuteNonQuery();
            }
        }

        private static void SetSingleUser(SqlConnection connection, string databaseName)
        {
            using (SqlCommand command = new SqlCommand(
                "ALTER DATABASE " + QuoteIdentifier(databaseName) +
                " SET SINGLE_USER WITH ROLLBACK IMMEDIATE", connection))
                command.ExecuteNonQuery();
        }

        private static void RestoreMultiUser(SqlConnection connection, string databaseName)
        {
            try
            {
                using (SqlCommand command = new SqlCommand(
                    "ALTER DATABASE " + QuoteIdentifier(databaseName) + " SET MULTI_USER",
                    connection))
                    command.ExecuteNonQuery();
            }
            catch { }
        }

        private static string QuoteIdentifier(string name)
        {
            return "[" + name.Replace("]", "]]") + "]";
        }

        internal static string TranslateSqlError(Exception ex)
        {
            SqlException sqlEx = ex as SqlException;
            if (sqlEx != null)
            {
                foreach (SqlError error in sqlEx.Errors)
                {
                    switch (error.Number)
                    {
                        case 18456:
                            return "Oturum açılamadı. Kullanıcı adı/parola veya Windows yetkisi geçersiz.";
                        case 5120:
                            return "Erişim engellendi. SQL Server hizmet hesabının bu klasöre okuma/yazma izni yok.";
                        case 5105:
                            return "Dosya etkinleştirme hatası. Klasör erişim izinlerini denetleyin.";
                        case 5133:
                            return "Dizin veya dosya yolu doğrulanamadı.";
                        case 5170:
                            return "Dosya eksik ya da geçersiz. Veritabanına ait tüm dosyalar aynı klasörde olmalıdır.";
                        case 5172:
                            return "Dosya başlığı geçerli bir veritabanı dosyası değil.";
                        case 1801:
                            return "Sunucuda aynı ada sahip bir veritabanı zaten bağlı.";
                        case 1824:
                            return "Dosyalar farklı bir veritabanına ait; karışık dosyalar seçilmiş olabilir.";
                        case 4060:
                            return "Veritabanına erişilemedi. Hesabın sunucu erişim yetkisi yok.";
                        case 15350:
                            return "Başka bir istemci dosyayı kilitliyor; işlem tamamlanamadı.";
                    }
                    if (error.Number == 2 || error.Number == 3)
                        return "Sunucuya ulaşılamadı. Sunucu adını ve hizmetin çalıştığını denetleyin.";
                    if (error.Number == 4064)
                        return "Kullanıcının varsayılan veritabanına erişilemiyor.";
                }
                string message = sqlEx.Message ?? "";
                if (message.IndexOf("error 32", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Dosya başka bir işlem tarafından kilitli. Veritabanı başka bir örnekte bağlı olabilir.";
                if (message.IndexOf("error 5", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    message.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Erişim engellendi. SQL Server hizmet hesabının bu klasöre erişim izni yok.";
                if (message.IndexOf("Unable to open the physical file",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Fiziksel dosya açılamadı. Dosya yolunu ve hizmet hesabının izinlerini denetleyin.";
                if (message.IndexOf("cannot be opened because it is version",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("downgrade path is not supported",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Veritabanı dosyası bu sunucudan daha yeni bir sürüme ait. " +
                        "Daha güncel bir SQL Server örneğine bağlanın.";
                if (message.IndexOf("already exists",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Sunucuda aynı ada sahip bir veritabanı zaten var.";
                if (message.IndexOf("created by a different version",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("was backed up on a server running version",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Yedek, bu sunucudan daha yeni bir SQL Server sürümünde alınmış. " +
                        "Daha güncel bir örneğe bağlanın.";
                if (message.IndexOf("Cannot open backup device",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Yedek dosyası açılamadı. SQL Server hizmet hesabının dosyaya " +
                        "erişim izni olduğundan emin olun.";
                if (message.IndexOf("cannot be restored over the existing",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Hedef klasörde aynı adlı dosya var; üzerine yazılamadı. " +
                        "Hedef klasörü değiştirin veya çakışan dosyayı taşıyın.";
            }
            return ex.Message;
        }
    }

    internal static class MdfHeader
    {
        private const int FileListOffset = 261970;
        private const int EntryStride = 792;
        private const int LogicalOffset = 280;
        private const int PathOffset = 536;
        private const int FieldBytes = 256;

        internal static void Read(DatabaseFile item)
        {
            byte[] buffer = new byte[327680];
            int read;
            try
            {
                using (FileStream stream = new FileStream(item.MdfPath, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    read = stream.Read(buffer, 0, buffer.Length);
            }
            catch
            {
                ApplyFallbackHeuristics(item);
                return;
            }
            if (read < 512) return;

            string databaseName = ScanPageZeroName(buffer, read);
            if (!String.IsNullOrEmpty(databaseName)) item.DatabaseName = databaseName;

            if (read < FileListOffset + PathOffset + FieldBytes)
            {
                ApplyFallbackHeuristics(item);
                return;
            }

            bool any = false;
            for (int entry = 0; entry < 64; entry++)
            {
                int baseOffset = FileListOffset + entry * EntryStride;
                if (baseOffset + PathOffset + FieldBytes > read) break;
                string logical = ReadField(buffer, baseOffset + LogicalOffset);
                string physical = ReadField(buffer, baseOffset + PathOffset);
                if (String.IsNullOrEmpty(logical) || String.IsNullOrEmpty(physical)) break;
                any = true;
                string lower = physical.ToLowerInvariant();
                if (lower.EndsWith(".mdf") || lower.EndsWith(".ndf"))
                {
                    if (!item.DataFiles.Contains(physical)) item.DataFiles.Add(physical);
                }
                else if (lower.EndsWith(".ldf"))
                {
                    if (!item.LogFiles.Contains(physical)) item.LogFiles.Add(physical);
                }
            }
            if (any) return;
            ApplyFallbackHeuristics(item);
        }

        private static string ScanPageZeroName(byte[] buffer, int read)
        {
            int limit = Math.Min(1200, read - 1);
            for (int start = 300; start < limit; start++)
            {
                if (!IsPrintable(buffer, start)) continue;
                StringBuilder builder = new StringBuilder();
                int i = start;
                while (i + 1 < limit && builder.Length < 96)
                {
                    char c = (char)(buffer[i] | (buffer[i + 1] << 8));
                    if (c < 32 || c >= 127) break;
                    builder.Append(c);
                    i += 2;
                }
                if (builder.Length >= 3)
                {
                    string value = builder.ToString();
                    if (LooksLikeIdentifier(value)) return value;
                }
                start = i;
            }
            return null;
        }

        private static bool IsPrintable(byte[] buffer, int offset)
        {
            if (offset + 1 >= buffer.Length) return false;
            char c = (char)(buffer[offset] | (buffer[offset + 1] << 8));
            return c >= 32 && c < 127;
        }

        private static bool LooksLikeIdentifier(string value)
        {
            if (String.IsNullOrEmpty(value)) return false;
            foreach (char c in value)
            {
                if (Char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ' ||
                    c == '.') continue;
                return false;
            }
            return true;
        }

        private static string ReadField(byte[] buffer, int offset)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = offset; i + 1 < offset + FieldBytes && i + 1 < buffer.Length; i += 2)
            {
                char c = (char)(buffer[i] | (buffer[i + 1] << 8));
                if (c == 0) break;
                if (c < 32 || c >= 127) break;
                builder.Append(c);
            }
            return builder.ToString().Trim();
        }

        private static void ApplyFallbackHeuristics(DatabaseFile item)
        {
            string folder = Path.GetDirectoryName(item.MdfPath);
            string[] ldfs = Directory.GetFiles(folder, "*.ldf", SearchOption.TopDirectoryOnly);
            foreach (string ldf in ldfs)
            {
                string baseName = Path.GetFileNameWithoutExtension(ldf);
                string target = item.Name;
                if (baseName.Equals(target + "_log", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals(target + "Log", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    if (!item.LogFiles.Contains(ldf)) item.LogFiles.Add(ldf);
                }
            }
        }
    }
}
