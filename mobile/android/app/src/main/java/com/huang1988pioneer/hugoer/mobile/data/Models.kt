package com.huang1988pioneer.hugoer.mobile.data

enum class ArticleStatus(val label: String) {
    DRAFT("草稿"),
    PUBLISHED("已發布"),
}

data class Site(
    val name: String,
    val repository: String,
    val branch: String,
    val pagesUrl: String,
    val lastCommit: String,
    val lastSynced: String,
)

data class Article(
    val id: String,
    val title: String,
    val path: String,
    val updated: String,
    val status: ArticleStatus,
    val excerpt: String,
    val body: String,
)

data class Deployment(
    val id: String,
    val message: String,
    val time: String,
    val status: DeploymentStatus,
    val duration: String,
)

enum class DeploymentStatus(val label: String) {
    LIVE("線上"),
    BUILDING("建置中"),
    FAILED("需要注意"),
}

data class Activity(
    val iconKey: String,
    val title: String,
    val detail: String,
    val time: String,
)
