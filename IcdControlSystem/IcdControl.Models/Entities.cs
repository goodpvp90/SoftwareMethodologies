using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IcdControl.Models
{
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
        // optional: when this struct is an instance linking to a named struct definition
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

    // אובייקט עזר להתחברות
    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    // Registration request model
    public class RegisterRequest
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }

    // Custom converter to deserialize BaseField polymorphically
    public class BaseFieldJsonConverter : JsonConverter<BaseField>
    {
        public override BaseField Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            // Determine type by properties
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            // If has 'Type' property it's a DataField
            if (root.TryGetProperty("Type", out _))
            {
                return JsonSerializer.Deserialize<DataField>(root.GetRawText(), options);
            }

            // If has 'IsRx' or 'IsMsb' or 'Description' it's a Message
            if (root.TryGetProperty("IsRx", out _) || root.TryGetProperty("IsMsb", out _) || root.TryGetProperty("Description", out _))
            {
                return JsonSerializer.Deserialize<Message>(root.GetRawText(), options);
            }

            // Otherwise treat as Struct
            return JsonSerializer.Deserialize<Struct>(root.GetRawText(), options);
        }

        public override void Write(Utf8JsonWriter writer, BaseField value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            var type = value.GetType();
            JsonSerializer.Serialize(writer, value, type, options);
        }
    }
}