using Microsoft.AspNetCore.Mvc;
using QuizzBackend.Services;

namespace QuizzBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameController : ControllerBase
{
    private readonly GameService _gameService;
    private readonly ILogger<GameController> _logger;

    public GameController(GameService gameService, ILogger<GameController> logger)
    {
        _gameService = gameService;
        _logger = logger;
    }

    // ── Vérifier si une room existe ──────────────────────────
    // Utile pour Flutter avant de tenter de rejoindre via SignalR

    [HttpGet("{code}")]
    public IActionResult GetRoom(string code)
    {
        var room = _gameService.GetRoom(code.ToUpper());

        if (room is null)
            return NotFound(new { message = "Room introuvable." });

        return Ok(new
        {
            code = room.Code,
            status = room.Status.ToString(),
            playerCount = room.Players.Count,
            players = room.Players.Select(p => new
            {
                pseudo = p.Pseudo,
                score = p.Score,
                role = p.Role.ToString()
            })
        });
    }
}