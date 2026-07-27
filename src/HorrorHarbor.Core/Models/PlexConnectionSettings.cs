namespace HorrorHarbor.Core.Models;

public sealed record PlexConnectionSettings(
    string ServerUrl,
    bool AllowInsecureHttp = false);

