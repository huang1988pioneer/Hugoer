package com.huang1988pioneer.hugoer.mobile.data

import com.huang1988pioneer.hugoer.mobile.domain.model.Article
import com.huang1988pioneer.hugoer.mobile.domain.model.Deployment
import com.huang1988pioneer.hugoer.mobile.domain.model.Site
import kotlinx.coroutines.flow.StateFlow

/**
 * The only boundary the presentation layer uses for Hugo/GitHub data.
 * A real OAuth-backed implementation can replace [DemoHugoerRepository]
 * without changing screens or navigation.
 */
interface HugoerRepository {
    val site: StateFlow<Site>
    val articles: StateFlow<List<Article>>
    val deployments: StateFlow<List<Deployment>>
    val isDeploying: StateFlow<Boolean>
    val latestDeploymentMessage: StateFlow<String>

    suspend fun refresh()
    suspend fun saveArticle(id: String, title: String, body: String)
    suspend fun createArticle(): Article
    suspend fun triggerDeployment()
}
