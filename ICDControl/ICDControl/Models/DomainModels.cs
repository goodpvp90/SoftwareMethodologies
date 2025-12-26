using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace ICDControl.Models
{
    // מחלקת בסיס לשדות ומבנים - תומכת בירושה ב-JSON
    [JsonDerivedType(typeof(DataField), typeDiscriminator: "field")]
    [JsonDerivedType(typeof(Struct), typeDiscriminator: "struct")]
    [JsonDerivedType(typeof(Message), typeDiscriminator: "message")]
    public abstract class BaseField
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }

        // מתודה ליצירת קוד C (Header)
        public abstract string GenerateHeaderString(int indentLevel);
    }

    // שדה פרימיטיבי (int, double, etc.)
    public class DataField : BaseField
    {
        public string Type { get; set; } // int, float, bool
        public int SizeInBits { get; set; }

        public override string GenerateHeaderString(int indentLevel)
        {
            string indent = new string(' ', indentLevel * 4);
            return $"{indent}{Type} {Name}; // {SizeInBits} bits\n";
        }
    }

    // מבנה לוגי שמכיל שדות אחרים
    public class Struct : BaseField
    {
        public bool IsUnion { get; set; } = false;
        public List<BaseField> Fields { get; set; } = new List<BaseField>();

        public void AddField(BaseField field) => Fields.Add(field);

        public override string GenerateHeaderString(int indentLevel)
        {
            string indent = new string(' ', indentLevel * 4);
            string typeDef = IsUnion ? "union" : "struct";
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"{indent}typedef {typeDef} {{");
            foreach (var field in Fields)
            {
                sb.Append(field.GenerateHeaderString(indentLevel + 1));
            }
            sb.AppendLine($"{indent}}} {Name};");

            return sb.ToString();
        }
    }

    // הודעת תקשורת - יורשת מ-Struct
    public class Message : Struct
    {
        public bool IsRx { get; set; }
        public bool IsMsb { get; set; }
        public string Description { get; set; }
    }

    // משתמש במערכת
    public class User
    {
        public string UserId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public bool IsAdmin { get; set; }
    }

    // פרויקט ICD שלם
    public class Icd
    {
        public string IcdId { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public double Version { get; set; }
        public string Description { get; set; }
        public DateTime LastUpdated { get; set; }

        // רשימת ההודעות שנשמרת כ-JSON בתוך ה-DB
        public List<Message> Messages { get; set; } = new List<Message>();
    }
}