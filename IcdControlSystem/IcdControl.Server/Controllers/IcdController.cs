using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using IcdControl.Models;
using IcdControl.Server.Data;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;

namespace IcdControl.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IcdController : ControllerBase
    {
        private readonly DatabaseService _db;
        private readonly ILogger<IcdController> _logger;

        // DatabaseService and ILogger are injected by DI
        public IcdController(DatabaseService db, ILogger<IcdController> logger)
        {
            _db = db;
            _logger = logger;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.Username) || string.IsNullOrEmpty(req.Password))
                return BadRequest("Username and password are required");

            var user = _db.Authenticate(req.Username, req.Password);
            return user != null ? Ok(user) : Unauthorized();
        }

        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.Username) || string.IsNullOrEmpty(req.Email) || string.IsNullOrEmpty(req.Password))
                return BadRequest("Username, email and password are required");
            var created = _db.CreateUser(req);
            return created ? Ok() : Conflict("Email or username already exists or invalid data");
        }

        // Save or update an ICD. The caller must include X-UserId header with their user id.
        [HttpPost("save")]
        public async Task<IActionResult> Save()
        {
            // read raw body
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(body)) return BadRequest("Empty body");

            // Parse minimal fields without polymorphic deserialization
            string icdId = null;
            string name = null;
            double version = 0;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                // case-insensitive search for properties
                foreach (var prop in root.EnumerateObject())
                {
                    var propName = prop.Name;
                    if (string.Equals(propName, "IcdId", System.StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        icdId = prop.Value.GetString();
                    }
                    else if (string.Equals(propName, "Name", System.StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        name = prop.Value.GetString();
                    }
                    else if (string.Equals(propName, "Version", System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.TryGetDouble(out var dv)) version = dv;
                    }
                }
            }
            catch (JsonException jex)
            {
                _logger.LogWarning(jex, "Failed to parse ICD minimal fields");
                return BadRequest($"Invalid JSON: {jex.Message}");
            }

            // If no id in payload, assign new
            if (string.IsNullOrEmpty(icdId)) icdId = System.Guid.NewGuid().ToString();

            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            var exists = _db.IcdExists(icdId);
            if (!exists)
            {
                // creating new ICD: allow any authenticated user to create, then grant them edit permission
                _db.SaveIcdRaw(icdId, name ?? string.Empty, version, body);
                _db.GrantPermission(userId, icdId, true);
                return Ok(new { IcdId = icdId });
            }
            else
            {
                // updating existing ICD: require edit permission
                if (!_db.HasEditPermission(userId, icdId))
                    return Forbid();
                _db.SaveIcdRaw(icdId, name ?? string.Empty, version, body);
                return Ok(new { IcdId = icdId });
            }
        }

        [HttpGet("list")]
        public IActionResult List()
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            var items = _db.GetIcdsForUser(userId);
            return Ok(items);
        }

        [HttpGet("{id}")]
        public IActionResult Get(string id)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasViewPermission(userId, id))
                return Forbid();

            var icd = _db.GetIcdById(id);
            if (icd == null) return NotFound();
            return Ok(icd);
        }

        [HttpGet("{id}/export")]
        public IActionResult Export(string id)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasViewPermission(userId, id))
                return Forbid();

            var icd = _db.GetIcdById(id);
            if (icd == null) return NotFound();

            var header = GenerateHeader(icd);
            return Content(header, "text/plain", Encoding.UTF8);
        }

        // Admin endpoints
        [HttpGet("admin/users")]
        public IActionResult GetUsers()
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();
            if (!_db.IsUserAdmin(userId)) return Forbid();

            var users = _db.GetUsers();
            return Ok(users);
        }

        [HttpGet("admin/{icdId}/structs")]
        public IActionResult GetStructs(string icdId)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();
            if (!_db.IsUserAdmin(userId)) return Forbid();

            var names = _db.GetStructNames(icdId);
            return Ok(names);
        }

        [HttpGet("admin/{icdId}/user/{userId}/struct-perms")]
        public IActionResult GetStructPerms(string icdId, string userId)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var adminId = uid.ToString();
            if (!_db.IsUserAdmin(adminId)) return Forbid();

            var perms = _db.GetStructPermissionsForUser(userId, icdId);
            return Ok(perms);
        }

        [HttpPost("admin/{icdId}/user/{userId}/struct-perms")]
        public IActionResult SetStructPerms(string icdId, string userId, [FromBody] List<SetStructPermRequest> req)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var adminId = uid.ToString();
            if (!_db.IsUserAdmin(adminId)) return Forbid();

            if (req == null) return BadRequest();
            foreach (var item in req)
            {
                _db.GrantStructPermission(userId, icdId, item.StructName, item.CanView, item.CanEdit);
            }
            return Ok();
        }

        public class SetStructPermRequest { public string StructName { get; set; } public bool CanView { get; set; } public bool CanEdit { get; set; } }

        // Very simple .h generator. Produces typedef structs for messages and nested structs.
        private string GenerateHeader(Icd icd)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"/* Generated header for ICD: {icd.Name} (v{icd.Version}) */");
            sb.AppendLine("#pragma once");
            sb.AppendLine("#include <stdint.h>");
            sb.AppendLine();

            // Generate structs for each message
            int idx = 0;
            foreach (var msg in icd.Messages ?? new List<Message>())
            {
                var typeName = MakeSafeName(msg.Name) ?? $"message_{idx}";
                sb.AppendLine($"// Message: {msg.Name}");
                GenerateStruct(sb, msg, typeName);
                sb.AppendLine();
                idx++;
            }

            return sb.ToString();
        }

        private void GenerateStruct(StringBuilder sb, Struct s, string typeName)
        {
            sb.AppendLine($"typedef struct {typeName}_t {{");
            foreach (var f in s.Fields ?? new List<BaseField>())
            {
                if (f is DataField df)
                {
                    var ctype = MapType(df.Type, df.SizeInBits);
                    var fname = MakeSafeName(df.Name) ?? "field";
                    sb.AppendLine($" {ctype} {fname};");
                }
                else if (f is Struct sub)
                {
                    var subName = MakeSafeName(sub.Name) ?? "substruct";
                    // generate nested struct type
                    GenerateStruct(sb, sub, subName);
                    sb.AppendLine($" {subName}_t {MakeSafeName(sub.Name)};");
                }
                else
                {
                    var fname = MakeSafeName(f.Name) ?? "field";
                    sb.AppendLine($" uint8_t {fname};");
                }
            }
            sb.AppendLine($"}} {typeName}_t;");
        }

        private string MapType(string type, int sizeInBits)
        {
            if (string.IsNullOrEmpty(type)) return "uint32_t";
            type = type.ToLowerInvariant();
            return type switch
            {
                "int" => "int32_t",
                "float" => "float",
                "double" => "double",
                "bool" => "uint8_t",
                _ => "uint32_t",
            };
        }

        private string MakeSafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var sb = new StringBuilder();
            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
                else sb.Append('_');
            }
            return sb.ToString();
        }
    }
}