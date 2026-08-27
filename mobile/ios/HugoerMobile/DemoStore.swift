import Foundation

/// Compatibility name for integrations that used the original demo store.
/// New code should depend on `HugoerStore` and inject a `HugoerRepository`.
@available(*, deprecated, renamed: "HugoerStore")
typealias DemoStore = HugoerStore
