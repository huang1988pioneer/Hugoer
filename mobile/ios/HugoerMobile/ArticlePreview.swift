import AVKit
import Foundation
import PDFKit
import SwiftUI
import UIKit

enum ArticlePreviewBlock {
    case heading(text: String, level: Int)
    case paragraph(String)
    case quote(String)
    case listItem(text: String, ordered: Bool)
    case code(String)
    case image(alt: String, source: String)
    case video(source: String)
    case pdf(source: String)
}

enum PreviewMediaKind: Equatable {
    case image
    case video
    case pdf
}

enum ArticlePreviewParser {
    static func parse(_ markdown: String) -> [ArticlePreviewBlock] {
        var blocks: [ArticlePreviewBlock] = []
        var paragraphLines: [String] = []
        var codeLines: [String] = []
        var inCodeFence = false

        func flushParagraph() {
            guard !paragraphLines.isEmpty else { return }
            blocks.append(.paragraph(paragraphLines.joined(separator: " ").trimmingCharacters(in: .whitespacesAndNewlines)))
            paragraphLines.removeAll()
        }

        func flushCode() {
            guard !codeLines.isEmpty else { return }
            blocks.append(.code(codeLines.joined(separator: "\n")))
            codeLines.removeAll()
        }

        for rawLine in stripFrontMatter(markdown).components(separatedBy: .newlines) {
            let line = rawLine.trimmingCharacters(in: .whitespacesAndNewlines)
            if inCodeFence {
                if line.hasPrefix("```") {
                    inCodeFence = false
                    flushCode()
                } else {
                    codeLines.append(rawLine)
                }
                continue
            }
            if line.hasPrefix("```") {
                flushParagraph()
                inCodeFence = true
                continue
            }
            if line.isEmpty {
                flushParagraph()
                continue
            }

            if let heading = heading(from: line) {
                flushParagraph()
                blocks.append(.heading(text: cleanInlineMarkdown(heading.text), level: heading.level))
                continue
            }
            if let image = markdownLink(from: line, image: true) {
                flushParagraph()
                blocks.append(.image(alt: image.label, source: normalizeSource(image.source)))
                continue
            }
            if let source = attribute("src", in: line), line.range(of: "<img", options: [.caseInsensitive]) != nil {
                flushParagraph()
                blocks.append(.image(alt: "文章圖片", source: normalizeSource(source)))
                continue
            }
            let isVideoTag = line.range(of: "<video", options: [.caseInsensitive]) != nil ||
                line.range(of: "<source", options: [.caseInsensitive]) != nil
            if let source = attribute("src", in: line), isVideoTag {
                flushParagraph()
                blocks.append(.video(source: normalizeSource(source)))
                continue
            }
            let isPDFTag = line.range(of: "<iframe", options: [.caseInsensitive]) != nil ||
                line.range(of: "<embed", options: [.caseInsensitive]) != nil ||
                line.range(of: "<object", options: [.caseInsensitive]) != nil
            if let source = attribute("src", in: line) ?? attribute("data", in: line),
               isPDFTag, mediaKind(source, label: line) == .pdf {
                flushParagraph()
                blocks.append(.pdf(source: normalizeSource(source)))
                continue
            }
            if let shortcode = shortcode(from: line), let source = shortcodeSource(shortcode.attributes) {
                flushParagraph()
                switch shortcode.name {
                case "figure", "image", "img":
                    blocks.append(.image(alt: "文章圖片", source: normalizeSource(source)))
                case "video":
                    blocks.append(.video(source: normalizeSource(source)))
                case "pdf":
                    blocks.append(.pdf(source: normalizeSource(source)))
                default:
                    break
                }
                continue
            }
            if let link = markdownLink(from: line, image: false) {
                let source = normalizeSource(link.source)
                switch mediaKind(source, label: link.label) {
                case .image:
                    flushParagraph()
                    blocks.append(.image(alt: link.label, source: source))
                    continue
                case .video:
                    flushParagraph()
                    blocks.append(.video(source: source))
                    continue
                case .pdf:
                    flushParagraph()
                    blocks.append(.pdf(source: source))
                    continue
                case nil:
                    break
                }
            }
            if let kind = mediaKind(line) {
                flushParagraph()
                switch kind {
                case .image:
                    blocks.append(.image(alt: "文章圖片", source: normalizeSource(line)))
                case .video:
                    blocks.append(.video(source: normalizeSource(line)))
                case .pdf:
                    blocks.append(.pdf(source: normalizeSource(line)))
                }
                continue
            }
            if line.hasPrefix(">") {
                flushParagraph()
                blocks.append(.quote(cleanInlineMarkdown(String(line.dropFirst()).trimmingCharacters(in: .whitespaces))))
                continue
            }
            if let first = line.first, "-*+".contains(first), String(line.dropFirst()).hasPrefix(" ") {
                flushParagraph()
                blocks.append(.listItem(text: cleanInlineMarkdown(String(line.dropFirst()).trimmingCharacters(in: .whitespaces)), ordered: false))
                continue
            }
            if let ordered = capture(#"^\d+[.)]\s+(.+)$"#, in: line) {
                flushParagraph()
                blocks.append(.listItem(text: cleanInlineMarkdown(ordered), ordered: true))
                continue
            }
            paragraphLines.append(cleanInlineMarkdown(line))
        }

        if inCodeFence { flushCode() }
        flushParagraph()
        return blocks
    }

    static func mediaKind(_ source: String, label: String = "") -> PreviewMediaKind? {
        let candidate = normalizeSource(source).lowercased()
        if candidate.hasPrefix("data:image/") { return .image }
        if candidate.hasPrefix("data:video/") { return .video }
        if candidate.hasPrefix("data:application/pdf") { return .pdf }
        let path = URL(string: candidate)?.path ?? candidate.split(separator: "?").first.map(String.init) ?? candidate
        let extensionName = (path as NSString).pathExtension.lowercased()
        if ["png", "jpg", "jpeg", "gif", "webp", "avif", "heic", "svg"].contains(extensionName) { return .image }
        if ["mp4", "m4v", "mov", "webm", "mkv", "avi", "3gp"].contains(extensionName) { return .video }
        if extensionName == "pdf" || label.lowercased().contains("pdf") { return .pdf }
        return nil
    }

    static func normalizeSource(_ source: String) -> String {
        source.trimmingCharacters(in: .whitespacesAndNewlines).trimmingCharacters(in: CharacterSet(charactersIn: "<>"))
    }

    static func cleanInlineMarkdown(_ text: String) -> String {
        text
            .replacingOccurrences(of: #"!\[([^\]]*)\]\([^)]*\)"#, with: "$1", options: .regularExpression)
            .replacingOccurrences(of: #"\[([^\]]+)\]\([^)]*\)"#, with: "$1", options: .regularExpression)
            .replacingOccurrences(of: #"[*_~`]"#, with: "", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func stripFrontMatter(_ markdown: String) -> String {
        var lines = markdown.components(separatedBy: .newlines)
        guard let delimiter = lines.first?.trimmingCharacters(in: .whitespacesAndNewlines),
              ["---", "+++", ";;;"].contains(delimiter) else { return markdown }
        for index in 1..<lines.count where lines[index].trimmingCharacters(in: .whitespacesAndNewlines) == delimiter {
            lines.removeFirst(index + 1)
            return lines.joined(separator: "\n")
        }
        return markdown
    }

    private static func heading(from line: String) -> (text: String, level: Int)? {
        let hashes = line.prefix { $0 == "#" }
        guard !hashes.isEmpty, hashes.count <= 6 else { return nil }
        let remainder = String(line.dropFirst(hashes.count))
        guard remainder.first == " " else { return nil }
        return (String(remainder.drop(while: { $0 == " " })), hashes.count)
    }

    private static func markdownLink(from line: String, image: Bool) -> (label: String, source: String)? {
        let prefix = image ? "![" : "["
        guard line.hasPrefix(prefix), let close = line.firstIndex(of: "]") else { return nil }
        let afterClose = line.index(after: close)
        guard afterClose < line.endIndex, line[afterClose] == "(" else { return nil }
        guard line.hasSuffix(")") else { return nil }
        let sourceStart = line.index(after: afterClose)
        let sourceEnd = line.index(before: line.endIndex)
        guard sourceStart <= sourceEnd else { return nil }
        let destination = String(line[sourceStart..<sourceEnd])
        let source = destination.split(whereSeparator: { $0 == " " || $0 == "\t" }).first.map(String.init) ?? destination
        return (String(line[line.index(line.startIndex, offsetBy: image ? 2 : 1)..<close]), source)
    }

    private static func attribute(_ name: String, in line: String) -> String? {
        guard let regex = try? NSRegularExpression(pattern: #"(?i)\b"# + name + #"\s*=\s*["']([^"']+)["']"#) else { return nil }
        let range = NSRange(line.startIndex..<line.endIndex, in: line)
        guard let match = regex.firstMatch(in: line, range: range), match.numberOfRanges > 1,
              let valueRange = Range(match.range(at: 1), in: line) else { return nil }
        return String(line[valueRange])
    }

    private static func capture(_ pattern: String, in line: String) -> String? {
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let match = regex.firstMatch(in: line, range: NSRange(line.startIndex..<line.endIndex, in: line)),
              match.numberOfRanges > 1,
              let range = Range(match.range(at: 1), in: line) else { return nil }
        return String(line[range])
    }

    private static func shortcode(from line: String) -> (name: String, attributes: String)? {
        guard line.hasPrefix("{{<"), line.hasSuffix(">}}") else { return nil }
        let start = line.index(line.startIndex, offsetBy: 3)
        let end = line.index(line.endIndex, offsetBy: -3)
        let inner = String(line[start..<end]).trimmingCharacters(in: .whitespacesAndNewlines)
        guard let name = inner.split(separator: " ").first else { return nil }
        return (name.lowercased(), String(inner.dropFirst(name.count)))
    }

    private static func shortcodeSource(_ attributes: String) -> String? {
        for name in ["src", "url", "data", "file"] {
            if let value = attribute(name, in: attributes) { return value }
        }
        if let quoted = capture(#"["']([^"']+)["']"#, in: attributes) { return quoted }
        return nil
    }
}

struct ArticlePreview: View {
    let title: String
    let markdown: String
    let baseURL: String?

    private var blocks: [ArticlePreviewBlock] { ArticlePreviewParser.parse(markdown) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Label(title.isEmpty ? "未命名文章" : title, systemImage: "doc.text")
                .font(.headline)
                .foregroundStyle(HugoerPalette.cyan)
            if blocks.isEmpty {
                Text("輸入 Markdown 後，文章與媒體會在這裡顯示。")
                    .font(.body)
                    .foregroundStyle(.secondary)
            } else {
                ForEach(Array(blocks.enumerated()), id: \.offset) { _, block in
                    ArticlePreviewBlockView(block: block, baseURL: baseURL)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(16)
        .background(HugoerPalette.surface, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
    }
}

private struct ArticlePreviewBlockView: View {
    let block: ArticlePreviewBlock
    let baseURL: String?

    var body: some View {
        switch block {
        case let .heading(text, level):
            Text(text)
                .font(level == 1 ? .title2.bold() : level == 2 ? .title3.weight(.semibold) : .headline)
        case let .paragraph(text):
            Text(text).font(.body)
        case let .quote(text):
            Text(text)
                .font(.body)
                .padding(.horizontal, 14)
                .padding(.vertical, 11)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(HugoerPalette.cyan.opacity(0.12), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        case let .listItem(text, ordered):
            HStack(alignment: .top, spacing: 8) {
                Text(ordered ? "1." : "•")
                    .foregroundStyle(HugoerPalette.cyan)
                Text(text).frame(maxWidth: .infinity, alignment: .leading)
            }
            .font(.body)
        case let .code(text):
            ScrollView(.horizontal, showsIndicators: false) {
                Text(text)
                    .font(.body.monospaced())
                    .padding(14)
            }
            .background(HugoerPalette.surfaceElevated, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        case let .image(alt, source):
            PreviewMediaCard(kind: .image, title: alt.isEmpty ? "文章圖片" : alt, source: source, baseURL: baseURL)
        case let .video(source):
            PreviewMediaCard(kind: .video, title: "文章影片", source: source, baseURL: baseURL)
        case let .pdf(source):
            PreviewMediaCard(kind: .pdf, title: "PDF 文件", source: source, baseURL: baseURL)
        }
    }
}

private struct PreviewMediaCard: View {
    let kind: PreviewMediaKind
    let title: String
    let source: String
    let baseURL: String?

    private var resolvedSource: String { resolvePreviewSource(source, baseURL: baseURL) }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Label(title, systemImage: iconName)
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(HugoerPalette.cyan)
            if resolvedSource.isEmpty {
                MediaUnavailable(source: source, reason: "需要完整網址，或可讀取的本機檔案。")
            } else {
                switch kind {
                case .image:
                    ImagePreview(source: resolvedSource, originalSource: source)
                case .video:
                    VideoPreview(source: resolvedSource, originalSource: source)
                case .pdf:
                    PDFPreview(source: resolvedSource, originalSource: source)
                }
            }
        }
    }

    private var iconName: String {
        switch kind {
        case .image: return "photo"
        case .video: return "video"
        case .pdf: return "doc.richtext"
        }
    }
}

private struct ImagePreview: View {
    let source: String
    let originalSource: String
    @State private var reloadID = UUID()

    var body: some View {
        Group {
            if let localImage = localImage(from: source) {
                Image(uiImage: localImage)
                    .resizable()
                    .scaledToFit()
                    .frame(maxWidth: .infinity, maxHeight: 360)
            } else if let url = allowedURL(from: source) {
                AsyncImage(url: url, transaction: Transaction(animation: nil)) { phase in
                    switch phase {
                    case .empty:
                        MediaLoading(message: "正在載入圖片…")
                    case let .success(image):
                        image.resizable().scaledToFit().frame(maxWidth: .infinity, maxHeight: 360)
                    case .failure:
                        MediaUnavailable(source: originalSource, reason: "圖片無法載入，請確認網址可公開存取。", onRetry: { reloadID = UUID() })
                    @unknown default:
                        MediaUnavailable(source: originalSource, reason: "圖片格式或網址無法讀取。")
                    }
                }
                .id(reloadID)
            } else {
                MediaUnavailable(source: originalSource, reason: "圖片網址格式不受支援。")
            }
        }
        .frame(maxWidth: .infinity)
        .padding(8)
        .background(HugoerPalette.surfaceElevated, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .accessibilityLabel("文章圖片")
    }
}

private struct VideoPreview: View {
    let source: String
    let originalSource: String
    @State private var player: AVPlayer?
    @State private var failed = false
    @State private var reloadID = UUID()

    var body: some View {
        Group {
            if failed {
                MediaUnavailable(source: originalSource, reason: "影片無法播放；請確認網址是可直接播放的 MP4、M4V 或 WebM。", onRetry: {
                    failed = false
                    player = nil
                    reloadID = UUID()
                })
            } else if let player {
                VideoPlayer(player: player)
                    .frame(maxWidth: .infinity)
                    .frame(minHeight: 200, maxHeight: 320)
                    .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
            } else {
                MediaLoading(message: "正在準備影片…")
            }
        }
        .task(id: source + reloadID.uuidString) {
            failed = false
            player = nil
            guard let url = allowedURL(from: source), url.scheme?.lowercased() != "data" else {
                failed = true
                return
            }
            player = AVPlayer(url: url)
        }
        .onDisappear { player?.pause() }
    }
}

private struct PDFPreview: View {
    let source: String
    let originalSource: String
    @State private var document: PDFDocument?
    @State private var loading = false
    @State private var errorMessage: String?
    @State private var reloadID = UUID()

    var body: some View {
        Group {
            if let document {
                PDFKitPreview(document: document)
                    .frame(maxWidth: .infinity)
                    .frame(minHeight: 220, maxHeight: 440)
                    .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                Text(document.pageCount == 1 ? "PDF 預覽 · 第 1 頁" : "PDF 預覽 · 可捲動閱讀 (\(document.pageCount)) 頁")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            } else if loading {
                MediaLoading(message: "正在載入 PDF…")
            } else {
                MediaUnavailable(source: originalSource, reason: errorMessage ?? "PDF 格式或網址無法讀取。", onRetry: { reloadID = UUID() })
            }
        }
        .task(id: source + reloadID.uuidString) { await loadDocument() }
    }

    private func loadDocument() async {
        loading = true
        errorMessage = nil
        document = nil
        do {
            let data: Data
            if source.lowercased().hasPrefix("data:application/pdf"), let embedded = dataURLData(source) {
                data = embedded
            } else if let url = allowedURL(from: source), url.isFileURL {
                data = try Data(contentsOf: url)
            } else if let url = allowedURL(from: source) {
                let response: (Data, URLResponse) = try await URLSession.shared.data(from: url)
                if let http = response.1 as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                    throw URLError(.badServerResponse)
                }
                data = response.0
            } else {
                throw URLError(.unsupportedURL)
            }
            guard let loaded = PDFDocument(data: data), loaded.pageCount > 0 else {
                throw URLError(.cannotDecodeContentData)
            }
            document = loaded
        } catch {
            errorMessage = "下載或解析 PDF 失敗，請確認網路後重試。"
        }
        loading = false
    }
}

private struct PDFKitPreview: UIViewRepresentable {
    let document: PDFDocument

    func makeUIView(context: Context) -> PDFView {
        let view = PDFView()
        view.autoScales = true
        view.displayMode = .singlePageContinuous
        view.displayDirection = .vertical
        view.backgroundColor = .systemBackground
        view.document = document
        return view
    }

    func updateUIView(_ view: PDFView, context: Context) {
        view.document = document
    }
}

private struct MediaLoading: View {
    let message: String

    var body: some View {
        HStack(spacing: 9) {
            ProgressView()
            Text(message)
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, minHeight: 80)
    }
}

private struct MediaUnavailable: View {
    let source: String
    let reason: String
    var onRetry: (() -> Void)? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Label("媒體預覽不可用", systemImage: "exclamationmark.triangle")
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(.red)
            Text(reason).font(.caption).foregroundStyle(.secondary)
            Text(source).font(.caption2).foregroundStyle(.tertiary).lineLimit(2)
            if let onRetry {
                Button("重試", action: onRetry)
                    .font(.subheadline.weight(.semibold))
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .background(HugoerPalette.surfaceElevated, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}

private func resolvePreviewSource(_ source: String, baseURL: String?) -> String {
    let normalized = ArticlePreviewParser.normalizeSource(source)
    guard !normalized.isEmpty else { return "" }
    if normalized.lowercased().hasPrefix("data:") { return normalized }
    if let url = URL(string: normalized), let scheme = url.scheme?.lowercased() {
        return ["http", "https", "file"].contains(scheme) ? normalized : ""
    }
    let rawBase = baseURL?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    guard !rawBase.isEmpty else { return "" }
    let baseString = rawBase.range(of: #"^[a-zA-Z][a-zA-Z0-9+.-]*:"#, options: .regularExpression) == nil ? "https://" + rawBase : rawBase
    guard let base = URL(string: baseString.hasSuffix("/") ? baseString : baseString + "/"),
          let resolved = URL(string: normalized, relativeTo: base)?.absoluteURL else { return "" }
    if let scheme = resolved.scheme?.lowercased(), !["http", "https", "file"].contains(scheme) { return "" }
    return resolved.absoluteString
}

private func allowedURL(from source: String) -> URL? {
    guard let url = URL(string: source), let scheme = url.scheme?.lowercased(),
          ["https", "http", "file"].contains(scheme) else { return nil }
    return url
}

private func localImage(from source: String) -> UIImage? {
    if source.lowercased().hasPrefix("data:image/"), let comma = source.firstIndex(of: ",") {
        let payload = String(source[source.index(after: comma)...])
        if String(source[..<comma]).localizedCaseInsensitiveContains(";base64"), let data = Data(base64Encoded: payload) {
            return UIImage(data: data)
        }
    }
    guard let url = URL(string: source), url.isFileURL else { return nil }
    return UIImage(contentsOfFile: url.path)
}

private func dataURLData(_ source: String) -> Data? {
    guard let comma = source.firstIndex(of: ",") else { return nil }
    let metadata = String(source[..<comma])
    let payload = String(source[source.index(after: comma)...])
    if metadata.localizedCaseInsensitiveContains(";base64") {
        return Data(base64Encoded: payload)
    }
    return payload.removingPercentEncoding?.data(using: .utf8)
}
