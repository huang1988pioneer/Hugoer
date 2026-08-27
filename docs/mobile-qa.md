# Mobile QA record

## Android

- Build: `C:\Users\chbon\.gradle\wrapper\dists\gradle-8.11.1-bin\bpt9gzteqjrbo1mjrsomdt32c\gradle-8.11.1\bin\gradle.bat --project-cache-dir D:\codex\HugoerMobile\.gradle-cache :app:assembleDebug`
- Result: `BUILD SUCCESSFUL` (Android Gradle Plugin 8.10.1, compile/target SDK 35).
- Runtime: installed and launched on a Pixel API 35 emulator (`HugoerPixel`, `emulator-5554`).
- Verified screens: Overview (light and dark), Articles, Deploy, editor sheet, and publish confirmation dialog.
- Evidence is captured locally under `.impeccable/review/` and intentionally ignored from source control.

### Architecture release verification (v2.0.0)

- `:core:data:test`: passed repository, front matter, duplicate-deployment,
  and cancellation tests.
- `test :app:assembleDebug`: passed with the refactored Compose presentation
  layer.
- `test :app:assembleRelease`: passed R8 shrinking, resource shrinking, and
  release lint checks.
- `:app:assembleRelease -PpreviewSigning=true`: passed; APK signature v2 was
  verified with `apksigner`. The preview key is a debug key by design.
- The current Windows session has no running Android AVD, so this pass records
  package-level verification; the prior Pixel API 35 screen smoke test remains
  the UI baseline.

The generated Gradle wrapper is included for Android Studio/CI. The local sandbox cannot download a wrapper distribution, so the verification command uses the already-installed Gradle 8.11.1 binary.

## iOS

The SwiftUI source and Xcode project target iOS/iPadOS 17 and are ready to open in Xcode 15+. The Xcode project statically references all nine Swift source files, including the new repository and store boundaries. This Windows environment has no Xcode toolchain, so an on-device iOS build remains a macOS CI/reviewer step.

The pull-request workflow `.github/workflows/ios-mobile-validation.yml` now builds
the simulator target on `macos-14` with signing disabled. This keeps SwiftUI
compiler coverage in the same review loop as the Android checks.
