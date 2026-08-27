# Hugoer Mobile

Native Android and iOS companions for the Hugoer desktop workbench in the parent project.

## Product shape

Mobile keeps the reference app's Hugo concepts but narrows the first release to the work that benefits from a phone or tablet:

- see the selected repository/site and its Pages health;
- browse, search, edit, and preview Markdown articles;
- deliberately trigger an existing GitHub Actions deployment;
- inspect deployment history and hand off desktop-only work such as local Hugo installation, preview servers, themes, menus, and bulk migration.

Both implementations use a deterministic `DemoStore` adapter with synthetic sample content. It is an explicit seam for a future authenticated GitHub/desktop bridge; credentials and remote writes are not hidden behind the demo.

## Android

Open `android` in Android Studio or run `gradlew.bat :app:assembleDebug` from `mobile/android`. The app targets Android 8.0+ (API 26), compiles with API 35, and uses Jetpack Compose + Material 3. Compact windows use a Material navigation bar; expanded windows (600 dp+) use a navigation rail.

## iOS

Open `ios/HugoerMobile.xcodeproj` in Xcode 15 or newer and run the `HugoerMobile` scheme. The app targets iOS/iPadOS 17, uses SwiftUI `TabView`/`NavigationStack`, system materials, SF Symbols, safe areas, Dynamic Type, and both orientations.

## Design contract

The product and direction assumptions are recorded in `PRODUCT.md` and `docs/mobile-design-direction.md`. The visual idea is a release dispatch board: draft → preview → queue → live. Android and iOS translate that idea into their own platform controls instead of sharing a web shell.
