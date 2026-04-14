using Microsoft.EntityFrameworkCore;
using QuizzBackend.Data;
using QuizzBackend.Hubs;
using QuizzBackend.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Base de données ──────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── SignalR ──────────────────────────────────────────────────
builder.Services.AddSignalR();

// ── Services métier ──────────────────────────────────────────
builder.Services.AddSingleton<GameService>();
builder.Services.AddSingleton<RoundManager>();
builder.Services.AddSingleton<AnswerValidator>();
builder.Services.AddScoped<QuestionService>();

// ── Cache mémoire ────────────────────────────────────────────
builder.Services.AddMemoryCache();

// ── Seed au démarrage ────────────────────────────────────────
//builder.Services.AddHostedService<SeedService>();

// ── Swagger ──────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── Controllers + CORS ───────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

// ── Migrations automatiques au démarrage ─────────────────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        // 1. Migration
        db.Database.Migrate();
        var dbPath = db.Database.GetDbConnection().DataSource;
        logger.LogInformation("Base SQLite utilisée : {Path}", dbPath);

        // 2. Seed — même connexion, même scope
        var questionsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Questions");
        await SeedService.SeedAsync(db, logger, questionsPath);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogCritical(ex, "Échec au démarrage — arrêt du serveur");
        throw;
    }
}
// ── Middleware ───────────────────────────────────────────────
app.UseStaticFiles();
app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseStaticFiles();
app.MapControllers();
app.MapHub<GameHub>("/gamehub");

app.Run();