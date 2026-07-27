namespace HorrorHarbor.Core.Models;

public sealed record RecommendationScoreBreakdown(
    double Taste,
    double Franchise,
    double Intensity,
    double Format,
    double Discovery,
    double Quality,
    double Availability,
    double Feedback)
{
    public double Total =>
        Taste + Franchise + Intensity + Format + Discovery + Quality + Availability + Feedback;

    public string Summary =>
        $"Taste {Taste:0.0} • Intensity {Intensity:0.0} • Discovery {Discovery:0.0} • Quality {Quality:0.0}";
}
