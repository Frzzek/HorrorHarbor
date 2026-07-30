# Third-Party and Provider Notices

## Plex

HorrorHarbor is an independent community project. It is not reviewed, approved,
or endorsed by Plex.

Plex and Plex Media Server are trademarks of Plex, Inc. or its affiliates. Their
names are used only to identify the optional interoperable service described by
this project.

- [Plex developer documentation](https://developer.plex.tv/)
- [Plex trademark guidelines](https://www.plex.tv/about/privacy-legal/plex-trademarks-and-guidelines/)

No Plex logo is bundled with HorrorHarbor.

## TMDB

![The Movie Database](src/HorrorHarbor.App/Assets/tmdb-logo.png)

This product uses the TMDB API but is not endorsed or certified by TMDB.

- [TMDB attribution and branding](https://www.themoviedb.org/about/logos-attribution)
- [TMDB API terms of use](https://www.themoviedb.org/api-terms-of-use)

Official TMDB image bytes are per-user cache data and are not bundled in this
repository or release payload. Each user must supply a credential they are
authorized to use and comply with the current provider terms.

## Software dependencies

Direct package versions are centrally declared in `Directory.Packages.props`.
The following license identifiers describe the current direct dependency set;
transitive dependencies remain subject to their own package notices and terms.

| Dependency | License |
|---|---|
| Microsoft.Data.Sqlite.Core | MIT |
| Microsoft.Extensions.Hosting | MIT |
| System.Security.Cryptography.ProtectedData | MIT |
| SQLite3MC.PCLRaw.bundle | MIT; includes SQLite3 Multiple Ciphers 2.3.6 based on SQLite 3.53.3 |
| SQLite3MC.PCLRaw.lib and SQLite3MC.PCLRaw.provider (transitive) | MIT |
| SQLitePCLRaw.core (transitive) | Apache-2.0 |
| SkiaSharp and SkiaSharp.NativeAssets.Win32 | MIT |
| MSTest | MIT |
| Coverlet | MIT |

GitHub Actions are also third-party software. Workflow references are pinned to
reviewed immutable commit SHAs in `.github/workflows` and remain subject to each
action's license and terms.

## HorrorHarbor license status

No open-source license has been selected for HorrorHarbor at this time. Public
source visibility does not by itself grant permission to copy, modify,
redistribute, or sublicense the project beyond rights provided by applicable
law and the licenses of third-party components.
