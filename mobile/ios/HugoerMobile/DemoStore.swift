import Combine
import Foundation

/// Deterministic adapter for the mobile UI. Replace this boundary with the
/// authenticated GitHub/desktop bridge without changing the screens.
@MainActor
final class DemoStore: ObservableObject {
    let site = Site(
        name: "Hugoer Journal",
        repository: "huang1988pioneer/hugoer-journal",
        branch: "main",
        pagesURL: "huang1988pioneer.github.io/hugoer-journal",
        lastCommit: "a91c2e7",
        lastSynced: "8 分鐘前"
    )

    @Published var articles: [Article] = [
        Article(
            id: "welcome",
            title: "把 Hugo 站帶在身上",
            path: "content/post/mobile-workflow.md",
            updated: "今天 09:42",
            status: .draft,
            excerpt: "一份給遠端工作日的內容工作流筆記。",
            body: "---\ntitle: \"把 Hugo 站帶在身上\"\ndraft: true\n---\n\n手機適合做小而安全的發布決策。先看站點狀態，再決定要不要把這次變更送上線。"
        ),
        Article(
            id: "shortcodes",
            title: "短碼與媒體預覽的整理",
            path: "content/post/shortcodes.md",
            updated: "昨天 18:20",
            status: .published,
            excerpt: "讓圖片、影音與 PDF 在編輯流程裡保持可見。",
            body: "---\ntitle: \"短碼與媒體預覽的整理\"\ndraft: false\n---\n\nHugo 的內容不只是一段文字。預覽應該保留媒體的比例、語意與來源。"
        ),
        Article(
            id: "migration",
            title: "從 Jekyll 搬到 Hugo 的三個檢查點",
            path: "content/post/migration-checklist.md",
            updated: "2026/08/21",
            status: .published,
            excerpt: "先保留來源，再處理 front matter 與 permalink。",
            body: "---\ntitle: \"從 Jekyll 搬到 Hugo 的三個檢查點\"\ndraft: false\n---\n\n1. 保留原始 front matter。\n2. 檢查內容路徑與媒體。\n3. 在部署前比對公開網址。"
        )
    ]

    @Published var deployments: [Deployment] = [
        Deployment(id: "#184", message: "把 Hugo 站帶在身上", time: "今天 09:48", status: .live, duration: "41 秒"),
        Deployment(id: "#183", message: "短碼與媒體預覽的整理", time: "昨天 18:22", status: .live, duration: "38 秒"),
        Deployment(id: "#182", message: "更新 Stack 設定", time: "昨天 16:10", status: .failed, duration: "—")
    ]

    @Published var isDeploying = false
    @Published var latestDeploymentMessage = "線上版本與 main 同步"

    func save(articleID: String, title: String, body: String) {
        guard let index = articles.firstIndex(where: { $0.id == articleID }) else { return }
        let excerpt = body
            .split(separator: "\n")
            .map(String.init)
            .first(where: { !$0.isEmpty && !$0.hasPrefix("---") })
            .map { String($0.prefix(64)) }
        articles[index].title = title.isEmpty ? "未命名文章" : title
        articles[index].body = body
        articles[index].excerpt = excerpt ?? articles[index].excerpt
        articles[index].updated = "剛剛"
        articles[index].status = .draft
    }

    @discardableResult
    func createArticle() -> Article {
        let count = articles.count + 1
        let article = Article(
            id: "new-\(count)",
            title: "未命名文章",
            path: "content/post/new-post-\(count).md",
            updated: "剛剛",
            status: .draft,
            excerpt: "從手機開始一篇新的 Hugo 文章。",
            body: "---\ntitle: \"未命名文章\"\ndraft: true\n---\n\n在這裡寫下內容……"
        )
        articles.insert(article, at: 0)
        return article
    }

    func triggerDeployment() async {
        guard !isDeploying else { return }
        isDeploying = true
        latestDeploymentMessage = "GitHub Actions 已排入佇列"
        try? await Task.sleep(for: .milliseconds(1400))
        let nextNumber = 185 + deployments.count - 3
        deployments.insert(
            Deployment(id: "#\(nextNumber)", message: "從 Hugoer Mobile 發布", time: "剛剛", status: .live, duration: "39 秒"),
            at: 0
        )
        latestDeploymentMessage = "已發布到 GitHub Pages"
        isDeploying = false
    }
}
