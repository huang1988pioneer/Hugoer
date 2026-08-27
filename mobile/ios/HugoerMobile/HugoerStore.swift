import Combine
import Foundation

/// Observable presentation state shared by all tabs. Screens render this
/// state and send intents here rather than reaching into repository details.
@MainActor
final class HugoerStore: ObservableObject {
    private let repository: any HugoerRepository

    @Published private(set) var site: Site
    @Published private(set) var articles: [Article]
    @Published private(set) var deployments: [Deployment]
    @Published private(set) var latestDeploymentMessage: String
    @Published private(set) var isDeploying = false
    @Published private(set) var isRefreshing = false
    @Published private(set) var errorMessage: String?

    init(repository: any HugoerRepository = DemoRepository()) {
        self.repository = repository
        let snapshot = repository.snapshot
        site = snapshot.site
        articles = snapshot.articles
        deployments = snapshot.deployments
        latestDeploymentMessage = snapshot.latestDeploymentMessage
    }

    func refresh() async {
        guard !isRefreshing else { return }
        isRefreshing = true
        defer { isRefreshing = false }
        do {
            apply(try await repository.refresh())
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    @discardableResult
    func save(articleID: String, title: String, body: String) -> Bool {
        do {
            apply(try repository.save(articleID: articleID, title: title, body: body))
            errorMessage = nil
            return true
        } catch {
            errorMessage = error.localizedDescription
            return false
        }
    }

    @discardableResult
    func createArticle() -> Article {
        let result = repository.createArticle()
        apply(result.1)
        errorMessage = nil
        return result.0
    }

    func triggerDeployment() async {
        guard !isDeploying else { return }
        isDeploying = true
        defer { isDeploying = false }
        do {
            apply(try await repository.triggerDeployment())
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func clearError() {
        errorMessage = nil
    }

    private func apply(_ snapshot: HugoerSnapshot) {
        site = snapshot.site
        articles = snapshot.articles
        deployments = snapshot.deployments
        latestDeploymentMessage = snapshot.latestDeploymentMessage
    }
}
