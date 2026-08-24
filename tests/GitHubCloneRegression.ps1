param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$setupView = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot "Views\SetupView.axaml")
$setupVm = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot "ViewModels\SetupViewModel.cs")
$githubView = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot "Views\GitHubView.axaml")
$githubVm = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot "ViewModels\GitHubViewModel.cs")
$service = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot "Services\GitHubService.cs")

if ($setupView.IndexOf("CloneSiteCommand", [System.StringComparison]::Ordinal) -lt 0) {
    throw "The setup page must offer cloning a GitHub site to the local machine."
}

if ($setupVm.IndexOf("CloneSiteFromGitHubAsync", [System.StringComparison]::Ordinal) -lt 0) {
    throw "SetupViewModel must call CloneSiteFromGitHubAsync."
}

if ($githubView.IndexOf("CloneSiteToLocalCommand", [System.StringComparison]::Ordinal) -lt 0) {
    throw "The GitHub page must offer cloning when no local site is selected."
}

if ($githubView.IndexOf("!HasLocalSite", [System.StringComparison]::Ordinal) -lt 0) {
    throw "The GitHub clone card must be visible only when there is no local site."
}

if ($githubVm.IndexOf("CloneSiteToLocalAsync", [System.StringComparison]::Ordinal) -lt 0) {
    throw "GitHubViewModel must clone a GitHub Pages site to the local machine."
}

if ($service.IndexOf("CloneSiteFromGitHubAsync", [System.StringComparison]::Ordinal) -lt 0) {
    throw "GitHubService must expose clone-to-local."
}

if ($service.IndexOf("ListPagesRepositoriesAsync", [System.StringComparison]::Ordinal) -lt 0) {
    throw "GitHubService must list Pages-enabled repositories."
}

Write-Output "GITHUB_CLONE_REGRESSION_OK"
