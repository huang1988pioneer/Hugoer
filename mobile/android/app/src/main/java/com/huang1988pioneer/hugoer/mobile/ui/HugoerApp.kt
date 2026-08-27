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
import androidx.compose.material.icons.automirrored.rounded.Article
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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.huang1988pioneer.hugoer.mobile.data.DemoHugoerRepository
import com.huang1988pioneer.hugoer.mobile.presentation.HugoerEvent
import com.huang1988pioneer.hugoer.mobile.presentation.HugoerViewModel
import kotlinx.coroutines.launch

enum class Destination(
    val label: String,
    val icon: ImageVector,
) {
    Overview("總覽", Icons.Rounded.Home),
    Articles("文章", Icons.AutoMirrored.Rounded.Article),
    Deploy("發布", Icons.Rounded.CloudUpload),
    More("更多", Icons.Rounded.MoreHoriz),
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HugoerApp() {
    val repository = remember { DemoHugoerRepository() }
    val viewModel: HugoerViewModel = viewModel(factory = HugoerViewModel.Factory(repository))
    val state by viewModel.state.collectAsStateWithLifecycle()
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
                            state = state,
                            onSelectArticles = { destinationIndex = Destination.Articles.ordinal },
                            onSelectDeploy = { destinationIndex = Destination.Deploy.ordinal },
                            onOpenArticle = { editorArticleId = it.id },
                            onSync = viewModel::refresh,
                        )

                        Destination.Articles -> ArticlesScreen(
                            state = state,
                            onOpenArticle = { editorArticleId = it.id },
                            onCreateArticle = viewModel::createArticle,
                        )

                        Destination.Deploy -> DeployScreen(
                            state = state,
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

    LaunchedEffect(viewModel) {
        viewModel.events.collect { event ->
            when (event) {
                is HugoerEvent.Snackbar -> snackbarHostState.showSnackbar(event.message)
                is HugoerEvent.OpenEditor -> editorArticleId = event.articleId
            }
        }
    }

    val editorArticle = state.articles.firstOrNull { it.id == editorArticleId }
    if (editorArticle != null) {
        ArticleEditorSheet(
            article = editorArticle,
            onDismiss = { editorArticleId = null },
            onSave = { title, body ->
                viewModel.saveArticle(editorArticle.id, title, body)
                editorArticleId = null
            },
        )
    }

    if (showPublishConfirmation) {
        PublishConfirmationDialog(
            isBusy = state.isDeploying,
            onDismiss = { if (!state.isDeploying) showPublishConfirmation = false },
            onConfirm = {
                showPublishConfirmation = false
                viewModel.triggerDeployment()
            },
        )
    }
}
