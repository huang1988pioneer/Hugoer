param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

function Read-RepoFile([string]$relativePath) {
    return Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot $relativePath)
}

$settings = Read-RepoFile "Models\AppSettings.cs"
$settingsService = Read-RepoFile "Services\SettingsService.cs"
$publishing = Read-RepoFile "Services\PublishingService.cs"
$githubVm = Read-RepoFile "ViewModels\GitHubViewModel.cs"
$providerVm = Read-RepoFile "ViewModels\GitHubViewModel.ProviderSettings.cs"
$hosting = Read-RepoFile "Services\GitHubService.Hosting.cs"
$githubService = Read-RepoFile "Services\GitHubService.cs"
$githubView = Read-RepoFile "Views\GitHubView.axaml"
$readme = Read-RepoFile "README.md"

if ($settings.IndexOf("DeploymentMode.GitHubPages", [StringComparison]::Ordinal) -lt 0 -or
    $settings.IndexOf("AllowLocalDeploymentFallback { get; set; } = true", [StringComparison]::Ordinal) -lt 0) {
    throw "New settings must default to remote GitHub Pages with local fallback enabled."
}

if ($settingsService.IndexOf("Enum.IsDefined(deploymentMode)", [StringComparison]::Ordinal) -lt 0) {
    throw "Settings loading must reject undefined deployment enum values."
}

if ($publishing.IndexOf("remoteOperation", [StringComparison]::Ordinal) -lt 0 -or
    $publishing.IndexOf("FallbackOrFailAsync", [StringComparison]::Ordinal) -lt 0 -or
    $publishing.IndexOf("LocalFallback", [StringComparison]::Ordinal) -lt 0 -or
    $publishing.IndexOf("remote.IsPartialSuccess", [StringComparison]::Ordinal) -lt 0) {
    throw "PublishingService must expose remote-first and explicit local-fallback routes."
}

if ($hosting.IndexOf("source[branch]", [StringComparison]::Ordinal) -lt 0 -or
    $hosting.IndexOf("source[path]", [StringComparison]::Ordinal) -lt 0 -or
    $hosting.IndexOf("HasPagesManagementPermission", [StringComparison]::Ordinal) -lt 0) {
    throw "GitHub Pages API calls must include the required source payload and maintainer-aware permission check."
}

if ($githubService.IndexOf("HUGO_VERSION: 0.165.0", [StringComparison]::Ordinal) -lt 0 -or
    $githubService.IndexOf("actions/checkout@v7", [StringComparison]::Ordinal) -lt 0 -or
    $githubService.IndexOf("actions/configure-pages@v6", [StringComparison]::Ordinal) -lt 0 -or
    $githubService.IndexOf("actions/deploy-pages@v5", [StringComparison]::Ordinal) -lt 0) {
    throw "Generated GitHub Pages workflow must track the current Hugo documentation template."
}

if ($githubVm.IndexOf("Services.Publishing.PublishAsync", [StringComparison]::Ordinal) -lt 0 -or
    $githubVm.IndexOf("Services.Publishing.ConnectAndPublishAsync", [StringComparison]::Ordinal) -lt 0 -or
    $githubVm.IndexOf("Services.Publishing.CreateAndPublishAsync", [StringComparison]::Ordinal) -lt 0 -or
    $githubVm.IndexOf("Services.Publishing.DeployLocallyAsync", [StringComparison]::Ordinal) -lt 0) {
    throw "GitHubViewModel must route publish actions through the publishing policy."
}

if ($githubVm.IndexOf("if (requestedMode == DeploymentMode.Local)", [StringComparison]::Ordinal) -lt 0 -or
    $githubVm.IndexOf("A missing origin is a remote-route failure", [StringComparison]::Ordinal) -lt 0) {
    throw "Local mode and a missing remote must remain usable through the explicit fallback policy."
}

if ($githubVm.IndexOf("正在以 production 設定建置 Hugo 網站", [StringComparison]::Ordinal) -ge 0) {
    throw "The remote publish path must not reintroduce a local Hugo preflight."
}

if ($providerVm.IndexOf("OpenRepositoryUrl", [StringComparison]::Ordinal) -lt 0 -or
    $githubView.IndexOf("OpenRepositoryUrlCommand", [StringComparison]::Ordinal) -lt 0) {
    throw "The GitHub page must provide a direct repository browser action."
}

if ($githubVm.IndexOf("origin is the source of truth", [StringComparison]::Ordinal) -lt 0 -or
    $providerVm.IndexOf("repository-specific", [StringComparison]::Ordinal) -lt 0) {
    throw "Site switches must not retain a previous repository's deployment target."
}

$parser = Read-RepoFile "Helpers\GitHubRepositoryParser.cs"
if ($parser.IndexOf("SshRemoteRegex", [StringComparison]::Ordinal) -lt 0) {
    throw "SSH origins must normalize to a safe hosted repository target."
}

if ($githubVm.IndexOf("status.HtmlUrl ?? target?.PagesUrl", [StringComparison]::Ordinal) -lt 0) {
    throw "Pages URL fallback must remain available before the first Pages deployment."
}

if ($readme.IndexOf("GitHub Pages", [StringComparison]::Ordinal) -lt 0 -or
    $readme.IndexOf("repository", [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
    $readme.IndexOf("5", [StringComparison]::Ordinal) -lt 0) {
    throw "Documentation must describe the remote-first route and deployment monitor."
}

Write-Output "REMOTE_FIRST_PUBLISHING_REGRESSION_OK"
