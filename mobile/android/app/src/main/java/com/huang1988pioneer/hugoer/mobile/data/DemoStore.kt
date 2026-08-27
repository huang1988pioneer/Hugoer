package com.huang1988pioneer.hugoer.mobile.data

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import kotlinx.coroutines.delay

/**
 * Deterministic adapter for the mobile UI. Replace this boundary with the
 * authenticated GitHub/desktop bridge without changing the screens.
 */
class DemoStore {
    val site = Site(
        name = "Hugoer Journal",
        repository = "huang1988pioneer/hugoer-journal",
        branch = "main",
        pagesUrl = "huang1988pioneer.github.io/hugoer-journal",
        lastCommit = "a91c2e7",
        lastSynced = "8 分鐘前",
    )

    var articles by mutableStateOf(
        listOf(
            Article(
                id = "welcome",
                title = "把 Hugo 站帶在身上",
                path = "content/post/mobile-workflow.md",
                updated = "今天 09:42",
                status = ArticleStatus.DRAFT,
                excerpt = "一份給遠端工作日的內容工作流筆記。",
                body = "---\ntitle: \"把 Hugo 站帶在身上\"\ndraft: true\n---\n\n手機適合做小而安全的發布決策。先看站點狀態，再決定要不要把這次變更送上線。",
            ),
            Article(
                id = "shortcodes",
                title = "短碼與媒體預覽的整理",
                path = "content/post/shortcodes.md",
                updated = "昨天 18:20",
                status = ArticleStatus.PUBLISHED,
                excerpt = "讓圖片、影音與 PDF 在編輯流程裡保持可見。",
                body = "---\ntitle: \"短碼與媒體預覽的整理\"\ndraft: false\n---\n\nHugo 的內容不只是一段文字。預覽應該保留媒體的比例、語意與來源。",
            ),
            Article(
                id = "migration",
                title = "從 Jekyll 搬到 Hugo 的三個檢查點",
                path = "content/post/migration-checklist.md",
                updated = "2026/08/21",
                status = ArticleStatus.PUBLISHED,
                excerpt = "先保留來源，再處理 front matter 與 permalink。",
                body = "---\ntitle: \"從 Jekyll 搬到 Hugo 的三個檢查點\"\ndraft: false\n---\n\n1. 保留原始 front matter。\n2. 檢查內容路徑與媒體。\n3. 在部署前比對公開網址。",
            ),
        ),
    )

    var deployments by mutableStateOf(
        listOf(
            Deployment("#184", "把 Hugo 站帶在身上", "今天 09:48", DeploymentStatus.LIVE, "41 秒"),
            Deployment("#183", "短碼與媒體預覽的整理", "昨天 18:22", DeploymentStatus.LIVE, "38 秒"),
            Deployment("#182", "更新 Stack 設定", "昨天 16:10", DeploymentStatus.FAILED, "—"),
        ),
    )

    var isDeploying by mutableStateOf(false)
        private set

    var latestDeploymentMessage by mutableStateOf("線上版本與 main 同步")
        private set

    fun saveArticle(id: String, title: String, body: String) {
        articles = articles.map { article ->
            if (article.id == id) {
                article.copy(
                    title = title.ifBlank { "未命名文章" },
                    body = body,
                    excerpt = body.lineSequence().firstOrNull { it.isNotBlank() && !it.startsWith("---") }
                        ?.take(64)
                        ?: article.excerpt,
                    updated = "剛剛",
                    status = ArticleStatus.DRAFT,
                )
            } else article
        }
    }

    fun createArticle(): Article {
        val article = Article(
            id = "new-${articles.size + 1}",
            title = "未命名文章",
            path = "content/post/new-post-${articles.size + 1}.md",
            updated = "剛剛",
            status = ArticleStatus.DRAFT,
            excerpt = "從手機開始一篇新的 Hugo 文章。",
            body = "---\ntitle: \"未命名文章\"\ndraft: true\n---\n\n在這裡寫下內容……",
        )
        articles = listOf(article) + articles
        return article
    }

    suspend fun triggerDeployment() {
        if (isDeploying) return
        isDeploying = true
        latestDeploymentMessage = "GitHub Actions 已排入佇列"
        delay(1_400)
        val nextNumber = 185 + deployments.size - 3
        deployments = listOf(
            Deployment("#${nextNumber}", "從 Hugoer Mobile 發布", "剛剛", DeploymentStatus.LIVE, "39 秒"),
        ) + deployments
        latestDeploymentMessage = "已發布到 GitHub Pages"
        isDeploying = false
    }
}
