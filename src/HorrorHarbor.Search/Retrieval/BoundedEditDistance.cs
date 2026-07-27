using System.Buffers;
using System.Text;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Retrieval;

internal static class BoundedEditDistance
{
    public const int MaximumSupportedDistance = 2;

    public static bool TryCalculate(
        string source,
        string target,
        int maximumDistance,
        out int distance)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ValidateMaximumDistance(maximumDistance);

        Span<Rune> sourceScalars =
            stackalloc Rune[SearchGrammarLimits.MaximumNormalizedTokenRunes];
        Span<Rune> targetScalars =
            stackalloc Rune[SearchGrammarLimits.MaximumNormalizedTokenRunes];
        int sourceLength = CopyScalars(source, sourceScalars, nameof(source));
        int targetLength = CopyScalars(target, targetScalars, nameof(target));

        return TryCalculate(
            sourceScalars[..sourceLength],
            targetScalars[..targetLength],
            maximumDistance,
            out distance);
    }

    public static bool TryCalculate(
        ReadOnlySpan<Rune> source,
        ReadOnlySpan<Rune> target,
        int maximumDistance,
        out int distance)
    {
        ValidateScalarLength(source.Length, nameof(source));
        ValidateScalarLength(target.Length, nameof(target));
        ValidateMaximumDistance(maximumDistance);

        int outsideBound = maximumDistance + 1;
        if (Math.Abs(source.Length - target.Length) > maximumDistance)
        {
            distance = outsideBound;
            return false;
        }

        Span<int> previous =
            stackalloc int[SearchGrammarLimits.MaximumNormalizedTokenRunes + 1];
        Span<int> current =
            stackalloc int[SearchGrammarLimits.MaximumNormalizedTokenRunes + 1];
        previous.Fill(outsideBound);
        for (int column = 0;
             column <= Math.Min(target.Length, maximumDistance);
             column++)
        {
            previous[column] = column;
        }

        for (int row = 1; row <= source.Length; row++)
        {
            current.Fill(outsideBound);
            if (row <= maximumDistance)
            {
                current[0] = row;
            }

            int firstColumn = Math.Max(1, row - maximumDistance);
            int lastColumn = Math.Min(target.Length, row + maximumDistance);
            int rowMinimum = current[0];
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                int substitutionCost = source[row - 1] == target[column - 1]
                    ? 0
                    : 1;
                int deletion = IncrementWithinBound(
                    previous[column],
                    outsideBound);
                int insertion = IncrementWithinBound(
                    current[column - 1],
                    outsideBound);
                int substitution = AddWithinBound(
                    previous[column - 1],
                    substitutionCost,
                    outsideBound);
                int value = Math.Min(
                    deletion,
                    Math.Min(insertion, substitution));
                current[column] = value;
                rowMinimum = Math.Min(rowMinimum, value);
            }

            if (rowMinimum > maximumDistance)
            {
                distance = outsideBound;
                return false;
            }

            Span<int> swap = previous;
            previous = current;
            current = swap;
        }

        distance = previous[target.Length];
        return distance <= maximumDistance;
    }

    public static int CopyScalars(
        string value,
        Span<Rune> destination,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        int count = 0;
        ReadOnlySpan<char> remaining = value;
        while (!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(
                remaining,
                out Rune rune,
                out int consumed);
            if (status != OperationStatus.Done)
            {
                throw new ArgumentException(
                    "Value must contain well-formed UTF-16.",
                    parameterName);
            }

            if (count == destination.Length)
            {
                throw new ArgumentException(
                    $"Value must not exceed {destination.Length} Unicode scalar values.",
                    parameterName);
            }

            destination[count] = rune;
            count++;
            remaining = remaining[consumed..];
        }

        return count;
    }

    private static int IncrementWithinBound(int value, int outsideBound) =>
        AddWithinBound(value, 1, outsideBound);

    private static int AddWithinBound(
        int value,
        int increment,
        int outsideBound) => value >= outsideBound
            ? outsideBound
            : Math.Min(value + increment, outsideBound);

    private static void ValidateMaximumDistance(int maximumDistance)
    {
        if (maximumDistance is < 0 or > MaximumSupportedDistance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDistance),
                maximumDistance,
                $"Maximum distance must be from 0 through {MaximumSupportedDistance}.");
        }
    }

    private static void ValidateScalarLength(int length, string parameterName)
    {
        if (length > SearchGrammarLimits.MaximumNormalizedTokenRunes)
        {
            throw new ArgumentException(
                $"Value must not exceed {SearchGrammarLimits.MaximumNormalizedTokenRunes} Unicode scalar values.",
                parameterName);
        }
    }
}
