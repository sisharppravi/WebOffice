using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WebOffice.Client;
using WebOffice.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ────────────────────────────────────────────────
// Главное исправление: теперь HttpClient работает через nginx
// Все запросы к API идут относительно текущего домена → http://localhost/api/...
// Никакого жёсткого https://localhost:7130 больше не нужно
// ────────────────────────────────────────────────
builder.Services.AddScoped(sp => new HttpClient 
{ 
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)   // ← это идеальный вариант для Blazor WASM
    // Альтернатива (если хочешь явно): new Uri("http://localhost/")
    // Но BaseAddress из HostEnvironment — надёжнее, особенно если потом будет https или другой домен
});

// Если в будущем захочешь использовать именованный клиент или IHttpClientFactory — можно переписать так:
// builder.Services.AddHttpClient("ApiClient", client => 
// {
//     client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
// });

builder.Services.AddScoped<AuthService>();

await builder.Build().RunAsync();