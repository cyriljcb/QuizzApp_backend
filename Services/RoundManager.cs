using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using QuizzBackend.Hubs;
using QuizzBackend.Models;

namespace QuizzBackend.Services;

public class RoundManager
{
    private readonly GameService _gameService;
    private readonly AnswerValidator _answerValidator;
    private readonly IHubContext<GameHub> _hubContext;
    private readonly IConfiguration _config;
    private readonly ILogger<RoundManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _roundTimers = new();
    private readonly ConcurrentDictionary<string, bool> _suddenDeathRooms = new();

    public RoundManager(
        GameService gameService,
        AnswerValidator answerValidator,
        IHubContext<GameHub> hubContext,
        IConfiguration config,
        ILogger<RoundManager> logger,
        IServiceScopeFactory scopeFactory)
    {
        _gameService = gameService;
        _answerValidator = answerValidator;
        _hubContext = hubContext;
        _config = config;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // ── Démarrer une partie ──────────────────────────────────

    public async Task StartGame(Room room, List<Question> questions)
    {
        room.Questions = questions;
        room.CurrentQuestionIndex = 0;
        await StartRound(room);
    }

    // ── Démarrer un round ────────────────────────────────────

    public async Task StartRound(Room room)
    {
        room.IsRoundEnding = false;

        if (room.Status != RoomStatus.Playing)
        {
            _logger.LogInformation(
                "Room {Code} — round annulé (statut: {Status})",
                room.Code, room.Status);
            return;
        }

        if (room.CurrentQuestionIndex >= room.Questions.Count)
        {
            await EndGame(room);
            return;
        }

        _gameService.ResetPlayerAnswers(room.Code);

        var question = room.Questions[room.CurrentQuestionIndex];
        var duration = _config.GetValue<int>("GameSettings:RoundDurationSeconds");

        var payload = new NewQuestionPayload
        {
            RoundNumber     = room.CurrentQuestionIndex + 1,
            TotalRounds     = room.Questions.Count,
            QuestionText    = question.QuestionText,
            ImageUrl        = question.ImageUrl,
            Type            = question.Type,
            DurationSeconds = duration
        };

        _logger.LogInformation(
            "Room {Code} — Round {Round}/{Total}",
            room.Code, payload.RoundNumber, payload.TotalRounds);

        await _hubContext.Clients
            .Group(room.Code)
            .SendAsync("NewQuestion", payload);

        StartTimer(room, duration, () => EndRound(room));
    }

    // ── Soumettre une réponse ────────────────────────────────

    public async Task SubmitAnswer(Room room, Player player, string answer)
    {
        if (player.HasAnswered) return;

        var question = room.Questions[room.CurrentQuestionIndex];
        var isCorrect = _answerValidator.IsCorrect(answer, question);

        player.CurrentAnswer = answer;
        player.HasAnswered   = true;
        player.IsCorrect     = isCorrect;

        if (isCorrect)
        {
            // En sudden death, premier correct gagne immédiatement
            if (_suddenDeathRooms.ContainsKey(room.Code))
            {
                player.Score += 100;
                CancelTimer(room.Code);
                await EndSuddenDeath(room, winner: player);
                return;
            }

            player.Score += 100;
        }

        // Round normal — si tout le monde a répondu
        var activePlayers = room.Players
            .Where(p => p.Role != PlayerRole.Spectator)
            .ToList();

        if (activePlayers.All(p => p.HasAnswered))
        {
            CancelTimer(room.Code);
            await EndRound(room);
        }
    }

    // ── Fin d'un round ───────────────────────────────────────

    private async Task EndRound(Room room)
    {
        lock (room)
        {
            if (room.IsRoundEnding) return;
            room.IsRoundEnding = true;
        }

        if (room.Status != RoomStatus.Playing) return;

        var question = room.Questions[room.CurrentQuestionIndex];
        var isLastRound = room.CurrentQuestionIndex + 1 >= room.Questions.Count;

        var result = new RoundResult
        {
            CorrectAnswer = question.Answer,
            Explanation   = question.Explanation,
            IsLastRound   = isLastRound,
            PlayerResults = room.Players
                .Where(p => p.Role == PlayerRole.Player)
                .OrderByDescending(p => p.Score)
                .Select(p => new PlayerResult
                {
                    Pseudo      = p.Pseudo,
                    IsCorrect   = p.IsCorrect,
                    Score       = p.Score,
                    ScoreGained = p.IsCorrect ? 100 : 0
                })
                .ToList()
        };

        await _hubContext.Clients
            .Group(room.Code)
            .SendAsync("RoundResult", result);

        room.CurrentQuestionIndex++;

        var delay = _config.GetValue<int>("GameSettings:ResultDisplaySeconds");
        await Task.Delay(TimeSpan.FromSeconds(delay));

        await StartRound(room);
    }

    // ── Fin de partie → détection égalité ───────────────────

    private async Task EndGame(Room room)
    {
        var players = room.Players
            .Where(p => p.Role == PlayerRole.Player)  // ✅ déjà présen
            .ToList();

        var topScore = players.First().Score;
        var topPlayers = players.Where(p => p.Score == topScore).ToList();

        if (topPlayers.Count > 1)
        {
            await StartSuddenDeath(room, topPlayers);
            return;
        }

        await SendGameOver(room, players);
    }

    // ── Sudden Death ─────────────────────────────────────────

    private async Task StartSuddenDeath(Room room, List<Player> tiedPlayers)
    {
        _suddenDeathRooms[room.Code] = true;
        _gameService.ResetPlayerAnswers(room.Code);

        var tiedPseudos = tiedPlayers.Select(p => p.Pseudo).ToList();

        _logger.LogInformation(
            "Room {Code} — Sudden Death entre {Players}",
            room.Code, string.Join(", ", tiedPseudos));

        // Notifie tous les joueurs
        await _hubContext.Clients
            .Group(room.Code)
            .SendAsync("SuddenDeath", new { tiedPlayers = tiedPseudos });

        // Récupère une question bonus aléatoire
        var suddenDeathQuestion = await GetSuddenDeathQuestion(room);

        if (suddenDeathQuestion is null)
        {
            _logger.LogWarning(
                "Room {Code} — Aucune question disponible pour le sudden death",
                room.Code);
            await SendGameOver(room, room.Players
                .Where(p => p.Role == PlayerRole.Player)
                .OrderByDescending(p => p.Score)
                .ToList());
            return;
        }

        // Ajoute la question et met à jour l'index
        room.Questions.Add(suddenDeathQuestion);

        var duration = _config.GetValue<int>("GameSettings:RoundDurationSeconds");

        var payload = new NewQuestionPayload
        {
            RoundNumber     = room.Questions.Count,
            TotalRounds     = room.Questions.Count,
            QuestionText    = suddenDeathQuestion.QuestionText,
            ImageUrl        = suddenDeathQuestion.ImageUrl,
            Type            = suddenDeathQuestion.Type,
            DurationSeconds = duration
        };

        await _hubContext.Clients
            .Group(room.Code)
            .SendAsync("NewQuestion", payload);

        room.CurrentQuestionIndex = room.Questions.Count - 1;
        room.IsRoundEnding = false;

        // Timer — si personne ne répond correctement, GameOver avec égalité
        StartTimer(room, duration, async () =>
        {
            _suddenDeathRooms.TryRemove(room.Code, out _);
            await SendGameOver(room, room.Players
                .Where(p => p.Role == PlayerRole.Player)
                .OrderByDescending(p => p.Score)
                .ToList());
        });
    }

    private async Task EndSuddenDeath(Room room, Player winner)
    {
        _suddenDeathRooms.TryRemove(room.Code, out _);

        var question = room.Questions[room.CurrentQuestionIndex];

        // Envoie le résultat du round sudden death
        var result = new RoundResult
        {
            CorrectAnswer = question.Answer,
            Explanation   = question.Explanation,
            IsLastRound   = true,
            PlayerResults = room.Players
                .Where(p => p.Role == PlayerRole.Player)
                .OrderByDescending(p => p.Score)
                .Select(p => new PlayerResult
                {
                    Pseudo      = p.Pseudo,
                    IsCorrect   = p.Pseudo == winner.Pseudo,
                    Score       = p.Score,
                    ScoreGained = p.Pseudo == winner.Pseudo ? 100 : 0
                })
                .ToList()
        };

        await _hubContext.Clients
            .Group(room.Code)
            .SendAsync("RoundResult", result);

        var delay = _config.GetValue<int>("GameSettings:ResultDisplaySeconds");
        await Task.Delay(TimeSpan.FromSeconds(delay));

        await SendGameOver(room, room.Players
            .Where(p => p.Role == PlayerRole.Player)
            .OrderByDescending(p => p.Score)
            .ToList());
    }

    private async Task SendGameOver(Room room, List<Player> players)
    {
        room.Status = RoomStatus.Finished;

        var finalScores = players
            .Select(p => new PlayerResult
            {
                Pseudo      = p.Pseudo,
                Score       = p.Score,
                IsCorrect   = false,
                ScoreGained = 0
            })
            .ToList();

        await _hubContext.Clients
            .Group(room.Code)
            .SendAsync("GameOver", finalScores);

        _logger.LogInformation("Partie terminée — Room {Code}", room.Code);
    }

    private async Task<Question?> GetSuddenDeathQuestion(Room room)
    {
        var usedIds = room.Questions.Select(q => q.Id).ToHashSet();
        var theme = room.Questions.FirstOrDefault()?.Theme;

        using var scope = _scopeFactory.CreateScope();
        var questionService = scope.ServiceProvider.GetRequiredService<QuestionService>();
        var allQuestions = await questionService.GetQuestionsByTheme(theme);

        var available = allQuestions
            .Where(q => !usedIds.Contains(q.Id))
            .ToList();

        if (available.Count == 0) return null;

        var rng = new Random();
        return available[rng.Next(available.Count)];
    }

    // ── Timer helper ─────────────────────────────────────────

    private void StartTimer(Room room, int durationSeconds, Func<Task> onExpired)
    {
        CancelTimer(room.Code);

        var cts = new CancellationTokenSource();
        _roundTimers[room.Code] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(durationSeconds), cts.Token);
                await onExpired();
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation(
                    "Timer annulé pour room {Code}", room.Code);
            }
        }, cts.Token);
    }

    private void CancelTimer(String roomCode)
    {
        if (_roundTimers.TryRemove(roomCode, out var cts))
            cts.Cancel();
    }

    public void CancelRoom(string roomCode)
    {
        CancelTimer(roomCode);
        _suddenDeathRooms.TryRemove(roomCode, out _);
        _logger.LogInformation("Room {Code} — timer annulé", roomCode);
    }
}
