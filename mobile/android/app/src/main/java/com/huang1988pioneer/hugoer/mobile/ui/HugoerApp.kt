package com.huang1988pioneer.hugoer.mobile.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Article
import androidx.compose.material.icons.rounded.CloudUpload
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.MoreHoriz
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import com.huang1988pioneer.hugoer.mobile.data.Article
import com.huang1988pioneer.hugoer.mobile.data.DemoStore
import kotlinx.coroutines.launch

enum class Destination(
    val label: String,
    val icon: ImageVector,
) {
    Overview("總覽", Icons.Rounded.Home),
    Articles("文章", Icons.Rounded.Article),
    Deploy("發布", Icons.Rounded.CloudUpload),
    More("更多", Icons.Rounded.MoreHoriz),
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HugoerApp() {
    val store = remember { DemoStore() }
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    var destinationIndex by rememberSaveable { mutableStateOf(0) }
    var editorArticleId by rememberSaveable { mutableStateOf<String?>(null) }
    var showPublishConfirmation by rememberSaveable { mutableStateOf(false) }
    val destination = Destination.entries[destinationIndex.coerceIn(0, Destination.entries.lastIndex)]

    BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
        // Window width is the adaptive boundary: phones keep a bottom bar while
        // tablets/foldables gain the Material navigation rail.
        val expanded = maxWidth >= 600.dp
        Scaffold(
            modifier = Modifier.fillMaxSize(),
            snackbarHost = { SnackbarHost(hostState = snackbarHostState) },
            bottomBar = {
                if (!expanded) {
                    AppNavigationBar(
                        selected = destination,
                        onSelect = { destinationIndex = it.ordinal },
                    )
                }
            },
        ) { innerPadding ->
            Row(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(innerPadding),
            ) {
                if (expanded) {
                    AppNavigationRail(
                        selected = destination,
                        onSelect = { destinationIndex = it.ordinal },
                    )
                }
                Surface(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxHeight(),
                    color = MaterialTheme.colorScheme.background,
                ) {
                    when (destination) {
                        Destination.Overview -> OverviewScreen(
                            store = store,
                            onSelectArticles = { destinationIndex = Destination.Articles.ordinal },
                            onSelectDeploy = { destinationIndex = Destination.Deploy.ordinal },
                            onOpenArticle = { editorArticleId = it.id },
                            onSync = {
                                scope.launch {
                                    snackbarHostState.showSnackbar("已同步 ${store.site.repository}")
                                }
                            },
                        )

                        Destination.Articles -> ArticlesScreen(
                            store = store,
                            onOpenArticle = { editorArticleId = it.id },
                            onCreateArticle = {
                                editorArticleId = store.createArticle().id
                            },
                        )

                        Destination.Deploy -> DeployScreen(
                            store = store,
                            onRequestPublish = { showPublishConfirmation = true },
                            onOpenArticle = { editorArticleId = it.id },
                        )

                        Destination.More -> MoreScreen(
                            onAction = { message ->
                                scope.launch { snackbarHostState.showSnackbar(message) }
                            },
                        )
                    }
                }
            }
        }
    }

    val editorArticle = store.articles.firstOrNull { it.id == editorArticleId }
    if (editorArticle != null) {
        ArticleEditorSheet(
            article = editorArticle,
            onDismiss = { editorArticleId = null },
            onSave = { title, body ->
                store.saveArticle(editorArticle.id, title, body)
                editorArticleId = null
                scope.launch { snackbarHostState.showSnackbar("草稿已儲存") }
            },
        )
    }

    if (showPublishConfirmation) {
        PublishConfirmationDialog(
            isBusy = store.isDeploying,
            onDismiss = { if (!store.isDeploying) showPublishConfirmation = false },
            onConfirm = {
                showPublishConfirmation = false
                scope.launch {
                    store.triggerDeployment()
                    snackbarHostState.showSnackbar(store.latestDeploymentMessage)
                }
            },
        )
    }
}
