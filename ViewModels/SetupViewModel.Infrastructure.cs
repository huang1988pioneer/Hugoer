using System.Diagnostics;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.ViewModels;

public partial class SetupViewModel
{
    protected override void OnBusyChanged(bool isBusy)
    {
        UpdateCanStartPreview();
        UpdateCloneState();
    }

    private void UpdateCloneState()
    {
        var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(CloneUrl);
        CanClone = target.IsValid && !string.IsNullOrWhiteSpace(CloneParent) && !IsBusy;
        if (!target.IsValid)
        {
            CloneTargetSummary = string.IsNullOrWhiteSpace(CloneUrl)
                ? "貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository / Pages 網址後，Hugoer 會複製到本機並開啟。"
                : target.ErrorMessage;
            return;
        }

        var destination = GitHubClonePath.TryGetDestination(CloneParent, target.Repository, out var pathError);
        var pagesUrl = string.IsNullOrWhiteSpace(target.PagesUrl)
            ? "此平台/此 repo 沒有可推導的專案 Pages 網址"
            : target.PagesUrl;
        CloneTargetSummary =
            $"平台：{target.ProviderName}\n" +
            $"Repository：{target.Owner}/{target.Repository}\n" +
            $"網站類型：{(target.IsUserOrOrganizationSite ? "使用者／組織網站" : "專案網站")}\n" +
            $"建議網址：{pagesUrl}\n" +
            $"本機目標：{destination ?? pathError}";
    }

    private void ApplyPagesRepositories(GitHubPagesRepositoryList list)
    {
        PagesRepositories.Clear();
        foreach (var item in list.Repositories)
            PagesRepositories.Add(item);
        HasPagesRepositories = PagesRepositories.Count > 0;
        if (PagesRepositories.Count == 1)
            SelectedPagesRepository = PagesRepositories[0];
    }

    partial void OnPreviewReadyChanged(bool value) => UpdateCanStartPreview();

    private void UpdateCanStartPreview() => CanStartPreview = !IsBusy && !PreviewReady;

    private void HandlePreviewProcessExited(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !ReferenceEquals(sender, _previewProcess))
                return;

            PreviewReady = false;
            try { _previewProcess?.Dispose(); } catch { /* ignore */ }
            _previewProcess = null;
            StatusMessage = "本機預覽已停止；請重新啟動預覽。";
            AppendLog(StatusMessage);
        });
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var process = _previewProcess;
        _previewProcess = null;
        PreviewReady = false;
        if (process is not null)
        {
            try { process.Exited -= HandlePreviewProcessExited; } catch { /* ignore */ }
            try { Services.Hugo.StopServer(process); } catch { /* ignore */ }
        }
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private void RefreshSite()
    {
        if (string.IsNullOrWhiteSpace(SitePath) || !Directory.Exists(SitePath))
        {
            SiteSummary = "尚未選擇網站";
            return;
        }

        var info = Services.Hugo.InspectSite(SitePath);
        if (info is null)
        {
            SiteSummary = "路徑無效";
            return;
        }

        SiteSummary = $"{info.Name}\n路徑：{info.Path}\n設定檔：{info.ConfigFile ?? "（無）"}\n主題：{info.ThemeName ?? "（未設定）"}\nGit：{(info.HasGit ? "已初始化" : "尚未")}";
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
        Log = string.IsNullOrEmpty(Log) ? line : Log + Environment.NewLine + line;
    }
}
