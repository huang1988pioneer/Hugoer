import SwiftUI

struct ArticleEditorView: View {
    let article: Article
    let onSave: (String, String) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var title: String
    @State private var markdown: String
    @State private var preview = false

    init(article: Article, onSave: @escaping (String, String) -> Void) {
        self.article = article
        self.onSave = onSave
        _title = State(initialValue: article.title)
        _markdown = State(initialValue: article.body)
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    Picker("模式", selection: $preview) {
                        Text("Markdown").tag(false)
                        Text("預覽").tag(true)
                    }
                    .pickerStyle(.segmented)
                    TextField("標題", text: $title)
                        .textFieldStyle(.roundedBorder)
                        .font(.title3.weight(.semibold))
                    if preview {
                        MarkdownPreview(markdown: markdown)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(4)
                    } else {
                        TextEditor(text: $markdown)
                            .font(.body.monospaced())
                            .frame(minHeight: 300)
                            .padding(8)
                            .background(HugoerPalette.surface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                    }
                    Text("儲存只會建立草稿；發布需在「發布」分頁再次確認。")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                .padding(20)
            }
            .navigationTitle("編輯文章")
            .navigationSubtitle(article.path)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("關閉") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("儲存") {
                        onSave(title, markdown)
                        dismiss()
                    }
                    .fontWeight(.semibold)
                }
            }
        }
    }
}

private struct MarkdownPreview: View {
    let markdown: String

    var bodyView: some View {
        VStack(alignment: .leading, spacing: 10) {
            ForEach(Array(markdown.split(separator: "\n").map(String.init).filter { !$0.isEmpty && !$0.hasPrefix("---") }.prefix(14).enumerated()), id: \.offset) { _, line in
                Text(line.trimmingCharacters(in: CharacterSet(charactersIn: "# ")))
                    .font(line.hasPrefix("#") ? .title2.bold() : .body)
            }
        }
    }

    var body: some View { bodyView }
}
