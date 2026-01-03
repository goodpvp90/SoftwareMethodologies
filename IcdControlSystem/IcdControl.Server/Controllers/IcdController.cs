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
                _db.SaveIcdRaw(icd.IcdId, icd.Name ?? "New ICD", icd.Version, jsonContent, userId);
                _db.GrantPermission(userId, icd.IcdId, true);
                _db.LogChange(icd.IcdId, userId, "CREATE", $"Created ICD: {icd.Name}");
                return Ok(new { IcdId = icd.IcdId });
            }
            else
            {
                if (!_db.HasEditPermission(userId, icd.IcdId))
                    return StatusCode(StatusCodes.Status403Forbidden, "You do not have edit permission for this ICD.");

                _db.SaveIcdRaw(icd.IcdId, icd.Name ?? "", icd.Version, jsonContent, userId);
                _db.LogChange(icd.IcdId, userId, "UPDATE", $"Updated ICD: {icd.Name} to version {icd.Version}");
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

        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasEditPermission(userId, id))
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have edit permission for this ICD.");

            var deleted = _db.DeleteIcd(id);
            if (!deleted)
                return NotFound();

            _db.LogChange(id, userId, "DELETE", "Deleted ICD");
            return NoContent();
        }

        // ---------------------------------------------------------
        // EXPORT LOGIC (UPDATED)
        // ---------------------------------------------------------
        [HttpGet("{id}/export")]
        public IActionResult Export(string id, [FromQuery] string? format = "c")
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasViewPermission(userId, id))
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view this ICD.");

            var icd = _db.GetIcdById(id);
            if (icd == null) return NotFound();

            format = format?.ToLowerInvariant() ?? "c";
            return format switch
            {
                "c" or "h" => Content(GenerateCHeader(icd), "text/plain", Encoding.UTF8),
                "json" => Ok(icd),
                "xml" => Content(GenerateXml(icd), "application/xml", Encoding.UTF8),
                _ => Content(GenerateCHeader(icd), "text/plain", Encoding.UTF8)
            };
        }

        [HttpGet("{id}/export/pdf")]
        public IActionResult ExportPdf(string id)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasViewPermission(userId, id))
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view this ICD.");

            var icd = _db.GetIcdById(id);
            if (icd == null) return NotFound();

            // Simple PDF generation (HTML-based)
            string html = GeneratePdfHtml(icd);
            return Content(html, "text/html", Encoding.UTF8);
        }

        [HttpPost("import/c")]
        public async Task<IActionResult> ImportFromC()
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            var raw = await ReadRawBodyAsync();
            try
            {
                var icd = ParseCHeader(raw);
                if (icd == null) return BadRequest("Failed to parse C header");

                var jsonContent = JsonSerializer.Serialize(icd);
                _db.SaveIcdRaw(icd.IcdId, icd.Name ?? "Imported ICD", icd.Version, jsonContent, userId);
                _db.GrantPermission(userId, icd.IcdId, true);
                _db.LogChange(icd.IcdId, userId, "IMPORT", "Imported from C header");
                return Ok(new { IcdId = icd.IcdId });
            }
            catch (Exception ex)
            {
                return BadRequest($"Import failed: {ex.Message}");
            }
        }

        private string GenerateXml(Icd icd)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<Icd Name=\"{System.Security.SecurityElement.Escape(icd.Name ?? "")}\" Version=\"{icd.Version}\">");
            if (!string.IsNullOrEmpty(icd.Description))
                sb.AppendLine($"  <Description>{System.Security.SecurityElement.Escape(icd.Description)}</Description>");

            if (icd.Structs != null && icd.Structs.Any())
            {
                sb.AppendLine("  <Structs>");
                foreach (var st in icd.Structs)
                    GenerateStructXml(sb, st, "    ");
                sb.AppendLine("  </Structs>");
            }

            if (icd.Messages != null && icd.Messages.Any())
            {
                sb.AppendLine("  <Messages>");
                foreach (var msg in icd.Messages)
                    GenerateMessageXml(sb, msg, "    ");
                sb.AppendLine("  </Messages>");
            }

            sb.AppendLine("</Icd>");
            return sb.ToString();
        }

        private void GenerateStructXml(StringBuilder sb, Struct s, string indent)
        {
            sb.AppendLine($"{indent}<Struct Name=\"{System.Security.SecurityElement.Escape(s.Name ?? "")}\" IsUnion=\"{s.IsUnion}\">");
            if (s.Fields != null)
            {
                foreach (var field in s.Fields)
                {
                    if (field is DataField df)
                        sb.AppendLine($"{indent}  <Field Name=\"{System.Security.SecurityElement.Escape(df.Name ?? "")}\" Type=\"{df.Type}\" SizeInBits=\"{df.SizeInBits}\" />");
                    else if (field is Struct nested)
                        GenerateStructXml(sb, nested, indent + "  ");
                }
            }
            sb.AppendLine($"{indent}</Struct>");
        }

        private void GenerateMessageXml(StringBuilder sb, Message msg, string indent)
        {
            sb.AppendLine($"{indent}<Message Name=\"{System.Security.SecurityElement.Escape(msg.Name ?? "")}\" IsRx=\"{msg.IsRx}\" IsMsb=\"{msg.IsMsb}\">");
            if (!string.IsNullOrEmpty(msg.Description))
                sb.AppendLine($"{indent}  <Description>{System.Security.SecurityElement.Escape(msg.Description)}</Description>");
            if (msg.Fields != null)
            {
                foreach (var field in msg.Fields)
                {
                    if (field is DataField df)
                        sb.AppendLine($"{indent}  <Field Name=\"{System.Security.SecurityElement.Escape(df.Name ?? "")}\" Type=\"{df.Type}\" SizeInBits=\"{df.SizeInBits}\" />");
                    else if (field is Struct nested)
                        GenerateStructXml(sb, nested, indent + "  ");
                }
            }
            sb.AppendLine($"{indent}</Message>");
        }

        private string GeneratePdfHtml(Icd icd)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'><style>");
            sb.AppendLine("body { font-family: Arial; margin: 40px; }");
            sb.AppendLine("h1 { color: #2563EB; } h2 { color: #1E40AF; margin-top: 30px; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; margin: 20px 0; }");
            sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            sb.AppendLine("th { background-color: #2563EB; color: white; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine($"<h1>{System.Security.SecurityElement.Escape(icd.Name ?? "ICD")}</h1>");
            sb.AppendLine($"<p><strong>Version:</strong> {icd.Version}</p>");
            if (!string.IsNullOrEmpty(icd.Description))
                sb.AppendLine($"<p><strong>Description:</strong> {System.Security.SecurityElement.Escape(icd.Description)}</p>");

            if (icd.Messages != null && icd.Messages.Any())
            {
                sb.AppendLine("<h2>Messages</h2>");
                foreach (var msg in icd.Messages)
                {
                    sb.AppendLine($"<h3>{System.Security.SecurityElement.Escape(msg.Name ?? "")}</h3>");
                    if (!string.IsNullOrEmpty(msg.Description))
                        sb.AppendLine($"<p>{System.Security.SecurityElement.Escape(msg.Description)}</p>");
                    sb.AppendLine("<table><tr><th>Field</th><th>Type</th><th>Size (bits)</th></tr>");
                    if (msg.Fields != null)
                        foreach (var f in msg.Fields.OfType<DataField>())
                            sb.AppendLine($"<tr><td>{System.Security.SecurityElement.Escape(f.Name ?? "")}</td><td>{f.Type}</td><td>{f.SizeInBits}</td></tr>");
                    sb.AppendLine("</table>");
                }
            }

            if (icd.Structs != null && icd.Structs.Any())
            {
                sb.AppendLine("<h2>Structs</h2>");
                foreach (var st in icd.Structs)
                {
                    sb.AppendLine($"<h3>{System.Security.SecurityElement.Escape(st.Name ?? "")}</h3>");
                    sb.AppendLine("<table><tr><th>Field</th><th>Type</th><th>Size (bits)</th></tr>");
                    if (st.Fields != null)
                        foreach (var f in st.Fields.OfType<DataField>())
                            sb.AppendLine($"<tr><td>{System.Security.SecurityElement.Escape(f.Name ?? "")}</td><td>{f.Type}</td><td>{f.SizeInBits}</td></tr>");
                    sb.AppendLine("</table>");
                }
            }

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private Icd? ParseCHeader(string cHeader)
        {
            // Basic C header parser - this is a simplified version
            // In production, you'd want a more robust parser
            var icd = new Icd
            {
                IcdId = Guid.NewGuid().ToString(),
                Name = "Imported ICD",
                Version = 1.0,
                Messages = new List<Message>(),
                Structs = new List<Struct>()
            };

            // Simple regex-based parsing (very basic implementation)
            // This would need to be more sophisticated for production use
            var structPattern = @"typedef\s+(?:struct|union)\s*\{([^}]+)\}\s*(\w+)_t;";
            var matches = System.Text.RegularExpressions.Regex.Matches(cHeader, structPattern, System.Text.RegularExpressions.RegexOptions.Multiline);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var structName = match.Groups[2].Value;
                var fieldsText = match.Groups[1].Value;
                var st = new Struct { Name = structName, Fields = new List<BaseField>() };

                // Parse fields (simplified)
                var fieldPattern = @"(\w+(?:_t)?)\s+(\w+)(?:\s*:\s*(\d+))?;";
                var fieldMatches = System.Text.RegularExpressions.Regex.Matches(fieldsText, fieldPattern);
                foreach (System.Text.RegularExpressions.Match fm in fieldMatches)
                {
                    var fieldType = fm.Groups[1].Value;
                    var fieldName = fm.Groups[2].Value;
                    var sizeBits = fm.Groups[3].Success ? int.Parse(fm.Groups[3].Value) : 0;
                    st.Fields.Add(new DataField { Name = fieldName, Type = fieldType, SizeInBits = sizeBits });
                }

                icd.Structs.Add(st);
            }

            return icd;
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

        // ---------------------------------------------------------
        // VERSION HISTORY
        // ---------------------------------------------------------
        [HttpGet("{id}/versions")]
        public IActionResult GetVersions(string id)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasViewPermission(userId, id))
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view this ICD.");

            var versions = _db.GetIcdVersions(id);
            return Ok(versions.Select(v => new { v.VersionId, v.VersionNumber, v.CreatedAt, v.CreatedBy }));
        }

        [HttpGet("version/{versionId}")]
        public IActionResult GetVersion(string versionId)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");

            var version = _db.GetIcdVersion(versionId);
            return version == null ? NotFound() : Ok(version);
        }

        [HttpPost("{id}/rollback")]
        public IActionResult RollbackToVersion(string id, [FromBody] RollbackRequest req)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasEditPermission(userId, id))
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have edit permission for this ICD.");

            var version = _db.GetIcdVersion(req.VersionId);
            if (version == null) return NotFound("Version not found");

            var jsonContent = JsonSerializer.Serialize(version);
            _db.SaveIcdRaw(id, version.Name, version.Version, jsonContent, userId);
            _db.LogChange(id, userId, "ROLLBACK", $"Rolled back to version {version.Version}");
            return Ok(new { IcdId = id });
        }

        public class RollbackRequest { public string VersionId { get; set; } }

        // ---------------------------------------------------------
        // COMMENTS
        // ---------------------------------------------------------
        [HttpPost("{id}/comments")]
        public IActionResult AddComment(string id, [FromBody] CommentRequest req)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasViewPermission(userId, id))
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view this ICD.");

            if (string.IsNullOrWhiteSpace(req.CommentText))
                return BadRequest("Comment text is required");

            _db.AddComment(id, userId, req.CommentText);
            return Ok();
        }

        [HttpGet("{id}/comments")]
        public IActionResult GetComments(string id)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasViewPermission(userId, id))
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view this ICD.");

            var comments = _db.GetIcdComments(id);
            return Ok(comments.Select(c => new { c.CommentId, c.UserId, c.CommentText, c.CreatedAt }));
        }

        public class CommentRequest { public string CommentText { get; set; } }

        // ---------------------------------------------------------
        // CHANGE HISTORY
        // ---------------------------------------------------------
        [HttpGet("{id}/history")]
        public IActionResult GetHistory(string id)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasViewPermission(userId, id))
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view this ICD.");

            var history = _db.GetIcdChangeHistory(id);
            return Ok(history.Select(h => new { h.ChangeId, h.UserId, h.ChangeType, h.ChangeDescription, h.CreatedAt }));
        }

        // ---------------------------------------------------------
        // USER SETTINGS
        // ---------------------------------------------------------
        [HttpGet("settings")]
        public IActionResult GetSettings()
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            var settings = _db.GetUserSettings(userId);
            return Ok(new { settings.DarkMode, settings.Language, settings.Theme });
        }

        [HttpPost("settings")]
        public IActionResult SaveSettings([FromBody] SettingsRequest req)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            _db.SaveUserSettings(userId, req.DarkMode, req.Language, req.Theme);
            return Ok();
        }

        public class SettingsRequest { public bool DarkMode { get; set; } public string Language { get; set; } public string Theme { get; set; } }

        // ---------------------------------------------------------
        // TEMPLATES
        // ---------------------------------------------------------
        [HttpGet("templates")]
        public IActionResult GetTemplates()
        {
            var templates = _db.GetTemplates();
            return Ok(templates.Select(t => new { t.TemplateId, t.Name, t.Description, t.CreatedBy }));
        }

        [HttpGet("template/{templateId}")]
        public IActionResult GetTemplate(string templateId)
        {
            var template = _db.GetTemplate(templateId);
            return template == null ? NotFound() : Ok(template);
        }

        [HttpPost("template")]
        public IActionResult SaveTemplate([FromBody] TemplateRequest req)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            var jsonContent = JsonSerializer.Serialize(req.Template);
            _db.SaveTemplate(req.Name, req.Description, jsonContent, userId);
            return Ok();
        }

        public class TemplateRequest { public string Name { get; set; } public string Description { get; set; } public Icd Template { get; set; } }

        // ---------------------------------------------------------
        // SEARCH & FILTER
        // ---------------------------------------------------------
        [HttpGet("search")]
        public IActionResult Search([FromQuery] string? query, [FromQuery] double? minVersion, [FromQuery] double? maxVersion)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            var items = _db.GetIcdsForUser(userId);
            
            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.ToLowerInvariant();
                items = items.Where(i => 
                    (i.Name?.ToLowerInvariant().Contains(q) ?? false) ||
                    (i.Description?.ToLowerInvariant().Contains(q) ?? false) ||
                    (i.Messages?.Any(m => m.Name?.ToLowerInvariant().Contains(q) ?? false) ?? false) ||
                    (i.Structs?.Any(s => s.Name?.ToLowerInvariant().Contains(q) ?? false) ?? false)
                ).ToList();
            }

            if (minVersion.HasValue)
                items = items.Where(i => i.Version >= minVersion.Value).ToList();

            if (maxVersion.HasValue)
                items = items.Where(i => i.Version <= maxVersion.Value).ToList();

            return Ok(items);
        }

        // ---------------------------------------------------------
        // PREVIEW
        // ---------------------------------------------------------
        [HttpPost("preview")]
        public IActionResult Preview([FromBody] PreviewRequest req)
        {
            if (req?.Icd == null) return BadRequest("ICD is required");
            string cHeader = GenerateCHeader(req.Icd);
            return Content(cHeader, "text/plain", Encoding.UTF8);
        }

        public class PreviewRequest { public Icd Icd { get; set; } }

        // ---------------------------------------------------------
        // VALIDATION
        // ---------------------------------------------------------
        [HttpPost("{id}/validate")]
        public IActionResult Validate(string id)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasViewPermission(userId, id))
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view this ICD.");

            var icd = _db.GetIcdById(id);
            if (icd == null) return NotFound();

            var errors = new List<string>();
            var warnings = new List<string>();

            // Validate structure
            if (string.IsNullOrWhiteSpace(icd.Name))
                errors.Add("ICD name is required");

            if (icd.Messages == null || !icd.Messages.Any())
                warnings.Add("No messages defined");

            if (icd.Messages != null)
            {
                foreach (var msg in icd.Messages)
                {
                    if (string.IsNullOrWhiteSpace(msg.Name))
                        errors.Add($"Message has no name");
                    ValidateFields(msg, errors, warnings, $"Message '{msg.Name}'");
                }
            }

            if (icd.Structs != null)
            {
                foreach (var st in icd.Structs)
                {
                    if (string.IsNullOrWhiteSpace(st.Name))
                        errors.Add($"Struct has no name");
                    ValidateFields(st, errors, warnings, $"Struct '{st.Name}'");
                }
            }

            return Ok(new { IsValid = errors.Count == 0, Errors = errors, Warnings = warnings });
        }

        private void ValidateFields(Struct s, List<string> errors, List<string> warnings, string context)
        {
            if (s.Fields == null || !s.Fields.Any())
            {
                warnings.Add($"{context} has no fields");
                return;
            }

            foreach (var field in s.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Name))
                    errors.Add($"{context}: Field has no name");

                if (field is DataField df)
                {
                    if (string.IsNullOrWhiteSpace(df.Type))
                        errors.Add($"{context}: Field '{field.Name}' has no type");
                    if (df.SizeInBits < 0)
                        errors.Add($"{context}: Field '{field.Name}' has invalid size");
                }
                else if (field is Struct nested)
                {
                    ValidateFields(nested, errors, warnings, $"{context}.{nested.Name}");
                }
            }
        }

        // ---------------------------------------------------------
        // STATISTICS
        // ---------------------------------------------------------
        [HttpGet("{id}/stats")]
        public IActionResult GetStats(string id)
        {
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid))
                return Unauthorized("Missing user header");
            var userId = uid.ToString();

            if (!_db.HasViewPermission(userId, id))
                return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view this ICD.");

            var icd = _db.GetIcdById(id);
            if (icd == null) return NotFound();

            int totalMessages = icd.Messages?.Count ?? 0;
            int totalStructs = icd.Structs?.Count ?? 0;
            int totalFields = (icd.Messages?.Sum(m => CountFields(m)) ?? 0) + (icd.Structs?.Sum(s => CountFields(s)) ?? 0);
            int totalSize = CalculateTotalSize(icd);

            return Ok(new { 
                TotalMessages = totalMessages, 
                TotalStructs = totalStructs, 
                TotalFields = totalFields,
                EstimatedSizeBytes = totalSize
            });
        }

        private int CountFields(Struct s)
        {
            if (s.Fields == null) return 0;
            return s.Fields.Count + s.Fields.OfType<Struct>().Sum(CountFields);
        }

        private int CalculateTotalSize(Struct s)
        {
            if (s.Fields == null) return 0;
            int size = 0;
            foreach (var field in s.Fields)
            {
                if (field is DataField df)
                {
                    if (df.SizeInBits > 0)
                        size += (df.SizeInBits + 7) / 8; // Round up to bytes
                    else
                        size += GetDefaultSize(df.Type);
                }
                else if (field is Struct nested)
                {
                    size += CalculateTotalSize(nested);
                }
            }
            return size;
        }

        private int CalculateTotalSize(Icd icd)
        {
            int size = 0;
            if (icd.Messages != null)
                foreach (var msg in icd.Messages)
                    size += CalculateTotalSize(msg);
            if (icd.Structs != null)
                foreach (var st in icd.Structs)
                    size += CalculateTotalSize(st);
            return size;
        }

        private int GetDefaultSize(string type)
        {
            if (string.IsNullOrEmpty(type)) return 4;
            type = type.ToLowerInvariant();
            return type switch
            {
                "uint8_t" or "int8_t" or "char" or "bool" => 1,
                "uint16_t" or "int16_t" => 2,
                "uint32_t" or "int32_t" or "float" => 4,
                "uint64_t" or "int64_t" or "double" => 8,
                _ => 4
            };
        }
    }
}