using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.ViewModels;

public partial class GitHubViewModel
{
    [RelayCommand]
    private void OpenPagesUrl()
    {
        if (string.IsNullOrWhiteSpace(PagesUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = PagesUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddWorkflowOnlyAsync()
    {
        if (!RequireSite(out var site)) return;
        var provider = GetActiveRepositoryTarget()?.Provider ?? _activeProvider;
        await Services.GitHub.EnsureHostingWorkflowAsync(site, provider);
        StatusMessage = $"已寫入 {provider.PagesProductName()} 部署設定";
        AppendLog(StatusMessage);
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
        Log = string.IsNullOrEmpty(Log) ? line : Log + Environment.NewLine + line;
    }

    [RelayCommand]
    private void SaveProviderSettings()
    {
        PersistProviderSettings();
        ProviderSettingsStatus = $"已儲存 {SelectedProvider?.DisplayName ?? _activeProvider.DisplayName()} 設定";
        StatusMessage = ProviderSettingsStatus;
    }

    private void MaybeAdoptProviderFromUrl(string value)
    {
        if (_switchingProviderSettings)
            return;

        var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(value);
        if (!target.IsValid || target.Provider == _activeProvider)
            return;

        _providerWasSelectedByUser = true;
        AdoptProvider(target.Provider, keepRepositoryUrl: true);
        ProviderSettingsStatus = $"已依網址切換到 {target.ProviderName}";
    }

    private void AdoptProvider(GitHostingProvider provider, bool keepRepositoryUrl)
    {
        var urlToKeep = RepositoryUrl;
        _switchingProviderSettings = true;
        try
        {
            _activeProvider = provider;
            var profile = Services.Settings.GetGitProviderSettings(provider);
            var option = ProviderOptions.First(item => item.Provider == provider);
            SelectedProvider = option;
            ProviderHint = option.Hint;
            IsGitHubProvider = provider == GitHostingProvider.GitHub;
            ProviderAccount = profile.AccountOrWorkspace;
            ProviderPagesUrl = string.IsNullOrWhiteSpace(profile.PagesUrl)
                ? ProviderPagesUrl
                : profile.PagesUrl;
            SyncRecommendedBaseUrl = profile.SyncRecommendedBaseUrl;
            CommitMessage = Hugoer.Services.GitHubService.IsAutomaticCommitMessage(profile.CommitMessage)
                ? string.Empty
                : profile.CommitMessage;
            if (!keepRepositoryUrl)
                RepositoryUrl = profile.RepositoryUrl;
            else
                RepositoryUrl = urlToKeep;
            if (!string.IsNullOrWhiteSpace(ProviderPagesUrl))
                PagesUrl = ProviderPagesUrl;
            HasPagesRepositories = provider == GitHostingProvider.GitHub && PagesRepositories.Count > 0;
            ResetDeploymentMonitorForProvider(option.DisplayName);
        }
        finally
        {
            _switchingProviderSettings = false;
        }
    }

    private void LoadProviderSettings(GitHostingProvider provider)
    {
        AdoptProvider(provider, keepRepositoryUrl: false);
        ProviderSettingsStatus = $"已載入 {SelectedProvider?.DisplayName ?? provider.DisplayName()} 的獨立設定";
        UpdateRepositoryTarget(RepositoryUrl);
    }

    private GitHubRepositoryTarget? GetActiveRepositoryTarget()
    {
        var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(RepositoryUrl);
        return target.IsValid && target.Provider == _activeProvider ? target : null;
    }

    private void PersistProviderSettings()
    {
        if (_switchingProviderSettings)
            return;

        var profile = Services.Settings.GetGitProviderSettings(_activeProvider);
        var parsed = Hugoer.Services.GitHubService.ParseRepositoryTarget(RepositoryUrl);
        if (!parsed.IsValid || parsed.Provider == _activeProvider)
            profile.RepositoryUrl = RepositoryUrl.Trim();
        profile.AccountOrWorkspace = ProviderAccount.Trim();
        profile.PagesUrl = ProviderPagesUrl.Trim();
        profile.SyncRecommendedBaseUrl = SyncRecommendedBaseUrl;
        profile.CommitMessage = Hugoer.Services.GitHubService.IsAutomaticCommitMessage(CommitMessage)
            ? string.Empty
            : CommitMessage.Trim();
        Services.Settings.SaveGitProviderSettings(profile);
    }

    private void UpdateRepositoryTarget(string value)
    {
        var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(value);
        CanConnectRepository = target.IsValid && target.Provider == _activeProvider;
        UpdateCloneAvailability();
        if (!target.IsValid)
        {
            RepositoryTargetSummary = string.IsNullOrWhiteSpace(value)
                ? "貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository / Pages 網址後，Hugoer 會先顯示目標與建議網址。"
                : target.ErrorMessage;
            return;
        }

        if (target.Provider != _activeProvider)
        {
            RepositoryTargetSummary =
                $"這個網址屬於 {target.ProviderName}，目前選擇的是 {_activeProvider.DisplayName()}。請先切換上方 Git 平台，再使用此網址。";
            return;
        }

        RepoName = target.Repository!;
        if (string.IsNullOrWhiteSpace(ProviderPagesUrl) && !string.IsNullOrWhiteSpace(target.PagesUrl))
            ProviderPagesUrl = target.PagesUrl;
        if (string.IsNullOrWhiteSpace(ProviderAccount) && !string.IsNullOrWhiteSpace(target.Owner))
            ProviderAccount = target.Owner;
        var destination = GitHubClonePath.TryGetDestination(CloneParent, target.Repository, out var pathError);
        var pagesUrl = string.IsNullOrWhiteSpace(ProviderPagesUrl)
            ? (string.IsNullOrWhiteSpace(target.PagesUrl)
                ? "此平台/此 repo 沒有可推導的專案 Pages 網址"
                : target.PagesUrl)
            : ProviderPagesUrl;
        var localLine = HasLocalSite
            ? $"本地資料夾（現在）：{Services.CurrentSitePath}"
            : $"本地資料夾（複製後）：{destination ?? pathError}";
        RepositoryTargetSummary =
            $"平台：{target.ProviderName}\n" +
            $"遠端 Repository：{target.Owner}/{target.Repository}（{target.CanonicalUrl}）\n" +
            $"{localLine}\n" +
            $"網站類型：{(target.IsUserOrOrganizationSite ? "使用者／組織網站" : "專案網站")}\n" +
            $"最終發布網址（Pages）：{pagesUrl}";
    }

    private void UpdateCloneAvailability() =>
        CanCloneToLocal = CanConnectRepository && !string.IsNullOrWhiteSpace(CloneParent) && !IsBusy && !HasLocalSite;

    private bool IsCurrentDeploymentTarget(GitHostingProvider provider, string? pagesUrl) =>
        _activeProvider == provider
        && string.Equals(PagesUrl, pagesUrl, StringComparison.OrdinalIgnoreCase);

    private void ResetDeploymentMonitorForProvider(string providerName)
    {
        _lastDeploymentState = null;
        _lastExpectedDeploymentId = null;
        _lastDeploymentProvider = _activeProvider;
        _lastDeploymentPagesUrl = PagesUrl;
        DeploymentMonitorTitle = $"等待 {providerName} 部署";
        DeploymentMonitorSummary = string.IsNullOrWhiteSpace(PagesUrl)
            ? $"尚未取得 {providerName} Pages 網址；推送後 Hugoer 會重新檢查。"
            : $"目前監控 {providerName}：{PagesUrl}";
        DeploymentMonitorSchedule = "每 5 分鐘自動檢查";
    }

    private void ApplyPagesRepositories(GitHubPagesRepositoryList list)
    {
        PagesRepositories.Clear();
        foreach (var item in list.Repositories)
            PagesRepositories.Add(item);
        HasPagesRepositories = IsGitHubProvider && PagesRepositories.Count > 0;
        if (PagesRepositories.Count == 1)
            SelectedPagesRepository = PagesRepositories[0];
    }

    private void RecordRecentRepository(GitHubRepositoryTarget? target, string? localPath = null)
    {
        if (target is not { IsValid: true })
            return;
        Services.Settings.AddRecentRepository(target, localPath);
        ReloadRecentRepositories();
    }

    private void ReloadRecentRepositories()
    {
        RecentRepositories.Clear();
        foreach (var item in Services.Settings.GetRecentRepositories())
            RecentRepositories.Add(item);
        HasRecentRepositories = RecentRepositories.Count > 0;
        SelectedRecentRepository = null;
    }

    [RelayCommand]
    private void RemoveRecentRepository(RecentRepositoryEntry entry)
    {
        Services.Settings.RemoveRecentRepository(entry);
        ReloadRecentRepositories();
    }

    [RelayCommand]
    private void ClearRecentRepositories()
    {
        Services.Settings.ClearRecentRepositories();
        ReloadRecentRepositories();
    }
}
