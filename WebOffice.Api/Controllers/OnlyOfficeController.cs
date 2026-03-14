using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Minio;
using Minio.DataModel.Args;
using System.IO.Compression;
using System.IO;

namespace bsckend.Controllers;

[ApiController]
[Route("onlyoffice")]
public class OnlyOfficeController : ControllerBase
{
    private readonly IConfiguration _cfg;
    private readonly IMinioClient _minio;

    public OnlyOfficeController(IConfiguration cfg, IMinioClient minio)
    {
        _cfg = cfg;
        _minio = minio;
    }

    // GET /onlyoffice/create?fileName=...&userId=...
    [HttpGet("create")]
    public async Task<IActionResult> Create(string? fileName, string? userId)
    {
        if (string.IsNullOrEmpty(fileName)) fileName = $"NewDocument_{DateTime.UtcNow:yyyyMMddHHmmss}.docx";
        if (string.IsNullOrEmpty(userId)) userId = "anonymous";

        var bucket = _cfg["Minio:Bucket"] ?? "documents";
        var objectName = $"{userId}/{fileName}";

        try
        {
            // Ensure bucket exists (create if missing) to avoid MinIO errors
            try
            {
                var existsArgs = new BucketExistsArgs().WithBucket(bucket);
                bool exists = await _minio.BucketExistsAsync(existsArgs);
                if (!exists)
                {
                    var makeArgs = new MakeBucketArgs().WithBucket(bucket);
                    await _minio.MakeBucketAsync(makeArgs);
                    Console.WriteLine($"OnlyOffice Create: created missing bucket '{bucket}'");
                }
            }
            catch (Exception bex)
            {
                Console.WriteLine($"OnlyOffice Create: bucket check/create failed: {bex.Message}");
                // continue - PutObject may still fail but we log diagnostic info
            }

            var bytes = CreateMinimalDocxBytes();
            using var ms = new MemoryStream(bytes);

            var putArgs = new PutObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectName)
                .WithStreamData(ms)
                .WithObjectSize(ms.Length)
                .WithContentType("application/vnd.openxmlformats-officedocument.wordprocessingml.document");

            ms.Position = 0;
            try
            {
                await _minio.PutObjectAsync(putArgs);
            }
            catch (Exception mex)
            {
                Console.WriteLine($"OnlyOffice Create: PutObject failed: {mex}");
                return BadRequest(new { error = "failed to create document", message = mex.Message });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OnlyOffice Create: failed to upload template (outer): {ex}");
            return BadRequest(new { error = "failed to create document", message = ex.Message });
        }

        var apiBase = _cfg["ApiBase"] ?? "http://localhost:7130";
        var redirectUrl = $"{apiBase}/onlyoffice/edit?fileName={Uri.EscapeDataString(fileName)}&userId={Uri.EscapeDataString(userId)}";
        return Redirect(redirectUrl);
    }

    private static byte[] CreateMinimalDocxBytes()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void AddEntry(string name, string content)
            {
                var entry = archive.CreateEntry(name);
                using var s = entry.Open();
                using var sw = new StreamWriter(s, Encoding.UTF8);
                sw.Write(content);
            }

            AddEntry("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n  <Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>\n</Types>");

            AddEntry("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"/word/document.xml\"/>\n</Relationships>");

            AddEntry("word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t></w:t></w:r></w:p></w:body></w:document>");

            AddEntry("word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n</Relationships>");
        }
        ms.Position = 0;
        return ms.ToArray();
    }

    // GET /onlyoffice/edit?fileName=...&userId=...
    [HttpGet("edit")]
    public IActionResult Edit(string fileName, string? userId, bool noJwt = false)
    {
        if (string.IsNullOrEmpty(fileName)) return BadRequest("fileName required");

        var docServer = _cfg["OnlyOffice:DocServer"] ?? "http://localhost:8080";
        var apiBase = _cfg["ApiBase"] ?? "http://localhost:7130";
        var downloadUrl = apiBase + "/api/storage/download/" + Uri.EscapeDataString(fileName) + (string.IsNullOrEmpty(userId) ? "" : "?userId=" + Uri.EscapeDataString(userId));
        var callbackUrl = apiBase + "/api/storage/callback?fileName=" + Uri.EscapeDataString(fileName) + (string.IsNullOrEmpty(userId) ? "" : "&userId=" + Uri.EscapeDataString(userId));

        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = "docx";

        var key = Guid.NewGuid().ToString();
        var title = fileName;

        var documentObj = new Dictionary<string, object?>
        {
            ["fileType"] = ext,
            ["key"] = key,
            ["title"] = title,
            ["url"] = downloadUrl
        };

        var editorConfigObj = new Dictionary<string, object?>
        {
            ["callbackUrl"] = callbackUrl,
            ["mode"] = "edit",
            ["lang"] = "en"
        };

        // Generate OnlyOffice token if secret present
        string? onlyofficeToken = null;
        var secret = _cfg["ONLYOFFICE_JWT_SECRET"] ?? _cfg["OnlyOffice:JwtSecret"];
        if (!string.IsNullOrEmpty(secret) && !noJwt)
        {
            try
            {
                var keyBytes = Encoding.UTF8.GetBytes(secret);
                var sigKey = new SymmetricSecurityKey(keyBytes);
                var creds = new SigningCredentials(sigKey, SecurityAlgorithms.HmacSha256);

                var now = DateTime.UtcNow;
                var iat = new DateTimeOffset(now).ToUnixTimeSeconds();
                var exp = new DateTimeOffset(now.AddMinutes(60)).ToUnixTimeSeconds();

                var payload = new Dictionary<string, object?>
                {
                    ["document"] = documentObj,
                    ["editorConfig"] = editorConfigObj,
                    ["user"] = userId ?? "anonymous",
                    ["iat"] = iat,
                    ["exp"] = exp
                };

                var header = new JwtHeader(creds);
                var jwtPayload = new JwtPayload();
                // Add document and editorConfig at top-level (OnlyOffice expects top-level claims)
                jwtPayload.Add("document", documentObj);
                jwtPayload.Add("editorConfig", editorConfigObj);
                jwtPayload.Add("user", userId ?? "anonymous");
                jwtPayload.Add("iat", iat);
                jwtPayload.Add("exp", exp);
                var jwt = new JwtSecurityToken(header, jwtPayload);
                onlyofficeToken = new JwtSecurityTokenHandler().WriteToken(jwt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OnlyOffice token generation failed: {ex}");
                onlyofficeToken = null;
            }
        }

        var config = new Dictionary<string, object?>
        {
            ["document"] = documentObj,
            ["editorConfig"] = editorConfigObj
        };
        if (!string.IsNullOrEmpty(onlyofficeToken)) config["token"] = onlyofficeToken;

        var configJson = JsonSerializer.Serialize(config);

        // Encode JSON config as Base64 to avoid embedding issues (quotes, control chars, encoding)
        var configB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson));

        // Log small diagnostic info to server console (length and prefix)
        try
        {
            Console.WriteLine($"OnlyOffice config base64 length={configB64.Length}, prefix={configB64.Substring(0, Math.Min(64, configB64.Length))}");
        }
        catch { /* ignore logging errors */ }

        var html = "<!doctype html>" +
                   "<html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
                   "<title>OnlyOffice Editor - " + System.Net.WebUtility.HtmlEncode(title) + "</title>" +
                   "<script src=\"" + docServer + "/web-apps/apps/api/documents/api.js\"></script>" +
                   "<style>html,body,#placeholder{height:100%;margin:0;padding:0}</style></head><body>" +
                   "<div id=\"placeholder\"></div>" +
                   // Embed Base64 as raw text inside a script tag of type application/json to avoid any JS parsing/quoting issues
                   "<script id=\"onlyoffice-config\" type=\"application/json\">" + configB64 + "</script>" +
                   "<script>try{const cfgB64 = document.getElementById('onlyoffice-config').textContent; const config = JSON.parse(atob(cfgB64)); new DocsAPI.DocEditor('placeholder', config);}catch(e){document.body.innerHTML='<pre style=\"color:red\">Error initializing OnlyOffice editor:\\n'+e+'</pre>';console.error(e);} </script>" +
                   "</body></html>";

        return Content(html, "text/html");
    }

    // Quick endpoint that opens editor without token (debugging)
    [HttpGet("edit-nojwt")]
    public IActionResult EditNoJwt(string fileName, string? userId)
    {
        return Edit(fileName, userId, true);
    }

    // Debug endpoint: return the OnlyOffice config as JSON (safe for debugging)
    [HttpGet("debug-config")]
    public IActionResult DebugConfig(string fileName, string? userId, bool includeToken = false)
    {
        if (string.IsNullOrEmpty(fileName)) return BadRequest("fileName required");

        var docServer = _cfg["OnlyOffice:DocServer"] ?? "http://localhost:8080";
        var apiBase = _cfg["ApiBase"] ?? "http://localhost:7130";
        var downloadUrl = apiBase + "/api/storage/download/" + Uri.EscapeDataString(fileName) + (string.IsNullOrEmpty(userId) ? "" : "?userId=" + Uri.EscapeDataString(userId));
        var callbackUrl = apiBase + "/api/storage/callback?fileName=" + Uri.EscapeDataString(fileName) + (string.IsNullOrEmpty(userId) ? "" : "&userId=" + Uri.EscapeDataString(userId));

        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = "docx";

        var key = Guid.NewGuid().ToString();
        var title = fileName;

        var documentObj = new Dictionary<string, object?>
        {
            ["fileType"] = ext,
            ["key"] = key,
            ["title"] = title,
            ["url"] = downloadUrl
        };

        var editorConfigObj = new Dictionary<string, object?>
        {
            ["callbackUrl"] = callbackUrl,
            ["mode"] = "edit",
            ["lang"] = "en"
        };

        string? onlyofficeToken = null;
        var secret = _cfg["ONLYOFFICE_JWT_SECRET"] ?? _cfg["OnlyOffice:JwtSecret"];
        if (!string.IsNullOrEmpty(secret) && includeToken)
        {
            try
            {
                var keyBytes = Encoding.UTF8.GetBytes(secret);
                var sigKey = new SymmetricSecurityKey(keyBytes);
                var creds = new SigningCredentials(sigKey, SecurityAlgorithms.HmacSha256);

                var now = DateTime.UtcNow;
                var iat = new DateTimeOffset(now).ToUnixTimeSeconds();
                var exp = new DateTimeOffset(now.AddMinutes(60)).ToUnixTimeSeconds();

                var payload = new Dictionary<string, object?>
                {
                    ["document"] = documentObj,
                    ["editorConfig"] = editorConfigObj,
                    ["user"] = userId ?? "anonymous",
                    ["iat"] = iat,
                    ["exp"] = exp
                };

                var header = new JwtHeader(creds);
                var jwtPayload = new JwtPayload();
                // Add document and editorConfig at top-level (OnlyOffice expects top-level claims)
                jwtPayload.Add("document", documentObj);
                jwtPayload.Add("editorConfig", editorConfigObj);
                jwtPayload.Add("user", userId ?? "anonymous");
                jwtPayload.Add("iat", iat);
                jwtPayload.Add("exp", exp);
                var jwt = new JwtSecurityToken(header, jwtPayload);
                onlyofficeToken = new JwtSecurityTokenHandler().WriteToken(jwt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OnlyOffice debug token generation failed: {ex}");
                onlyofficeToken = null;
            }
        }

        var config = new Dictionary<string, object?>
        {
            ["document"] = documentObj,
            ["editorConfig"] = editorConfigObj
        };
        if (!string.IsNullOrEmpty(onlyofficeToken)) config["token"] = onlyofficeToken;

        return new JsonResult(config);
    }

    // Debug endpoint: embed raw JSON config directly into the page (no base64)
    [HttpGet("edit-raw")]
    public IActionResult EditRaw(string fileName, string? userId, bool noJwt = false)
    {
        if (string.IsNullOrEmpty(fileName)) return BadRequest("fileName required");

        var docServer = _cfg["OnlyOffice:DocServer"] ?? "http://localhost:8080";
        var apiBase = _cfg["ApiBase"] ?? "http://localhost:7130";
        var downloadUrl = apiBase + "/api/storage/download/" + Uri.EscapeDataString(fileName) + (string.IsNullOrEmpty(userId) ? "" : "?userId=" + Uri.EscapeDataString(userId));
        var callbackUrl = apiBase + "/api/storage/callback?fileName=" + Uri.EscapeDataString(fileName) + (string.IsNullOrEmpty(userId) ? "" : "&userId=" + Uri.EscapeDataString(userId));

        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = "docx";

        var key = Guid.NewGuid().ToString();
        var title = fileName;

        var documentObj = new Dictionary<string, object?>
        {
            ["fileType"] = ext,
            ["key"] = key,
            ["title"] = title,
            ["url"] = downloadUrl
        };

        var editorConfigObj = new Dictionary<string, object?>
        {
            ["callbackUrl"] = callbackUrl,
            ["mode"] = "edit",
            ["lang"] = "en"
        };

        string? onlyofficeToken = null;
        var secret = _cfg["ONLYOFFICE_JWT_SECRET"] ?? _cfg["OnlyOffice:JwtSecret"];
        if (!string.IsNullOrEmpty(secret) && !noJwt)
        {
            try
            {
                var keyBytes = Encoding.UTF8.GetBytes(secret);
                var sigKey = new SymmetricSecurityKey(keyBytes);
                var creds = new SigningCredentials(sigKey, SecurityAlgorithms.HmacSha256);

                var now = DateTime.UtcNow;
                var iat = new DateTimeOffset(now).ToUnixTimeSeconds();
                var exp = new DateTimeOffset(now.AddMinutes(60)).ToUnixTimeSeconds();

                var jwtPayload = new JwtPayload();
                jwtPayload.Add("document", documentObj);
                jwtPayload.Add("editorConfig", editorConfigObj);
                jwtPayload.Add("user", userId ?? "anonymous");
                jwtPayload.Add("iat", iat);
                jwtPayload.Add("exp", exp);
                var header = new JwtHeader(new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256));
                var jwt = new JwtSecurityToken(header, jwtPayload);
                onlyofficeToken = new JwtSecurityTokenHandler().WriteToken(jwt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OnlyOffice token generation failed (edit-raw): {ex}");
                onlyofficeToken = null;
            }
        }

        var config = new Dictionary<string, object?>
        {
            ["document"] = documentObj,
            ["editorConfig"] = editorConfigObj
        };
        if (!string.IsNullOrEmpty(onlyofficeToken)) config["token"] = onlyofficeToken;

        var configJson = JsonSerializer.Serialize(config);
        // Escape closing </script> to avoid prematurely ending script tag if present in JSON
        var safeJson = configJson.Replace("</script>", "<\\/script>");

        var html = "<!doctype html>" +
                   "<html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
                   "<title>OnlyOffice Editor (raw) - " + System.Net.WebUtility.HtmlEncode(title) + "</title>" +
                   "<script src=\"" + docServer + "/web-apps/apps/api/documents/api.js\"></script>" +
                   "<style>html,body,#placeholder{height:100%;margin:0;padding:0}</style></head><body>" +
                   "<div id=\"placeholder\"></div>" +
                   "<script>try{const config=" + safeJson + "; new DocsAPI.DocEditor('placeholder', config);}catch(e){document.body.innerHTML='<pre style=\"color:red\">Error initializing OnlyOffice editor:\\n'+e+'</pre>';console.error(e);} </script>" +
                   "</body></html>";

        return Content(html, "text/html");
    }
}
