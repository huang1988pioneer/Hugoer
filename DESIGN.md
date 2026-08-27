# Hugoer Mobile design system

Hugoer Mobile is a native companion for the desktop Hugo workbench. Its first viewport is a release dispatch board: a selected site, a four-station path (draft → preview → queued → live), and one deliberate next action. The mobile app is intentionally not a scaled desktop sidebar.

## Visual language

- **Surface:** graphite/slate backgrounds in dark mode and warm neutral paper in light mode. Cards use restrained tonal elevation and large, calm corner radii.
- **Signal:** Hugoer cyan identifies the selected site, healthy Pages state, links, and the active workflow station.
- **Decision:** warm amber is reserved for publish/deploy actions and the selected Publish destination. It is never used as decoration.
- **Type:** platform system typography carries Dynamic Type / font scaling. Headings are compact and editorial; supporting copy is short enough to scan while holding a phone.
- **Shape:** rounded cards and chips group decisions; the dispatch rail uses four repeated station marks so progress is legible without color alone.

## Interaction contract

1. Overview answers “which site, where is the release, what is safe next?”
2. Articles provides search, status filters, a focused article card, and an editor sheet with Markdown/Preview modes.
3. Deploy requires an explicit confirmation dialog before it records a deployment intent. The demo adapter makes the remote-write seam visible.
4. More hands off desktop-only work (local Hugo installation, preview servers, themes, menus, and bulk migration) instead of pretending a phone can do it safely.

## Platform translation

Android uses edge-to-edge Compose Material 3, a bottom navigation bar on compact windows, a navigation rail on expanded windows, a single Articles FAB, modal sheets, and snackbars.

iOS uses SwiftUI `TabView`, `NavigationStack`, sheets, confirmation dialogs, SF Symbols, semantic `Color`, safe-area behavior, and Dynamic Type. iPad uses the same content model while allowing the system navigation container to widen naturally.

## Accessibility and resilience

- Every primary action has a text label and a system icon; status is not communicated by color alone.
- Controls are given Material/SwiftUI minimum touch targets and remain usable with larger text.
- Empty, pending, and desktop-only states explain what happens next rather than leaving a blank card.
- The initial store is deterministic demo data behind a replaceable repository seam; credentials and network writes are not hidden in the UI layer.
