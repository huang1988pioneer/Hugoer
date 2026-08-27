import Foundation

enum ArticleStatus: String, CaseIterable, Identifiable, Hashable {
    case draft = "草稿"
    case published = "已發布"

    var id: String { rawValue }
}

struct Site {
    let name: String
    let repository: String
    let branch: String
    let pagesURL: String
    let lastCommit: String
    let lastSynced: String
}

struct Article: Identifiable, Hashable {
    let id: String
    var title: String
    let path: String
    var updated: String
    var status: ArticleStatus
    var excerpt: String
    var body: String
}

enum DeploymentStatus: String {
    case live = "線上"
    case building = "建置中"
    case failed = "需要注意"
}

struct Deployment: Identifiable {
    let id: String
    let message: String
    let time: String
    let status: DeploymentStatus
    let duration: String
}
