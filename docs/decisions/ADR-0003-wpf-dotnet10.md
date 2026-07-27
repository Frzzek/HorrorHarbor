# ADR-0003: Target WPF on .NET 10 for the Windows 11 product

**Status:** Accepted  
**Date:** 2026-07-24

The production client is Windows-only, needs a native desktop experience, and should remain maintainable for years. The foundation targets WPF on .NET 10 and isolates the UI from data and recommendation logic so a future UI replacement does not require rewriting the engine.

The application runs without elevation and opens the canonical database read-only.
