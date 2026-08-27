import SwiftUI

@main
struct HugoerMobileApp: App {
    @StateObject private var store = DemoStore()
    @State private var selection = 0
    @State private var editorArticle: Article?
    @State private var toastMessage: String?

    var body: some Scene {
        WindowGroup {
            TabView(selection: $selection) {
                OverviewView(
                    onSelectArticles: { selection = 1 },
                    onSelectDeploy: { selection = 2 },
                    onOpenArticle: { editorArticle = $0 }
                )
                .tabItem { Label("總覽", systemImage: "house") }
                .tag(0)

                ArticlesView(
                    onOpenArticle: { editorArticle = $0 },
                    onCreateArticle: { editorArticle = store.createArticle() }
                )
                .tabItem { Label("文章", systemImage: "doc.text") }
                .tag(1)

                DeployView(onOpenArticle: { editorArticle = $0 })
                    .tabItem { Label("發布", systemImage: "icloud.and.arrow.up") }
                    .tag(2)

                MoreView(onAction: { toastMessage = $0 })
                    .tabItem { Label("更多", systemImage: "ellipsis") }
                    .tag(3)
            }
            .tint(HugoerPalette.cyan)
            .environmentObject(store)
            .sheet(item: $editorArticle) { article in
                ArticleEditorView(article: article) { title, body in
                    store.save(articleID: article.id, title: title, body: body)
                    toastMessage = "草稿已儲存"
                }
            }
            .overlay(alignment: .top) {
                if let toastMessage {
                    Text(toastMessage)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(.primary)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 11)
                        .background(.regularMaterial, in: Capsule())
                        .padding(.top, 8)
                        .transition(.move(edge: .top).combined(with: .opacity))
                        .task {
                            try? await Task.sleep(for: .seconds(2))
                            withAnimation { self.toastMessage = nil }
                        }
                }
            }
        }
    }
}
