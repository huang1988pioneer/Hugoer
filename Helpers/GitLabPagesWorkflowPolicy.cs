namespace Hugoer.Helpers;

public static class GitLabPagesWorkflowPolicy
{
    public static bool ShouldRewrite(string? workflowText)
    {
        if (string.IsNullOrWhiteSpace(workflowText))
            return true;

        return workflowText.Contains("hugomods/hugo", StringComparison.OrdinalIgnoreCase)
               || workflowText.Contains("image: alpine", StringComparison.OrdinalIgnoreCase)
               || workflowText.Contains("apk add", StringComparison.OrdinalIgnoreCase)
               || !workflowText.Contains("HUGO_VERSION", StringComparison.Ordinal)
               || !workflowText.Contains("0.165.0", StringComparison.Ordinal)
               || !workflowText.Contains("GIT_SUBMODULE_STRATEGY", StringComparison.Ordinal);
    }
}
