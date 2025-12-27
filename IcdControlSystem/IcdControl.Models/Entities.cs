using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IcdControl.Models
{
    // Apply converter to the base class so it handles all instances in lists/properties
    [JsonConverter(typeof(BaseFieldJsonConverter))]
    public abstract class BaseField
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
    }

    public class DataField : BaseField
    {
        public string Type { get; set; } // int, float, double, bool
        public int SizeInBits { get; set; }
    }

    public class Struct : BaseField
    {
        public bool IsUnion { get; set; }
        public List<BaseField> Fields { get; set; } = new List<BaseField>();
        public string StructType { get; set; }
    }

    public class Message : Struct
    {
        public bool IsRx { get; set; }
        public bool IsMsb { get; set; }
        public string Description { get; set; }
    }

    public class User
    {
        public string UserId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public bool IsAdmin { get; set; }
    }

    public class Icd
    {
        public string IcdId { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public double Version { get; set; }
        public string Description { get; set; }
        public List<Message> Messages { get; set; } = new List<Message>();
        public List<Struct> Structs { get; set; } = new List<Struct>();
    }

    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class RegisterRequest
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }

    // FIXED: Manual serialization to prevent recursion and ensure data persistence
    public class BaseFieldJsonConverter : JsonConverter<BaseField>
    {
        public override BaseField Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            BaseField field = null;

            // Discriminator logic: Check properties to decide type
            if (root.TryGetProperty("Type", out _))
            {
                var df = new DataField();
                df.Type = GetStr(root, "Type");
                df.SizeInBits = GetInt(root, "SizeInBits");
                field = df;
            }
            else if (root.TryGetProperty("IsRx", out _) || root.TryGetProperty("IsMsb", out _) || root.TryGetProperty("Description", out _))
            {
                var msg = new Message();
                msg.IsRx = GetBool(root, "IsRx");
                msg.IsMsb = GetBool(root, "IsMsb");
                msg.Description = GetStr(root, "Description");
                // Message also has Struct properties
                msg.IsUnion = GetBool(root, "IsUnion");
                msg.StructType = GetStr(root, "StructType");
                PopulateFields(root, msg, options);
                field = msg;
            }
            else
            {
                var s = new Struct();
                s.IsUnion = GetBool(root, "IsUnion");
                s.StructType = GetStr(root, "StructType");
                PopulateFields(root, s, options);
                field = s;
            }

            // Common properties
            field.Id = GetStr(root, "Id") ?? Guid.NewGuid().ToString();
            field.Name = GetStr(root, "Name");

            return field;
        }

        private void PopulateFields(JsonElement root, Struct s, JsonSerializerOptions options)
        {
            if (root.TryGetProperty("Fields", out var fieldsArr) && fieldsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in fieldsArr.EnumerateArray())
                {
                    // This recursion is safe because it calls Read() for the child element
                    var child = JsonSerializer.Deserialize<BaseField>(el.GetRawText(), options);
                    if (child != null) s.Fields.Add(child);
                }
            }
        }

        public override void Write(Utf8JsonWriter writer, BaseField value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // Common
            writer.WriteString("Id", value.Id);
            writer.WriteString("Name", value.Name);

            if (value is DataField df)
            {
                writer.WriteString("Type", df.Type);
                writer.WriteNumber("SizeInBits", df.SizeInBits);
            }
            else if (value is Struct s)
            {
                // Message Specifics
                if (s is Message msg)
                {
                    writer.WriteBoolean("IsRx", msg.IsRx);
                    writer.WriteBoolean("IsMsb", msg.IsMsb);
                    writer.WriteString("Description", msg.Description);
                }

                // Struct properties
                writer.WriteBoolean("IsUnion", s.IsUnion);
                writer.WriteString("StructType", s.StructType);

                writer.WritePropertyName("Fields");
                writer.WriteStartArray();
                foreach (var field in s.Fields)
                {
                    // Recursively serialize children
                    JsonSerializer.Serialize(writer, field, options);
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        // Helper methods
        private string GetStr(JsonElement el, string prop) => el.TryGetProperty(prop, out var v) ? v.ToString() : null;
        private int GetInt(JsonElement el, string prop) => el.TryGetProperty(prop, out var v) && v.TryGetInt32(out int i) ? i : 0;
        private bool GetBool(JsonElement el, string prop) => el.TryGetProperty(prop, out var v) && v.GetBoolean();
    }
}