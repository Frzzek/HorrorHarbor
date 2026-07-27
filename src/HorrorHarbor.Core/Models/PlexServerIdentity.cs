namespace HorrorHarbor.Core.Models;

public sealed record PlexServerIdentity(
    string MachineIdentifier,
    string Name,
    string Version);

