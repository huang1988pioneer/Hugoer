package com.huang1988pioneer.hugoer.mobile.ui

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowForward
import androidx.compose.material.icons.automirrored.rounded.Article
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Build
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.Code
import androidx.compose.material.icons.rounded.CloudDone
import androidx.compose.material.icons.rounded.CloudUpload
import androidx.compose.material.icons.rounded.Edit
import androidx.compose.material.icons.rounded.History
import androidx.compose.material.icons.rounded.Link
import androidx.compose.material.icons.rounded.Menu
import androidx.compose.material.icons.rounded.Palette
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.RocketLaunch
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Settings
import androidx.compose.material.icons.rounded.SwapHoriz
import androidx.compose.material.icons.rounded.WarningAmber
import androidx.compose.material.icons.rounded.Visibility
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Button
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ElevatedCard
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.huang1988pioneer.hugoer.mobile.domain.model.Article
import com.huang1988pioneer.hugoer.mobile.domain.model.ArticleStatus
import com.huang1988pioneer.hugoer.mobile.domain.model.Deployment
import com.huang1988pioneer.hugoer.mobile.domain.model.DeploymentStatus
import com.huang1988pioneer.hugoer.mobile.domain.model.Site
import com.huang1988pioneer.hugoer.mobile.presentation.HugoerUiState

@Composable
fun OverviewScreen(
    state: HugoerUiState,
    onSelectArticles: () -> Unit,
    onSelectDeploy: () -> Unit,
    onOpenArticle: (Article) -> Unit,
    onSync: () -> Unit,
) {
    Column(modifier = Modifier.fillMaxSize()) {
        HugoerTopBar(
            title = "站點總覽",
            subtitle = state.site.repository,
            onAction = onSync,
            actionDescription = "同步站點",
        )
        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start = 20.dp, end = 20.dp, bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp),
        ) {
            item {
                SiteIdentity(site = state.site)
            }
            item {
                DispatchBoard(
                    state = state,
                    onSelectDeploy = onSelectDeploy,
                )
            }
            item {
                QuickActions(
                    onEdit = onSelectArticles,
                    onPreview = {
                        // Preview is intentionally a no-op in the demo adapter;
                        // the repository bridge can open the Pages URL here.
                    },
                    onPublish = onSelectDeploy,
                )
            }
            item {
                SectionHeading(title = "最近活動")
            }
            item {
                Surface(
                    shape = RoundedCornerShape(16.dp),
                    color = MaterialTheme.colorScheme.surfaceContainer,
                ) {
                    Column {
                        ActivityRow(
                            title = "草稿已更新",
                            detail = "把 Hugo 站帶在身上",
                            time = "今天 09:42",
                            icon = Icons.Rounded.Edit,
                            onClick = { state.articles.firstOrNull()?.let(onOpenArticle) },
                        )
                        ThinDivider(Modifier.padding(horizontal = 16.dp))
                        ActivityRow(
                            title = "Pages 部署完成",
                            detail = "#184 · 41 秒",
                            time = "今天 09:48",
                            icon = Icons.Rounded.CloudDone,
                            onClick = onSelectDeploy,
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun SiteIdentity(site: Site) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Surface(
            modifier = Modifier.size(52.dp),
            shape = RoundedCornerShape(16.dp),
            color = MaterialTheme.colorScheme.primaryContainer,
            contentColor = MaterialTheme.colorScheme.onPrimaryContainer,
        ) {
            Box(contentAlignment = Alignment.Center) {
                Text("H", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            }
        }
        Column(modifier = Modifier.weight(1f)) {
            Text(site.name, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
            Text(
                text = "${site.branch} · 最近同步 ${site.lastSynced}",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        SitePill(label = "健康", icon = Icons.Rounded.CheckCircle)
    }
}

@Composable
private fun DispatchBoard(
    state: HugoerUiState,
    onSelectDeploy: () -> Unit,
) {
    ElevatedCard(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(22.dp),
        colors = CardDefaults.elevatedCardColors(
            containerColor = MaterialTheme.colorScheme.surfaceContainer,
        ),
        elevation = CardDefaults.elevatedCardElevation(defaultElevation = 2.dp),
    ) {
        Column(
            modifier = Modifier.padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.Top,
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text("發布路線", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.primary)
                    Spacer(Modifier.height(4.dp))
                    Text("版本正在航行", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
                    Text(
                        text = state.latestDeploymentMessage,
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                SitePill(label = "${state.site.lastCommit} · ${state.site.branch}", icon = Icons.Rounded.Code)
            }
            DispatchRail(currentIndex = 3)
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column {
                    Text("GitHub Pages", style = MaterialTheme.typography.labelLarge)
                    Text(
                        state.site.pagesUrl,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                TextButton(onClick = onSelectDeploy) {
                    Text("查看記錄")
                    Spacer(Modifier.width(4.dp))
                    Icon(Icons.AutoMirrored.Rounded.ArrowForward, contentDescription = null, modifier = Modifier.size(18.dp))
                }
            }
        }
    }
}

@Composable
private fun QuickActions(
    onEdit: () -> Unit,
    onPreview: () -> Unit,
    onPublish: () -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        SectionHeading(title = "下一步")
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            FilledTonalButton(onClick = onEdit, contentPadding = PaddingValues(horizontal = 16.dp)) {
                Icon(Icons.Rounded.Edit, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(8.dp))
                Text("編輯文章")
            }
            OutlinedButton(onClick = onPreview, contentPadding = PaddingValues(horizontal = 16.dp)) {
                Icon(Icons.Rounded.Visibility, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(8.dp))
                Text("預覽網站")
            }
            Button(
                onClick = onPublish,
                contentPadding = PaddingValues(horizontal = 16.dp),
            ) {
                Icon(Icons.Rounded.RocketLaunch, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(8.dp))
                Text("發布")
            }
        }
    }
}

@Composable
fun ArticlesScreen(
    state: HugoerUiState,
    onOpenArticle: (Article) -> Unit,
    onCreateArticle: () -> Unit,
) {
    var query by remember { mutableStateOf("") }
    var filter by remember { mutableStateOf(ArticleStatusFilter.All) }
    val filtered = state.articles.filter { article ->
        val matchesQuery = query.isBlank() || article.title.contains(query, ignoreCase = true) || article.path.contains(query, ignoreCase = true)
        val matchesFilter = when (filter) {
            ArticleStatusFilter.All -> true
            ArticleStatusFilter.Draft -> article.status == ArticleStatus.DRAFT
            ArticleStatusFilter.Published -> article.status == ArticleStatus.PUBLISHED
        }
        matchesQuery && matchesFilter
    }

    Scaffold(
        topBar = {
            HugoerTopBar(title = "文章", subtitle = "${state.articles.size} 篇 · repository content")
        },
        floatingActionButton = {
            androidx.compose.material3.FloatingActionButton(onClick = onCreateArticle) {
                Icon(Icons.Rounded.Add, contentDescription = "新增文章")
            }
        },
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
            contentPadding = PaddingValues(start = 20.dp, end = 20.dp, bottom = 96.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item {
                OutlinedTextField(
                    value = query,
                    onValueChange = { query = it },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    label = { Text("搜尋文章") },
                    leadingIcon = { Icon(Icons.Rounded.Search, contentDescription = null) },
                )
            }
            item {
                Row(
                    modifier = Modifier.horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    ArticleStatusFilter.entries.forEach { option ->
                        FilterChip(
                            selected = filter == option,
                            onClick = { filter = option },
                            label = { Text(option.label) },
                        )
                    }
                }
            }
            item { SectionHeading(title = "內容清單") }
            if (filtered.isEmpty()) {
                item {
                    Surface(
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(16.dp),
                        color = MaterialTheme.colorScheme.surfaceContainer,
                    ) {
                        Column(
                            modifier = Modifier.padding(24.dp),
                            verticalArrangement = Arrangement.spacedBy(8.dp),
                        ) {
                            Icon(Icons.AutoMirrored.Rounded.Article, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                            Text("找不到符合條件的文章", style = MaterialTheme.typography.titleMedium)
                            Text("試試看其他關鍵字，或新增一篇草稿。", color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                }
            } else {
                items(filtered, key = { it.id }) { article ->
                    ArticleRow(article = article, onClick = { onOpenArticle(article) })
                }
            }
        }
    }
}

private enum class ArticleStatusFilter(val label: String) {
    All("全部"),
    Draft("草稿"),
    Published("已發布"),
}

@Composable
private fun ArticleRow(article: Article, onClick: () -> Unit) {
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .clickable(onClick = onClick),
        color = MaterialTheme.colorScheme.surfaceContainer,
    ) {
        Row(
            modifier = Modifier.padding(16.dp),
            horizontalArrangement = Arrangement.spacedBy(14.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            StatusMark(article.status)
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(article.title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(article.excerpt, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("${article.path} · ${article.updated}", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Icon(Icons.AutoMirrored.Rounded.ArrowForward, contentDescription = "編輯", tint = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
fun DeployScreen(
    state: HugoerUiState,
    onRequestPublish: () -> Unit,
    onOpenArticle: (Article) -> Unit,
) {
    Column(modifier = Modifier.fillMaxSize()) {
        HugoerTopBar(title = "發布", subtitle = "GitHub Pages")
        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start = 20.dp, end = 20.dp, bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            item {
                Surface(
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(20.dp),
                    color = MaterialTheme.colorScheme.primaryContainer,
                    contentColor = MaterialTheme.colorScheme.onPrimaryContainer,
                ) {
                    Column(
                        modifier = Modifier.padding(20.dp),
                        verticalArrangement = Arrangement.spacedBy(14.dp),
                    ) {
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            Icon(Icons.Rounded.CloudUpload, contentDescription = null)
                            Text("準備發布 ${state.site.branch}", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                        }
                        Text(
                            "手機端只觸發既有的 GitHub Actions，不在裝置上執行 Hugo。發布前請確認草稿內容與 repository。",
                            style = MaterialTheme.typography.bodyMedium,
                        )
                        Button(
                            onClick = onRequestPublish,
                            enabled = !state.isDeploying,
                            modifier = Modifier.fillMaxWidth(),
                        ) {
                            if (state.isDeploying) {
                                CircularProgressIndicator(modifier = Modifier.size(18.dp), strokeWidth = 2.dp)
                                Spacer(Modifier.width(10.dp))
                                Text("正在排入佇列…")
                            } else {
                                Icon(Icons.Rounded.RocketLaunch, contentDescription = null, modifier = Modifier.size(18.dp))
                                Spacer(Modifier.width(10.dp))
                                Text("確認並發布")
                            }
                        }
                    }
                }
            }
            item {
                DispatchBoard(state = state, onSelectDeploy = {})
            }
            item { SectionHeading(title = "部署記錄") }
            items(state.deployments, key = { it.id }) { deployment ->
                DeploymentRow(deployment)
            }
            item {
                SectionHeading(title = "最近變更", actionLabel = "查看文章", onAction = { state.articles.firstOrNull()?.let(onOpenArticle) })
            }
            item {
                Surface(shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.surfaceContainer) {
                    ActivityRow(
                        title = state.articles.firstOrNull()?.title ?: "沒有變更",
                        detail = "${state.site.branch} · ${state.site.lastCommit}",
                        time = "未提交",
                        icon = Icons.Rounded.Edit,
                        onClick = { state.articles.firstOrNull()?.let(onOpenArticle) },
                    )
                }
            }
        }
    }
}

@Composable
private fun DeploymentRow(deployment: Deployment) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(14.dp),
        color = MaterialTheme.colorScheme.surfaceContainer,
    ) {
        Row(
            modifier = Modifier.padding(14.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            DeploymentStatusMark(deployment.status)
            Column(modifier = Modifier.weight(1f)) {
                Text(deployment.message, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("${deployment.id} · ${deployment.time}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Column(horizontalAlignment = Alignment.End) {
                Text(deployment.status.label, style = MaterialTheme.typography.labelLarge)
                Text(deployment.duration, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

@Composable
fun MoreScreen(onAction: (String) -> Unit) {
    Column(modifier = Modifier.fillMaxSize()) {
        HugoerTopBar(title = "更多", subtitle = "沿用桌面工作台的完整能力")
        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start = 20.dp, end = 20.dp, bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            item { SectionHeading(title = "站點工具") }
            item { MoreActionRow(Icons.Rounded.Settings, "設定檔", "網站基本欄位、params 與原始 TOML", onClick = { onAction("設定檔已加入接續清單") }) }
            item { MoreActionRow(Icons.Rounded.Palette, "主題", "安裝或切換 Stack 等 Hugo themes", onClick = { onAction("主題管理需要桌面 Hugoer") }) }
            item { MoreActionRow(Icons.Rounded.Menu, "選單", "編輯 menu.main、menu.social 與頁面連結", onClick = { onAction("選單編輯已加入接續清單") }) }
            item { MoreActionRow(Icons.Rounded.SwapHoriz, "遷移", "Hexo／Jekyll 與 Hugo 的雙向轉換", onClick = { onAction("遷移工作會在桌面端執行") }) }
            item { SectionHeading(title = "連線") }
            item {
                Surface(
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(18.dp),
                    color = MaterialTheme.colorScheme.surfaceContainer,
                ) {
                    Column(modifier = Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            Icon(Icons.Rounded.Link, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                            Text("GitHub 連線", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                        }
                        Text("目前使用示範 repository adapter。接入 OAuth 後，token 只保存在系統安全儲存。", color = MaterialTheme.colorScheme.onSurfaceVariant)
                        OutlinedButton(onClick = { onAction("GitHub OAuth 連線尚未設定") }) { Text("連接 GitHub") }
                    }
                }
            }
            item { SectionHeading(title = "桌面接續") }
            item {
                Surface(
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(18.dp),
                    color = MaterialTheme.colorScheme.secondaryContainer,
                    contentColor = MaterialTheme.colorScheme.onSecondaryContainer,
                ) {
                    Row(
                        modifier = Modifier.padding(18.dp),
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Icon(Icons.Rounded.Build, contentDescription = null)
                        Column(modifier = Modifier.weight(1f)) {
                            Text("需要完整 Hugo 工具鏈？", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                            Text("在桌面 Hugoer 執行本機預覽、安裝 Hugo 或大量遷移。")
                        }
                        IconButton(onClick = { onAction("已複製桌面接續提示") }) {
                            Icon(Icons.AutoMirrored.Rounded.ArrowForward, contentDescription = "查看接續提示")
                        }
                    }
                }
            }
        }
    }
}
