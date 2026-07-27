namespace HorrorHarbor.Core.Validation;

public static class CandidateIdValidator
{
    public const int MaximumLength = 200;

    public static void Validate(string candidateId, string? parameterName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId, parameterName);
        if (candidateId.Length > MaximumLength)
        {
            throw new ArgumentException($"Candidate ID cannot exceed {MaximumLength} characters.", parameterName ?? nameof(candidateId));
        }

        if (!candidateId.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw new ArgumentException(
                "Candidate ID may contain only ASCII letters, digits, hyphens, underscores, and periods.",
                parameterName ?? nameof(candidateId));
        }
    }
}
