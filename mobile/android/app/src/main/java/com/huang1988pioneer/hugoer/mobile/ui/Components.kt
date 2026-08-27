package com.huang1988pioneer.hugoer.mobile.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.Circle
import androidx.compose.material.icons.rounded.CloudDone
import androidx.compose.material.icons.rounded.Code
import androidx.compose.material.icons.rounded.Edit
import androidx.compose.material.icons.rounded.ErrorOutline
import androidx.compose.material.icons.rounded.History
import androidx.compose.material.icons.rounded.OpenInNew
import androidx.compose.material.icons.rounded.Palette
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.RocketLaunch
import androidx.compose.material.icons.rounded.Settings
import androidx.compose.material.icons.rounded.SwapHoriz
import androidx.compose.material.icons.rounded.Sync
import androidx.compose.material.icons.rounded.Visibility
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationRail
import androidx.compose.material3.NavigationRailItem
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.huang1988pioneer.hugoer.mobile.data.ArticleStatus
import com.huang1988pioneer.hugoer.mobile.data.DeploymentStatus

@Composable
fun AppNavigationBar(
    selected: Destination,
    onSelect: (Destination) -> Unit,
) {
    NavigationBar(
        containerColor = MaterialTheme.colorScheme.surface,
        tonalElevation = 3.dp,
    ) {
        Destination.entries.forEach { item ->
            NavigationBarItem(
                selected = selected == item,
                onClick = { onSelect(item) },
                icon = { Icon(item.icon, contentDescription = item.label) },
                label = { Text(item.label) },
            )
        }
    }
}

@Composable
fun AppNavigationRail(
    selected: Destination,
    onSelect: (Destination) -> Unit,
) {
    NavigationRail(
        modifier = Modifier.fillMaxHeight(),
        containerColor = MaterialTheme.colorScheme.surface,
        header = {
            Surface(
                modifier = Modifier.padding(top = 16.dp, bottom = 12.dp),
                shape = RoundedCornerShape(14.dp),
                color = MaterialTheme.colorScheme.primaryContainer,
            ) {
                Text(
                    text = "H",
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 10.dp),
                    style = MaterialTheme.typography.titleLarge,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                    fontWeight = FontWeight.Bold,
                )
            }
        },
    ) {
        Destination.entries.forEach { item ->
            NavigationRailItem(
                selected = selected == item,
                onClick = { onSelect(item) },
                icon = { Icon(item.icon, contentDescription = item.label) },
                label = { Text(item.label) },
            )
        }
    }
}

@OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
@Composable
fun HugoerTopBar(
    title: String,
    subtitle: String? = null,
    onAction: (() -> Unit)? = null,
    actionIcon: ImageVector = Icons.Rounded.Sync,
    actionDescription: String = "同步",
) {
    androidx.compose.material3.TopAppBar(
        title = {
            Column {
                Text(title, maxLines = 1, overflow = TextOverflow.Ellipsis)
                if (subtitle != null) {
                    Text(
                        text = subtitle,
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        },
        actions = {
            if (onAction != null) {
                IconButton(onClick = onAction) {
                    Icon(actionIcon, contentDescription = actionDescription)
                }
            }
        },
        windowInsets = androidx.compose.foundation.layout.WindowInsets(0, 0, 0, 0),
    )
}

@Composable
fun SitePill(
    label: String,
    icon: ImageVector = Icons.Rounded.Code,
    color: Color = MaterialTheme.colorScheme.primary,
) {
    AssistChip(
        onClick = {},
        enabled = false,
        label = { Text(label) },
        leadingIcon = { Icon(icon, contentDescription = null, modifier = Modifier.size(16.dp)) },
        colors = androidx.compose.material3.AssistChipDefaults.assistChipColors(
            disabledContainerColor = color.copy(alpha = 0.14f),
            disabledLabelColor = color,
            disabledLeadingIconContentColor = color,
        ),
    )
}

@Composable
fun StatusMark(status: ArticleStatus) {
    val color = if (status == ArticleStatus.PUBLISHED) {
        MaterialTheme.colorScheme.primary
    } else {
        MaterialTheme.colorScheme.secondary
    }
    Surface(
        shape = CircleShape,
        color = color.copy(alpha = 0.16f),
        contentColor = color,
    ) {
        Icon(
            imageVector = if (status == ArticleStatus.PUBLISHED) Icons.Rounded.Check else Icons.Rounded.Edit,
            contentDescription = status.label,
            modifier = Modifier.padding(6.dp).size(16.dp),
        )
    }
}

@Composable
fun DeploymentStatusMark(status: DeploymentStatus) {
    val color = when (status) {
        DeploymentStatus.LIVE -> MaterialTheme.colorScheme.primary
        DeploymentStatus.BUILDING -> MaterialTheme.colorScheme.secondary
        DeploymentStatus.FAILED -> MaterialTheme.colorScheme.error
    }
    Surface(
        shape = CircleShape,
        color = color.copy(alpha = 0.16f),
        contentColor = color,
    ) {
        Icon(
            imageVector = when (status) {
                DeploymentStatus.LIVE -> Icons.Rounded.CloudDone
                DeploymentStatus.BUILDING -> Icons.Rounded.Refresh
                DeploymentStatus.FAILED -> Icons.Rounded.ErrorOutline
            },
            contentDescription = status.label,
            modifier = Modifier.padding(6.dp).size(16.dp),
        )
    }
}

@Composable
fun SectionHeading(
    title: String,
    actionLabel: String? = null,
    onAction: (() -> Unit)? = null,
) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
        if (actionLabel != null && onAction != null) {
            TextButton(onClick = onAction) { Text(actionLabel) }
        }
    }
}

@Composable
fun DispatchRail(
    currentIndex: Int,
    modifier: Modifier = Modifier,
) {
    val stages = listOf("草稿", "預覽", "佇列", "線上")
    Column(modifier = modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            stages.forEachIndexed { index, _ ->
                val active = index <= currentIndex
                Surface(
                    modifier = Modifier.size(30.dp),
                    shape = CircleShape,
                    color = if (active) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surfaceVariant,
                    contentColor = if (active) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurfaceVariant,
                ) {
                    Box(contentAlignment = Alignment.Center) {
                        if (index < currentIndex) {
                            Icon(Icons.Rounded.Check, contentDescription = null, modifier = Modifier.size(18.dp))
                        } else {
                            Icon(Icons.Rounded.Circle, contentDescription = null, modifier = Modifier.size(10.dp))
                        }
                    }
                }
                if (index < stages.lastIndex) {
                    HorizontalDivider(
                        modifier = Modifier.weight(1f),
                        thickness = 2.dp,
                        color = if (index < currentIndex) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outlineVariant,
                    )
                }
            }
        }
        Row(modifier = Modifier.fillMaxWidth()) {
            stages.forEachIndexed { index, label ->
                Text(
                    text = label,
                    modifier = Modifier.weight(1f),
                    style = MaterialTheme.typography.labelMedium,
                    color = if (index == currentIndex) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                    fontWeight = if (index == currentIndex) FontWeight.Bold else FontWeight.Normal,
                )
            }
        }
    }
}

@Composable
fun ActivityRow(
    title: String,
    detail: String,
    time: String,
    icon: ImageVector = Icons.Rounded.History,
    onClick: (() -> Unit)? = null,
) {
    ListItem(
        modifier = if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier,
        headlineContent = { Text(title, maxLines = 1, overflow = TextOverflow.Ellipsis) },
        supportingContent = { Text("$detail · $time", maxLines = 1, overflow = TextOverflow.Ellipsis) },
        leadingContent = {
            Surface(shape = CircleShape, color = MaterialTheme.colorScheme.surfaceVariant) {
                Icon(icon, contentDescription = null, modifier = Modifier.padding(8.dp).size(18.dp))
            }
        },
    )
}

@Composable
fun MoreActionRow(
    icon: ImageVector,
    title: String,
    detail: String,
    onClick: () -> Unit,
) {
    ListItem(
        modifier = Modifier
            .clip(RoundedCornerShape(14.dp))
            .clickable(onClick = onClick),
        headlineContent = { Text(title) },
        supportingContent = { Text(detail, maxLines = 2, overflow = TextOverflow.Ellipsis) },
        leadingContent = {
            Surface(shape = RoundedCornerShape(12.dp), color = MaterialTheme.colorScheme.secondaryContainer) {
                Icon(
                    icon,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onSecondaryContainer,
                    modifier = Modifier.padding(10.dp).size(20.dp),
                )
            }
        },
        trailingContent = { Icon(Icons.Rounded.OpenInNew, contentDescription = "開啟") },
    )
}

@Composable
fun ThinDivider(modifier: Modifier = Modifier) {
    HorizontalDivider(
        modifier = modifier,
        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.55f),
    )
}
