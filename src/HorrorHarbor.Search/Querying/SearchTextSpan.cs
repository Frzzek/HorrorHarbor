namespace HorrorHarbor.Search.Querying;

public readonly record struct SearchTextSpan
{
    public SearchTextSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => checked(Start + Length);
}
