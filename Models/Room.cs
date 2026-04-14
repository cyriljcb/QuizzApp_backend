namespace QuizzBackend.Models;

public class Room
{
    public string Code { get; set; } = string.Empty;
    public List<Player> Players { get; set; } = new();
    public RoomStatus Status { get; set; } = RoomStatus.Waiting;
    public List<Question> Questions { get; set; } = new();
    public int CurrentQuestionIndex { get; set; } = 0;
    public string HostConnectionId { get; set; } = string.Empty;
    public bool IsRoundEnding { get; set; } = false;
}

public enum RoomStatus
{
    Waiting,    // En attente de joueurs
    Playing,    // Partie en cours
    Finished    // Partie terminée
}