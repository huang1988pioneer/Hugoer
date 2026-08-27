# Hugoer architecture

## Composition seam

`Services/AppServices.cs` is the application composition root. It creates one
shared graph for settings, the site session, front matter parsing, content,
menus, migration, Hugo, deployment monitoring and Git hosting. Page view models
receive that graph through constructors; their parameterless constructors remain
as design-time compatibility adapters.

`Services/SiteSession.cs` is the deep module for the active site. Its small
interface (`CurrentPath`, `HasSite`, `Set`, `Changed`) owns path normalization,
persistence and change notification. This keeps path rules out of views and
prevents each page from maintaining a different notion of the open site.

The preview control accepts `SitePath` as a bindable input. Native controls and
the Markdown preview therefore resolve media through the same page session,
without reaching into a global singleton.

## Module boundaries

- **Page models** coordinate user intent and presentation state.
- **Domain modules** (`FrontMatterService`, `ContentService`, `MenuService`,
  `SiteMigrationService`) own file-format and content rules.
- **Adapters** (`HugoService`, `GitHubService`, `DeploymentMonitorService`)
  own process and network integration.
- **Publishing policy** (`PublishingService`) defaults to the repository-backed
  Pages route, matching the repository/service boundary used by Hugoer Mobile.
  The desktop reads the selected repository with Git fetch/merge and writes it
  with a guarded commit/push; it does not build `public/` before a remote
  publish. Production rendering stays on GitHub Actions or the selected
  hosting platform. A local `hugo build` is only used when the user explicitly
  selects local mode or when the remote route fails and the fallback preference
  is enabled.
- **Views/controls** render state and forward UI events; they do not construct
  domain modules.

The service constructors keep optional defaults for the standalone harnesses,
while the desktop composition root injects shared instances. That gives tests a
stable seam without adding a dependency-injection framework solely for wiring.

## Publishing contract

`AppSettings.DeploymentMode` defaults to `GitHubPages`, including when loading
an older settings file that has no such key. The Git deployment page exposes
the mode and fallback preference so the route is visible before a destructive
push. `PublishingService` returns a `PublishResult` identifying whether the
remote push, a local fallback, or neither completed; the UI never reports a
local build as if it were live Pages. GitHub access checks accept either `gh`
permissions or the Git Credential Manager/SSH transport used by the push.

## Release contract

`scripts/publish.ps1` is the reproducible Windows release entry point. It
cleans generated output before publishing a self-contained `win-x64` single
file, then emits a portable ZIP, SHA-256 checksums and a JSON manifest. Optional
Velopack and Inno Setup packages are added when their tools are available.
