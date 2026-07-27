# ADR-0005: Nocturne Harbor visual and poster architecture

**Status:** Accepted  
**Scope:** Windows presentation and recommendation artwork

## Historical recovery findings

The complete archive, extracted workspace snapshots, Alpha 1 source package, current catalog, release notes, and both preview images were reviewed before design work.

- An earlier spreadsheet-style prototype contributed information-provenance and
  restrained dark-blue hierarchy ideas, but is not the application interaction
  model.
- Alpha 1 introduced the two-column “recommendation theater,” intensity controls, unseen-only messaging, and color-coded title rectangles. The release screenshot confirms those rectangles were the only poster concept.
- Searches across archived C#, XAML, Markdown, catalogs, and manifests found no poster downloader, image cache, artwork field, image validator, or dormant poster module.
- The catalog contains a deterministic `posterTone` plus lens, intensity, and tags, but no licensed artwork URL or artwork rights metadata.
- The earlier sidebar became crowded as Plex and taste signals accumulated, and the non-virtualized wrapping card surface was unsuitable for larger discovery lists.

## Decision

The customer-facing product name is **Nocturne Harbor**. Existing assembly names and namespaces remain `HorrorHarbor` in this release to avoid a broad, risky migration of persistence paths, contracts, packaging automation, and concurrent Plex work.

The Windows UI retains the proven theater concept but uses a calmer Windows 11 visual system: rounded surfaces, a friendly typographic hierarchy, compact mood controls, progressive disclosure for Plex, horizontal recommendation rows, recycling virtualization, explicit focus states, touch-sized controls, automation names/live regions, system DPI scaling, and a runtime high-contrast palette driven by Windows settings.

Posters use a separate dependency-free `HorrorHarbor.Posters` module:

1. Metadata is canonicalized and hashed with SHA-256.
2. A versioned deterministic renderer creates original atmospheric artwork from catalog tone, lens, and identity. It does not imitate or retrieve studio key art.
3. The renderer writes a constrained PNG profile: RGBA8, no interlace, bounded dimensions, and standard zlib compression.
4. A strict validator checks signature, dimensions, chunk bounds/count, CRCs, critical chunks, inflate size, and row filters before a cache asset is trusted.
5. Cache writes are same-directory temporary files followed by atomic replacement; per-key semaphores collapse concurrent requests.
6. WPF decodes validated local files with `OnLoad`, checks dimensions again, freezes the bitmap, and releases file handles. Cards load asynchronously and retain an accessible title fallback.

## Security, privacy, and copyright tradeoffs

- There is no network poster provider and no scraping. This eliminates tracking, secret-bearing image requests, redirect/SSRF policy expansion, availability failures, and uncertain artwork rights.
- Generated art is intentionally abstract. It is richer than a title rectangle but does not claim to be official key art.
- Cache filenames are content hashes rather than user-controlled title paths. Inputs, dimensions, encoded files, decompressed output, and metadata lengths are bounded.
- The generated image is deterministic, but the recommendation order remains exclusively owned by the deterministic recommendation engine.
- The renderer uses raw pixels and a small PNG encoder rather than adding an imaging package. This reduces dependency and native-code attack surface at the cost of deliberately limited illustration primitives.

## Known limitations and future extension

- No official posters are bundled because the catalog has no provenance/license manifest.
- A future artwork source must provide explicit rights/provenance, use HTTPS with the same host/redirect protections as other remote content, enforce response and pixel bounds before decode, and fall back to this local renderer.
- Horizontal row cards optimize scanning and virtualization; a future detail page can present a larger 2:3 asset without changing the poster service.
