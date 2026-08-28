package com.huang1988pioneer.hugoer.mobile.data

import com.huang1988pioneer.hugoer.mobile.domain.model.Article
import com.huang1988pioneer.hugoer.mobile.domain.model.ArticleStatus
import com.huang1988pioneer.hugoer.mobile.domain.model.Deployment
import com.huang1988pioneer.hugoer.mobile.domain.model.DeploymentStatus
import com.huang1988pioneer.hugoer.mobile.domain.model.Site
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** Deterministic data source used by previews, local QA, and first-run demos. */
class DemoHugoerRepository(
    private val deploymentDelayMillis: Long = 1_400L,
) : HugoerRepository {
    private val mutex = Mutex()

    private val _site = MutableStateFlow(
        Site(
            name = "Hugoer Journal",
            repository = "huang1988pioneer/hugoer-journal",
            branch = "main",
            pagesUrl = "huang1988pioneer.github.io/hugoer-journal",
            lastCommit = "a91c2e7",
            lastSynced = "8 分鐘前",
        ),
    )
    override val site: StateFlow<Site> = _site.asStateFlow()

    private val _articles = MutableStateFlow(
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
                body = "---\ntitle: \"短碼與媒體預覽的整理\"\ndraft: false\n---\n\nHugo 的內容不只是一段文字。預覽應該保留媒體的比例、語意與來源。\n\n![Hugoer 預覽示意圖](https://www.w3.org/Icons/w3c_home.png)\n\n[影片預覽示範](https://storage.googleapis.com/gtv-videos-bucket/sample/ForBiggerEscapes.mp4)\n\n[PDF 預覽示範](https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf)",
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
    override val articles: StateFlow<List<Article>> = _articles.asStateFlow()

    private val _deployments = MutableStateFlow(
        listOf(
            Deployment("#184", "把 Hugo 站帶在身上", "今天 09:48", DeploymentStatus.LIVE, "41 秒"),
            Deployment("#183", "短碼與媒體預覽的整理", "昨天 18:22", DeploymentStatus.LIVE, "38 秒"),
            Deployment("#182", "更新 Stack 設定", "昨天 16:10", DeploymentStatus.FAILED, "—"),
        ),
    )
    override val deployments: StateFlow<List<Deployment>> = _deployments.asStateFlow()

    private val _isDeploying = MutableStateFlow(false)
    override val isDeploying: StateFlow<Boolean> = _isDeploying.asStateFlow()

    private val _latestDeploymentMessage = MutableStateFlow("線上版本與 main 同步")
    override val latestDeploymentMessage: StateFlow<String> = _latestDeploymentMessage.asStateFlow()

    override suspend fun refresh() {
        // The real adapter will fetch repository and Pages state here. The demo
        // remains deterministic while still exercising the refresh boundary.
        mutex.withLock {
            _site.value = _site.value.copy(lastSynced = "剛剛")
        }
    }

    override suspend fun saveArticle(id: String, title: String, body: String) {
        mutex.withLock {
            _articles.value = _articles.value.map { article ->
                if (article.id != id) return@map article
                article.copy(
                    title = title.ifBlank { "未命名文章" },
                    body = body,
                    excerpt = extractExcerpt(body, article.excerpt),
                    updated = "剛剛",
                    status = ArticleStatus.DRAFT,
                )
            }
        }
    }

    override suspend fun createArticle(): Article = mutex.withLock {
        val number = _articles.value.size + 1
        val article = Article(
            id = "new-$number",
            title = "未命名文章",
            path = "content/post/new-post-$number.md",
            updated = "剛剛",
            status = ArticleStatus.DRAFT,
            excerpt = "從手機開始一篇新的 Hugo 文章。",
            body = "---\ntitle: \"未命名文章\"\ndraft: true\n---\n\n在這裡寫下內容……",
        )
        _articles.value = listOf(article) + _articles.value
        article
    }

    override suspend fun triggerDeployment() {
        mutex.withLock {
            if (_isDeploying.value) return
            _isDeploying.value = true
            _latestDeploymentMessage.value = "GitHub Actions 已排入佇列"
        }
        try {
            delay(deploymentDelayMillis)
            mutex.withLock {
                val nextNumber = 185 + _deployments.value.size - 3
                _deployments.value = listOf(
                    Deployment("#$nextNumber", "從 Hugoer Mobile 發布", "剛剛", DeploymentStatus.LIVE, "39 秒"),
                ) + _deployments.value
                _latestDeploymentMessage.value = "已發布到 GitHub Pages"
            }
        } finally {
            mutex.withLock {
                _isDeploying.value = false
            }
        }
    }

    private fun extractExcerpt(body: String, fallback: String): String {
        val lines = body.lines()
        val contentStart = if (lines.firstOrNull()?.trim() == "---") {
            val closingOffset = lines.drop(1).indexOfFirst { it.trim() == "---" }
            if (closingOffset >= 0) closingOffset + 2 else 0
        } else {
            0
        }
        return lines.drop(contentStart)
            .firstOrNull { it.isNotBlank() }
            ?.trim()
            ?.take(64)
            ?.ifBlank { fallback }
            ?: fallback
    }
}
