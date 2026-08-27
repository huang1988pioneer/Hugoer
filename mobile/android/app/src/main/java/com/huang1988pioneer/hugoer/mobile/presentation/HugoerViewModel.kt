package com.huang1988pioneer.hugoer.mobile.presentation

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.huang1988pioneer.hugoer.mobile.data.HugoerRepository
import com.huang1988pioneer.hugoer.mobile.domain.model.Article
import com.huang1988pioneer.hugoer.mobile.domain.model.Deployment
import com.huang1988pioneer.hugoer.mobile.domain.model.Site
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class HugoerUiState(
    val site: Site,
    val articles: List<Article> = emptyList(),
    val deployments: List<Deployment> = emptyList(),
    val isDeploying: Boolean = false,
    val isRefreshing: Boolean = false,
    val latestDeploymentMessage: String = "",
    val errorMessage: String? = null,
)

sealed interface HugoerEvent {
    data class Snackbar(val message: String) : HugoerEvent
    data class OpenEditor(val articleId: String) : HugoerEvent
}

/**
 * Presentation boundary for the app. Screens render [HugoerUiState] and send
 * intents here; they never know whether data came from demo, GitHub, or the
 * desktop bridge.
 */
class HugoerViewModel(
    private val repository: HugoerRepository,
) : ViewModel() {
    private val _state = MutableStateFlow(
        HugoerUiState(
            site = repository.site.value,
            articles = repository.articles.value,
            deployments = repository.deployments.value,
            isDeploying = repository.isDeploying.value,
            latestDeploymentMessage = repository.latestDeploymentMessage.value,
        ),
    )
    val state: StateFlow<HugoerUiState> = _state.asStateFlow()

    private val _events = MutableSharedFlow<HugoerEvent>(extraBufferCapacity = 16)
    val events = _events.asSharedFlow()

    init {
        viewModelScope.launch {
            combine(
                repository.site,
                repository.articles,
                repository.deployments,
                repository.isDeploying,
                repository.latestDeploymentMessage,
            ) { site, articles, deployments, isDeploying, latestMessage ->
                _state.update {
                    it.copy(
                        site = site,
                        articles = articles,
                        deployments = deployments,
                        isDeploying = isDeploying,
                        latestDeploymentMessage = latestMessage,
                    )
                }
            }.collect { }
        }
    }

    fun refresh() {
        viewModelScope.launch {
            _state.update { it.copy(isRefreshing = true, errorMessage = null) }
            try {
                repository.refresh()
                _events.emit(HugoerEvent.Snackbar("已同步 ${repository.site.value.repository}"))
            } catch (error: Throwable) {
                reportFailure(error, "同步失敗，請稍後重試")
            } finally {
                _state.update { it.copy(isRefreshing = false) }
            }
        }
    }

    fun createArticle() {
        viewModelScope.launch {
            try {
                val article = repository.createArticle()
                _events.emit(HugoerEvent.OpenEditor(article.id))
            } catch (error: Throwable) {
                reportFailure(error, "無法建立文章，請稍後重試")
            }
        }
    }

    fun saveArticle(id: String, title: String, body: String) {
        viewModelScope.launch {
            try {
                repository.saveArticle(id, title, body)
                _events.emit(HugoerEvent.Snackbar("草稿已儲存"))
            } catch (error: Throwable) {
                reportFailure(error, "草稿儲存失敗，請保留內容後重試")
            }
        }
    }

    fun triggerDeployment() {
        viewModelScope.launch {
            try {
                repository.triggerDeployment()
                _events.emit(HugoerEvent.Snackbar(repository.latestDeploymentMessage.value))
            } catch (error: Throwable) {
                reportFailure(error, "發布失敗，請檢查 GitHub 權限後重試")
            }
        }
    }

    private suspend fun reportFailure(error: Throwable, fallback: String) {
        if (error is CancellationException) throw error
        _state.update { it.copy(errorMessage = fallback) }
        _events.emit(HugoerEvent.Snackbar(fallback))
    }

    class Factory(private val repository: HugoerRepository) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : ViewModel> create(modelClass: Class<T>): T {
            require(modelClass.isAssignableFrom(HugoerViewModel::class.java))
            return HugoerViewModel(repository) as T
        }
    }
}
