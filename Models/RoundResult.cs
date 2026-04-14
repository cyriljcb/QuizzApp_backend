namespace QuizzBackend.Models;

// Ce modèle est uniquement envoyé via SignalR aux clients
public class RoundResult
{
    public string CorrectAnswer { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public List<PlayerResult> PlayerResults { get; set; } = new();
    public bool IsLastRound { get; set; } = false;
}

public class PlayerResult
{
    public string Pseudo { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int Score { get; set; }
    public int ScoreGained { get; set; }
}