namespace QuizzBackend.Models;

public class Player
{
    public string ConnectionId { get; set; } = string.Empty;
    public string Pseudo { get; set; } = string.Empty;
    public int Score { get; set; } = 0;
    public PlayerRole Role { get; set; } = PlayerRole.Player;

    // Réinitialisé à chaque round
    public string? CurrentAnswer { get; set; }
    public bool HasAnswered { get; set; } = false;
    public bool IsCorrect { get; set; } = false;
}

public enum PlayerRole
{
    Player,
    Host    // Maître du jeu — observe, ne répond pas
}