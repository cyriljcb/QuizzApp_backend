namespace QuizzBackend.Models;

public class Question
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;  // "seq-001"
    public string Type { get; set; } = string.Empty;
    public int Difficulty { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Answer { get; set; } = string.Empty;
    public List<string> AcceptedAnswers { get; set; } = new();
    public string Hint { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string Theme { get; set; } = string.Empty;
}