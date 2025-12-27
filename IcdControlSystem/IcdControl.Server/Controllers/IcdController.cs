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
                opts.Converters.Add(new BaseFieldJsonConverter());

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

            // Serialize with the updated BaseFieldJsonConverter to ensure all nested fields are saved
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

            // Simplified header generation
            return Content($"/* ICD: {icd.Name} */\n#pragma once\n", "text/plain");
        }

        // --- ADMIN ---

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

        private bool CheckAdmin(out string userId)
        {
            userId = null;
            if (!Request.Headers.TryGetValue("X-UserId", out var uid) || string.IsNullOrEmpty(uid)) return false;
            userId = uid.ToString();
            return _db.IsUserAdmin(userId);
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