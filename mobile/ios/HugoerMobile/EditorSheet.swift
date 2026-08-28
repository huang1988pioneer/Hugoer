import SwiftUI

struct ArticleEditorView: View {
    let article: Article
    let previewBaseURL: String?
    let onSave: (String, String) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var title: String
    @State private var markdown: String
    @State private var preview = false

    init(article: Article, previewBaseURL: String? = nil, onSave: @escaping (String, String) -> Void) {
        self.article = article
        self.previewBaseURL = previewBaseURL
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
                    Text(article.path)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    TextField("標題", text: $title)
                        .textFieldStyle(.roundedBorder)
                        .font(.title3.weight(.semibold))
                    if preview {
                        ArticlePreview(title: title, markdown: markdown, baseURL: previewBaseURL)
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
