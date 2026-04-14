using Microsoft.AspNetCore.Mvc;
using QuizzBackend.Services;

namespace QuizzBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuestionController : ControllerBase
{
    private readonly QuestionService _questionService;
    private readonly ILogger<QuestionController> _logger;

    public QuestionController(QuestionService questionService, ILogger<QuestionController> logger)
    {
        _questionService = questionService;
        _logger = logger;
    }

    // ── Thèmes disponibles ───────────────────────────────────
    // Flutter l'utilise pour afficher les choix avant de lancer une partie

    [HttpGet("themes")]
    public async Task<IActionResult> GetThemes()
    {
        var themes = await _questionService.GetAvailableThemes();
        return Ok(themes);
    }

    // ── Stats par thème ──────────────────────────────────────
    // Utile pour debug et pour la page maître du jeu

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var stats = await _questionService.GetQuestionCountByTheme();
        return Ok(stats);
    }
}