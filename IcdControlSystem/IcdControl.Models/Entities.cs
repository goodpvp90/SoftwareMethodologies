using System.Text.Json;
using System.Text.Json.Serialization;

namespace IcdControl.Models
{
    // 1. Apply converter to the base class so it handles all instances in lists/properties automatically
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

    // FIXED: JsonConverter with Type Discriminator ($type)
    public class BaseFieldJsonConverter : JsonConverter<BaseField>
    {
        public override BaseField Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            // Use the explicit discriminator to determine the type
            var type = GetStr(root, "$type");

            BaseField field;

            switch (type)
            {
                case nameof(DataField):
                    var df = new DataField();
                    df.Type = GetStr(root, "Type");
                    df.SizeInBits = GetInt(root, "SizeInBits");
                    field = df;
                    break;

                case nameof(Message):
                    var msg = new Message();
                    msg.IsRx = GetBool(root, "IsRx");
                    msg.IsMsb = GetBool(root, "IsMsb");
                    msg.Description = GetStr(root, "Description");
                    // Message inherits from Struct, so populate struct fields too
                    msg.IsUnion = GetBool(root, "IsUnion");
                    msg.StructType = GetStr(root, "StructType");
                    PopulateFields(root, msg, options);
                    field = msg;
                    break;

                case nameof(Struct):
                default:
                    // Fallback to Struct if unknown type or missing discriminator (backward compatibility)
                    var s = new Struct();
                    s.IsUnion = GetBool(root, "IsUnion");
                    s.StructType = GetStr(root, "StructType");
                    PopulateFields(root, s, options);
                    field = s;
                    break;
            }

            // Common properties
            field.Id = GetStr(root, "Id") ?? Guid.NewGuid().ToString();
            field.Name = GetStr(root, "Name");

            return field;
        }

        private void PopulateFields(JsonElement root, Struct s, JsonSerializerOptions options)
        {
            if (TryGetPropertyIgnoreCase(root, "Fields", out var fieldsArr) && fieldsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in fieldsArr.EnumerateArray())
                {
                    var child = JsonSerializer.Deserialize<BaseField>(el.GetRawText(), options);
                    if (child != null) s.Fields.Add(child);
                }
            }
        }

        public override void Write(Utf8JsonWriter writer, BaseField value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // Write the discriminator
            writer.WriteString("$type", value.GetType().Name);

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
                    JsonSerializer.Serialize(writer, field, options);
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        private bool TryGetPropertyIgnoreCase(JsonElement el, string prop, out JsonElement value)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in el.EnumerateObject())
                {
                    if (string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase))
                    {
                        value = p.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        // Helper methods (case-insensitive to tolerate camelCase vs PascalCase and "$type" casing)
        private string GetStr(JsonElement el, string prop)
        {
            if (!TryGetPropertyIgnoreCase(el, prop, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Null) return null;
            return v.ToString();
        }

        private int GetInt(JsonElement el, string prop)
        {
            if (!TryGetPropertyIgnoreCase(el, prop, out var v)) return 0;
            return v.ValueKind != JsonValueKind.Null && v.TryGetInt32(out int i) ? i : 0;
        }

        private bool GetBool(JsonElement el, string prop)
        {
            if (!TryGetPropertyIgnoreCase(el, prop, out var v)) return false;
            return v.ValueKind != JsonValueKind.Null && v.ValueKind == JsonValueKind.True;
        }
    }
}