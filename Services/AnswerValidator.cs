using QuizzBackend.Models;

namespace QuizzBackend.Services;

public class AnswerValidator
{
    // ── Validation principale ────────────────────────────────

    public bool IsCorrect(string playerAnswer, Question question)
    {
        var normalized = Normalize(playerAnswer);

        // On vérifie contre la réponse principale
        if (normalized == Normalize(question.Answer))
            return true;

        // On vérifie contre toutes les réponses acceptées
        return question.AcceptedAnswers
            .Any(accepted => normalized == Normalize(accepted));
    }

    // ── Normalisation ────────────────────────────────────────
    // Minuscules + suppression accents + trim + espaces multiples

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var lowered = input.Trim().ToLowerInvariant();
        var normalized = RemoveDiacritics(lowered);

        // Supprime les espaces multiples
        return string.Join(" ",
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                stringBuilder.Append(c);
        }

        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}