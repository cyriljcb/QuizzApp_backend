namespace QuizzBackend.Models;
// Ce que le serveur envoie aux clients au début de chaque round
public class NewQuestionPayload
{
    public int RoundNumber { get; set; }
    public int TotalRounds { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Type { get; set; } = string.Empty;
    public int DurationSeconds { get; set; } = 30;
}