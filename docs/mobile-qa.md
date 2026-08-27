# Mobile QA record

## Android

- Build: `C:\Users\chbon\.gradle\wrapper\dists\gradle-8.11.1-bin\bpt9gzteqjrbo1mjrsomdt32c\gradle-8.11.1\bin\gradle.bat --project-cache-dir D:\codex\HugoerMobile\.gradle-cache :app:assembleDebug`
- Result: `BUILD SUCCESSFUL` (Android Gradle Plugin 8.10.1, compile/target SDK 35).
- Runtime: installed and launched on a Pixel API 35 emulator (`HugoerPixel`, `emulator-5554`).
- Verified screens: Overview (light and dark), Articles, Deploy, editor sheet, and publish confirmation dialog.
- Evidence is captured locally under `.impeccable/review/` and intentionally ignored from source control.

The generated Gradle wrapper is included for Android Studio/CI. The local sandbox cannot download a wrapper distribution, so the verification command uses the already-installed Gradle 8.11.1 binary.

## iOS

The SwiftUI source and Xcode project target iOS/iPadOS 17 and are ready to open in Xcode 15+. This Windows environment has no Xcode toolchain, so an on-device iOS build remains a macOS CI/reviewer step.
