using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using QuizzBackend.Hubs;
using QuizzBackend.Models;

namespace QuizzBackend.Services;

public class GameService
{
    // ConcurrentDictionary — thread-safe pour le multi-room
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<GameService> _logger;

    public GameService(IHubContext<GameHub> hubContext, ILogger<GameService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    // ── Création d'une room ──────────────────────────────────

    public Room CreateRoom(string hostConnectionId, string hostPseudo)
    {
        var code = GenerateRoomCode();

        var host = new Player
        {
            ConnectionId = hostConnectionId,
            Pseudo = hostPseudo,
            Role = PlayerRole.Host
        };

        var room = new Room
        {
            Code = code,
            HostConnectionId = hostConnectionId,
            Players = new List<Player> { host }
        };

        _rooms[code] = room;
        _logger.LogInformation("Room créée : {Code} par {Pseudo}", code, hostPseudo);

        return room;
    }

    // ── Rejoindre une room ───────────────────────────────────

    public (bool success, string? error, Room? room) JoinRoom(
        string code, string connectionId, string pseudo)
    {
        if (!_rooms.TryGetValue(code.ToUpper(), out var room))
            return (false, "Room introuvable.", null);

        if (room.Status != RoomStatus.Waiting)
            return (false, "La partie a déjà commencé.", null);

        if (room.Players.Any(p => p.Pseudo == pseudo))
            return (false, "Ce pseudo est déjà pris.", null);

        var player = new Player
        {
            ConnectionId = connectionId,
            Pseudo = pseudo,
            Role = PlayerRole.Player
        };

        room.Players.Add(player);
        _logger.LogInformation("{Pseudo} a rejoint la room {Code}", pseudo, code);

        return (true, null, room);
    }

    // ── Démarrage atomique ───────────────────────────────────
    // Garantit qu'une room ne peut pas démarrer deux fois simultanément

    public bool TryStartRoom(string code)
    {
        if (!_rooms.TryGetValue(code, out var room))
            return false;

        lock (room)
        {
            if (room.Status != RoomStatus.Waiting)
                return false;

            room.Status = RoomStatus.Playing;
            return true;
        }
    }

    // ── Récupérer une room ───────────────────────────────────

    public Room? GetRoom(string code) =>
        _rooms.TryGetValue(code.ToUpper(), out var room) ? room : null;

    public Room? GetRoomByConnectionId(string connectionId) =>
        _rooms.Values.FirstOrDefault(r =>
            r.Players.Any(p => p.ConnectionId == connectionId));

    // ── Quitter / déconnexion ────────────────────────────────

    public void RemovePlayer(string connectionId)
    {
        var room = GetRoomByConnectionId(connectionId);
        if (room is null) return;

        var player = room.Players
            .FirstOrDefault(p => p.ConnectionId == connectionId);
        if (player is null) return;

        room.Players.Remove(player);
        _logger.LogInformation(
            "{Pseudo} a quitté la room {Code}", player.Pseudo, room.Code);

        if (room.Players.Count == 0)
        {
            _rooms.TryRemove(room.Code, out _);
            _logger.LogInformation("Room {Code} supprimée (vide)", room.Code);
        }
    }

    // ── Réinitialiser les réponses pour un nouveau round ────

    public void ResetPlayerAnswers(string roomCode)
    {
        var room = GetRoom(roomCode);
        if (room is null) return;

        // Reset du guard en même temps que les réponses
        room.IsRoundEnding = false;

        foreach (var player in room.Players)
        {
            player.CurrentAnswer = null;
            player.HasAnswered = false;
            player.IsCorrect = false;
        }
    }

    // ── Utilitaire ───────────────────────────────────────────

    private string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string code;

        do
        {
            code = new string(Enumerable.Range(0, 4)
                .Select(_ => chars[Random.Shared.Next(chars.Length)])
                .ToArray());
        }
        while (_rooms.ContainsKey(code));

        return code;
    }
}