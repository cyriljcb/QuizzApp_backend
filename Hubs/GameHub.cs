using Microsoft.AspNetCore.SignalR;
using QuizzBackend.Models;
using QuizzBackend.Services;

namespace QuizzBackend.Hubs;

public class GameHub : Hub
{
    private readonly GameService _gameService;
    private readonly RoundManager _roundManager;
    private readonly QuestionService _questionService;
    private readonly IConfiguration _config;
    private readonly ILogger<GameHub> _logger;

    public GameHub(
        GameService gameService,
        RoundManager roundManager,
        QuestionService questionService,
        IConfiguration config,
        ILogger<GameHub> logger)
    {
        _gameService = gameService;
        _roundManager = roundManager;
        _questionService = questionService;
        _config = config;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════
    // CONNEXION / DÉCONNEXION
    // ══════════════════════════════════════════════════════

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var room = _gameService.GetRoomByConnectionId(Context.ConnectionId);

        if (room is not null)
        {
            var player = room.Players
                .FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);

            if (player is not null)
            {
                if (room.HostConnectionId == Context.ConnectionId)
                {
                    _roundManager.CancelRoom(room.Code);
                    room.Status = RoomStatus.Finished;
                    _gameService.RemoveRoom(room.Code);

                    await Clients.Group(room.Code)
                        .SendAsync("HostLeft",
                            "L'écran maître s'est déconnecté, la partie est annulée.");
                }
                else if (room.Status == RoomStatus.Playing)
                {
                    await Clients.Group(room.Code)
                        .SendAsync("PlayerLeft", player.Pseudo);
                }
                else
                {
                    _gameService.RemovePlayer(Context.ConnectionId);

                    await Clients.Group(room.Code)
                        .SendAsync("PlayerLeft", player.Pseudo);
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ══════════════════════════════════════════════════════
    // GESTION DE LA ROOM
    // ══════════════════════════════════════════════════════
    public async Task JoinAsSpectator(string roomCode)
    {
        try
        {
            var (success, error, room) = _gameService.JoinAsSpectator(
                roomCode, Context.ConnectionId);

            if (!success || room is null)
            {
                await SendError(error ?? "Impossible de rejoindre comme spectateur.");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

            // On confirme la connexion et on envoie l'état actuel
            await Clients.Caller.SendAsync("SpectatorJoined", new
            {
                roomCode = room.Code,
                status = room.Status.ToString(),
                players = room.Players
                    .Where(p => p.Role != PlayerRole.Spectator)
                    .Select(p => p.Pseudo)
                    .ToList()
            });

            _logger.LogInformation(
                "Écran maître connecté à la room {Code}", roomCode);
        }
        catch (Exception ex)
        {
            await SendError("Impossible de rejoindre la room.");
            _logger.LogError(ex, "Erreur JoinAsSpectator");
        }
    }
    public async Task CreateRoom(string pseudo)
    {
        try
        {
            var room = _gameService.CreateRoom(Context.ConnectionId, pseudo);

            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

            await Clients.Caller.SendAsync("RoomCreated", new
            {
                roomCode = room.Code,
                pseudo
            });

            await Clients.Caller.SendAsync("RoomJoined", new
            {
                roomCode = room.Code,
                pseudo,
                players = room.Players
                    .Where(p => p.Role != PlayerRole.Spectator)
                    .Select(p => p.Pseudo)
                    .ToList()
            });

            _logger.LogInformation(
                "Room {Code} créée par {Pseudo}", room.Code, pseudo);
        }
        catch (Exception ex)
        {
            await SendError("Impossible de créer la room.");
            _logger.LogError(ex, "Erreur CreateRoom");
        }
    }

    public async Task JoinRoom(string roomCode, string pseudo)
    {
        try
        {
            var (success, error, room) = _gameService.JoinRoom(
                roomCode, Context.ConnectionId, pseudo);

            if (!success || room is null)
            {
                await SendError(error ?? "Impossible de rejoindre la room.");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

            await Clients.Caller.SendAsync("RoomJoined", new
            {
                roomCode = room.Code,
                pseudo,
                players = room.Players
                    .Where(p => p.Role != PlayerRole.Spectator)
                    .Select(p => p.Pseudo)
                    .ToList()
            });

            await Clients.OthersInGroup(room.Code)
                .SendAsync("PlayerJoined", pseudo);

            _logger.LogInformation(
                "{Pseudo} a rejoint la room {Code}", pseudo, roomCode);
        }
        catch (Exception ex)
        {
            await SendError("Impossible de rejoindre la room.");
            _logger.LogError(ex, "Erreur JoinRoom");
        }
    }
    public async Task SubmitAnswer(string roomCode, string answer)
    {
        try
        {
            var room = _gameService.GetRoom(roomCode);

            if (room is null || room.Status != RoomStatus.Playing)
                return;

            var player = room.Players
                .FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);

            // Seuls les joueurs (Player) soumettent des réponses
            // Host Angular (Role == Host) exclus
            if (player is null || player.Role == PlayerRole.Spectator)
                return;

            if (player.HasAnswered)
                return;

            await _roundManager.SubmitAnswer(room, player, answer);

            await Clients.Caller.SendAsync("AnswerReceived", new { answer });

            await Clients.OthersInGroup(roomCode)
                .SendAsync("PlayerAnswered", player.Pseudo);
        }
        catch (Exception ex)
        {
            await SendError("Impossible d'envoyer la réponse.");
            _logger.LogError(ex, "Erreur SubmitAnswer");
        }
    }

    // ══════════════════════════════════════════════════════
    // DÉROULEMENT DE LA PARTIE
    // ══════════════════════════════════════════════════════

  public async Task StartGame(string roomCode, string? theme = null, int questionCount = 10)
  {
        try
        {
            var room = _gameService.GetRoom(roomCode);

            if (room is null)
            {
                await SendError("Room introuvable.");
                return;
            }

            if (room.HostConnectionId != Context.ConnectionId)
            {
                await SendError("Seul l'hôte peut lancer la partie.");
                return;
            }

            if (room.Players.Count(p => p.Role != PlayerRole.Spectator) < 1)
            {
                await SendError("Au moins un joueur est requis.");
                return;
            }

            if (!_gameService.TryStartRoom(roomCode))
            {
                await SendError("La partie a déjà commencé.");
                return;
            }

            // questionCount borné entre 5 et 30
            var count = Math.Clamp(questionCount, 5, 30);

            var questions = await _questionService.GetQuestionsForGame(
                theme: theme,
                count: count);

            if (questions.Count == 0)
            {
                await SendError("Aucune question disponible pour ce thème.");
                return;
            }

            await Clients.Group(roomCode).SendAsync("GameStarted", new
            {
                totalRounds = questions.Count,
                theme = theme ?? "tous"
            });

            await _roundManager.StartGame(room, questions);
        }
        catch (Exception ex)
        {
            await SendError("Impossible de démarrer la partie.");
            _logger.LogError(ex, "Erreur StartGame");
        }
    }
    public async Task RejoinRoom(string roomCode, string pseudo)
    {
        try
        {
            var room = _gameService.GetRoom(roomCode);

            if (room is null)
            {
                await SendError("Room introuvable.");
                return;
            }

            // Vérifie que le joueur existait bien dans cette room
            var existingPlayer = room.Players
                .FirstOrDefault(p => p.Pseudo == pseudo 
                                && p.Role == PlayerRole.Player);

            if (existingPlayer is null)
            {
                await SendError("Joueur introuvable dans cette room.");
                return;
            }

            // Met à jour le ConnectionId avec le nouveau
            existingPlayer.ConnectionId = Context.ConnectionId;
            existingPlayer.HasAnswered = false;

            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

            await Clients.Caller.SendAsync("RoomJoined", new
            {
                roomCode = room.Code,
                pseudo,
                players = room.Players
                    .Where(p => p.Role != PlayerRole.Spectator)
                    .Select(p => p.Pseudo)
                    .ToList()
            });

            // Informe les autres que le joueur est de retour
            await Clients.OthersInGroup(room.Code)
                .SendAsync("PlayerJoined", pseudo);

            _logger.LogInformation(
                "{Pseudo} a rejoint la room {Code}", pseudo, roomCode);

            // Si partie en cours, renvoie la question actuelle au joueur
            if (room.Status == RoomStatus.Playing)
            {
                var currentQuestion = room.Questions
                    .ElementAtOrDefault(room.CurrentQuestionIndex);

                if (currentQuestion is not null)
                {
                    var durationSeconds = _config
                        .GetValue<int>("GameSettings:QuestionDurationSeconds");

                    await Clients.Caller.SendAsync("NewQuestion", new
                    {
                        roundNumber = room.CurrentQuestionIndex + 1,
                        totalRounds = room.Questions.Count,
                        questionText = currentQuestion.QuestionText,
                        imageUrl = currentQuestion.ImageUrl,
                        type = currentQuestion.Type,
                        durationSeconds
                    });
                }
            }
        }
        catch (Exception ex)
        {
            await SendError("Impossible de rejoindre la room.");
            _logger.LogError(ex, "Erreur RejoinRoom");
        }
    }

    // ══════════════════════════════════════════════════════
    // UTILITAIRES
    // ══════════════════════════════════════════════════════

    private async Task SendError(string message)
    {
        await Clients.Caller.SendAsync("Error", message);
    }
}
