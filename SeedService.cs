using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuizzBackend.Data;
using QuizzBackend.Models;

namespace QuizzBackend.Services;

public static class SeedService
{
    public static async Task SeedAsync(
        AppDbContext db,
        ILogger logger,
        string questionsPath)
    {
        if (!Directory.Exists(questionsPath))
        {
            logger.LogWarning("Dossier questions introuvable : {Path}", questionsPath);
            return;
        }

        var jsonFiles = Directory.GetFiles(questionsPath, "*.json");

        foreach (var file in jsonFiles)
        {
            var theme = Path.GetFileNameWithoutExtension(file);
            var json = await File.ReadAllTextAsync(file);
            int totalInserted = 0;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement questionsArray;

            if (root.ValueKind == JsonValueKind.Array)
            {
                questionsArray = root;
            }
            else if (root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty("questions", out var nested))
            {
                questionsArray = nested;
            }
            else
            {
                logger.LogWarning("Format JSON non reconnu dans {File}", file);
                continue;
            }

            foreach (var item in questionsArray.EnumerateArray())
            {
                var externalId = item.GetProperty("id").GetString() ?? string.Empty;

                var exists = await db.Questions
                    .AnyAsync(q => q.ExternalId == externalId);

                if (exists) continue;

                var question = new Question
                {
                    ExternalId      = externalId,
                    Theme           = theme,
                    Type            = item.GetProperty("type").GetString() ?? string.Empty,
                    Difficulty      = item.GetProperty("difficulty").GetInt32(),
                    QuestionText    = item.GetProperty("question").GetString() ?? string.Empty,
                    Answer          = item.GetProperty("answer").GetString() ?? string.Empty,
                    Hint            = item.GetProperty("hint").GetString() ?? string.Empty,
                    Explanation     = item.GetProperty("explanation").GetString() ?? string.Empty,
                    ImageUrl        = item.TryGetProperty("image_url", out var imgProp)
                                          ? imgProp.GetString()
                                          : null,
                    AcceptedAnswers = item.GetProperty("accepted_answers")
                                         .EnumerateArray()
                                         .Select(a => a.GetString() ?? string.Empty)
                                         .ToList(),
                    Tags            = item.GetProperty("tags")
                                         .EnumerateArray()
                                         .Select(t => t.GetString() ?? string.Empty)
                                         .ToList()
                };

                db.Questions.Add(question);
                totalInserted++;
            }

            await db.SaveChangesAsync();
            logger.LogInformation(
                "Thème [{Theme}] — {Count} questions importées", theme, totalInserted);
        }
    }
}