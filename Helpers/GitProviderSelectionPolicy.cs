using Hugoer.Models;

namespace Hugoer.Helpers;

/// <summary>Determines whether a refresh may replace the user's selected Git provider.</summary>
public static class GitProviderSelectionPolicy
{
    public static bool ShouldAdoptDetectedProvider(
        bool providerWasSelectedByUser,
        GitHostingProvider? detectedProvider,
        GitHostingProvider activeProvider) =>
        !providerWasSelectedByUser
        && detectedProvider is { } provider
        && provider != activeProvider;
}
