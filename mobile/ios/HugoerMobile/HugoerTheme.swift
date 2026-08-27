import SwiftUI

enum HugoerPalette {
    static let cyan = Color(red: 0.28, green: 0.78, blue: 0.86)
    static let cyanSoft = Color(red: 0.71, green: 0.92, blue: 0.95)
    static let amber = Color(red: 0.98, green: 0.68, blue: 0.30)
    static let graphite = Color(uiColor: .systemBackground)
    static let surface = Color(uiColor: .secondarySystemBackground)
    static let surfaceElevated = Color(uiColor: .tertiarySystemBackground)
}

struct HugoerCardModifier: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding(16)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
    }
}

extension View {
    func hugoerCard() -> some View { modifier(HugoerCardModifier()) }
}
