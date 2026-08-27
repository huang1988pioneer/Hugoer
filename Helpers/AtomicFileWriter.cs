using System.Text;

namespace Hugoer.Helpers;

/// <summary>
/// Writes text through a same-directory temporary file and replaces the
/// destination only after the complete payload is on disk.
/// </summary>
public static class AtomicFileWriter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string content)
    {
        var (fullPath, tempPath) = Prepare(path);
        try
        {
            File.WriteAllText(tempPath, content, Utf8NoBom);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public static async Task WriteAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default)
    {
        var (fullPath, tempPath) = Prepare(path);
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Utf8NoBom, cancellationToken)
                .ConfigureAwait(false);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static (string FullPath, string TempPath) Prepare(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("檔案路徑不可為空。", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("檔案路徑格式無效。", nameof(path));
        Directory.CreateDirectory(directory);
        return (fullPath, Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp"));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Never mask the original write/replace failure with cleanup noise.
        }
    }
}
