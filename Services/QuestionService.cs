using Microsoft.EntityFrameworkCore;
using QuizzBackend.Data;
using QuizzBackend.Models;

namespace QuizzBackend.Services;

public class QuestionService
{
    private readonly AppDbContext _db;
    private readonly ILogger<QuestionService> _logger;

    public QuestionService(AppDbContext db, ILogger<QuestionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Récupérer N questions aléatoires pour une partie ────

    public async Task<List<Question>> GetQuestionsForGame(
        string? theme = null,
        int? difficulty = null,
        int count = 10)
    {
        var query = _db.Questions.AsQueryable();

        if (!string.IsNullOrEmpty(theme))
            query = query.Where(q => q.Theme == theme);

        if (difficulty.HasValue)
            query = query.Where(q => q.Difficulty == difficulty.Value);

        // SQLite supporte ORDER BY RANDOM() via EF Core
        var questions = await query
            .OrderBy(_ => EF.Functions.Random())
            .Take(count)
            .ToListAsync();

        _logger.LogInformation(
            "{Count} questions chargées (thème: {Theme}, difficulté: {Difficulty})",
            questions.Count, theme ?? "tous", difficulty?.ToString() ?? "tous");

        return questions;
    }

    // ── Récupérer les thèmes disponibles ────────────────────

    public async Task<List<string>> GetAvailableThemes()
    {
        return await _db.Questions
            .Select(q => q.Theme)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();
    }

    // ── Récupérer une question par son ExternalId ────────────

    public async Task<Question?> GetByExternalId(string externalId)
    {
        return await _db.Questions
            .FirstOrDefaultAsync(q => q.ExternalId == externalId);
    }

    // ── Stats rapides pour debug ─────────────────────────────

    public async Task<Dictionary<string, int>> GetQuestionCountByTheme()
    {
        return await _db.Questions
            .GroupBy(q => q.Theme)
            .Select(g => new { Theme = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Theme, x => x.Count);
    }
    public async Task<List<Question>> GetQuestionsByTheme(string? theme)
    {
        var query = _db.Questions.AsQueryable();

        if (!string.IsNullOrEmpty(theme))
            query = query.Where(q => q.Theme == theme);

        return await query.ToListAsync();
    }
}