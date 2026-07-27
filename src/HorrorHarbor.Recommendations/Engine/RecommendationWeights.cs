namespace HorrorHarbor.Recommendations.Engine;

public sealed record RecommendationWeights(
    double PrimaryLens = 16,
    double SecondaryLens = 4,
    double Franchise = 13,
    double ScareIntensity = 8,
    double GoreIntensity = 8,
    double DreadIntensity = 7,
    double Slasher = 6,
    double Extreme = 4,
    double Discovery = 10,
    double Quality = 8,
    double Availability = 5,
    double Feedback = 11)
{
    public double Total =>
        PrimaryLens + SecondaryLens + Franchise + ScareIntensity + GoreIntensity +
        DreadIntensity + Slasher + Extreme + Discovery + Quality + Availability + Feedback;
}
