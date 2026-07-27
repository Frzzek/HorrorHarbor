# ADR-0002: Preserve raw events and separate taste weight

**Status:** Accepted  
**Date:** 2026-07-24

No source watch event is deleted. Exact and near duplicate secondary events receive zero taste/rewatch weight. Bulk-timestamp events receive a 0.25 taste weight pending provenance review, but still enforce watched-title exclusion.

This prevents corrupted rewatch/recency signals without destroying evidence or accidentally recommending an already-watched title.
