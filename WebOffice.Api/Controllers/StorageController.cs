using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.ApiEndpoints;
using Minio.DataModel.Args;

namespace bsckend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StorageController : ControllerBase
{
    private const string BucketName = "documents";

    private readonly IMinioClient _minioClient;
    private readonly ILogger<StorageController> _logger;

    public StorageController(IMinioClient minioClient, ILogger<StorageController> logger)
    {
        _minioClient = minioClient;
        _logger = logger;
    }

    [HttpPost("init")]
    public async Task<IActionResult> InitializeStorage()
    {
        try
        {
            _logger.LogInformation("Initializing storage bucket: {Bucket}", BucketName);

            var existsArgs = new BucketExistsArgs().WithBucket(BucketName);
            bool found = await _minioClient.BucketExistsAsync(existsArgs);

            if (!found)
            {
                var makeArgs = new MakeBucketArgs().WithBucket(BucketName);
                await _minioClient.MakeBucketAsync(makeArgs);

                _logger.LogInformation("Bucket {Bucket} created successfully", BucketName);
                return Ok($"Бакет '{BucketName}' успешно создан.");
            }

            _logger.LogInformation("Bucket {Bucket} already exists", BucketName);
            return Ok($"Бакет '{BucketName}' уже существует.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing bucket {Bucket}", BucketName);
            return BadRequest($"Ошибка при связи с MinIO: {ex.Message}");
        }
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile file, [FromQuery] string userId)
    {
        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("Upload attempt with empty file");
            return BadRequest("Файл не выбран.");
        }

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Upload attempt without userId");
            return BadRequest("Не указан ID пользователя.");
        }

        try
        {
            string objectName = $"{userId}/{file.FileName}";
            _logger.LogInformation("Uploading file {File} for user {User}", file.FileName, userId);

            using var stream = file.OpenReadStream();

            var putObjectArgs = new PutObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectName)
                .WithStreamData(stream)
                .WithObjectSize(file.Length)
                .WithContentType(file.ContentType);

            await _minioClient.PutObjectAsync(putObjectArgs);

            _logger.LogInformation("File {File} uploaded successfully for user {User}", file.FileName, userId);

            return Ok($"Файл '{file.FileName}' успешно загружен.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {File} for user {User}", file.FileName, userId);
            return BadRequest($"Ошибка при загрузке: {ex.Message}");
        }
    }

    [HttpGet("download/{fileName}")]
    public async Task<IActionResult> DownloadFile(string fileName, [FromQuery] string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Download attempt without userId");
            return BadRequest("Не указан ID пользователя.");
        }

        try
        {
            string objectName = $"{userId}/{fileName}";
            _logger.LogInformation("Downloading file {File} for user {User}", fileName, userId);

            var memoryStream = new MemoryStream();

            var getObjectArgs = new GetObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectName)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream));

            await _minioClient.GetObjectAsync(getObjectArgs);

            memoryStream.Position = 0;

            _logger.LogInformation("File {File} downloaded successfully for user {User}", fileName, userId);

            return File(memoryStream, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {File} for user {User}", fileName, userId);
            return BadRequest($"Ошибка при скачивании: {ex.Message}");
        }
    }

    [HttpPost("callback")]
    public async Task<IActionResult> Callback(
        [FromBody] System.Text.Json.JsonElement data,
        [FromQuery] string fileName,
        [FromQuery] string userId)
    {
        try
        {
            int status = data.GetProperty("status").GetInt32();

            _logger.LogInformation("OnlyOffice callback received. Status: {Status}, File: {File}", status, fileName);

            if (status == 2)
            {
                string downloadUrl = data.GetProperty("url").GetString();
                string objectName = $"{userId}/{fileName}";

                using var httpClient = new HttpClient();

                var response = await httpClient.GetAsync(downloadUrl);
                using var stream = await response.Content.ReadAsStreamAsync();

                var putObjectArgs = new PutObjectArgs()
                    .WithBucket(BucketName)
                    .WithObject(objectName)
                    .WithStreamData(stream)
                    .WithObjectSize(response.Content.Headers.ContentLength ?? 0)
                    .WithContentType("application/octet-stream");

                await _minioClient.PutObjectAsync(putObjectArgs);

                _logger.LogInformation("File {File} updated in MinIO after OnlyOffice edit", fileName);
            }

            return Ok(new { error = 0 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OnlyOffice callback for file {File}", fileName);
            return BadRequest();
        }
    }

    [HttpGet("editor-config")]
    public async Task<IActionResult> GetEditorConfig(string fileName, string userId)
    {
        try
        {
            string objectName = $"{userId}/{fileName}";

            _logger.LogInformation("Generating OnlyOffice config for file {File} user {User}", fileName, userId);

            var url = await _minioClient.PresignedGetObjectAsync(
                new PresignedGetObjectArgs()
                    .WithBucket(BucketName)
                    .WithObject(objectName)
                    .WithExpiry(60 * 60)
            );

            var config = new
            {
                document = new
                {
                    fileType = "docx",
                    key = $"{userId}-{fileName}",
                    title = fileName,
                    url = url
                },
                editorConfig = new
                {
                    callbackUrl =
                        $"http://localhost:5000/api/storage/callback?fileName={fileName}&userId={userId}"
                }
            };

            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating editor config for file {File}", fileName);
            return BadRequest();
        }
    }

    [HttpGet("list")]
    public async Task<IActionResult> List(string userId)
    {
        try
        {
            _logger.LogInformation("Listing files for user {User}", userId);

            var objects = new List<string>();

            var args = new ListObjectsArgs()
                .WithBucket(BucketName)
                .WithPrefix($"{userId}/")
                .WithRecursive(true);

            var observable = _minioClient.ListObjectsAsync(args);

            var completion = new TaskCompletionSource<bool>();

            observable.Subscribe(
                item => objects.Add(item.Key.Replace($"{userId}/", "")),
                ex => completion.SetException(ex),
                () => completion.SetResult(true)
            );

            await completion.Task;

            return Ok(objects);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files for user {User}", userId);
            return BadRequest();
        }
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(string userId, string fileName)
    {
        try
        {
            _logger.LogInformation("Creating document {File} for user {User}", fileName, userId);

            string objectName = $"{userId}/{fileName}";

            using var stream = new MemoryStream(new byte[0]);

            var args = new PutObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectName)
                .WithStreamData(stream)
                .WithObjectSize(stream.Length)
                .WithContentType(
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

            await _minioClient.PutObjectAsync(args);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating document {File} for user {User}", fileName, userId);
            return BadRequest();
        }
    }

    [HttpDelete("delete")]
    public async Task<IActionResult> Delete(string fileName, [FromQuery] string userId)
    {
        if (string.IsNullOrEmpty(userId)) return BadRequest("Не указан ID пользователя.");

        try
        {
            string objectName = $"{userId}/{fileName}";

            var removeArgs = new RemoveObjectArgs()
                .WithBucket("documents")
                .WithObject(objectName);

            await _minioClient.RemoveObjectAsync(removeArgs);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest($"Ошибка при удалении: {ex.Message}");
        }
    }
}