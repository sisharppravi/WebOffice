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

// Force listening URL to 7130 in development if not overridden
builder.WebHost.UseUrls("http://localhost:7130");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=users.db"));

builder.Services
    .AddIdentity<UserModel, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// CORS - разрешаем запросы с клиентского приложения (для разработки)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// JWT Configuration
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey))
    throw new InvalidOperationException("JWT Key is not configured in appsettings.json");

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
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(2)
    };
});

builder.Services.AddScoped<JwtService>();

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMinio(configureSource => configureSource
    .WithEndpoint("localhost:9000")
    .WithCredentials("admin", "admin123") // Твой новый пароль из .env
    .WithSSL(false));

var app = builder.Build();

// Применить миграции при запуске
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await DatabaseInitializer.InitializeAsync(db);

    // Проверка и создание бакета documents в MinIO при старте
    try
    {
        var minio = scope.ServiceProvider.GetRequiredService<Minio.IMinioClient>();
        var bucketName = scope.ServiceProvider.GetRequiredService<IConfiguration>()["Minio:Bucket"] ?? "documents";
        try
        {
            var existsArgs = new Minio.DataModel.Args.BucketExistsArgs().WithBucket(bucketName);
            var exists = await minio.BucketExistsAsync(existsArgs);
            if (!exists)
            {
                var makeArgs = new Minio.DataModel.Args.MakeBucketArgs().WithBucket(bucketName);
                await minio.MakeBucketAsync(makeArgs);
                Console.WriteLine($"MinIO: created bucket '{bucketName}' on startup.");
            }
            else
            {
                Console.WriteLine($"MinIO: bucket '{bucketName}' already exists.");
            }
        }
        catch (Exception mex)
        {
            Console.WriteLine($"MinIO startup check failed: {mex}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"MinIO client not available at startup: {ex.Message}");
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseRouting();
// Включаем CORS (используем политику AllowAll)
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();