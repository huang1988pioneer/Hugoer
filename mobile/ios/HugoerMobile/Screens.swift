import SwiftUI

struct OverviewView: View {
    @EnvironmentObject private var store: HugoerStore
    let onSelectArticles: () -> Void
    let onSelectDeploy: () -> Void
    let onOpenArticle: (Article) -> Void

    var body: some View {
        NavigationStack {
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 20) {
                    SiteIdentity(site: store.site)
                    DispatchBoard(onSelectDeploy: onSelectDeploy)
                    QuickActions(onEdit: onSelectArticles, onPreview: {}, onPublish: onSelectDeploy)
                    SectionHeading(title: "最近活動")
                        .padding(.top, 2)
                    VStack(spacing: 0) {
                        ActivityRow(
                            title: "草稿已更新",
                            detail: "把 Hugo 站帶在身上",
                            time: "今天 09:42",
                            systemImage: "pencil.circle.fill",
                            onTap: { if let article = store.articles.first { onOpenArticle(article) } }
                        )
                        Divider().padding(.leading, 54)
                        ActivityRow(
                            title: "Pages 部署完成",
                            detail: "#184 · 41 秒",
                            time: "今天 09:48",
                            systemImage: "checkmark.circle.fill",
                            onTap: onSelectDeploy
                        )
                    }
                    .background(HugoerPalette.surface, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
                }
                .padding(.horizontal, 20)
                .padding(.bottom, 28)
            }
            .navigationTitle("站點總覽")
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        Task { await store.refresh() }
                    } label: {
                        if store.isRefreshing {
                            ProgressView()
                        } else {
                            Image(systemName: "arrow.triangle.2.circlepath")
                        }
                    }
                    .disabled(store.isRefreshing)
                    .accessibilityLabel("同步站點")
                }
            }
            .refreshable { await store.refresh() }
        }
    }
}

private struct DispatchBoard: View {
    @EnvironmentObject private var store: HugoerStore
    let onSelectDeploy: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(alignment: .top, spacing: 12) {
                VStack(alignment: .leading, spacing: 4) {
                    Text("發布路線")
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(HugoerPalette.cyan)
                    Text("版本正在航行")
                        .font(.title2.bold())
                    Text(store.latestDeploymentMessage)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                Spacer(minLength: 10)
                SitePill(label: "\(store.site.lastCommit) · main", systemImage: "chevron.left.forwardslash.chevron.right")
            }
            DispatchRail(currentIndex: 3)
            HStack(alignment: .firstTextBaseline) {
                VStack(alignment: .leading, spacing: 3) {
                    Text("GitHub Pages")
                        .font(.subheadline.weight(.semibold))
                    Text(store.site.pagesURL)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                Spacer()
                Button("查看記錄", action: onSelectDeploy)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(HugoerPalette.cyan)
            }
        }
        .hugoerCard()
    }
}

private struct QuickActions: View {
    let onEdit: () -> Void
    let onPreview: () -> Void
    let onPublish: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            SectionHeading(title: "下一步")
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 10) {
                Button(action: onEdit) {
                    Label("編輯文章", systemImage: "pencil")
                }
                .buttonStyle(.borderedProminent)
                .tint(HugoerPalette.cyan)
                Button(action: onPreview) {
                    Label("預覽網站", systemImage: "eye")
                }
                .buttonStyle(.bordered)
                Button(action: onPublish) {
                    Label("發布", systemImage: "paperplane.fill")
                }
                .buttonStyle(.bordered)
                .tint(HugoerPalette.amber)
                }
            }
            .labelStyle(.titleAndIcon)
            .controlSize(.regular)
        }
    }
}

struct ArticlesView: View {
    @EnvironmentObject private var store: HugoerStore
    @State private var query = ""
    @State private var filter: ArticleStatus? = nil
    let onOpenArticle: (Article) -> Void
    let onCreateArticle: () -> Void

    private var filteredArticles: [Article] {
        store.articles.filter { article in
            let matchesQuery = query.isEmpty || article.title.localizedCaseInsensitiveContains(query) || article.path.localizedCaseInsensitiveContains(query)
            let matchesFilter = filter == nil || article.status == filter
            return matchesQuery && matchesFilter
        }
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 12) {
                    Text("\(store.articles.count) 篇 · repository content")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                    HStack(spacing: 8) {
                        ForEach([ArticleStatus?](arrayLiteral: nil) + ArticleStatus.allCases.map(Optional.some), id: \.self) { option in
                            FilterChip(
                                title: option?.rawValue ?? "全部",
                                isSelected: filter == option,
                                action: { filter = option }
                            )
                        }
                    }
                    .padding(.vertical, 4)
                    SectionHeading(title: "內容清單")
                    if filteredArticles.isEmpty {
                        ContentUnavailableView(
                            "找不到符合條件的文章",
                            systemImage: "doc.text.magnifyingglass",
                            description: Text("試試看其他關鍵字，或新增一篇草稿。")
                        )
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 42)
                    } else {
                        ForEach(filteredArticles) { article in
                            ArticleRow(article: article, onTap: { onOpenArticle(article) })
                        }
                    }
                }
                .padding(.horizontal, 20)
                .padding(.bottom, 28)
            }
            .navigationTitle("文章")
            .searchable(text: $query, placement: .navigationBarDrawer(displayMode: .always), prompt: "搜尋文章")
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button(action: onCreateArticle) { Image(systemName: "plus") }
                        .accessibilityLabel("新增文章")
                }
            }
            .refreshable { await store.refresh() }
        }
    }
}

private struct FilterChip: View {
    let title: String
    let isSelected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(isSelected ? .white : .primary)
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .background(isSelected ? HugoerPalette.cyan : HugoerPalette.surface, in: Capsule())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}

struct DeployView: View {
    @EnvironmentObject private var store: HugoerStore
    let onOpenArticle: (Article) -> Void
    @State private var showPublishConfirmation = false

    var body: some View {
        NavigationStack {
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 16) {
                    VStack(alignment: .leading, spacing: 14) {
                        Label("準備發布 \(store.site.branch)", systemImage: "icloud.and.arrow.up")
                            .font(.title3.bold())
                        Text("手機端只觸發既有的 GitHub Actions，不在裝置上執行 Hugo。發布前請確認草稿內容與 repository。")
                            .font(.subheadline)
                        Button {
                            showPublishConfirmation = true
                        } label: {
                            HStack {
                                if store.isDeploying { ProgressView().tint(.white) }
                                Text(store.isDeploying ? "正在排入佇列…" : "確認並發布")
                                Spacer()
                                Image(systemName: "paperplane.fill")
                            }
                            .frame(maxWidth: .infinity)
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(HugoerPalette.cyan)
                        .disabled(store.isDeploying)
                    }
                    .foregroundStyle(Color.primary)
                    .padding(20)
                    .background(HugoerPalette.cyan.opacity(0.18), in: RoundedRectangle(cornerRadius: 20, style: .continuous))
                    DispatchBoard(onSelectDeploy: {})
                    SectionHeading(title: "部署記錄")
                    ForEach(store.deployments) { deployment in
                        DeploymentRow(deployment: deployment)
                    }
                    SectionHeading(title: "最近變更", action: "查看文章", onAction: {
                        if let article = store.articles.first { onOpenArticle(article) }
                    })
                    if let article = store.articles.first {
                        ActivityRow(
                            title: article.title,
                            detail: "\(store.site.branch) · \(store.site.lastCommit)",
                            time: "未提交",
                            systemImage: "pencil.circle.fill",
                            onTap: { onOpenArticle(article) }
                        )
                        .background(HugoerPalette.surface, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
                    }
                }
                .padding(.horizontal, 20)
                .padding(.bottom, 28)
            }
            .navigationTitle("發布")
            .alert("發布到 GitHub Pages？", isPresented: $showPublishConfirmation) {
                Button("確認發布") {
                    Task { await store.triggerDeployment() }
                }
                Button("先不要", role: .cancel) {}
            } message: {
                Text("這會把目前 main 分支交給既有的 GitHub Actions 建置。你可以在發布分頁追蹤結果。")
            }
        }
    }
}

struct MoreView: View {
    let onAction: (String) -> Void

    var body: some View {
        NavigationStack {
            List {
                Section("站點工具") {
                    MoreActionRow(systemImage: "gearshape", title: "設定檔", detail: "網站基本欄位、params 與原始 TOML", onTap: { onAction("設定檔已加入接續清單") })
                    MoreActionRow(systemImage: "paintpalette", title: "主題", detail: "安裝或切換 Stack 等 Hugo themes", onTap: { onAction("主題管理需要桌面 Hugoer") })
                    MoreActionRow(systemImage: "list.bullet", title: "選單", detail: "編輯 menu.main、menu.social 與頁面連結", onTap: { onAction("選單編輯已加入接續清單") })
                    MoreActionRow(systemImage: "arrow.left.arrow.right", title: "遷移", detail: "Hexo／Jekyll 與 Hugo 的雙向轉換", onTap: { onAction("遷移工作會在桌面端執行") })
                }
                Section("連線") {
                    VStack(alignment: .leading, spacing: 10) {
                        Label("GitHub 連線", systemImage: "link")
                            .font(.headline)
                        Text("目前使用示範 repository adapter。接入 OAuth 後，token 只保存在系統安全儲存。")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                        Button("連接 GitHub") { onAction("GitHub OAuth 連線尚未設定") }
                            .buttonStyle(.bordered)
                    }
                    .padding(.vertical, 6)
                }
                Section("桌面接續") {
                    HStack(alignment: .top, spacing: 12) {
                        Image(systemName: "desktopcomputer")
                            .foregroundStyle(HugoerPalette.amber)
                        VStack(alignment: .leading, spacing: 5) {
                            Text("需要完整 Hugo 工具鏈？")
                                .font(.headline)
                            Text("在桌面 Hugoer 執行本機預覽、安裝 Hugo 或大量遷移。")
                                .font(.subheadline)
                        }
                    }
                    .padding(.vertical, 6)
                }
            }
            .navigationTitle("更多")
        }
    }
}

private struct MoreActionRow: View {
    let systemImage: String
    let title: String
    let detail: String
    let onTap: () -> Void

    var body: some View {
        Button(action: onTap) {
            HStack(spacing: 12) {
                Image(systemName: systemImage)
                    .frame(width: 30, height: 30)
                    .foregroundStyle(HugoerPalette.cyan)
                VStack(alignment: .leading, spacing: 3) {
                    Text(title).font(.body.weight(.medium)).foregroundStyle(.primary)
                    Text(detail).font(.subheadline).foregroundStyle(.secondary)
                }
                Spacer()
                Image(systemName: "chevron.right")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.tertiary)
            }
        }
        .buttonStyle(.plain)
    }
}
