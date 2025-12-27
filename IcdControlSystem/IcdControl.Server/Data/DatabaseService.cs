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
            // create tables if not exists
            using var cmd1 = c.CreateCommand();
            cmd1.CommandText = "CREATE TABLE IF NOT EXISTS Users (UserId TEXT PRIMARY KEY, Username TEXT, Email TEXT, PasswordHash TEXT, IsAdmin INTEGER)";
            cmd1.ExecuteNonQuery();

            // ensure Username column exists if older DB
            try
            {
                using var alter = c.CreateCommand();
                alter.CommandText = "ALTER TABLE Users ADD COLUMN Username TEXT";
                alter.ExecuteNonQuery();
            }
            catch { /* ignore if column exists or unable to alter */ }

            using var cmd2 = c.CreateCommand();
            cmd2.CommandText = "CREATE TABLE IF NOT EXISTS Icds (IcdId TEXT PRIMARY KEY, Name TEXT, Version REAL, StructureContent TEXT)";
            cmd2.ExecuteNonQuery();

            using var cmd3 = c.CreateCommand();
            cmd3.CommandText = "CREATE TABLE IF NOT EXISTS UserIcdPermissions (UserId TEXT, IcdId TEXT, CanEdit INTEGER, PRIMARY KEY(UserId, IcdId))";
            cmd3.ExecuteNonQuery();

            using var cmd4 = c.CreateCommand();
            cmd4.CommandText = "CREATE TABLE IF NOT EXISTS UserStructPermissions (UserId TEXT, IcdId TEXT, StructName TEXT, CanView INTEGER, CanEdit INTEGER, PRIMARY KEY(UserId,IcdId,StructName))";
            cmd4.ExecuteNonQuery();

            // ensure admin user exists
            using var checkAdmin = c.CreateCommand();
            checkAdmin.CommandText = "SELECT COUNT(1) FROM Users WHERE Username = 'admin'";
            var adminExists = (long)checkAdmin.ExecuteScalar()! >0;
            if (!adminExists)
            {
                var pwd = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("admin")));
                using var insert = c.CreateCommand();
                insert.CommandText = "INSERT INTO Users (UserId, Username, Email, PasswordHash, IsAdmin) VALUES ($id, $username, $email, $passwordHash, $isAdmin)";
                insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                insert.Parameters.AddWithValue("$username", "admin");
                insert.Parameters.AddWithValue("$email", "admin@local");
                insert.Parameters.AddWithValue("$passwordHash", pwd);
                insert.Parameters.AddWithValue("$isAdmin",1);
                insert.ExecuteNonQuery();
            }
        }

        // Authenticate by username
        public User? Authenticate(string username, string pass)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(pass))
                return null;

            // Hash incoming password to compare with stored hash
            var hashed = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(pass)));

            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT UserId, Username, Email, PasswordHash, IsAdmin FROM Users WHERE Username = $u AND PasswordHash = $p";
            cmd.Parameters.AddWithValue("$u", username);
            cmd.Parameters.AddWithValue("$p", hashed);

            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var user = new User
                {
                    UserId = r.GetString(0),
                    Username = r.IsDBNull(1) ? null : r.GetString(1),
                    Email = r.IsDBNull(2) ? null : r.GetString(2),
                    PasswordHash = r.IsDBNull(3) ? null : r.GetString(3),
                    IsAdmin = !r.IsDBNull(4) && r.GetInt64(4) !=0
                };
                return user;
            }
            return null;
        }

        // Save using strongly-typed Icd (keeps for compatibility)
        public void SaveIcd(Icd icd)
        {
            // delegate to SaveIcdRaw using default serialization of messages/structs
            var wrapper = new
            {
                Messages = icd.Messages,
                Structs = icd.Structs
            };
            var json = JsonSerializer.Serialize(wrapper);
            SaveIcdRaw(icd.IcdId, icd.Name, icd.Version, json);
        }

        // Save raw JSON payload for structure content to avoid polymorphism issues
        public void SaveIcdRaw(string icdId, string name, double version, string structureContentJson)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT OR REPLACE INTO Icds (IcdId, Name, Version, StructureContent) VALUES ($id, $n, $v, $s)";
            cmd.Parameters.AddWithValue("$id", icdId ?? Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$n", name ?? string.Empty);
            cmd.Parameters.AddWithValue("$v", version);
            cmd.Parameters.AddWithValue("$s", structureContentJson ?? string.Empty);
            cmd.ExecuteNonQuery();
        }

        public bool CreateUser(RegisterRequest req)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();

            // Check if email or username already exists
            using var checkCmd = c.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(1) FROM Users WHERE Email = $email OR Username = $username";
            checkCmd.Parameters.AddWithValue("$email", req.Email);
            checkCmd.Parameters.AddWithValue("$username", req.Username);
            var exists = (long)checkCmd.ExecuteScalar()! >0;

            if (exists)
            {
                return false; // Email or username already exists
            }

            // Hash the password (in production, use a proper hashing library)
            var passwordHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(req.Password)));

            // Insert the new user
            using var insertCmd = c.CreateCommand();
            insertCmd.CommandText = "INSERT INTO Users (UserId, Username, Email, PasswordHash, IsAdmin) VALUES ($id, $username, $email, $passwordHash, $isAdmin)";
            insertCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insertCmd.Parameters.AddWithValue("$username", req.Username);
            insertCmd.Parameters.AddWithValue("$email", req.Email);
            insertCmd.Parameters.AddWithValue("$passwordHash", passwordHash);
            insertCmd.Parameters.AddWithValue("$isAdmin",0);
            insertCmd.ExecuteNonQuery();

            return true;
        }

        // New: get all ICDs
        public List<Icd> GetAllIcds()
        {
            var list = new List<Icd>();
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT IcdId, Name, Version, StructureContent FROM Icds";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var icd = new Icd
                {
                    IcdId = r.IsDBNull(0) ? Guid.NewGuid().ToString() : r.GetString(0),
                    Name = r.IsDBNull(1) ? string.Empty : r.GetString(1),
                    Version = r.IsDBNull(2) ?0 : r.GetDouble(2),
                };
                var content = r.IsDBNull(3) ? string.Empty : r.GetString(3);
                try
                {
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        icd.Messages = new List<Message>();
                        icd.Structs = new List<Struct>();
                    }
                    else
                    {
                        var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("Messages", out var msgs))
                        {
                            icd.Messages = JsonSerializer.Deserialize<List<Message>>(msgs.GetRawText()) ?? new List<Message>();
                        }
                        if (doc.RootElement.TryGetProperty("Structs", out var structs))
                        {
                            icd.Structs = JsonSerializer.Deserialize<List<Struct>>(structs.GetRawText()) ?? new List<Struct>();
                        }
                    }
                }
                catch
                {
                    icd.Messages = new List<Message>();
                    icd.Structs = new List<Struct>();
                }
                list.Add(icd);
            }
            return list;
        }

        // New: get ICDs for specific user (admin -> all)
        public List<Icd> GetIcdsForUser(string userId)
        {
            var list = new List<Icd>();
            using var c = new SqliteConnection(_conn);
            c.Open();

            // check if admin
            bool isAdmin = false;
            using (var uc = c.CreateCommand())
            {
                uc.CommandText = "SELECT IsAdmin FROM Users WHERE UserId = $uid";
                uc.Parameters.AddWithValue("$uid", userId);
                var res = uc.ExecuteScalar();
                if (res != null && res != DBNull.Value)
                {
                    isAdmin = Convert.ToInt64(res) !=0;
                }
            }

            if (isAdmin)
            {
                return GetAllIcds();
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT i.IcdId, i.Name, i.Version, i.StructureContent
                                FROM Icds i
                                JOIN UserIcdPermissions p ON p.IcdId = i.IcdId
                                WHERE p.UserId = $uid";
            cmd.Parameters.AddWithValue("$uid", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var icd = new Icd
                {
                    IcdId = r.IsDBNull(0) ? Guid.NewGuid().ToString() : r.GetString(0),
                    Name = r.IsDBNull(1) ? string.Empty : r.GetString(1),
                    Version = r.IsDBNull(2) ?0 : r.GetDouble(2),
                };
                var content = r.IsDBNull(3) ? string.Empty : r.GetString(3);
                try
                {
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        icd.Messages = new List<Message>();
                        icd.Structs = new List<Struct>();
                    }
                    else
                    {
                        var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("Messages", out var msgs))
                        {
                            icd.Messages = JsonSerializer.Deserialize<List<Message>>(msgs.GetRawText()) ?? new List<Message>();
                        }
                        if (doc.RootElement.TryGetProperty("Structs", out var structs))
                        {
                            icd.Structs = JsonSerializer.Deserialize<List<Struct>>(structs.GetRawText()) ?? new List<Struct>();
                        }
                    }
                }
                catch
                {
                    icd.Messages = new List<Message>();
                    icd.Structs = new List<Struct>();
                }
                list.Add(icd);
            }
            return list;
        }

        // New: get single ICD by id
        public Icd? GetIcdById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT IcdId, Name, Version, StructureContent FROM Icds WHERE IcdId = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var icd = new Icd
                {
                    IcdId = r.IsDBNull(0) ? Guid.NewGuid().ToString() : r.GetString(0),
                    Name = r.IsDBNull(1) ? string.Empty : r.GetString(1),
                    Version = r.IsDBNull(2) ?0 : r.GetDouble(2),
                };
                var content = r.IsDBNull(3) ? string.Empty : r.GetString(3);
                try
                {
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        icd.Messages = new List<Message>();
                        icd.Structs = new List<Struct>();
                    }
                    else
                    {
                        var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("Messages", out var msgs))
                        {
                            icd.Messages = JsonSerializer.Deserialize<List<Message>>(msgs.GetRawText()) ?? new List<Message>();
                        }
                        if (doc.RootElement.TryGetProperty("Structs", out var structs))
                        {
                            icd.Structs = JsonSerializer.Deserialize<List<Struct>>(structs.GetRawText()) ?? new List<Struct>();
                        }
                    }
                }
                catch
                {
                    icd.Messages = new List<Message>();
                    icd.Structs = new List<Struct>();
                }
                return icd;
            }
            return null;
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
                    Username = r.IsDBNull(1) ? null : r.GetString(1),
                    Email = r.IsDBNull(2) ? null : r.GetString(2),
                    IsAdmin = !r.IsDBNull(3) && r.GetInt64(3) !=0
                });
            }
            return list;
        }

        public List<(string StructName, bool CanView, bool CanEdit)> GetStructPermissionsForUser(string userId, string icdId)
        {
            var list = new List<(string, bool, bool)>();
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT StructName, CanView, CanEdit FROM UserStructPermissions WHERE UserId = $u AND IcdId = $i";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$i", icdId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((r.IsDBNull(0) ? string.Empty : r.GetString(0), !r.IsDBNull(1) && r.GetInt64(1) !=0, !r.IsDBNull(2) && r.GetInt64(2) !=0));
            }
            return list;
        }

        public void GrantStructPermission(string userId, string icdId, string structName, bool canView, bool canEdit)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO UserStructPermissions (UserId, IcdId, StructName, CanView, CanEdit) VALUES ($u, $i, $s, $v, $e)";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$i", icdId);
            cmd.Parameters.AddWithValue("$s", structName ?? string.Empty);
            cmd.Parameters.AddWithValue("$v", canView ?1 :0);
            cmd.Parameters.AddWithValue("$e", canEdit ?1 :0);
            cmd.ExecuteNonQuery();
        }

        public User? GetUserByUsername(string username)
        {
            if (string.IsNullOrEmpty(username)) return null;
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT UserId, Username, Email, PasswordHash, IsAdmin FROM Users WHERE Username = $u";
            cmd.Parameters.AddWithValue("$u", username);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                return new User
                {
                    UserId = r.GetString(0),
                    Username = r.IsDBNull(1) ? null : r.GetString(1),
                    Email = r.IsDBNull(2) ? null : r.GetString(2),
                    PasswordHash = r.IsDBNull(3) ? null : r.GetString(3),
                    IsAdmin = !r.IsDBNull(4) && r.GetInt64(4) !=0
                };
            }
            return null;
        }

        public bool IsUserAdmin(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT IsAdmin FROM Users WHERE UserId = $uid";
            cmd.Parameters.AddWithValue("$uid", userId);
            var res = cmd.ExecuteScalar();
            return res != null && res != DBNull.Value && Convert.ToInt64(res) !=0;
        }

        public bool HasEditPermission(string userId, string icdId)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(icdId)) return false;
            using var c = new SqliteConnection(_conn);
            c.Open();

            // admin check
            using (var uc = c.CreateCommand())
            {
                uc.CommandText = "SELECT IsAdmin FROM Users WHERE UserId = $uid";
                uc.Parameters.AddWithValue("$uid", userId);
                var res = uc.ExecuteScalar();
                if (res != null && res != DBNull.Value && Convert.ToInt64(res) !=0)
                    return true;
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT CanEdit FROM UserIcdPermissions WHERE UserId = $uid AND IcdId = $id";
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$id", icdId);
            var r = cmd.ExecuteScalar();
            if (r == null || r == DBNull.Value) return false;
            return Convert.ToInt64(r) !=0;
        }

        public bool HasViewPermission(string userId, string icdId)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(icdId)) return false;
            using var c = new SqliteConnection(_conn);
            c.Open();

            // admin check
            using (var uc = c.CreateCommand())
            {
                uc.CommandText = "SELECT IsAdmin FROM Users WHERE UserId = $uid";
                uc.Parameters.AddWithValue("$uid", userId);
                var res = uc.ExecuteScalar();
                if (res != null && res != DBNull.Value && Convert.ToInt64(res) !=0)
                    return true;
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM UserIcdPermissions WHERE UserId = $uid AND IcdId = $id";
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$id", icdId);
            var r = cmd.ExecuteScalar();
            return r != null && r != DBNull.Value;
        }

        public void GrantPermission(string userId, string icdId, bool canEdit)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO UserIcdPermissions (UserId, IcdId, CanEdit) VALUES ($u, $i, $e)";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$i", icdId);
            cmd.Parameters.AddWithValue("$e", canEdit ?1 :0);
            cmd.ExecuteNonQuery();
        }

        public bool IcdExists(string icdId)
        {
            if (string.IsNullOrEmpty(icdId)) return false;
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM Icds WHERE IcdId = $id";
            cmd.Parameters.AddWithValue("$id", icdId);
            var r = cmd.ExecuteScalar();
            return r != null && r != DBNull.Value;
        }

        // Return top-level struct names for an ICD
        public List<string> GetStructNames(string icdId)
        {
            var names = new List<string>();
            var icd = GetIcdById(icdId);
            if (icd == null) return names;
            foreach (var s in icd.Structs)
            {
                if (!string.IsNullOrEmpty(s.Name)) names.Add(s.Name);
            }
            return names;
        }
    }
}