# Hugoer Mobile — iOS

This directory contains the native SwiftUI companion for Hugoer. Open `HugoerMobile.xcodeproj` in Xcode 15 or newer and run the `HugoerMobile` scheme on an iPhone or iPad simulator.

The UI uses a deterministic `DemoRepository` behind the observable
`HugoerStore`, so screens can be reviewed without credentials. Replace the
repository implementation with the authenticated GitHub/desktop bridge when
wiring production data; SwiftUI screens do not need to change. The app
deliberately keeps local Hugo installation, bulk migration, and local server
control as desktop hand-off tasks.

The first release targets iOS 17 and iPadOS 17, supports portrait and landscape, follows system safe areas and Dynamic Type, and uses native TabView, NavigationStack, sheets, alerts, and SF Symbols.
