# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Stack

delegated: native Android (Jetpack Compose + Material 3) and native iOS/iPadOS (SwiftUI), with a shared product contract and repository-backed data model. The mobile app is a companion control plane for the existing Hugoer desktop workflow.

## Users

Primary users are Hugo site authors and developers who need to check a site, make a small content/configuration change, or start a deployment while away from their desktop.

## Product Purpose

Hugoer Mobile makes the most frequent Hugo site-management tasks usable on a phone or tablet: choose a repository/site, inspect deployment health, edit Markdown and site settings, preview the change, and trigger or monitor GitHub Pages deployment. Success means a user can understand site health and safely publish a small change in under a few minutes.

## Positioning

The product is a local-first Hugo workbench on desktop and a repository-aware companion on mobile: it keeps the desktop workflow's Hugo concepts while reducing the mobile experience to safe, high-value actions that do not require a local shell.

## Operating Context

Users work one-handed on phones and two-handed on tablets, often with intermittent connectivity. A repository is the source of truth; GitHub Actions/Pages is the deployment path. Mobile previews may use rendered Markdown or the remote Pages URL. Full Hugo binary installation, bulk migration, and local server control remain desktop hand-off tasks.

## Capabilities and Constraints

- Preserve the reference app's concepts: Environment, Configuration, Themes, Articles, Migration, Menu, and Git deployment.
- Mobile MVP focuses on site/repository selection, health overview, article editing, Markdown preview, deployment trigger, deployment history, and hand-off links.
- Data and actions are represented behind a repository/service boundary so a later authenticated GitHub integration can replace the demo adapter without changing the screens.
- Mobile cannot assume a local Hugo or `gh` executable; no destructive push or migration is implied without an explicit user action.
- Android follows Material 3 navigation/components and window insets; iOS follows SwiftUI navigation, system controls, safe areas, Dynamic Type, and Dark Mode.

## Brand Commitments

Keep the Hugoer name and the reference app's dark graphite/cyan identity, translating it into semantic system/Material color roles rather than copying desktop pixels. Product language is concise Traditional Chinese with familiar Hugo/GitHub terms.

## Evidence on Hand

- Reference implementation: `D:\codex\Hugoer` and the synchronized repository history in this checkout.
- Reference README documents the seven desktop areas and GitHub Pages workflow.
- No approved mobile visual comps or mobile-specific brand assets were provided; sample repository/site data in the MVP must be clearly synthetic.

## Product Principles

1. Show site health before configuration detail.
2. Make publishing deliberate, reversible, and observable.
3. Keep the user's repository as the source of truth.
4. Reduce desktop complexity without hiding important states.
5. Use native platform conventions so the app feels at home on each OS.

## Accessibility & Inclusion

Use platform semantic labels, minimum 44 pt iOS / 48 dp Android touch targets, Dynamic Type and scalable `sp`, sufficient color contrast, keyboard/IME-safe layouts, and Reduce Motion/Remove animations behavior. Support light and dark appearance from the first build.
