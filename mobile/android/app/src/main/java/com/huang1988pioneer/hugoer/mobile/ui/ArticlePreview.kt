package com.huang1988pioneer.hugoer.mobile.ui

import java.util.Locale

/**
 * Small, dependency-free block model for the editor preview. It intentionally
 * covers the Markdown/Hugo media forms that are useful on a phone without
 * pretending to be a complete Hugo renderer.
 */
internal sealed interface ArticlePreviewBlock {
    data class Heading(val text: String, val level: Int) : ArticlePreviewBlock
    data class Paragraph(val text: String) : ArticlePreviewBlock
    data class Quote(val text: String) : ArticlePreviewBlock
    data class ListItem(val text: String, val ordered: Boolean) : ArticlePreviewBlock
    data class Code(val text: String) : ArticlePreviewBlock
    data class Image(val alt: String, val source: String) : ArticlePreviewBlock
    data class Video(val source: String) : ArticlePreviewBlock
    data class Pdf(val source: String) : ArticlePreviewBlock
}

internal enum class PreviewMediaKind {
    IMAGE,
    VIDEO,
    PDF,
}

internal object ArticlePreviewParser {
    private val imagePattern = Regex("""^!\[([^]]*)\]\((\S+?)(?:\s+[\"'][^)]*[\"'])?\)\s*$""")
    private val linkPattern = Regex("""^\[([^]]+)\]\((\S+?)(?:\s+[\"'][^)]*[\"'])?\)\s*$""")
    private val headingPattern = Regex("""^(#{1,6})\s+(.+)$""")
    private val imageTagPattern = Regex("""(?i)<img\b[^>]*\bsrc\s*=\s*[\"']([^\"']+)[\"'][^>]*>""")
    private val videoTagPattern = Regex("""(?i)<(?:video|source)\b[^>]*\bsrc\s*=\s*[\"']([^\"']+)[\"'][^>]*>""")
    private val pdfTagPattern = Regex("""(?i)<(?:iframe|embed|object)\b[^>]*\b(?:src|data)\s*=\s*[\"']([^\"']+)[\"'][^>]*>""")
    private val shortcodePattern = Regex("""(?i)^\{\{<\s*(figure|image|img|video|pdf)\b([^>]*)>\}\}$""")
    private val attributePattern = Regex("""(?i)\b(?:src|url|data|file)\s*=\s*[\"']([^\"']+)[\"']""")
    private val quotedShortcodeArgumentPattern = Regex("""[\"']([^\"']+)[\"']""")

    fun parse(markdown: String): List<ArticlePreviewBlock> {
        val blocks = mutableListOf<ArticlePreviewBlock>()
        val paragraphLines = mutableListOf<String>()
        var inCodeFence = false
        val codeLines = mutableListOf<String>()

        fun flushParagraph() {
            if (paragraphLines.isNotEmpty()) {
                blocks += ArticlePreviewBlock.Paragraph(paragraphLines.joinToString(" ").trim())
                paragraphLines.clear()
            }
        }

        fun flushCode() {
            if (codeLines.isNotEmpty()) {
                blocks += ArticlePreviewBlock.Code(codeLines.joinToString("\n"))
                codeLines.clear()
            }
        }

        for (rawLine in stripFrontMatter(markdown).lines()) {
            val line = rawLine.trim()
            if (inCodeFence) {
                if (line.startsWith("```")) {
                    inCodeFence = false
                    flushCode()
                } else {
                    codeLines += rawLine
                }
                continue
            }
            if (line.startsWith("```")) {
                flushParagraph()
                inCodeFence = true
                continue
            }
            if (line.isBlank()) {
                flushParagraph()
                continue
            }

            val heading = headingPattern.matchEntire(line)
            if (heading != null) {
                flushParagraph()
                blocks += ArticlePreviewBlock.Heading(
                    text = cleanInlineMarkdown(heading.groupValues[2]),
                    level = heading.groupValues[1].length,
                )
                continue
            }

            val image = imagePattern.matchEntire(line)
            if (image != null) {
                flushParagraph()
                blocks += ArticlePreviewBlock.Image(image.groupValues[1], normalizeSource(image.groupValues[2]))
                continue
            }

            val imageTag = imageTagPattern.find(line)
            if (imageTag != null) {
                flushParagraph()
                blocks += ArticlePreviewBlock.Image("文章圖片", normalizeSource(imageTag.groupValues[1]))
                continue
            }

            val videoTag = videoTagPattern.find(line)
            if (videoTag != null) {
                flushParagraph()
                blocks += ArticlePreviewBlock.Video(normalizeSource(videoTag.groupValues[1]))
                continue
            }

            val pdfTag = pdfTagPattern.find(line)
            if (pdfTag != null) {
                flushParagraph()
                blocks += ArticlePreviewBlock.Pdf(normalizeSource(pdfTag.groupValues[1]))
                continue
            }

            val shortcode = shortcodePattern.matchEntire(line)
            if (shortcode != null) {
                val attributes = shortcode.groupValues[2]
                val source = attributePattern.find(attributes)?.groupValues?.get(1)
                    ?: quotedShortcodeArgumentPattern.find(attributes)?.groupValues?.get(1)
                if (source != null) {
                    flushParagraph()
                    when (shortcode.groupValues[1].lowercase(Locale.ROOT)) {
                        "figure", "image", "img" -> blocks += ArticlePreviewBlock.Image("文章圖片", normalizeSource(source))
                        "video" -> blocks += ArticlePreviewBlock.Video(normalizeSource(source))
                        "pdf" -> blocks += ArticlePreviewBlock.Pdf(normalizeSource(source))
                    }
                    continue
                }
            }

            val link = linkPattern.matchEntire(line)
            if (link != null) {
                val source = normalizeSource(link.groupValues[2])
                when (mediaKind(source, link.groupValues[1])) {
                    PreviewMediaKind.IMAGE -> {
                        flushParagraph()
                        blocks += ArticlePreviewBlock.Image(link.groupValues[1], source)
                        continue
                    }
                    PreviewMediaKind.VIDEO -> {
                        flushParagraph()
                        blocks += ArticlePreviewBlock.Video(source)
                        continue
                    }
                    PreviewMediaKind.PDF -> {
                        flushParagraph()
                        blocks += ArticlePreviewBlock.Pdf(source)
                        continue
                    }
                    null -> Unit
                }
            }

            val bareMedia = mediaKind(line)
            if (bareMedia != null) {
                flushParagraph()
                when (bareMedia) {
                    PreviewMediaKind.IMAGE -> blocks += ArticlePreviewBlock.Image("文章圖片", normalizeSource(line))
                    PreviewMediaKind.VIDEO -> blocks += ArticlePreviewBlock.Video(normalizeSource(line))
                    PreviewMediaKind.PDF -> blocks += ArticlePreviewBlock.Pdf(normalizeSource(line))
                }
                continue
            }

            if (line.startsWith(">")) {
                flushParagraph()
                blocks += ArticlePreviewBlock.Quote(cleanInlineMarkdown(line.removePrefix(">").trim()))
                continue
            }

            val unordered = line.removePrefix("-").removePrefix("*").removePrefix("+")
            if (unordered != line && unordered.startsWith(" ")) {
                flushParagraph()
                blocks += ArticlePreviewBlock.ListItem(cleanInlineMarkdown(unordered.trim()), ordered = false)
                continue
            }
            val ordered = Regex("""^\d+[.)]\s+(.+)$""").matchEntire(line)
            if (ordered != null) {
                flushParagraph()
                blocks += ArticlePreviewBlock.ListItem(cleanInlineMarkdown(ordered.groupValues[1]), ordered = true)
                continue
            }

            paragraphLines += cleanInlineMarkdown(line)
        }

        if (inCodeFence) flushCode()
        flushParagraph()
        return blocks
    }

    fun mediaKind(source: String, label: String = ""): PreviewMediaKind? {
        val candidate = source.trim().removeSurrounding("<", ">").lowercase(Locale.ROOT)
        if (candidate.startsWith("data:image/")) return PreviewMediaKind.IMAGE
        if (candidate.startsWith("data:video/")) return PreviewMediaKind.VIDEO
        if (candidate.startsWith("data:application/pdf")) return PreviewMediaKind.PDF
        val path = candidate.substringBefore('#').substringBefore('?')
        val extension = path.substringAfterLast('.', missingDelimiterValue = "")
        return when {
            extension in setOf("png", "jpg", "jpeg", "gif", "webp", "avif", "heic", "svg") -> PreviewMediaKind.IMAGE
            extension in setOf("mp4", "m4v", "mov", "webm", "mkv", "avi", "3gp") -> PreviewMediaKind.VIDEO
            extension == "pdf" || label.lowercase(Locale.ROOT).contains("pdf") -> PreviewMediaKind.PDF
            else -> null
        }
    }

    fun normalizeSource(source: String): String = source.trim().removeSurrounding("<", ">")

    fun cleanInlineMarkdown(text: String): String = text
        .replace(Regex("""!\[([^]]*)\]\([^)]*\)"""), "$1")
        .replace(Regex("""\[([^]]+)\]\([^)]*\)"""), "$1")
        .replace(Regex("[*_~`]"), "")
        .trim()

    private fun stripFrontMatter(markdown: String): String {
        val lines = markdown.lines()
        val delimiter = lines.firstOrNull()?.trim()
        if (delimiter !in setOf("---", "+++", ";;;")) return markdown
        val closing = lines.drop(1).indexOfFirst { it.trim() == delimiter }
        return if (closing >= 0) lines.drop(closing + 2).joinToString("\n") else markdown
    }
}
