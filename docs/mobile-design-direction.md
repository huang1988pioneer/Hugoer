# Hugoer Mobile — direction contract

<!-- impeccable:direction-contract -->
<!-- THESIS: Turn the desktop Hugo workbench into a calm release-dispatch board: the first screen makes the path from draft to live legible, and every action leaves an observable mark. The category default (a generic dashboard full of equal metric cards) and its predictable opposite (a shrunken desktop sidebar) are refused. -->
<!-- OWN-WORLD: Graphite surfaces, a single Hugoer cyan signal, warm amber for a deliberate publish action, and a fixed dispatch rail of stations. Native Material 3 / SwiftUI components carry structure; the rail, station marks, and terse operational copy carry the brand. -->
<!-- STORY: A returning author sees which site is selected, where the latest change sits, and what can safely happen next. They inspect an article, preview it, publish deliberately, then watch the remote Pages run settle. -->
<!-- FIRST VIEWPORT: Top app bar with site identity and sync; one dominant dispatch surface showing Draft → Preview → Queued → Live; the current station is high-contrast and the single primary action sits directly below the rail. Recent activity follows as a quiet list, not a second dashboard. -->
<!-- FORM: Release dispatch board, grounded candidate 3 of 7; concept seed key c78af186, assigned index 3, API roll with six challengers and no challenger winning both audience identification and product clarity. -->
<!-- FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance -->

## Grounded directions (ordered before the roll)

1. **Commit graph / branch map** — repository history becomes the primary visual; resonant for developers, but can make writing feel secondary.
2. **Print proof desk** — article proofs, annotations, and registration marks make editing tactile; strong for content, weaker for deployment health.
3. **Release dispatch board** — a railway/airport-style route of stations turns draft, preview, queue, and live into one operational story; clear on a phone and extensible to deployment history.
4. **Darkroom contact sheet** — articles appear as a contact sheet with a focused proof; memorable browsing, but status can get buried in imagery.
5. **Harbor bridge log** — site, branch, and deployment are a watch log; reassuring for monitoring, less direct for editing.
6. **Library card catalog** — repositories and articles are indexed cards; excellent findability, familiar and less distinctive.
7. **Field trail map** — progress follows checkpoints from local draft to published site; friendly, but risks implying a linear workflow where branches exist.

The seed assigned candidate 3, so the release dispatch board is the build direction. The assignment was made after the API roll (`c78af186`); no user answer mechanism was available in this session, so the explicit brief plus repository evidence is the recorded decision.

## Challenger weighing

The API roll supplied six catalog challengers. They were fused with Hugoer facts and judged only on audience identification and product clarity:

| Challenger | Verdict | What it contributes or why it loses |
|---|---|---|
| Film cutting bench / select rail | Competitive | The snapped scrub and “set aside on a pin” state are excellent for article editing; it does not explain remote deployment as clearly as the dispatch rail. Keep snap-to-station behavior. |
| Streaming catalog wall | Competitive | Focus expansion and dimming can make article selection obvious; its poster-wall density is less useful for configuration and deployment. Keep focused-item hierarchy in the article list. |
| Pixel arcade cabinet | Declined | Low audience identification and low product clarity; keep its strict state palette discipline and integer-like station marks. |
| French pop record sleeve | Declined | Memorable color but weak Hugo/Git identification and no useful workflow topology; keep one saturated site accent moment, never decorative gradients. |
| Alphabet storm | Declined | Expressive transformation obscures safe publishing; keep reversible state changes and readable copy. |
| Iridescent cloud edge | Declined | Observation metaphor is too far from the user's job; keep explicit uncertainty/error messaging rather than visual ambiguity. |

## Platform expression

- Android uses a compact `NavigationBar`, expanded `NavigationRail`, Material 3 top app bars, a single FAB on Articles, snackbars for transient status, and edge-to-edge insets.
- iOS uses `TabView` for the four top-level sections, `NavigationStack`, sheets for article editing and publish confirmation, SF Symbols, semantic colors, safe areas, and Dynamic Type.
- Both platforms share the same demo repository contract and copy; platform controls are not visually transplanted across OSes.
