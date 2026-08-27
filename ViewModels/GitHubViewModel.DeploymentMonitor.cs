using CommunityToolkit.Mvvm.Input;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.ViewModels;

public partial class GitHubViewModel
{
    [RelayCommand]
    private Task CheckDeploymentNowAsync() =>
        CheckDeploymentVersionAsync(manual: true, CancellationToken.None);

    private void EnsureDeploymentMonitorStarted()
    {
        if (_deploymentMonitorCts is not null) return;
        _deploymentMonitorCts = new CancellationTokenSource();
        _ = MonitorDeploymentLoopAsync(_deploymentMonitorCts.Token);
    }

    private async Task MonitorDeploymentLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await CheckDeploymentVersionAsync(manual: false, cancellationToken);
                await Task.Delay(DeploymentCheckInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The application is closing.
        }
    }

    private async Task CheckDeploymentVersionAsync(bool manual, CancellationToken cancellationToken)
    {
        if (!await _deploymentCheckGate.WaitAsync(0, cancellationToken)) return;

        var providerAtStart = _activeProvider;
        var pagesUrlAtStart = PagesUrl;
        IsCheckingDeployment = true;
        DeploymentMonitorTitle = "正在檢查線上版本…";
        DeploymentMonitorSchedule = "每 5 分鐘自動檢查 · 正在連線";
        try
        {
            if (!HasLocalSite)
            {
                DeploymentMonitorTitle = "尚未選擇網站";
                DeploymentMonitorSummary = "請先在「環境」開啟、建立或從 Git 平台複製 Hugo 網站。";
                DeploymentMonitorSchedule = "選擇網站後開始每 5 分鐘檢查";
                return;
            }

            var site = Services.CurrentSitePath!;

            if (string.IsNullOrWhiteSpace(PagesUrl))
            {
                var pages = await Services.GitHub.GetPagesStatusAsync(site, GetActiveRepositoryTarget(), cancellationToken);
                PagesUrl = pages.HtmlUrl;
                pagesUrlAtStart = PagesUrl;
            }

            var result = await Services.DeploymentMonitor.CheckAsync(site, pagesUrlAtStart, cancellationToken);
            if (!IsCurrentDeploymentTarget(providerAtStart, pagesUrlAtStart))
                return;

            var stateChanged = result.State != _lastDeploymentState
                               || !string.Equals(result.ExpectedDeploymentId, _lastExpectedDeploymentId,
                                   StringComparison.Ordinal)
                               || _lastDeploymentProvider != providerAtStart
                               || !string.Equals(_lastDeploymentPagesUrl, pagesUrlAtStart, StringComparison.OrdinalIgnoreCase);

            DeploymentMonitorTitle = result.State switch
            {
                DeploymentVersionState.Latest => "線上網站已是最新版本",
                DeploymentVersionState.Previous => "線上網站仍是上一版本",
                DeploymentVersionState.Unavailable => "暫時無法檢查",
                _ => "等待下一次部署"
            };
            DeploymentMonitorSummary = result.Message;
            DeploymentMonitorSchedule =
                $"每 5 分鐘自動檢查 · 上次：{result.CheckedAt.LocalDateTime:yyyy/MM/dd HH:mm:ss}";

            if (manual || stateChanged)
                AppendLog($"線上版本監控：{result.Message}");

            if (result.State == DeploymentVersionState.Latest && stateChanged)
            {
                StatusMessage = "網站已更新：線上內容是最新版本。";
                Services.SetAppStatus("網站已更新為最新版本");
            }
            else if (result.State == DeploymentVersionState.Previous && stateChanged)
            {
                StatusMessage = "線上網站仍是上一版本，將在 5 分鐘後再次檢查。";
                Services.SetAppStatus("線上網站仍是上一版本 · 自動監控中");
            }
            else if (manual)
            {
                StatusMessage = result.Message;
            }

            _lastDeploymentState = result.State;
            _lastExpectedDeploymentId = result.ExpectedDeploymentId;
            _lastDeploymentProvider = providerAtStart;
            _lastDeploymentPagesUrl = pagesUrlAtStart;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The application is closing.
        }
        catch (Exception ex)
        {
            DeploymentMonitorTitle = "暫時無法檢查";
            DeploymentMonitorSummary = $"檢查線上版本時發生錯誤：{ex.Message}";
            DeploymentMonitorSchedule = "每 5 分鐘自動檢查 · 稍後重試";
            if (manual) AppendLog(DeploymentMonitorSummary);
        }
        finally
        {
            IsCheckingDeployment = false;
            _deploymentCheckGate.Release();
        }
    }

    private async Task CheckDeploymentVersionAfterPushAsync(CancellationToken cancellationToken)
    {
        await CheckDeploymentVersionAsync(manual: false, cancellationToken);
        if (_lastDeploymentState == DeploymentVersionState.Latest)
            return;

        for (var attempt = 0; attempt < PostPushDeploymentCheckDelays.Length; attempt++)
        {
            var delay = PostPushDeploymentCheckDelays[attempt];
            DeploymentMonitorTitle = "等待 Pages 發布最新內容…";
            DeploymentMonitorSummary = $"{_activeProvider.PagesProductName()} 可能還在更新快取；Hugoer 會在 {delay.TotalSeconds:0} 秒後再次檢查。";
            DeploymentMonitorSchedule = $"推送後快速檢查 · 第 {attempt + 2} 次";
            StatusMessage = "網站已推送，正在等待線上版本更新。";

            await Task.Delay(delay, cancellationToken);
            await CheckDeploymentVersionAsync(manual: true, cancellationToken);
            if (_lastDeploymentState == DeploymentVersionState.Latest)
                return;
        }
    }

    public override void Dispose()
    {
        _deploymentMonitorCts?.Cancel();
        _deploymentMonitorCts?.Dispose();
        _deploymentMonitorCts = null;
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
