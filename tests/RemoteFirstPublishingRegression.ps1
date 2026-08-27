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
    $publishing.IndexOf("LocalFallback", [StringComparison]::Ordinal) -lt 0) {
    throw "PublishingService must expose remote-first and explicit local-fallback routes."
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

if ($readme.IndexOf("GitHub Pages", [StringComparison]::Ordinal) -lt 0 -or
    $readme.IndexOf("repository", [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
    $readme.IndexOf("5", [StringComparison]::Ordinal) -lt 0) {
    throw "Documentation must describe the remote-first route and deployment monitor."
}

Write-Output "REMOTE_FIRST_PUBLISHING_REGRESSION_OK"
