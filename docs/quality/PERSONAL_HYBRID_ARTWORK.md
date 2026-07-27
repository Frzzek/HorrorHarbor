# Personal hybrid artwork workflow

## Usage boundary

Live TMDB preparation is optional and is permitted by this workflow only after
the person running it explicitly certifies that the current use is exclusively
personal, non-commercial, revenue-free, and outside a business environment.
The acknowledgement is not permission for later commercial use and is not a
substitute for TMDB approval or legal advice.

Before monetization, business use, or commercial distribution:

1. Stop using the personal TMDB credential.
2. Request and receive the provider approval or commercial license applicable
   at that time.
3. Review the current API and image terms and attribution requirements.
4. Configure an appropriately authorized credential and repeat provider
   acceptance testing before release.

## Storage and release boundary

- Official TMDB images are per-user cache data under
  `%LOCALAPPDATA%\NocturneHarbor\official-posters-v1`.
- Official images are not written into the catalog, source tree, installer,
  portable package, or published application directory.
- The token remains in Windows Credential Manager and never appears in command
  arguments, reports, URLs, logs, cache metadata, or provenance.
- Generated fallback images are the distributable/offline baseline.
- Every official image retains TMDB attribution, terms URI, identity evidence,
  retrieval time, and content hash in local cache metadata.

## Prepare and review the catalog

First save a user-owned TMDB read-access token in the application's **Official
Film Posters** panel. Do not paste the token into a terminal or chat.

From the repository root, run:

```powershell
dotnet run --project src/HorrorHarbor.Tools.PosterAudit -c Release -- `
  --catalog data/catalog/recommendation_catalog.json `
  --output artifacts/personal-hybrid-artwork `
  --use-tmdb `
  --acknowledge-personal-noncommercial-use
```

The command attempts official artwork through the hardened TMDB client and
uses deterministic local artwork whenever credentials are absent, a match is
ambiguous, artwork is unavailable, or provider access fails safely. It writes
machine-readable manifests and browser-viewable contact sheets under the
requested artifact directory.

Generated-only preparation requires no provider acknowledgement:

```powershell
dotnet run --project src/HorrorHarbor.Tools.PosterAudit -c Release -- `
  --catalog data/catalog/recommendation_catalog.json `
  --output artifacts/poster-audit/generated-only
```

`PASS` means all catalog records produced valid artwork and evidence. It does
not replace human review of poster identity, crop, legibility, attribution, or
visual quality.
