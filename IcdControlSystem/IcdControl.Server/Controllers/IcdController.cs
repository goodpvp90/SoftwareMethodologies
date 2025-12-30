using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using IcdControl.Models;
using IcdControl.Server.Data;
using System.Text.Json;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace IcdControl.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IcdController : ControllerBase
    {
        private readonly DatabaseService _db;
        private readonly ILogger<IcdController> _logger;

        public IcdController(DatabaseService db, ILogger<IcdController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ---------------------------------------------------------
        // AUTH
        // ---------------------------------------------------------
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

        // ---------------------------------------------------------
        // CRUD
        // ---------------------------------------------------------

        // Save uses manual body parsing so malformed JSON doesn't get auto-rejected by MVC formatters.
        [HttpPost("save")]
        public async Task<IActionResult> Save()
        {
            var raw = await ReadRawBodyAsync();
            Icd icd;
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNameCaseInsensitive = true
                };
                // Ensure BaseFieldJsonConverter is defined in your project or removed if standard polymorphism works
                // opts.Converters.Add(new BaseFieldJsonConverter()); 

                icd = JsonSerializer.Deserialize<Icd>(raw, opts);
                if (icd == null) return BadRequest("Invalid Data: unable to parse payload");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize ICD payload. Body: {Body}", raw);
                return BadRequest($"Invalid Data: {ex.Message}");
            }

            if (string.IsNullOrEmpty(icd.IcdId)) icd.IcdId = System.Guid.NewGuid().ToString();

            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            // Serialize again to ensure consistency
            var jsonContent = JsonSerializer.Serialize(icd);

            var exists = _db.IcdExists(icd.IcdId);
            if (!exists)
            {
                _db.SaveIcdRaw(icd.IcdId, icd.Name ?? "New ICD", icd.Version, jsonContent);
                _db.GrantPermission(userId, icd.IcdId, true);
                return Ok(new { IcdId = icd.IcdId });
            }
            else
            {
                if (!_db.HasEditPermission(userId, icd.IcdId))
                    return StatusCode(StatusCodes.Status403Forbidden, "You do not have edit permission for this ICD.");

                _db.SaveIcdRaw(icd.IcdId, icd.Name ?? "", icd.Version, jsonContent);
                return Ok(new { IcdId = icd.IcdId });
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
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view this ICD.");

            var icd = _db.GetIcdById(id);
            if (icd == null) return NotFound();
            return Ok(icd);
        }

        // ---------------------------------------------------------
        // EXPORT LOGIC (UPDATED)
        // ---------------------------------------------------------
        [HttpGet("{id}/export")]
        public IActionResult Export(string id)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasViewPermission(userId, id))
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view this ICD.");

            var icd = _db.GetIcdById(id);
            if (icd == null) return NotFound();

            // Generate full C Header content
            string cHeader = GenerateCHeader(icd);
            return Content(cHeader, "text/plain", Encoding.UTF8);
        }

        // --- C Generation Helpers ---

        private string GenerateCHeader(Icd icd)
        {
            var sb = new StringBuilder();
            string safeIcdName = MakeSafeName(icd.Name).ToUpper();

            // Header Guards
            sb.AppendLine($"#ifndef {safeIcdName}_H");
            sb.AppendLine($"#define {safeIcdName}_H");
            sb.AppendLine();
            sb.AppendLine("/*");
            sb.AppendLine($" * Generated ICD Header: {icd.Name}");
            sb.AppendLine($" * Version: {icd.Version}");
            sb.AppendLine(" */");
            sb.AppendLine();
            sb.AppendLine("#include <stdint.h>");
            sb.AppendLine("#include <stdbool.h>");
            sb.AppendLine();

            // 1. Define Structs First (so messages can use them)
            sb.AppendLine("/******************************************************************************");
            sb.AppendLine(" * STRUCT DEFINITIONS");
            sb.AppendLine(" ******************************************************************************/");
            if (icd.Structs != null)
            {
                foreach (var st in icd.Structs)
                {
                    GenerateStructCode(sb, st);
                    sb.AppendLine();
                }
            }

            // 2. Define Messages
            sb.AppendLine("/******************************************************************************");
            sb.AppendLine(" * MESSAGES");
            sb.AppendLine(" ******************************************************************************/");
            if (icd.Messages != null)
            {
                foreach (var msg in icd.Messages)
                {
                    GenerateStructCode(sb, msg, isMessage: true);
                    sb.AppendLine();
                }
            }

            sb.AppendLine($"#endif /* {safeIcdName}_H */");
            return sb.ToString();
        }

        private void GenerateStructCode(StringBuilder sb, Struct s, bool isMessage = false)
        {
            string safeName = MakeSafeName(s.Name);
            string typedefName = $"{safeName}_t";

            // Handle Union vs Struct
            string typeKeyword = s.IsUnion ? "union" : "struct";

            sb.AppendLine($"/* {(isMessage ? "Message" : "Struct")}: {s.Name} */");
            sb.AppendLine($"typedef {typeKeyword} {{");

            if (s.Fields != null && s.Fields.Any())
            {
                foreach (var field in s.Fields)
                {
                    if (field is DataField df)
                    {
                        string cType = MapToCType(df.Type);
                        string fName = MakeSafeName(df.Name);

                        // Handle Bitfields: Only if SizeInBits > 0 AND it differs from standard size
                        if (df.SizeInBits > 0 && !IsStandardSize(df.Type, df.SizeInBits))
                        {
                            sb.AppendLine($"    {cType} {fName} : {df.SizeInBits};");
                        }
                        else
                        {
                            sb.AppendLine($"    {cType} {fName};");
                        }
                    }
                    else if (field is Struct nestedInstance)
                    {
                        // Handle Nested Structs (Instances)
                        string fName = MakeSafeName(nestedInstance.Name);

                        // If it links to a defined struct type, use that type
                        if (!string.IsNullOrEmpty(nestedInstance.StructType))
                        {
                            string typeName = MakeSafeName(nestedInstance.StructType) + "_t";
                            sb.AppendLine($"    {typeName} {fName};");
                        }
                        else
                        {
                            // Fallback for anonymous structs (should be rare with new logic)
                            sb.AppendLine($"    /* Anonymous struct {fName} not fully supported */");
                            sb.AppendLine($"    void* {fName}_ptr;");
                        }
                    }
                }
            }
            else
            {
                // Empty struct handler for C compliance
                sb.AppendLine("    uint8_t _dummy;");
            }

            sb.AppendLine($"}} {typedefName};");
        }

        private string MapToCType(string type)
        {
            if (string.IsNullOrEmpty(type)) return "uint32_t";
            // Normalize type
            string t = type.Trim().ToLowerInvariant();

            return t switch
            {
                "int" => "int32_t",
                "int32" => "int32_t",
                "int32_t" => "int32_t",
                "uint32" => "uint32_t",
                "uint32_t" => "uint32_t",
                "int16" => "int16_t",
                "int16_t" => "int16_t",
                "uint16" => "uint16_t",
                "uint16_t" => "uint16_t",
                "int8" => "int8_t",
                "int8_t" => "int8_t",
                "uint8" => "uint8_t",
                "uint8_t" => "uint8_t",
                "byte" => "uint8_t",
                "char" => "char",
                "float" => "float",
                "double" => "double",
                "bool" => "bool",
                "int64" => "int64_t",
                "uint64" => "uint64_t",
                "long" => "int64_t",
                _ => type // Fallback
            };
        }

        private bool IsStandardSize(string type, int bits)
        {
            type = type.ToLower();
            if (type.Contains("8") && bits == 8) return true;
            if (type.Contains("16") && bits == 16) return true;
            if (type.Contains("32") && bits == 32) return true;
            if (type.Contains("64") && bits == 64) return true;
            if (type == "bool" && bits == 8) return true;
            return false;
        }

        private string MakeSafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unknown";
            var safe = new string(name.Select(c => (char.IsLetterOrDigit(c) || c == '_') ? c : '_').ToArray());
            if (char.IsDigit(safe[0])) return "_" + safe;
            return safe;
        }

        // ---------------------------------------------------------
        // ADMIN
        // ---------------------------------------------------------

        [HttpGet("admin/users")]
        public IActionResult GetUsers()
        {
            if (!CheckAdmin(out _)) return StatusCode(StatusCodes.Status403Forbidden, "Admin permission required.");
            return Ok(_db.GetUsers());
        }

        [HttpGet("admin/icd/{icdId}/permissions")]
        public IActionResult GetIcdPermissions(string icdId)
        {
            if (!CheckAdmin(out _)) return StatusCode(StatusCodes.Status403Forbidden, "Admin permission required.");
            var perms = _db.GetIcdPermissions(icdId)
                .Select(p => new PermissionDto { UserId = p.UserId, CanEdit = p.CanEdit })
                .ToList();
            return Ok(perms);
        }

        public class PermissionDto
        {
            public string UserId { get; set; }
            public bool CanEdit { get; set; }
        }

        [HttpPost("admin/grant")]
        public IActionResult GrantPermission([FromBody] GrantRequest req)
        {
            if (!CheckAdmin(out _)) return StatusCode(StatusCodes.Status403Forbidden, "Admin permission required.");
            if (req.Revoke)
                _db.RevokePermission(req.UserId, req.IcdId);
            else
                _db.GrantPermission(req.UserId, req.IcdId, req.CanEdit);
            return Ok();
        }

        public class GrantRequest { public string UserId { get; set; } public string IcdId { get; set; } public bool CanEdit { get; set; } public bool Revoke { get; set; } }

        [HttpGet("admin/user/{userId}")]
        public IActionResult GetUser(string userId)
        {
            if (!CheckAdmin(out _)) return StatusCode(StatusCodes.Status403Forbidden, "Admin permission required.");
            var user = _db.GetUserById(userId);
            return user == null ? NotFound() : Ok(user);
        }

        [HttpGet("admin/user/{userId}/permissions")]
        public IActionResult GetUserPermissions(string userId)
        {
            if (!CheckAdmin(out _)) return StatusCode(StatusCodes.Status403Forbidden, "Admin permission required.");

            var perms = _db.GetUserPermissions(userId)
                .Select(p => new UserIcdPermissionDto { IcdId = p.IcdId, CanEdit = p.CanEdit })
                .ToList();

            return Ok(perms);
        }

        public class UserIcdPermissionDto
        {
            public string IcdId { get; set; }
            public bool CanEdit { get; set; }
        }

        [HttpPost("admin/user/{userId}/email")]
        public IActionResult UpdateUserEmail(string userId, [FromBody] UpdateEmailRequest req)
        {
            if (!CheckAdmin(out _)) return StatusCode(StatusCodes.Status403Forbidden, "Admin permission required.");
            if (req == null || string.IsNullOrWhiteSpace(req.Email)) return BadRequest("Email is required");

            var ok = _db.UpdateUserEmail(userId, req.Email.Trim());
            return ok ? Ok() : Conflict("Email already in use or invalid user");
        }

        public class UpdateEmailRequest
        {
            public string Email { get; set; }
        }

        [HttpPost("admin/user/{userId}/password")]
        public IActionResult UpdateUserPassword(string userId, [FromBody] UpdatePasswordRequest req)
        {
            if (!CheckAdmin(out _)) return StatusCode(StatusCodes.Status403Forbidden, "Admin permission required.");
            if (req == null || string.IsNullOrWhiteSpace(req.Password)) return BadRequest("Password is required");

            var ok = _db.UpdateUserPassword(userId, req.Password);
            return ok ? Ok() : NotFound();
        }

        public class UpdatePasswordRequest
        {
            public string Password { get; set; }
        }

        private bool CheckAdmin(out string userId)
        {
            userId = null;
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid)) return false;
            // If the header is accidentally repeated, ASP.NET will join values as "id1,id2".
            // Accept the first token to avoid false 403s.
            var raw = uid.ToString();
            userId = raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(userId)) return false;
            var isAdmin = _db.IsUserAdmin(userId);
            if (!isAdmin)
            {
                _logger.LogWarning("Admin check failed. Raw X-UserId='{Raw}', Parsed='{Parsed}'", raw, userId);
            }
            return isAdmin;
        }

        private async Task<string> ReadRawBodyAsync()
        {
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
            var raw = await reader.ReadToEndAsync();
            Request.Body.Position = 0;
            return raw;
        }
    }
}