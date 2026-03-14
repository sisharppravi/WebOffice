using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.JSInterop;

namespace WebOffice.Client.Services;

public class DocumentService
{
    private readonly HttpClient _http;
    private readonly AuthService _auth;
    private readonly IJSRuntime _js;
    private const string ApiBase = "http://localhost:7130/";

    public DocumentService(HttpClient http, AuthService auth, IJSRuntime js)
    {
        _http = http;
        _auth = auth;
        _js = js;
    }

    private async Task AddAuthHeaderIfPresent()
    {
        try
        {
            var token = await _js.InvokeAsync<string>("localStorage.getItem", "authToken");
            if (!string.IsNullOrEmpty(token))
            {
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                _http.DefaultRequestHeaders.Authorization = null;
            }
        }
        catch
        {
            // ignore
        }
    }

    public async Task<(int StatusCode, string Body, List<string>? Files)> GetUserFilesAsync()
    {
        await AddAuthHeaderIfPresent();
        var user = await _auth.GetCurrentUserAsync();
        if (string.IsNullOrEmpty(user)) return (401, "No user", null);

        var resp = await _http.GetAsync($"{ApiBase}api/storage/list?userId={Uri.EscapeDataString(user)}");
        var body = await resp.Content.ReadAsStringAsync();
        List<string>? list = null;
        try
        {
            list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(body);
        }
        catch { }
        return ((int)resp.StatusCode, body, list);
    }

    public async Task<(int StatusCode, string Body)> DeleteFileAsync(string fileName)
    {
        await AddAuthHeaderIfPresent();
        var user = await _auth.GetCurrentUserAsync();
        if (string.IsNullOrEmpty(user)) return (401, "No user");

        var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiBase}api/storage/delete?userId={Uri.EscapeDataString(user)}&fileName={Uri.EscapeDataString(fileName)}");
        var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        return ((int)resp.StatusCode, body);
    }

    public async Task<(int StatusCode, string Body)> UploadFileAsync(Stream stream, string fileName, string contentType, long? fileSize = null)
    {
        await AddAuthHeaderIfPresent();
        var user = await _auth.GetCurrentUserAsync();
        if (string.IsNullOrEmpty(user)) return (401, "No user");

        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(stream);
        if (!string.IsNullOrEmpty(contentType))
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        // Let MultipartFormDataContent set Content-Disposition and length
        content.Add(fileContent, "file", fileName);
        // Also add userId as a form field (fallback for server that reads form fields)
        content.Add(new StringContent(user), "userId");

        var resp = await _http.PostAsync($"{ApiBase}api/storage/upload?userId={Uri.EscapeDataString(user)}", content);
        var body = await resp.Content.ReadAsStringAsync();
        return ((int)resp.StatusCode, body);
    }

    public async Task<(int StatusCode, byte[]? Data)> DownloadFileAsync(string fileName)
    {
        await AddAuthHeaderIfPresent();
        var user = await _auth.GetCurrentUserAsync();
        if (string.IsNullOrEmpty(user)) return (401, null);

        var resp = await _http.GetAsync($"{ApiBase}api/storage/download/{Uri.EscapeDataString(fileName)}?userId={Uri.EscapeDataString(user)}");
        if (!resp.IsSuccessStatusCode) return ((int)resp.StatusCode, null);
        var data = await resp.Content.ReadAsByteArrayAsync();
        return ((int)resp.StatusCode, data);
    }
}
