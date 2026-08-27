# Hugoer Mobile v2.0.1

## Delivery

- Android unit tests and optimized release packaging run before publishing.
- iOS/iPadOS 17 is archived on a macOS runner and packaged as an IPA.
- When Apple signing secrets are present, the release includes an ad-hoc
  signed IPA; otherwise it includes a clearly labelled unsigned IPA for
  downstream signing.
- The release contains both Android APK variants: preview-signed QA and
  production-signing-ready unsigned.

The workflow can also be started manually. To publish a manual run, enable
`publish` and enter `mobile-v2.0.1` as `release_tag`. An existing tag is built
from its tagged commit; a new tag is created at the selected workflow commit.

## Signing

The preview APK uses the Android debug key and is for QA only. The unsigned
APK and unsigned IPA are not installable until signed with the production
credentials for their platform.
