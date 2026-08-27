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

## Mobile release outputs

Pull requests touching either mobile target run platform-native build checks.
Tagging `mobile-v*` runs `.github/workflows/android-mobile-release.yml`; a
manual dispatch can package both platforms and can publish when `publish` is
enabled with an explicit `release_tag` such as `mobile-v2.0.1`. The workflow
executes Android unit tests, archives the iOS target, and publishes clearly
labelled APK/IPA artifacts. The platform packaging jobs only have read access;
the release job receives the artifacts and is the only job granted
`contents: write`.

- `*-preview-signed.apk`: optimized, installable device-QA build signed using
  the Android debug key. It is never a Play Store submission artifact.
- `*-unsigned.apk`: optimized release output for signing with the production
  upload key.

- `*-ad-hoc-signed.ipa`: emitted when Apple signing secrets and an ad-hoc
  provisioning profile are configured; installability is limited to devices in
  that profile.
- `*-unsigned.ipa`: device archive packaged for downstream signing. It cannot
  be installed on iOS until signed by Apple tooling.

Use `./gradlew assembleRelease -PpreviewSigning=true` only for the first
artifact. Keep a production key outside the repository and configure it in a
secure release environment. For signed iOS output, configure the
`APPLE_TEAM_ID`, `BUILD_CERTIFICATE_BASE64`, `P12_PASSWORD`,
`BUILD_PROVISION_PROFILE_BASE64`, and `KEYCHAIN_PASSWORD` GitHub secrets.
