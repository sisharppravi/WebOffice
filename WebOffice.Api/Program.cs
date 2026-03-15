using bsckend.Repository;
using bsckend.Models.User;
using bsckend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Minio;

var builder = WebApplication.CreateBuilder(args);

// Настройка базы данных (SQLite)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=users.db"));

// Identity + EF хранилище
builder.Services
    .AddIdentity<UserModel, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// CORS — для разработки разрешаем всё (в продакшене нужно сузить!)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// JWT настройка
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey))
    throw new InvalidOperationException("JWT Key is not configured in appsettings.json or secrets");

var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidIssuer              = builder.Configuration["Jwt:Issuer"],
        ValidateAudience         = true,
        ValidAudience            = builder.Configuration["Jwt:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey         = new SymmetricSecurityKey(keyBytes),
        ValidateLifetime         = true,
        ClockSkew                = TimeSpan.FromMinutes(5)   // чуть увеличил, 2 мин иногда слишком строго
    };
});

builder.Services.AddScoped<JwtService>();

// Контроллеры + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// MinIO клиент (S3-совместимое хранилище)
builder.Services.AddMinio(configureSource => configureSource
    .WithEndpoint("localhost:9000")
    .WithCredentials("admin", "admin123")
    .WithSSL(false));

var app = builder.Build();

// Можно закомментировать после первого запуска
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await DatabaseInitializer.InitializeAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/initdb", async (IServiceProvider sp) =>
    {
        using var scope = sp.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            await dbContext.Database.MigrateAsync();

            return Results.Ok(new { message = "База данных успешно инициализирована / миграции применены" });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Ошибка инициализации базы данных"
            );
        }
    })
    .WithName("InitDatabase")
    .WithOpenApi();     // будет виден в Swagger
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();