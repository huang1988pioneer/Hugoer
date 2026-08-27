import Foundation

/// A value snapshot keeps the UI independent from the storage or network
/// implementation behind the mobile companion.
struct HugoerSnapshot {
    var site: Site
    var articles: [Article]
    var deployments: [Deployment]
    var latestDeploymentMessage: String
}

@MainActor
protocol HugoerRepository {
    var snapshot: HugoerSnapshot { get }

    func refresh() async throws -> HugoerSnapshot
    func save(articleID: String, title: String, body: String) throws -> HugoerSnapshot
    func createArticle() -> (Article, HugoerSnapshot)
    func triggerDeployment() async throws -> HugoerSnapshot
}

enum HugoerRepositoryError: LocalizedError {
    case articleNotFound

    var errorDescription: String? {
        switch self {
        case .articleNotFound:
            return "找不到要編輯的文章。"
        }
    }
}

/// Deterministic first-run adapter. A GitHub/desktop bridge can implement the
/// same protocol without requiring changes to any SwiftUI screen.
@MainActor
final class DemoRepository: HugoerRepository {
    private var currentSnapshot: HugoerSnapshot = .seed
    private var deploymentInFlight = false

    var snapshot: HugoerSnapshot { currentSnapshot }

    func refresh() async throws -> HugoerSnapshot {
        currentSnapshot.site.lastSynced = "剛剛"
        return currentSnapshot
    }

    func save(articleID: String, title: String, body: String) throws -> HugoerSnapshot {
        guard let index = currentSnapshot.articles.firstIndex(where: { $0.id == articleID }) else {
            throw HugoerRepositoryError.articleNotFound
        }

        var article = currentSnapshot.articles[index]
        article.title = title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "未命名文章" : title
        article.body = body
        article.excerpt = Self.excerpt(from: body, fallback: article.excerpt)
        article.updated = "剛剛"
        article.status = .draft
        currentSnapshot.articles[index] = article
        return currentSnapshot
    }

    func createArticle() -> (Article, HugoerSnapshot) {
        let number = currentSnapshot.articles.count + 1
        let article = Article(
            id: "new-\(number)",
            title: "未命名文章",
            path: "content/post/new-post-\(number).md",
            updated: "剛剛",
            status: .draft,
            excerpt: "從手機開始一篇新的 Hugo 文章。",
            body: "---\ntitle: \"未命名文章\"\ndraft: true\n---\n\n在這裡寫下內容……"
        )
        currentSnapshot.articles.insert(article, at: 0)
        return (article, currentSnapshot)
    }

    func triggerDeployment() async throws -> HugoerSnapshot {
        guard !deploymentInFlight else { return currentSnapshot }
        deploymentInFlight = true
        defer { deploymentInFlight = false }
        currentSnapshot.latestDeploymentMessage = "GitHub Actions 已排入佇列"
        try await Task.sleep(for: .milliseconds(1_400))

        let nextNumber = 185 + currentSnapshot.deployments.count - 3
        currentSnapshot.deployments.insert(
            Deployment(
                id: "#\(nextNumber)",
                message: "從 Hugoer Mobile 發布",
                time: "剛剛",
                status: .live,
                duration: "39 秒"
            ),
            at: 0
        )
        currentSnapshot.latestDeploymentMessage = "已發布到 GitHub Pages"
        return currentSnapshot
    }

    private static func excerpt(from body: String, fallback: String) -> String {
        let lines = body.components(separatedBy: .newlines)
        var contentStart = 0
        if lines.first?.trimmingCharacters(in: .whitespacesAndNewlines) == "---" {
            contentStart = 1
            while contentStart < lines.count {
                if lines[contentStart].trimmingCharacters(in: .whitespacesAndNewlines) == "---" {
                    contentStart += 1
                    break
                }
                contentStart += 1
            }
        }

        return lines.dropFirst(contentStart)
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .first(where: { !$0.isEmpty })
            .map { String($0.prefix(64)) } ?? fallback
    }
}

private extension HugoerSnapshot {
    static let seed = HugoerSnapshot(
        site: Site(
            name: "Hugoer Journal",
            repository: "huang1988pioneer/hugoer-journal",
            branch: "main",
            pagesURL: "huang1988pioneer.github.io/hugoer-journal",
            lastCommit: "a91c2e7",
            lastSynced: "8 分鐘前"
        ),
        articles: [
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
                body: "---\ntitle: \"短碼與媒體預覽的整理\"\ndraft: false\n---\n\nHugo 的內容不只是一段文字。預覽應該保留媒體的比例、語意與來源。\n\n![Hugoer 預覽示意圖](https://www.w3.org/Icons/w3c_home.png)\n\n[影片預覽示範](https://storage.googleapis.com/gtv-videos-bucket/sample/ForBiggerEscapes.mp4)\n\n[PDF 預覽示範](https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf)"
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
        ],
        deployments: [
            Deployment(id: "#184", message: "把 Hugo 站帶在身上", time: "今天 09:48", status: .live, duration: "41 秒"),
            Deployment(id: "#183", message: "短碼與媒體預覽的整理", time: "昨天 18:22", status: .live, duration: "38 秒"),
            Deployment(id: "#182", message: "更新 Stack 設定", time: "昨天 16:10", status: .failed, duration: "—")
        ],
        latestDeploymentMessage: "線上版本與 main 同步"
    )
}
