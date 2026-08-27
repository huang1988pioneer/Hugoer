import SwiftUI

struct SitePill: View {
    let label: String
    var systemImage = "checkmark.seal.fill"
    var tint = HugoerPalette.cyan

    var body: some View {
        Label(label, systemImage: systemImage)
            .font(.caption.weight(.semibold))
            .foregroundStyle(tint)
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .background(tint.opacity(0.14), in: Capsule())
    }
}

struct SectionHeading: View {
    let title: String
    var action: String?
    var onAction: (() -> Void)?

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            Text(title)
                .font(.headline)
            Spacer()
            if let action, let onAction {
                Button(action, action: onAction)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(HugoerPalette.cyan)
            }
        }
    }
}

struct DispatchRail: View {
    let currentIndex: Int
    private let stages = ["草稿", "預覽", "佇列", "線上"]

    var body: some View {
        VStack(spacing: 8) {
            HStack(spacing: 0) {
                ForEach(stages.indices, id: \.self) { index in
                    Circle()
                        .fill(index <= currentIndex ? HugoerPalette.cyan : Color.secondary.opacity(0.24))
                        .frame(width: 28, height: 28)
                        .overlay {
                            if index < currentIndex {
                                Image(systemName: "checkmark")
                                    .font(.caption.weight(.bold))
                                    .foregroundStyle(.white)
                            } else {
                                Circle()
                                    .fill(index <= currentIndex ? .white.opacity(0.9) : .secondary.opacity(0.5))
                                    .frame(width: 8, height: 8)
                            }
                        }
                    if index < stages.count - 1 {
                        Rectangle()
                            .fill(index < currentIndex ? HugoerPalette.cyan : Color.secondary.opacity(0.24))
                            .frame(height: 2)
                    }
                }
            }
            HStack {
                ForEach(stages.indices, id: \.self) { index in
                    Text(stages[index])
                        .font(.caption.weight(index == currentIndex ? .bold : .regular))
                        .foregroundStyle(index == currentIndex ? HugoerPalette.cyan : .secondary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        }
    }
}

struct SiteIdentity: View {
    let site: Site

    var body: some View {
        HStack(spacing: 14) {
            Text("H")
                .font(.title.bold())
                .foregroundStyle(.primary)
                .frame(width: 52, height: 52)
                .background(HugoerPalette.cyan.opacity(0.22), in: RoundedRectangle(cornerRadius: 16, style: .continuous))
            VStack(alignment: .leading, spacing: 4) {
                Text(site.name)
                    .font(.title3.bold())
                Text("\(site.branch) · 最近同步 \(site.lastSynced)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            SitePill(label: "健康")
        }
    }
}

struct ArticleRow: View {
    let article: Article
    let onTap: () -> Void

    var body: some View {
        Button(action: onTap) {
            HStack(spacing: 14) {
                Image(systemName: article.status == .published ? "checkmark.circle.fill" : "pencil.circle.fill")
                    .font(.title3)
                    .foregroundStyle(article.status == .published ? HugoerPalette.cyan : HugoerPalette.amber)
                VStack(alignment: .leading, spacing: 4) {
                    Text(article.title)
                        .font(.body.weight(.semibold))
                        .foregroundStyle(.primary)
                        .lineLimit(1)
                    Text(article.excerpt)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                    Text("\(article.path) · \(article.updated)")
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                        .lineLimit(1)
                }
                Spacer(minLength: 8)
                Image(systemName: "chevron.right")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.tertiary)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .padding(16)
        .background(HugoerPalette.surface, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        .accessibilityHint("開啟文章編輯器")
    }
}

struct DeploymentRow: View {
    let deployment: Deployment

    private var tint: Color {
        switch deployment.status {
        case .live: HugoerPalette.cyan
        case .building: HugoerPalette.amber
        case .failed: .red
        }
    }

    private var icon: String {
        switch deployment.status {
        case .live: "checkmark.circle.fill"
        case .building: "arrow.triangle.2.circlepath"
        case .failed: "exclamationmark.triangle.fill"
        }
    }

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: icon)
                .font(.title3)
                .foregroundStyle(tint)
            VStack(alignment: .leading, spacing: 4) {
                Text(deployment.message)
                    .font(.body.weight(.medium))
                    .lineLimit(1)
                Text("\(deployment.id) · \(deployment.time)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            VStack(alignment: .trailing, spacing: 4) {
                Text(deployment.status.rawValue)
                    .font(.caption.weight(.semibold))
                Text(deployment.duration)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(14)
        .background(HugoerPalette.surface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
    }
}

struct ActivityRow: View {
    let title: String
    let detail: String
    let time: String
    var systemImage = "clock.arrow.circlepath"
    var onTap: (() -> Void)?

    var body: some View {
        Group {
            if let onTap {
                Button(action: onTap) { content }
                    .buttonStyle(.plain)
            } else {
                content
            }
        }
    }

    private var content: some View {
        HStack(spacing: 12) {
            Image(systemName: systemImage)
                .foregroundStyle(HugoerPalette.cyan)
                .frame(width: 28, height: 28)
                .background(HugoerPalette.cyan.opacity(0.13), in: Circle())
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(.body.weight(.medium)).lineLimit(1)
                Text("\(detail) · \(time)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer()
        }
        .padding(14)
    }
}
