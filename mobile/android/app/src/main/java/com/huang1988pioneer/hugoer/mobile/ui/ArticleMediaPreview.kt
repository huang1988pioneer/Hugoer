package com.huang1988pioneer.hugoer.mobile.ui

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color as AndroidColor
import android.graphics.pdf.PdfRenderer
import android.net.Uri
import android.os.ParcelFileDescriptor
import android.view.ViewGroup
import android.widget.MediaController
import android.widget.VideoView
import androidx.compose.foundation.Image as PreviewImage
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Description
import androidx.compose.material.icons.rounded.ErrorOutline
import androidx.compose.material.icons.rounded.Image
import androidx.compose.material.icons.rounded.Movie
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL
import java.net.URLDecoder
import java.nio.charset.StandardCharsets
import android.util.Base64

/**
 * Renders the media blocks produced by [ArticlePreviewParser]. Media is loaded
 * lazily so opening the editor never blocks the sheet on a remote asset.
 */
@Composable
fun ArticlePreview(
    title: String,
    markdown: String,
    modifier: Modifier = Modifier,
    baseUrl: String? = null,
) {
    val blocks = remember(markdown) { ArticlePreviewParser.parse(markdown) }
    Surface(
        modifier = modifier
            .fillMaxWidth()
            .heightIn(min = 260.dp),
        shape = RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surfaceContainer,
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Icon(Icons.Rounded.Description, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                Text(
                    text = title.ifBlank { "未命名文章" },
                    style = MaterialTheme.typography.titleMedium,
                )
            }
            if (blocks.isEmpty()) {
                Text(
                    text = "輸入 Markdown 後，文章與媒體會在這裡顯示。",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                blocks.forEach { block ->
                    ArticlePreviewBlockView(block = block, baseUrl = baseUrl)
                }
            }
        }
    }
}

@Composable
private fun ArticlePreviewBlockView(
    block: ArticlePreviewBlock,
    baseUrl: String?,
) {
    when (block) {
        is ArticlePreviewBlock.Heading -> {
            Text(
                text = block.text,
                style = when (block.level) {
                    1 -> MaterialTheme.typography.headlineSmall
                    2 -> MaterialTheme.typography.titleLarge
                    else -> MaterialTheme.typography.titleMedium
                },
            )
        }

        is ArticlePreviewBlock.Paragraph -> Text(
            text = block.text,
            style = MaterialTheme.typography.bodyLarge,
            color = MaterialTheme.colorScheme.onSurface,
        )

        is ArticlePreviewBlock.Quote -> Surface(
            color = MaterialTheme.colorScheme.secondaryContainer,
            contentColor = MaterialTheme.colorScheme.onSecondaryContainer,
            shape = RoundedCornerShape(12.dp),
        ) {
            Text(
                text = block.text,
                modifier = Modifier.padding(horizontal = 14.dp, vertical = 11.dp),
                style = MaterialTheme.typography.bodyMedium,
            )
        }

        is ArticlePreviewBlock.ListItem -> Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.Top,
        ) {
            Text(
                text = if (block.ordered) "1." else "•",
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.primary,
            )
            Text(text = block.text, style = MaterialTheme.typography.bodyLarge)
        }

        is ArticlePreviewBlock.Code -> Surface(
            color = MaterialTheme.colorScheme.surfaceVariant,
            shape = RoundedCornerShape(12.dp),
        ) {
            Text(
                text = block.text,
                modifier = Modifier
                    .fillMaxWidth()
                    .horizontalScroll(rememberScrollState())
                    .padding(14.dp),
                style = MaterialTheme.typography.bodyMedium,
            )
        }

        is ArticlePreviewBlock.Image -> PreviewMediaCard(
            kind = PreviewMediaKind.IMAGE,
            title = block.alt.ifBlank { "文章圖片" },
            source = block.source,
            resolvedSource = resolvePreviewSource(block.source, baseUrl),
        )

        is ArticlePreviewBlock.Video -> PreviewMediaCard(
            kind = PreviewMediaKind.VIDEO,
            title = "文章影片",
            source = block.source,
            resolvedSource = resolvePreviewSource(block.source, baseUrl),
        )

        is ArticlePreviewBlock.Pdf -> PreviewMediaCard(
            kind = PreviewMediaKind.PDF,
            title = "PDF 文件",
            source = block.source,
            resolvedSource = resolvePreviewSource(block.source, baseUrl),
        )
    }
}

@Composable
private fun PreviewMediaCard(
    kind: PreviewMediaKind,
    title: String,
    source: String,
    resolvedSource: String,
) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Icon(mediaIcon(kind), contentDescription = null, tint = MaterialTheme.colorScheme.primary)
            Text(title, style = MaterialTheme.typography.titleSmall)
        }
        when {
            resolvedSource.isBlank() -> MediaUnavailable(source = source, reason = "需要完整網址，或可讀取的本機檔案。")
            kind == PreviewMediaKind.IMAGE -> ImagePreview(source = resolvedSource, originalSource = source)
            kind == PreviewMediaKind.VIDEO -> VideoPreview(source = resolvedSource, originalSource = source)
            kind == PreviewMediaKind.PDF -> PdfPreview(source = resolvedSource, originalSource = source)
        }
    }
}

@Composable
private fun ImagePreview(source: String, originalSource: String) {
    val context = LocalContext.current
    var retryToken by remember(source) { mutableIntStateOf(0) }
    val state by produceState<PreviewLoadState<Bitmap>>(PreviewLoadState.Loading, source, retryToken) {
        value = try {
            PreviewLoadState.Success(withContext(Dispatchers.IO) { loadBitmap(context, source) })
        } catch (error: kotlinx.coroutines.CancellationException) {
            throw error
        } catch (error: Exception) {
            PreviewLoadState.Failure(mediaErrorMessage(error))
        }
    }
    Surface(
        modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)),
        color = MaterialTheme.colorScheme.surfaceVariant,
    ) {
        when (state) {
            PreviewLoadState.Loading -> MediaLoading("正在載入圖片…")
            is PreviewLoadState.Success -> PreviewImage(
                bitmap = (state as PreviewLoadState.Success<Bitmap>).value.asImageBitmap(),
                contentDescription = originalSource,
                modifier = Modifier.fillMaxWidth().heightIn(min = 160.dp, max = 360.dp),
                contentScale = ContentScale.Fit,
            )
            is PreviewLoadState.Failure -> MediaUnavailable(
                source = originalSource,
                reason = (state as PreviewLoadState.Failure).message,
                onRetry = { retryToken++ },
            )
        }
    }
}

@Composable
private fun VideoPreview(source: String, originalSource: String) {
    var failed by remember(source) { mutableStateOf(false) }
    var retryToken by remember(source) { mutableIntStateOf(0) }
    if (failed) {
        MediaUnavailable(
            source = originalSource,
            reason = "影片無法播放；請確認網址是可直接播放的 MP4、M4V 或 WebM。",
            onRetry = {
                failed = false
                retryToken++
            },
        )
        return
    }
    key(source, retryToken) {
        Surface(
            modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)),
            color = androidx.compose.ui.graphics.Color.Black,
        ) {
            AndroidView(
                modifier = Modifier.fillMaxWidth().heightIn(min = 200.dp, max = 320.dp),
                factory = { context ->
                    VideoView(context).apply {
                        layoutParams = ViewGroup.LayoutParams(
                            ViewGroup.LayoutParams.MATCH_PARENT,
                            ViewGroup.LayoutParams.MATCH_PARENT,
                        )
                        setMediaController(MediaController(context).also { controller -> controller.setAnchorView(this) })
                        setVideoURI(Uri.parse(source))
                        tag = source
                        setOnPreparedListener { player -> player.isLooping = false }
                        setOnErrorListener { _, _, _ ->
                            failed = true
                            true
                        }
                    }
                },
                update = { view ->
                    if (view.tag != source) {
                        view.setVideoURI(Uri.parse(source))
                        view.tag = source
                    }
                },
                onRelease = { view -> view.stopPlayback() },
            )
        }
    }
}

@Composable
private fun PdfPreview(source: String, originalSource: String) {
    val context = LocalContext.current
    var retryToken by remember(source) { mutableIntStateOf(0) }
    val state by produceState<PreviewLoadState<List<Bitmap>>>(PreviewLoadState.Loading, source, retryToken) {
        value = try {
            PreviewLoadState.Success(withContext(Dispatchers.IO) { renderPdf(context, source) })
        } catch (error: kotlinx.coroutines.CancellationException) {
            throw error
        } catch (error: Exception) {
            PreviewLoadState.Failure(mediaErrorMessage(error))
        }
    }
    when (state) {
        PreviewLoadState.Loading -> MediaLoading("正在載入 PDF…")
        is PreviewLoadState.Success -> {
            val pages = (state as PreviewLoadState.Success<List<Bitmap>>).value
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                pages.forEachIndexed { index, page ->
                    Surface(
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(10.dp),
                        color = androidx.compose.ui.graphics.Color.White,
                    ) {
                        PreviewImage(
                            bitmap = page.asImageBitmap(),
                            contentDescription = "PDF 第 ${index + 1} 頁",
                            modifier = Modifier.fillMaxWidth().heightIn(min = 180.dp, max = 420.dp),
                            contentScale = ContentScale.Fit,
                        )
                    }
                }
                Text(
                    text = if (pages.size == 1) "PDF 預覽 · 第 1 頁" else "PDF 預覽 · 顯示前 ${pages.size} 頁",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        is PreviewLoadState.Failure -> MediaUnavailable(
            source = originalSource,
            reason = (state as PreviewLoadState.Failure).message,
            onRetry = { retryToken++ },
        )
    }
}

@Composable
private fun MediaLoading(message: String) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(22.dp),
        horizontalArrangement = Arrangement.spacedBy(10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        CircularProgressIndicator(modifier = Modifier.size(20.dp), strokeWidth = 2.dp)
        Text(message, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun MediaUnavailable(
    source: String,
    reason: String,
    onRetry: (() -> Unit)? = null,
) {
    Column(
        modifier = Modifier.fillMaxWidth().padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Rounded.ErrorOutline, contentDescription = null, tint = MaterialTheme.colorScheme.error)
            Text("媒體預覽不可用", style = MaterialTheme.typography.titleSmall)
        }
        Text(reason, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(source, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1)
        if (onRetry != null) {
            TextButton(onClick = onRetry) {
                Icon(Icons.Rounded.Refresh, contentDescription = null, modifier = Modifier.size(16.dp))
                Text("重試", modifier = Modifier.padding(start = 5.dp))
            }
        }
    }
}

private fun mediaIcon(kind: PreviewMediaKind) = when (kind) {
    PreviewMediaKind.IMAGE -> Icons.Rounded.Image
    PreviewMediaKind.VIDEO -> Icons.Rounded.Movie
    PreviewMediaKind.PDF -> Icons.Rounded.Description
}

private sealed interface PreviewLoadState<out T> {
    object Loading : PreviewLoadState<Nothing>
    data class Success<T>(val value: T) : PreviewLoadState<T>
    data class Failure(val message: String) : PreviewLoadState<Nothing>
}

private fun loadBitmap(context: Context, source: String): Bitmap = withMediaInput(context, source) { input ->
    BitmapFactory.decodeStream(input) ?: throw IOException("圖片格式不支援")
}

private fun renderPdf(context: Context, source: String): List<Bitmap> {
    val file = File.createTempFile("hugoer-preview-", ".pdf", context.cacheDir)
    return try {
        withMediaInput(context, source) { input ->
            file.outputStream().use { output -> input.copyTo(output) }
        }
        ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY).use { descriptor ->
            PdfRenderer(descriptor).use { renderer ->
                if (renderer.pageCount == 0) throw IOException("PDF 沒有可預覽的頁面")
                (0 until minOf(renderer.pageCount, 3)).map { index ->
                    renderer.openPage(index).use { page ->
                        val width = 960
                        val height = (width.toFloat() * page.height / page.width).toInt().coerceAtLeast(1)
                        Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888).also { bitmap ->
                            bitmap.eraseColor(AndroidColor.WHITE)
                            page.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
                        }
                    }
                }
            }
        }
    } finally {
        file.delete()
    }
}

private fun <T> withMediaInput(context: Context, source: String, block: (InputStream) -> T): T {
    val normalized = source.trim()
    if (normalized.startsWith("data:", ignoreCase = true)) {
        val comma = normalized.indexOf(',')
        if (comma <= 5) throw IOException("資料網址格式不正確")
        val metadata = normalized.substring(5, comma)
        val payload = normalized.substring(comma + 1)
        val bytes = if (metadata.contains(";base64", ignoreCase = true)) {
            Base64.decode(payload, Base64.DEFAULT)
        } else {
            URLDecoder.decode(payload, StandardCharsets.UTF_8.name()).toByteArray(StandardCharsets.UTF_8)
        }
        return bytes.inputStream().use(block)
    }
    if (normalized.startsWith("https://", ignoreCase = true) || normalized.startsWith("http://", ignoreCase = true)) {
        val connection = (URL(normalized).openConnection() as? HttpURLConnection)
            ?: throw IOException("無法建立媒體連線")
        connection.connectTimeout = 10_000
        connection.readTimeout = 15_000
        connection.instanceFollowRedirects = true
        return try {
            connection.connect()
            if (connection.responseCode !in 200..299) throw IOException("伺服器回應 ${connection.responseCode}")
            connection.inputStream.use(block)
        } finally {
            connection.disconnect()
        }
    }
    if (normalized.startsWith("content://", ignoreCase = true)) {
        return (context.contentResolver.openInputStream(Uri.parse(normalized))
            ?: throw IOException("無法讀取選取的檔案")).use(block)
    }
    val file = if (normalized.startsWith("file://", ignoreCase = true)) {
        Uri.parse(normalized).path?.let(::File)
    } else {
        File(normalized)
    }
    if (file == null || !file.isFile) throw IOException("找不到本機檔案")
    return file.inputStream().use(block)
}

internal fun resolvePreviewSource(source: String, baseUrl: String?): String {
    val normalized = ArticlePreviewParser.normalizeSource(source)
    if (normalized.isBlank()) return ""
    val explicitScheme = Regex("^([a-zA-Z][a-zA-Z0-9+.-]*):").find(normalized)?.groupValues?.get(1)?.lowercase()
    if (normalized.startsWith("data:", ignoreCase = true)) return normalized
    if (explicitScheme != null) {
        return if (explicitScheme in setOf("http", "https", "file", "content")) normalized else ""
    }
    val rawBase = baseUrl?.trim().orEmpty()
    val base = if (rawBase.isNotBlank() && rawBase.matches(Regex("^[a-zA-Z][a-zA-Z0-9+.-]*:.*"))) {
        rawBase
    } else if (rawBase.isNotBlank()) {
        "https://$rawBase"
    } else {
        ""
    }
    if (base.isBlank()) return ""
    return try {
        val baseUri = URI(if (base.endsWith("/")) base else "$base/")
        val allowedSchemes = setOf("http", "https", "file", "content")
        if (baseUri.scheme?.lowercase() !in allowedSchemes) return ""
        val resolved = baseUri.resolve(normalized)
        if (resolved.scheme?.lowercase() !in allowedSchemes) return ""
        resolved.toString()
    } catch (_: IllegalArgumentException) {
        ""
    }
}

private fun mediaErrorMessage(error: Throwable): String = when (error) {
    is java.net.SocketTimeoutException -> "連線逾時，請確認網路後重試。"
    is java.net.UnknownHostException -> "找不到主機，離線時可稍後再試。"
    else -> error.message?.takeIf { it.isNotBlank() } ?: "檔案格式或網址無法讀取。"
}
