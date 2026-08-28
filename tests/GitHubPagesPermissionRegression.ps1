param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$serviceDirectory = Join-Path $RepositoryRoot "Services"
$source = (Get-ChildItem -LiteralPath $serviceDirectory -Filter "GitHubService*.cs" |
    Sort-Object Name |
    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"

$permissionCheck = $source.IndexOf("HasPagesManagementPermission(permission.StdOut)", [System.StringComparison]::Ordinal)
$managePagesCall = $source.IndexOf('source[branch]={sourceBranch}', [System.StringComparison]::Ordinal)

if ($permissionCheck -lt 0) {
    throw "GitHub Pages management does not check repository admin/maintainer permission."
}

if ($managePagesCall -lt 0) {
    throw "GitHub Pages management API call was not found."
}

if ($source.IndexOf("GetRepositoryDefaultBranch(permission.StdOut)", [System.StringComparison]::Ordinal) -lt 0) {
    throw "GitHub Pages source branch should follow the repository default branch."
}

if ($permissionCheck -gt $managePagesCall) {
    throw "Repository admin permission must be checked before the Pages management API call."
}

if ($source.IndexOf("Settings > Pages", [System.StringComparison]::Ordinal) -lt 0) {
    throw "The permission failure must tell the user where the repository owner can enable GitHub Actions Pages."
}

Write-Output "GITHUB_PAGES_PERMISSION_REGRESSION_OK"
