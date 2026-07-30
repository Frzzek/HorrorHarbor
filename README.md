# HorrorHarbor / Nocturne Harbor

HorrorHarbor is a local-first Windows horror discovery and movie-night
application. It combines deterministic full-catalog search, explainable
recommendations, optional privacy-conscious Plex history sync, and a hybrid
poster system. It is an independent community project, not an official Plex or
TMDB application.

![Nocturne Harbor application preview](src/HorrorHarbor.App/Assets/NocturneHarbor-preview.png)

## Highlights

- Advanced local horror search with phrases, exclusions, fields, ranges,
  status filters, aliases, deterministic ranking, and bounded typo tolerance.
- Explainable recommendations with mood controls and optional local Plex watch
  history.
- Full-page encrypted per-user SQLite storage with a random key protected for
  the current Windows user.
- Official TMDB artwork when a user explicitly configures personal access and
  identity matching is unambiguous.
- Deterministic locally generated fallback artwork for offline, unavailable, or
  ambiguous titles.
- Windows Credential Manager storage for provider credentials.
- WPF dark theme, keyboard navigation, accessible labels, live status regions,
  details views, empty states, and themed controls.
- Analyzer-clean builds, security tests, deterministic tests, performance
  budgets, and self-contained Windows publication.

## Privacy and network boundary

Ordinary catalog search and recommendation execute locally. The public project
contains no bundled developer/user watch history. Plex and TMDB are separate,
explicit user actions. Credentials are never stored in the catalog, source
code, logs, image URLs, manifests, or release packages. Official artwork is
per-user cache data and is not committed or bundled in public releases.

See the [privacy notice](PRIVACY.md), [security policy](SECURITY.md),
[third-party notices](THIRD_PARTY_NOTICES.md), [threat model](docs/quality/THREAT_MODEL.md), and
[personal hybrid artwork workflow](docs/quality/PERSONAL_HYBRID_ARTWORK.md).

## Plex notice

HorrorHarbor is an independent community project. It is not reviewed, approved,
or endorsed by Plex. Plex and Plex Media Server are trademarks of Plex, Inc. or
its affiliates. No Plex logo is bundled with HorrorHarbor.

- [Plex developer documentation](https://developer.plex.tv/)
- [Plex trademark guidelines](https://www.plex.tv/about/privacy-legal/plex-trademarks-and-guidelines/)

## TMDB attribution

![The Movie Database](src/HorrorHarbor.App/Assets/tmdb-logo.png)

This product uses the TMDB API but is not endorsed or certified by TMDB.

- [TMDB attribution and branding](https://www.themoviedb.org/about/logos-attribution)
- [TMDB API terms of use](https://www.themoviedb.org/api-terms-of-use)

TMDB integration requires each user to provide a credential they are authorized
to use and acknowledge current personal, non-commercial use. No shared TMDB
credential is embedded. Any later monetization, business use, or commercial
distribution requires stopping personal access and obtaining the applicable
provider approval before use resumes.

## Build and verify on Windows 11

Requirements: .NET SDK 10 and Windows PowerShell 5.1 or PowerShell 7.

```powershell
dotnet restore HorrorHarbor.sln
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/quality-gate.ps1 -Configuration Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/build.ps1 -Configuration Release -Runtime win-x64
```

The repository does not include user credentials, user history, generated
artifacts, provider caches, or signed installers. Public executable promotion is
blocked until release evidence and publisher signing are complete.

## Project documentation

- [Search requirements](docs/quality/HORROR_SEARCH_ENGINE_REQUIREMENTS.md)
- [Search architecture](docs/quality/HORROR_SEARCH_ENGINE_ARCHITECTURE.md)
- [Release verification](docs/quality/HORROR_SEARCH_RELEASE_VERIFICATION.md)
- [Performance budgets](docs/quality/PERFORMANCE_BUDGETS.md)
- [Poster architecture decision](docs/decisions/ADR-0005-nocturne-visual-and-poster-architecture.md)

## Status

This is an actively developed personal project and engineering reference. It
is not currently offered as a commercial service.

No open-source license has been selected yet. Source visibility does not by
itself grant redistribution or modification rights.
