using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICDControl.Models;

namespace ICDControl.Data
{
    public class DatabaseService
    {
        // יצירת קובץ DB בתיקיית ההרצה של הפרויקט
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseService()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _dbPath = Path.Combine(baseDir, "icd_control.db");
            _connectionString = $"Data Source={_dbPath};Version=3;";

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            if (!File.Exists(_dbPath))
            {
                SQLiteConnection.CreateFile(_dbPath);
            }

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // טבלת משתמשים
                string createUsers = @"
                    CREATE TABLE IF NOT EXISTS Users (
                        UserId TEXT PRIMARY KEY,
                        Username TEXT,
                        Email TEXT UNIQUE,
                        PasswordHash TEXT,
                        IsAdmin INTEGER
                    );";

                // טבלת ICD - שים לב לעמודת StructureContent שמחזיקה JSON
                string createIcds = @"
                    CREATE TABLE IF NOT EXISTS Icds (
                        IcdId TEXT PRIMARY KEY,
                        Name TEXT,
                        Version REAL,
                        Description TEXT,
                        LastUpdated TEXT,
                        StructureContent TEXT 
                    );";

                using (var cmd = new SQLiteCommand(createUsers, connection)) cmd.ExecuteNonQuery();
                using (var cmd = new SQLiteCommand(createIcds, connection)) cmd.ExecuteNonQuery();

                CreateDefaultAdmin(connection);
            }
        }

        // יצירת משתמש אדמין ראשוני לבדיקות
        private void CreateDefaultAdmin(SQLiteConnection conn)
        {
            using (var cmd = new SQLiteCommand("SELECT Count(*) FROM Users", conn))
            {
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                if (count == 0)
                {
                    var admin = new User
                    {
                        UserId = Guid.NewGuid().ToString(),
                        Username = "Admin",
                        Email = "admin@test.com",
                        IsAdmin = true,
                        PasswordHash = HashPassword("1234")
                    };
                    InsertUser(conn, admin);
                }
            }
        }

        private void InsertUser(SQLiteConnection conn, User user)
        {
            string sql = "INSERT INTO Users (UserId, Username, Email, PasswordHash, IsAdmin) VALUES (@Id, @User, @Email, @Hash, @Admin)";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", user.UserId);
                cmd.Parameters.AddWithValue("@User", user.Username);
                cmd.Parameters.AddWithValue("@Email", user.Email);
                cmd.Parameters.AddWithValue("@Hash", user.PasswordHash);
                cmd.Parameters.AddWithValue("@Admin", user.IsAdmin ? 1 : 0);
                cmd.ExecuteNonQuery();
            }
        }

        // --- פונקציות למשתמשים ---

        public User Login(string email, string password)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT * FROM Users WHERE Email = @Email", conn))
                {
                    cmd.Parameters.AddWithValue("@Email", email);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string storedHash = reader["PasswordHash"].ToString();
                            if (storedHash == HashPassword(password))
                            {
                                return new User
                                {
                                    UserId = reader["UserId"].ToString(),
                                    Username = reader["Username"].ToString(),
                                    Email = reader["Email"].ToString(),
                                    IsAdmin = Convert.ToBoolean(reader["IsAdmin"])
                                };
                            }
                        }
                    }
                }
            }
            return null;
        }

        public void Register(string username, string email, string password)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                var user = new User
                {
                    UserId = Guid.NewGuid().ToString(),
                    Username = username,
                    Email = email,
                    PasswordHash = HashPassword(password),
                    IsAdmin = false
                };
                InsertUser(conn, user);
            }
        }

        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(bytes);
            }
        }

        // --- פונקציות ICD ---

        public void SaveIcd(Icd icd)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                // המרת עץ האובייקטים ל-JSON לצורך שמירה במסד
                string json = JsonSerializer.Serialize(icd.Messages, new JsonSerializerOptions { WriteIndented = true });

                string sql = @"
                    INSERT INTO Icds (IcdId, Name, Version, Description, LastUpdated, StructureContent) 
                    VALUES (@Id, @Name, @Ver, @Desc, @Date, @Json)
                    ON CONFLICT(IcdId) DO UPDATE SET 
                        Name=@Name, Version=@Ver, Description=@Desc, LastUpdated=@Date, StructureContent=@Json";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", icd.IcdId);
                    cmd.Parameters.AddWithValue("@Name", icd.Name);
                    cmd.Parameters.AddWithValue("@Ver", icd.Version);
                    cmd.Parameters.AddWithValue("@Desc", icd.Description);
                    cmd.Parameters.AddWithValue("@Date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@Json", json);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<Icd> GetAllIcds()
        {
            var list = new List<Icd>();
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT * FROM Icds", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var icd = new Icd
                        {
                            IcdId = reader["IcdId"].ToString(),
                            Name = reader["Name"].ToString(),
                            Version = Convert.ToDouble(reader["Version"]),
                            Description = reader["Description"].ToString(),
                            LastUpdated = DateTime.Parse(reader["LastUpdated"].ToString())
                        };

                        // טעינת ה-JSON והמרתו חזרה לאובייקטים
                        string json = reader["StructureContent"].ToString();
                        if (!string.IsNullOrEmpty(json))
                        {
                            try
                            {
                                icd.Messages = JsonSerializer.Deserialize<List<Message>>(json);
                            }
                            catch { /* התעלמות אם ה-JSON ריק או פגום */ }
                        }
                        list.Add(icd);
                    }
                }
            }
            return list;
        }
    }
}