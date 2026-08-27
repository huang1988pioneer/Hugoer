# Hugoer Mobile architecture

Hugoer Mobile is a native companion to the desktop workbench. It supports the
small, reviewable tasks that make sense away from a desktop—inspect a site,
edit a draft, and deliberately trigger an existing GitHub Actions deployment.
Local Hugo installation, large migrations, and server control remain desktop
handoffs.

## Shared boundaries

The product is organized around three stable boundaries:

1. **Domain model** — site, article, and deployment values have no UI or
   network dependency.
2. **Repository** — the only interface responsible for loading and mutating
   site data. The current deterministic adapter supports first-run review and
   test coverage; a GitHub/OAuth or desktop bridge can replace it later.
3. **Presentation state** — screens observe one state holder and send intents
   such as refresh, save, create article, and deploy. UI never updates demo
   collections directly.

## Android

```
core:model  →  core:data (HugoerRepository)  →  app:presentation (ViewModel)  →  Compose UI
```

- `core:model` contains immutable Kotlin domain values.
- `core:data` owns `HugoerRepository`, its deterministic implementation, and
  repository tests. Mutation is protected with a `Mutex` so duplicate publish
  taps cannot create duplicate deployments.
- `app:presentation` exposes lifecycle-aware `HugoerUiState` plus one-off
  events for snackbars and editor navigation.
- Compose views render state only. They retain the adaptive phone/tablet
  navigation pattern and native Material controls.

## iOS

```
HugoerRepository  →  HugoerStore (@Published state)  →  SwiftUI tabs and sheets
```

- `Repository.swift` defines a replaceable repository protocol and the
  deterministic sample adapter.
- `HugoerStore.swift` is the sole observable presentation state. It handles
  refresh, save, article creation, deployment state, and recoverable errors.
- SwiftUI views use `@EnvironmentObject` and no longer own seed data or remote
  mutation logic. Pull-to-refresh and the toolbar refresh action both use the
  same store intent.

## Production bridge contract

A production adapter should implement the repository boundary and provide
these operations atomically: refresh repository/Pages state, save one article,
create one article, and trigger a deployment. OAuth tokens belong in platform
secure storage; the mobile UI must not execute Hugo locally or persist secrets
in Markdown content.

## Android release outputs

Tagging `mobile-v*` runs `.github/workflows/android-mobile-release.yml`.
It executes Android unit tests and publishes two clearly labelled APKs:

- `*-preview-signed.apk`: optimized, installable device-QA build signed using
  the Android debug key. It is never a Play Store submission artifact.
- `*-unsigned.apk`: optimized release output for signing with the production
  upload key.

Use `./gradlew assembleRelease -PpreviewSigning=true` only for the first
artifact. Keep a production key outside the repository and configure it in a
secure release environment.
