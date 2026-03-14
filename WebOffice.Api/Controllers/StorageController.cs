using Microsoft.AspNetCore.Mvc;
using Minio;
using Minio.ApiEndpoints;
using Minio.DataModel.Args;
namespace bsckend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StorageController : ControllerBase
{
    private readonly IMinioClient _minioClient;

    // 1. Dependency Injection: теперь клиент автоматически прокидывается из Program.cs
    public StorageController(IMinioClient minioClient)
    {
        _minioClient = minioClient;
    }

    [HttpPost("init")]
    public async Task<IActionResult> InitializeStorage()
    {
        try
        {
            var bucketName = "documents";
            var existsArgs = new BucketExistsArgs().WithBucket(bucketName);
            bool found = await _minioClient.BucketExistsAsync(existsArgs);

            if (!found)
            {
                var makeArgs = new MakeBucketArgs().WithBucket(bucketName);
                await _minioClient.MakeBucketAsync(makeArgs);
                return Ok($"Бакет '{bucketName}' успешно создан.");
            }

            return Ok($"Бакет '{bucketName}' уже существует.");
        }
        catch (Exception ex)
        {
            return BadRequest($"Ошибка при связи с MinIO: {ex.Message}");
        }
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    // 2. Добавили запрос userId
    public async Task<IActionResult> UploadFile([FromForm] IFormFile file, [FromQuery] string userId) 
    {
        // Diagnostic: if file is null, try to inspect Request.Form
        if (file == null)
        {
            try
            {
                var files = Request?.Form?.Files;
                var ct = Request?.ContentType ?? "(null)";
                var qs = Request?.QueryString.Value ?? "(null)";
                Console.WriteLine($"UploadFile: received null file. ContentType={ct}; Query={qs}; FormFilesCount={(files?.Count ?? 0)}");
                if (files != null && files.Count > 0)
                {
                    file = files[0];
                }
                else
                {
                    return BadRequest(new { error = "File missing", contentType = ct, query = qs, formFiles = files?.Count ?? 0 });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "Exception reading request form", message = ex.Message });
            }
        }

        // Fallback: if userId not provided via query, try to read it from form body (client might send it as form field)
        if (string.IsNullOrEmpty(userId))
        {
            try
            {
                var form = Request?.Form;
                if (form != null && form.TryGetValue("userId", out var sv))
                {
                    var formUser = sv.FirstOrDefault();
                    if (!string.IsNullOrEmpty(formUser)) userId = formUser;
                }
            }
            catch { /* ignore */ }
        }

        if (file == null || file.Length == 0) return BadRequest("Файл не выбран.");
        if (string.IsNullOrEmpty(userId)) return BadRequest("Не указан ID пользователя.");

        try
        {
            // Ensure bucket exists
            var bucketName = "documents";
            try
            {
                var existsArgs = new BucketExistsArgs().WithBucket(bucketName);
                bool exists = await _minioClient.BucketExistsAsync(existsArgs);
                if (!exists)
                {
                    var makeArgs = new MakeBucketArgs().WithBucket(bucketName);
                    await _minioClient.MakeBucketAsync(makeArgs);
                }
            }
            catch (Exception bex)
            {
                // Bucket check/create failed - include context but continue to attempt upload (PutObjectAsync may also fail)
                Console.WriteLine($"Warning: bucket check/create failed: {bex.Message}");
            }
             using var stream = file.OpenReadStream();
             
             // 3. Формируем путь: папка_юзера/имя_файла
            string objectName = $"{userId}/{file.FileName}";

            var putObjectArgs = new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectName)
                .WithStreamData(stream)
                .WithObjectSize(file.Length)
                .WithContentType(file.ContentType);

            try
            {
                await _minioClient.PutObjectAsync(putObjectArgs);
            }
            catch (Exception mex)
            {
                // Provide clearer error message
                // Log detailed diagnostic info
                try
                {
                    var existsArgs2 = new BucketExistsArgs().WithBucket(bucketName);
                    bool exists2 = await _minioClient.BucketExistsAsync(existsArgs2);
                    var formFiles = Request?.Form?.Files;
                    var fileNames = formFiles != null ? string.Join(",", formFiles.Select(f => f.FileName)) : "(none)";
                    Console.WriteLine($"MinIO PutObject failed: {mex}\nuserId={userId}; objectName={objectName}; bucketExists={exists2}; formFiles=[{fileNames}]");
                }
                catch (Exception logEx)
                {
                    Console.WriteLine($"MinIO PutObject failed and diagnostic logging failed: {logEx}\nOriginal: {mex}");
                }

                return BadRequest(new { error = "Ошибка при загрузке в MinIO", message = mex.ToString() });
            }

            return Ok(new { message = $"Файл '{file.FileName}' успешно загружен для пользователя '{userId}'." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Ошибка при загрузке", message = ex.Message });
        }
    }

    [HttpGet("download/{fileName}")]
    // Добавили запрос userId
    public async Task<IActionResult> DownloadFile(string fileName, [FromQuery] string userId)
    {
        if (string.IsNullOrEmpty(userId)) return BadRequest("Не указан ID пользователя.");

        try
        {
            var memoryStream = new MemoryStream();
            string objectName = $"{userId}/{fileName}"; // Ищем файл в папке пользователя

            var getObjectArgs = new GetObjectArgs()
                .WithBucket("documents")
                .WithObject(objectName)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream));

            await _minioClient.GetObjectAsync(getObjectArgs);
            memoryStream.Position = 0;

            var contentType = "application/octet-stream";
            return File(memoryStream, contentType, fileName);
        }
        catch (Exception ex)
        {
            return BadRequest($"Ошибка при скачивании: {ex.Message}");
        }
    }

    [HttpPost("callback")]
    // OnlyOffice теперь должен возвращать и имя файла, и владельца
    public async Task<IActionResult> Callback([FromBody] System.Text.Json.JsonElement data, [FromQuery] string fileName, [FromQuery] string userId)
    {
        
        
        if (data.GetProperty("status").GetInt32() == 2)
        {
            string downloadUrl = data.GetProperty("url").GetString();
            string objectName = $"{userId}/{fileName}"; // Перезаписываем в правильную папку

            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(downloadUrl);
            using var stream = await response.Content.ReadAsStreamAsync();

            var putObjectArgs = new PutObjectArgs()
                .WithBucket("documents")
                .WithObject(objectName)
                .WithStreamData(stream)
                .WithObjectSize(response.Content.Headers.ContentLength ?? 0)
                .WithContentType("application/octet-stream");

            await _minioClient.PutObjectAsync(putObjectArgs);
        }

        return Ok(new { error = 0 });
    }

    // --- New endpoints: list and delete ---
    [HttpGet("list")]
    public async Task<IActionResult> ListUserFiles([FromQuery] string userId)
    {
        if (string.IsNullOrEmpty(userId)) return BadRequest("Не указан ID пользователя.");

        try
        {
            var listArgs = new ListObjectsArgs().WithBucket("documents").WithPrefix($"{userId}/").WithRecursive(false);
            var results = new List<string>();

            // Use newer ListObjectsEnumAsync which supports async enumeration
            await foreach (var item in _minioClient.ListObjectsEnumAsync(listArgs))
            {
                if (!string.IsNullOrEmpty(item.Key))
                {
                    var name = item.Key.StartsWith($"{userId}/") ? item.Key.Substring(userId.Length + 1) : item.Key;
                    results.Add(name);
                }
            }

            // results populated

            return Ok(results);
        }
        catch (Exception ex)
        {
            return BadRequest($"Ошибка при получении списка: {ex.Message}");
        }
    }

    [HttpDelete("delete")]
    public async Task<IActionResult> DeleteUserFile([FromQuery] string userId, [FromQuery] string fileName)
    {
        if (string.IsNullOrEmpty(userId)) return BadRequest("Не указан ID пользователя.");
        if (string.IsNullOrEmpty(fileName)) return BadRequest("Не указан имя файла.");

        try
        {
            string objectName = $"{userId}/{fileName}";
            var removeArgs = new RemoveObjectArgs().WithBucket("documents").WithObject(objectName);
            await _minioClient.RemoveObjectAsync(removeArgs);
            return Ok(new { message = "Deleted" });
        }
        catch (Exception ex)
        {
            return BadRequest($"Ошибка при удалении: {ex.Message}");
        }
    }
}