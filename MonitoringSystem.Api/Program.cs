using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using MonitoringSystem.Api.Data;
using MonitoringSystem.Api.Hubs;
using MonitoringSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ── MongoDB ───────────────────────────────────────────────────────────────────
// Singleton — один екземпляр MongoDbContext на весь час роботи сервера.
// MongoClient всередині є thread-safe і connection-pooled за замовчуванням.
builder.Services.AddSingleton<MongoDbContext>();

// ── Бізнес-логіка ─────────────────────────────────────────────────────────────
// Scoped — новий екземпляр на кожен HTTP-запит (оптимально для stateless сервісів)
builder.Services.AddScoped<IRulesService, RulesService>();

// ── JWT аутентифікація ────────────────────────────────────────────────────────
// JWT (JSON Web Token) — stateless токен. Сервер не зберігає сесії,
// а лише перевіряє підпис токена за допомогою симетричного ключа (HMAC-SHA256).
// Токен містить: UserId, Username, Role — зашифровані і підписані.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,   // перевіряємо хто видав токен
            ValidateAudience         = true,   // перевіряємо для кого токен
            ValidateLifetime         = true,   // перевіряємо термін дії
            ValidateIssuerSigningKey = true,   // перевіряємо підпис
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };

        // SignalR передає токен через query string ?access_token=...
        // (WebSocket не підтримує Authorization header),
        // тому тут ручно витягуємо його і кладемо в контекст
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                var path  = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── SignalR — real-time WebSocket ─────────────────────────────────────────────
// SignalR автоматично обирає транспорт: WebSocket → SSE → Long Polling.
// На Windows з .NET завжди використовується WebSocket (найшвидший).
builder.Services.AddSignalR();

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.PropertyNamingPolicy = null); // зберігаємо PascalCase в JSON

// ── CORS ──────────────────────────────────────────────────────────────────────
// Дозволяємо запити з будь-якого origin (для розробки).
// У продакшні треба обмежити до конкретних доменів.
// CORS: AllowAnyOrigin + AllowCredentials не сумісні — для SignalR WebSocket
// потрібен AllowCredentials, тому вказуємо конкретні origins або SetIsOriginAllowed
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true)   // дозволяємо будь-який origin (включно з Radmin VPN)
     .AllowAnyMethod()
     .AllowAnyHeader()
     .AllowCredentials()));            // потрібно для SignalR WebSocket

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
// Порядок middleware критично важливий: CORS → Auth → Authorization → Controllers
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ActivityHub>("/hubs/activity");

// ── Seed тестових даних ───────────────────────────────────────────────────────
// Виконується один раз при першому запуску (якщо колекція users порожня).
// MongoDbContext ініціалізується тут — саме тут відбувається підключення до БД.
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("  🚀 Запуск MonitoringSystem API...");
Console.ResetColor();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
    await MongoSeeder.SeedAsync(db);
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("  ✅ API готовий до роботи на http://localhost:5000");
Console.WriteLine("  📡 SignalR Hub: http://localhost:5000/hubs/activity");
Console.ResetColor();
Console.WriteLine();

app.Run();
