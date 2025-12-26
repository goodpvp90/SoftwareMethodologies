using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using IcdControl.Models;
using IcdControl.Server.Data;
using System.Threading.Tasks;
using System.Text;

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
            if (req == null || string.IsNullOrEmpty(req.Email) || string.IsNullOrEmpty(req.Password))
                return BadRequest("Email and password are required");

            var user = _db.Authenticate(req.Email, req.Password);
            return user != null ? Ok(user) : Unauthorized();
        }

        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.Email) || string.IsNullOrEmpty(req.Password))
                return BadRequest("Email and password are required");
            var created = _db.CreateUser(req);
            return created ? Ok() : Conflict("Email already exists or invalid data");
        }

        [HttpPost("save")]
        public IActionResult Save([FromBody] Icd icd)
        {
            if (icd == null) return BadRequest();
            _db.SaveIcd(icd);
            return Ok();
        }

        [HttpGet("list")]
        public IActionResult List()
        {
            var items = _db.GetAllIcds();
            return Ok(items);
        }

        [HttpGet("{id}")]
        public IActionResult Get(string id)
        {
            var icd = _db.GetIcdById(id);
            if (icd == null) return NotFound();
            return Ok(icd);
        }

        [HttpGet("{id}/export")]
        public IActionResult Export(string id)
        {
            var icd = _db.GetIcdById(id);
            if (icd == null) return NotFound();

            var header = GenerateHeader(icd);
            return Content(header, "text/plain", Encoding.UTF8);
        }

        // Very simple .h generator. Produces typedef structs for messages and nested structs.
        private string GenerateHeader(Icd icd)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"/* Generated header for ICD: {icd.Name} (v{icd.Version}) */");
            sb.AppendLine("#pragma once");
            sb.AppendLine("#include <stdint.h>");
            sb.AppendLine();

            // Generate structs for each message
            int idx =0;
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