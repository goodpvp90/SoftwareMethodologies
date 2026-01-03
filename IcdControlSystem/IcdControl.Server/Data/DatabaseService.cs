using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using IcdControl.Models;
using System.IO;

namespace IcdControl.Server.Data
{
    public class DatabaseService
    {
        private readonly string _conn;

        public DatabaseService()
        {
            _conn = BuildConnectionString();
            using var c = new SqliteConnection(_conn);
            c.Open();

            // Create Tables
            using var cmd1 = c.CreateCommand();
            cmd1.CommandText = "CREATE TABLE IF NOT EXISTS Users (UserId TEXT PRIMARY KEY, Username TEXT, Email TEXT, PasswordHash TEXT, IsAdmin INTEGER)";
            cmd1.ExecuteNonQuery();

            using var cmd2 = c.CreateCommand();
            cmd2.CommandText = "CREATE TABLE IF NOT EXISTS Icds (IcdId TEXT PRIMARY KEY, Name TEXT, Version REAL, StructureContent TEXT)";
            cmd2.ExecuteNonQuery();

            using var cmd3 = c.CreateCommand();
            cmd3.CommandText = "CREATE TABLE IF NOT EXISTS UserIcdPermissions (UserId TEXT, IcdId TEXT, CanEdit INTEGER, PRIMARY KEY(UserId, IcdId))";
            cmd3.ExecuteNonQuery();

            // Version History
            using var cmd4 = c.CreateCommand();
            cmd4.CommandText = "CREATE TABLE IF NOT EXISTS IcdVersions (VersionId TEXT PRIMARY KEY, IcdId TEXT, VersionNumber REAL, StructureContent TEXT, CreatedAt TEXT, CreatedBy TEXT)";
            cmd4.ExecuteNonQuery();

            // Comments
            using var cmd5 = c.CreateCommand();
            cmd5.CommandText = "CREATE TABLE IF NOT EXISTS IcdComments (CommentId TEXT PRIMARY KEY, IcdId TEXT, UserId TEXT, CommentText TEXT, CreatedAt TEXT)";
            cmd5.ExecuteNonQuery();

            // Change History
            using var cmd6 = c.CreateCommand();
            cmd6.CommandText = "CREATE TABLE IF NOT EXISTS IcdChangeHistory (ChangeId TEXT PRIMARY KEY, IcdId TEXT, UserId TEXT, ChangeType TEXT, ChangeDescription TEXT, CreatedAt TEXT)";
            cmd6.ExecuteNonQuery();

            // User Settings
            using var cmd7 = c.CreateCommand();
            cmd7.CommandText = "CREATE TABLE IF NOT EXISTS UserSettings (UserId TEXT PRIMARY KEY, DarkMode INTEGER, Language TEXT, Theme TEXT)";
            cmd7.ExecuteNonQuery();

            // ICD Templates
            using var cmd8 = c.CreateCommand();
            cmd8.CommandText = "CREATE TABLE IF NOT EXISTS IcdTemplates (TemplateId TEXT PRIMARY KEY, Name TEXT, Description TEXT, StructureContent TEXT, CreatedBy TEXT, CreatedAt TEXT)";
            cmd8.ExecuteNonQuery();

            // Add CreatedAt to Icds if not exists
            try
            {
                using var cmd9 = c.CreateCommand();
                cmd9.CommandText = "ALTER TABLE Icds ADD COLUMN CreatedAt TEXT";
                cmd9.ExecuteNonQuery();
            }
            catch { } // Column might already exist

            EnsureAdminUser(c);
        }

        private string BuildConnectionString()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IcdControlSystem");

            Directory.CreateDirectory(appDataDir);

            var appDataDbPath = Path.Combine(appDataDir, "icd_system.db");

            // Best-effort migration: if an old DB exists in the process working directory, copy it once.
            // This prevents the common "different DB per run" issue when working directory changes.
            try
            {
                var legacyDbPath = Path.GetFullPath("icd_system.db");
                if (File.Exists(legacyDbPath) && !File.Exists(appDataDbPath))
                {
                    File.Copy(legacyDbPath, appDataDbPath);
                }
            }
            catch
            {
                // Ignore migration errors; we can always create a fresh DB.
            }

            return $"Data Source={appDataDbPath};Mode=ReadWriteCreate;Cache=Shared";
        }

        private void EnsureAdminUser(SqliteConnection c)
        {
            var adminHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("admin")));

            // Check if admin exists
            using var check = c.CreateCommand();
            check.CommandText = "SELECT COUNT(1) FROM Users WHERE Username = 'admin'";
            long count = (long)check.ExecuteScalar()!;

            if (count == 0)
            {
                // Create Admin
                using var ins = c.CreateCommand();
                ins.CommandText = "INSERT INTO Users (UserId, Username, Email, PasswordHash, IsAdmin) VALUES ($id, 'admin', 'admin@local', $pw, 1)";
                ins.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                ins.Parameters.AddWithValue("$pw", adminHash);
                ins.ExecuteNonQuery();
            }
            else
            {
                // FIX: Update password to ensure it matches 'admin' (handles case if DB has old hash)
                using var upd = c.CreateCommand();
                upd.CommandText = "UPDATE Users SET PasswordHash = $pw, IsAdmin = 1 WHERE Username = 'admin'";
                upd.Parameters.AddWithValue("$pw", adminHash);
                upd.ExecuteNonQuery();
            }
        }

        public User? Authenticate(string username, string pass)
        {
            username = username?.Trim() ?? "";
            pass = pass?.Trim() ?? "";
            var hashed = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(pass)));
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            // Case-insensitive username check generally handled by DB, but standardizing usually safer
            cmd.CommandText = "SELECT UserId, Username, Email, IsAdmin FROM Users WHERE Username = $u COLLATE NOCASE AND PasswordHash = $p";
            cmd.Parameters.AddWithValue("$u", username);
            cmd.Parameters.AddWithValue("$p", hashed);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                return new User
                {
                    UserId = r.GetString(0),
                    Username = r.IsDBNull(1) ? "Unknown" : r.GetString(1),
                    Email = r.IsDBNull(2) ? "" : r.GetString(2),
                    IsAdmin = !r.IsDBNull(3) && r.GetInt64(3) != 0
                };
            }
            return null;
        }

        // ... [Rest of the file follows, keeping SaveIcdRaw, CreateUser, etc.] ...

        public bool CreateUser(RegisterRequest req)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var check = c.CreateCommand();
            check.CommandText = "SELECT COUNT(1) FROM Users WHERE Username = $u OR Email = $e";
            check.Parameters.AddWithValue("$u", req.Username);
            check.Parameters.AddWithValue("$e", req.Email);
            if ((long)check.ExecuteScalar()! > 0) return false;

            var hashed = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(req.Password)));
            using var ins = c.CreateCommand();
            ins.CommandText = "INSERT INTO Users (UserId, Username, Email, PasswordHash, IsAdmin) VALUES ($id, $u, $e, $p, 0)";
            ins.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            ins.Parameters.AddWithValue("$u", req.Username);
            ins.Parameters.AddWithValue("$e", req.Email);
            ins.Parameters.AddWithValue("$p", hashed);
            ins.ExecuteNonQuery();
            return true;
        }

        public void SaveIcdRaw(string icdId, string name, double version, string structureContentJson, string? userId = null)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT OR REPLACE INTO Icds (IcdId, Name, Version, StructureContent, CreatedAt) VALUES ($id, $n, $v, $s, COALESCE((SELECT CreatedAt FROM Icds WHERE IcdId = $id), $t))";
            cmd.Parameters.AddWithValue("$id", icdId);
            cmd.Parameters.AddWithValue("$n", name ?? "ICD");
            cmd.Parameters.AddWithValue("$v", version);
            cmd.Parameters.AddWithValue("$s", structureContentJson ?? "{}");
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();

            // Save version history
            if (!string.IsNullOrEmpty(userId))
            {
                SaveIcdVersion(icdId, version, structureContentJson ?? "{}", userId);
            }
        }

        public List<Icd> GetIcdsForUser(string userId)
        {
            var list = new List<Icd>();
            using var c = new SqliteConnection(_conn);
            c.Open();

            bool isAdmin = IsUserAdmin(userId);
            using var cmd = c.CreateCommand();

            if (isAdmin)
                cmd.CommandText = "SELECT IcdId, Name, Version, StructureContent FROM Icds";
            else
            {
                cmd.CommandText = @"SELECT i.IcdId, i.Name, i.Version, i.StructureContent 
                                    FROM Icds i 
                                    JOIN UserIcdPermissions p ON p.IcdId = i.IcdId 
                                    WHERE p.UserId = $uid";
                cmd.Parameters.AddWithValue("$uid", userId);
            }

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(ParseIcd(r));
            }
            return list;
        }

        private Icd ParseIcd(SqliteDataReader r)
        {
            var icd = new Icd
            {
                IcdId = r.GetString(0),
                Name = r.GetString(1),
                Version = r.GetDouble(2)
            };
            var content = r.GetString(3);
            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    // With the fixed BaseFieldJsonConverter in Entities.cs, this will now deserialize correctly
                    var full = JsonSerializer.Deserialize<Icd>(content);
                    if (full != null)
                    {
                        icd.Messages = full.Messages ?? new List<Message>();
                        icd.Structs = full.Structs ?? new List<Struct>();
                        icd.Description = full.Description;
                    }
                }
                catch
                {
                    icd.Messages = new List<Message>();
                    icd.Structs = new List<Struct>();
                }
            }
            return icd;
        }

        public Icd? GetIcdById(string id)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT IcdId, Name, Version, StructureContent FROM Icds WHERE IcdId = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            if (r.Read()) return ParseIcd(r);
            return null;
        }

        public bool IsUserAdmin(string userId)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT IsAdmin FROM Users WHERE UserId = $u";
            cmd.Parameters.AddWithValue("$u", userId);
            var res = cmd.ExecuteScalar();
            return res != null && Convert.ToInt64(res) != 0;
        }

        public bool HasEditPermission(string userId, string icdId)
        {
            if (IsUserAdmin(userId)) return true;
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT CanEdit FROM UserIcdPermissions WHERE UserId = $u AND IcdId = $i";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$i", icdId);
            var res = cmd.ExecuteScalar();
            return res != null && Convert.ToInt64(res) != 0;
        }

        public bool HasViewPermission(string userId, string icdId)
        {
            if (IsUserAdmin(userId)) return true;
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM UserIcdPermissions WHERE UserId = $u AND IcdId = $i";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$i", icdId);
            return cmd.ExecuteScalar() != null;
        }

        public bool IcdExists(string icdId)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM Icds WHERE IcdId = $id";
            cmd.Parameters.AddWithValue("$id", icdId);
            return cmd.ExecuteScalar() != null;
        }

        public List<User> GetUsers()
        {
            var list = new List<User>();
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT UserId, Username, Email, IsAdmin FROM Users";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new User
                {
                    UserId = r.GetString(0),
                    Username = r.IsDBNull(1) ? "Unknown" : r.GetString(1),
                    Email = r.IsDBNull(2) ? "" : r.GetString(2),
                    IsAdmin = !r.IsDBNull(3) && r.GetInt64(3) != 0
                });
            }
            return list;
        }

        public User? GetUserById(string userId)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT UserId, Username, Email, IsAdmin FROM Users WHERE UserId = $u";
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new User
            {
                UserId = r.GetString(0),
                Username = r.IsDBNull(1) ? "Unknown" : r.GetString(1),
                Email = r.IsDBNull(2) ? "" : r.GetString(2),
                IsAdmin = !r.IsDBNull(3) && r.GetInt64(3) != 0
            };
        }

        public bool UpdateUserEmail(string userId, string email)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email)) return false;

            using var c = new SqliteConnection(_conn);
            c.Open();

            using var check = c.CreateCommand();
            check.CommandText = "SELECT COUNT(1) FROM Users WHERE Email = $e AND UserId <> $u";
            check.Parameters.AddWithValue("$e", email);
            check.Parameters.AddWithValue("$u", userId);
            if ((long)check.ExecuteScalar()! > 0) return false;

            using var upd = c.CreateCommand();
            upd.CommandText = "UPDATE Users SET Email = $e WHERE UserId = $u";
            upd.Parameters.AddWithValue("$e", email);
            upd.Parameters.AddWithValue("$u", userId);
            return upd.ExecuteNonQuery() > 0;
        }

        public bool UpdateUserPassword(string userId, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(newPassword)) return false;
            var hashed = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(newPassword)));

            using var c = new SqliteConnection(_conn);
            c.Open();
            using var upd = c.CreateCommand();
            upd.CommandText = "UPDATE Users SET PasswordHash = $p WHERE UserId = $u";
            upd.Parameters.AddWithValue("$p", hashed);
            upd.Parameters.AddWithValue("$u", userId);
            return upd.ExecuteNonQuery() > 0;
        }

        public List<(string IcdId, bool CanEdit)> GetUserPermissions(string userId)
        {
            var list = new List<(string, bool)>();
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT IcdId, CanEdit FROM UserIcdPermissions WHERE UserId = $u";
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((r.GetString(0), r.GetInt64(1) != 0));
            }
            return list;
        }

        public List<(string UserId, bool CanEdit)> GetIcdPermissions(string icdId)
        {
            var list = new List<(string, bool)>();
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT UserId, CanEdit FROM UserIcdPermissions WHERE IcdId = $id";
            cmd.Parameters.AddWithValue("$id", icdId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((r.GetString(0), r.GetInt64(1) != 0));
            }
            return list;
        }

        public void GrantPermission(string userId, string icdId, bool canEdit)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO UserIcdPermissions (UserId, IcdId, CanEdit) VALUES ($u, $i, $e)";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$i", icdId);
            cmd.Parameters.AddWithValue("$e", canEdit ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public void RevokePermission(string userId, string icdId)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM UserIcdPermissions WHERE UserId = $u AND IcdId = $i";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$i", icdId);
            cmd.ExecuteNonQuery();
        }

        public bool DeleteIcd(string icdId)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();

            using var tx = c.BeginTransaction();

            using var delPerms = c.CreateCommand();
            delPerms.Transaction = tx;
            delPerms.CommandText = "DELETE FROM UserIcdPermissions WHERE IcdId = $id";
            delPerms.Parameters.AddWithValue("$id", icdId);
            delPerms.ExecuteNonQuery();

            using var delVersions = c.CreateCommand();
            delVersions.Transaction = tx;
            delVersions.CommandText = "DELETE FROM IcdVersions WHERE IcdId = $id";
            delVersions.Parameters.AddWithValue("$id", icdId);
            delVersions.ExecuteNonQuery();

            using var delComments = c.CreateCommand();
            delComments.Transaction = tx;
            delComments.CommandText = "DELETE FROM IcdComments WHERE IcdId = $id";
            delComments.Parameters.AddWithValue("$id", icdId);
            delComments.ExecuteNonQuery();

            using var delHistory = c.CreateCommand();
            delHistory.Transaction = tx;
            delHistory.CommandText = "DELETE FROM IcdChangeHistory WHERE IcdId = $id";
            delHistory.Parameters.AddWithValue("$id", icdId);
            delHistory.ExecuteNonQuery();

            using var delIcd = c.CreateCommand();
            delIcd.Transaction = tx;
            delIcd.CommandText = "DELETE FROM Icds WHERE IcdId = $id";
            delIcd.Parameters.AddWithValue("$id", icdId);
            var rows = delIcd.ExecuteNonQuery();

            tx.Commit();

            return rows > 0;
        }

        // Version History
        public void SaveIcdVersion(string icdId, double versionNumber, string structureContentJson, string userId)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO IcdVersions (VersionId, IcdId, VersionNumber, StructureContent, CreatedAt, CreatedBy) VALUES ($vid, $id, $vn, $s, $t, $u)";
            cmd.Parameters.AddWithValue("$vid", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$id", icdId);
            cmd.Parameters.AddWithValue("$vn", versionNumber);
            cmd.Parameters.AddWithValue("$s", structureContentJson);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.ExecuteNonQuery();
        }

        public List<(string VersionId, double VersionNumber, string CreatedAt, string CreatedBy)> GetIcdVersions(string icdId)
        {
            var list = new List<(string, double, string, string)>();
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT VersionId, VersionNumber, CreatedAt, CreatedBy FROM IcdVersions WHERE IcdId = $id ORDER BY VersionNumber DESC";
            cmd.Parameters.AddWithValue("$id", icdId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((r.GetString(0), r.GetDouble(1), r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3)));
            }
            return list;
        }

        public Icd? GetIcdVersion(string versionId)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT StructureContent FROM IcdVersions WHERE VersionId = $vid";
            cmd.Parameters.AddWithValue("$vid", versionId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var content = r.GetString(0);
                return JsonSerializer.Deserialize<Icd>(content);
            }
            return null;
        }

        // Comments
        public void AddComment(string icdId, string userId, string commentText)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO IcdComments (CommentId, IcdId, UserId, CommentText, CreatedAt) VALUES ($cid, $id, $u, $t, $at)";
            cmd.Parameters.AddWithValue("$cid", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$id", icdId);
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$t", commentText);
            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        public List<(string CommentId, string UserId, string CommentText, string CreatedAt)> GetIcdComments(string icdId)
        {
            var list = new List<(string, string, string, string)>();
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT CommentId, UserId, CommentText, CreatedAt FROM IcdComments WHERE IcdId = $id ORDER BY CreatedAt DESC";
            cmd.Parameters.AddWithValue("$id", icdId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3)));
            }
            return list;
        }

        // Change History
        public void LogChange(string icdId, string userId, string changeType, string changeDescription)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO IcdChangeHistory (ChangeId, IcdId, UserId, ChangeType, ChangeDescription, CreatedAt) VALUES ($cid, $id, $u, $t, $d, $at)";
            cmd.Parameters.AddWithValue("$cid", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$id", icdId);
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$t", changeType);
            cmd.Parameters.AddWithValue("$d", changeDescription);
            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        public List<(string ChangeId, string UserId, string ChangeType, string ChangeDescription, string CreatedAt)> GetIcdChangeHistory(string icdId)
        {
            var list = new List<(string, string, string, string, string)>();
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT ChangeId, UserId, ChangeType, ChangeDescription, CreatedAt FROM IcdChangeHistory WHERE IcdId = $id ORDER BY CreatedAt DESC";
            cmd.Parameters.AddWithValue("$id", icdId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? "" : r.GetString(4)));
            }
            return list;
        }

        // User Settings
        public void SaveUserSettings(string userId, bool darkMode, string language, string theme)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO UserSettings (UserId, DarkMode, Language, Theme) VALUES ($u, $d, $l, $t)";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$d", darkMode ? 1 : 0);
            cmd.Parameters.AddWithValue("$l", language ?? "en");
            cmd.Parameters.AddWithValue("$t", theme ?? "default");
            cmd.ExecuteNonQuery();
        }

        public (bool DarkMode, string Language, string Theme) GetUserSettings(string userId)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT DarkMode, Language, Theme FROM UserSettings WHERE UserId = $u";
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                return (r.GetInt64(0) != 0, r.IsDBNull(1) ? "en" : r.GetString(1), r.IsDBNull(2) ? "default" : r.GetString(2));
            }
            return (false, "en", "default");
        }

        // Templates
        public void SaveTemplate(string name, string description, string structureContentJson, string userId)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO IcdTemplates (TemplateId, Name, Description, StructureContent, CreatedBy, CreatedAt) VALUES ($tid, $n, $d, $s, $u, $at)";
            cmd.Parameters.AddWithValue("$tid", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$d", description ?? "");
            cmd.Parameters.AddWithValue("$s", structureContentJson);
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        public List<(string TemplateId, string Name, string Description, string CreatedBy)> GetTemplates()
        {
            var list = new List<(string, string, string, string)>();
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT TemplateId, Name, Description, CreatedBy FROM IcdTemplates ORDER BY CreatedAt DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3)));
            }
            return list;
        }

        public Icd? GetTemplate(string templateId)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT StructureContent FROM IcdTemplates WHERE TemplateId = $tid";
            cmd.Parameters.AddWithValue("$tid", templateId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var content = r.GetString(0);
                return JsonSerializer.Deserialize<Icd>(content);
            }
            return null;
        }
    }
}