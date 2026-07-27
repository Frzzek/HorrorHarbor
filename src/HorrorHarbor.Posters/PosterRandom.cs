namespace HorrorHarbor.Posters;

internal struct PosterRandom
{
    private ulong _state;

    public PosterRandom(ulong seed) => _state = seed == 0 ? 0x9e3779b97f4a7c15UL : seed;

    public int Next(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        return (int)(NextValue() % (uint)maximum);
    }

    public float NextSingle() => (NextValue() >> 40) * (1f / (1 << 24));

    public float Between(float minimum, float maximum) =>
        minimum + (maximum - minimum) * NextSingle();

    private ulong NextValue()
    {
        unchecked
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 2685821657736338717UL;
        }
    }
}
