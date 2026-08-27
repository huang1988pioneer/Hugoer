# Hugoer Mobile v2.0.0

## Architecture

- Android now separates domain models, repository/data logic, presentation
  state, and Compose views.
- iOS now separates the replaceable repository adapter from the observable
  SwiftUI store.
- Both platforms have one mutation path for refresh, save, article creation,
  and intentional deployment.

## Quality and delivery

- Repository tests cover Markdown front matter extraction and duplicate
  deployment prevention.
- The Android release is optimized and verified before packaging.
- GitHub Actions releases two APKs: an installable preview-signed QA build and
  an optimized unsigned artifact for production signing.

## APK use

`HugoerMobile-mobile-v2.0.0-preview-signed.apk` is signed only with the
Android debug key for device QA. Do not submit it to Google Play.

`HugoerMobile-mobile-v2.0.0-unsigned.apk` must be signed with the production
upload key before external distribution.
