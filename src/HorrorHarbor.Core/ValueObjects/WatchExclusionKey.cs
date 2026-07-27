namespace HorrorHarbor.Core.ValueObjects;

public readonly record struct WatchExclusionKey
{
    public WatchExclusionKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
