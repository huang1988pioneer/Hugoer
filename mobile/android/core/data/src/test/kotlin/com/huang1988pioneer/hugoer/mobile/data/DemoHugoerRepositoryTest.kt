package com.huang1988pioneer.hugoer.mobile.data

import com.huang1988pioneer.hugoer.mobile.domain.model.ArticleStatus
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

@OptIn(ExperimentalCoroutinesApi::class)
class DemoHugoerRepositoryTest {
    @Test
    fun saveArticle_normalizesTitleAndKeepsDraftState() = runTest {
        val repository = DemoHugoerRepository(deploymentDelayMillis = 100)

        repository.saveArticle(
            id = "welcome",
            title = "",
            body = "---\ndraft: true\n---\n\n新的摘要內容",
        )

        val article = repository.articles.value.first { it.id == "welcome" }
        assertEquals("未命名文章", article.title)
        assertEquals("新的摘要內容", article.excerpt)
        assertEquals(ArticleStatus.DRAFT, article.status)
    }

    @Test
    fun saveArticle_usesBodyWhenMarkdownHasNoFrontMatter() = runTest {
        val repository = DemoHugoerRepository()

        repository.saveArticle(
            id = "welcome",
            title = "純 Markdown",
            body = "# 標題\n\n第一段會成為摘要。",
        )

        val article = repository.articles.value.first { it.id == "welcome" }
        assertEquals("# 標題", article.excerpt)
    }

    @Test
    fun triggerDeployment_isSerializedWhenRequestedConcurrently() = runTest {
        val repository = DemoHugoerRepository(deploymentDelayMillis = 100)

        listOf(
            async { repository.triggerDeployment() },
            async { repository.triggerDeployment() },
            async { repository.triggerDeployment() },
        ).awaitAll()

        assertEquals(4, repository.deployments.value.size)
        assertTrue(repository.deployments.value.first().message.contains("Hugoer Mobile"))
        assertEquals("已發布到 GitHub Pages", repository.latestDeploymentMessage.value)
        assertEquals(false, repository.isDeploying.value)
    }

    @Test
    fun cancelledDeployment_releasesInFlightState() = runTest {
        val repository = DemoHugoerRepository(deploymentDelayMillis = 1_000)
        val job = launch { repository.triggerDeployment() }
        runCurrent()

        assertTrue(repository.isDeploying.value)
        job.cancelAndJoin()

        assertFalse(repository.isDeploying.value)
    }
}
