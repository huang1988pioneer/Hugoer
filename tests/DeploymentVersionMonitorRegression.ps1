param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$service = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot "Services\DeploymentMonitorService.cs")
$github = (Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot "Services") -Filter "GitHubService*.cs" |
    Sort-Object Name |
    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$viewModel = (Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot "ViewModels") -Filter "GitHubViewModel*.cs" |
    Sort-Object Name |
    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$view = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot "Views\GitHubView.axaml")
$mainViewModel = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot "ViewModels\MainViewModel.cs")

if ($service.IndexOf('MarkerFileName = "hugoer-deployment.json"', [StringComparison]::Ordinal) -lt 0) {
    throw "The deployment marker filename is missing."
}

if ($service.IndexOf("NoCache = true", [StringComparison]::Ordinal) -lt 0 -or
    $service.IndexOf("NoStore = true", [StringComparison]::Ordinal) -lt 0) {
    throw "Online marker checks must bypass browser/CDN caches."
}

$prepareCalls = [regex]::Matches($github, 'await PrepareDeploymentMarkerAsync\(').Count
if ($prepareCalls -ne 3) {
    throw "Expected deployment markers in create, connect, and push flows; found $prepareCalls call sites."
}

if ($viewModel.IndexOf("TimeSpan.FromMinutes(5)", [StringComparison]::Ordinal) -lt 0) {
    throw "The five-minute deployment monitor interval is missing."
}

if ($viewModel.IndexOf("DeploymentVersionState.Latest", [StringComparison]::Ordinal) -lt 0 -or
    $viewModel.IndexOf("DeploymentVersionState.Previous", [StringComparison]::Ordinal) -lt 0) {
    throw "Latest and previous-version user notifications are required."
}

if ($view.IndexOf("DeploymentMonitorTitle", [StringComparison]::Ordinal) -lt 0 -or
    $view.IndexOf("CheckDeploymentNowCommand", [StringComparison]::Ordinal) -lt 0) {
    throw "The deployment monitor status surface or manual retry action is missing."
}

if ($mainViewModel.IndexOf("AppStatusChanged", [StringComparison]::Ordinal) -lt 0) {
    throw "Deployment transitions must reach the global application status bar."
}

Write-Output "DEPLOYMENT_VERSION_MONITOR_REGRESSION_OK"
