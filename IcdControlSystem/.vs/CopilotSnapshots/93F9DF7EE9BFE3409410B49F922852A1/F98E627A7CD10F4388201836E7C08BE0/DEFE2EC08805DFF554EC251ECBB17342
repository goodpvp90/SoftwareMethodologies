using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IcdControl.Models
{
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
    }

    // אובייקט עזר להתחברות
    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    // Registration request model
    public class RegisterRequest
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }
}