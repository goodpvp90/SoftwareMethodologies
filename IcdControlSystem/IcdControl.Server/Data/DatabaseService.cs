using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using IcdControl.Models;

namespace IcdControl.Server.Data
{
    public class DatabaseService
    {
        private string _conn = "Data Source=icd_system.db;Mode=ReadWriteCreate;Cache=Shared";

        public DatabaseService()
        {
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

            EnsureAdminUser(c);
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
            var hashed = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(pass)));
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            // Case-insensitive username check generally handled by DB, but standardizing usually safer
            cmd.CommandText = "SELECT UserId, Username, Email, IsAdmin FROM Users WHERE Username = $u AND PasswordHash = $p";
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
            check.CommandText = "SELECT COUNT(1) FROM Users WHERE Username = $u";
            check.Parameters.AddWithValue("$u", req.Username);
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

        public void SaveIcdRaw(string icdId, string name, double version, string structureContentJson)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT OR REPLACE INTO Icds (IcdId, Name, Version, StructureContent) VALUES ($id, $n, $v, $s)";
            cmd.Parameters.AddWithValue("$id", icdId);
            cmd.Parameters.AddWithValue("$n", name ?? "ICD");
            cmd.Parameters.AddWithValue("$v", version);
            cmd.Parameters.AddWithValue("$s", structureContentJson ?? "{}");
            cmd.ExecuteNonQuery();
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
    }
}