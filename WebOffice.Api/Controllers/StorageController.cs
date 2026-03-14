using Microsoft.AspNetCore.Mvc;
using Minio;
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
    // 2. Добавили запрос userId
    public async Task<IActionResult> UploadFile(IFormFile file, [FromQuery] string userId) 
    {
        if (file == null || file.Length == 0) return BadRequest("Файл не выбран.");
        if (string.IsNullOrEmpty(userId)) return BadRequest("Не указан ID пользователя.");

        try
        {
            using var stream = file.OpenReadStream();
            
            // 3. Формируем путь: папка_юзера/имя_файла
            string objectName = $"{userId}/{file.FileName}";

            var putObjectArgs = new PutObjectArgs()
                .WithBucket("documents")
                .WithObject(objectName) 
                .WithStreamData(stream)
                .WithObjectSize(file.Length)
                .WithContentType(file.ContentType);

            await _minioClient.PutObjectAsync(putObjectArgs);

            return Ok($"Файл '{file.FileName}' успешно загружен для пользователя '{userId}'.");
        }
        catch (Exception ex)
        {
            return BadRequest($"Ошибка при загрузке: {ex.Message}");
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
}