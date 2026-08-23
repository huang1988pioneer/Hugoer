using Hugoer.Services;

var root = Path.Combine(Path.GetTempPath(), "HugoerMediaAssetTests", Guid.NewGuid().ToString("N"));
var site = Path.Combine(root, "site");
Directory.CreateDirectory(site);

try
{
    Assert(MediaAssetService.Classify("a.PNG") == MediaKind.Image, "png is image");
    Assert(MediaAssetService.Classify("a.mp3") == MediaKind.Music, "mp3 is music");
    Assert(MediaAssetService.Classify("note.m4a") == MediaKind.Voice, "m4a is voice by default");
    Assert(MediaAssetService.Classify("note.m4a", MediaKind.Music) == MediaKind.Music, "forced music wins");
    Assert(MediaAssetService.Classify("clip.MP4") == MediaKind.Video, "mp4 is video");
    Assert(MediaAssetService.Classify("doc.PDF") == MediaKind.Pdf, "pdf is pdf");
    Assert(MediaAssetService.Classify("notes.docx") == MediaKind.Document, "docx is document");
    Assert(MediaAssetService.Classify("pack.zip") == MediaKind.File, "zip is file");
    Assert(MediaAssetService.FolderName(MediaKind.Image) == "image", "image folder");
    Assert(MediaAssetService.FolderName(MediaKind.Voice) == "voice", "voice folder");

    var photo = WriteTemp("photo.png", "png");
    var song = WriteTemp("song.mp3", "mp3");
    var voice = WriteTemp("hello.m4a", "m4a");
    var clip = WriteTemp("clip.mp4", "mp4");
    var pdf = WriteTemp("paper.pdf", "pdf");
    var zip = WriteTemp("pack.zip", "zip");
    var spaced = WriteTemp("evil name.png", "space");

    var imageAsset = MediaAssetService.Import(site, photo);
    Assert(imageAsset.Folder == "image", "copied into image");
    Assert(imageAsset.PublicUrl == "/image/photo.png", $"public url {imageAsset.PublicUrl}");
    Assert(File.Exists(Path.Combine(site, "static", "image", "photo.png")), "static/image file exists");
    Assert(imageAsset.Markdown.Contains("![photo](/image/photo.png)", StringComparison.Ordinal), imageAsset.Markdown);

    var duplicate = MediaAssetService.Import(site, photo);
    Assert(Path.GetFileName(duplicate.DestinationPath) == "photo-1.png", "collision gets unique name");
    Assert(duplicate.PublicUrl == "/image/photo-1.png", duplicate.PublicUrl);

    var reused = MediaAssetService.Import(site, imageAsset.DestinationPath);
    Assert(reused.DestinationPath == imageAsset.DestinationPath, "importing an existing static file reuses it");

    var musicAsset = MediaAssetService.Import(site, song);
    Assert(musicAsset.PublicUrl == "/music/song.mp3", musicAsset.PublicUrl);
    Assert(musicAsset.Markdown.Contains("<audio controls src=\"/music/song.mp3\"></audio>", StringComparison.Ordinal), musicAsset.Markdown);

    var forcedVoice = MediaAssetService.Import(site, song, MediaKind.Voice);
    Assert(forcedVoice.Folder == "voice", "語音按鈕強制 voice 資料夾");
    Assert(File.Exists(Path.Combine(site, "static", "voice", "song.mp3")), "static/voice exists");

    var voiceAsset = MediaAssetService.Import(site, voice);
    Assert(voiceAsset.PublicUrl == "/voice/hello.m4a", voiceAsset.PublicUrl);

    var videoAsset = MediaAssetService.Import(site, clip);
    Assert(videoAsset.Markdown.Contains("<video controls src=\"/video/clip.mp4\"></video>", StringComparison.Ordinal), videoAsset.Markdown);

    var pdfAsset = MediaAssetService.Import(site, pdf);
    Assert(pdfAsset.PublicUrl == "/pdf/paper.pdf", pdfAsset.PublicUrl);
    Assert(pdfAsset.Markdown.Contains("[paper.pdf](/pdf/paper.pdf)", StringComparison.Ordinal), pdfAsset.Markdown);

    var zipAsset = MediaAssetService.Import(site, zip);
    Assert(zipAsset.Folder == "file", zipAsset.Folder);
    Assert(zipAsset.PublicUrl == "/file/pack.zip", zipAsset.PublicUrl);

    var many = MediaAssetService.ImportMany(site, [photo, clip, pdf]);
    Assert(many.Count == 3, "import many");
    Assert(MediaAssetService.JoinMarkdown(many).Contains("/video/", StringComparison.Ordinal), "joined markdown");

    var sanitized = MediaAssetService.SanitizeFileName("..\\evil name.png");
    Assert(!sanitized.Contains('\\'), sanitized);
    Assert(!sanitized.Contains(".."), sanitized);
    var spacedAsset = MediaAssetService.Import(site, spaced);
    Assert(spacedAsset.PublicUrl == "/image/evil%20name.png", spacedAsset.PublicUrl);
    Assert(IsUnderStatic(site, spacedAsset.DestinationPath), "must stay in static");

    var html = """<p><img src="/image/photo.png" alt="photo"/></p>""";
    var preview = MediaAssetService.ToPreviewHtml(html, site);
    Assert(preview.Contains("file:", StringComparison.OrdinalIgnoreCase), preview);
    Assert(preview.Contains("photo.png", StringComparison.Ordinal), preview);
    var roundTrip = MediaAssetService.FromPreviewHtml(preview, site);
    Assert(roundTrip.Contains("src=\"/image/photo.png\"", StringComparison.Ordinal), roundTrip);

    var inserted = MarkdownEditingService.InsertSnippet("# 標題\n\n正文", 6, 0, "![photo](/image/photo.png)");
    Assert(inserted.Text.Contains("![photo](/image/photo.png)", StringComparison.Ordinal), inserted.Text);

    Console.WriteLine("MEDIA_ASSET_HARNESS_OK");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { /* best-effort cleanup */ }
}

static string WriteTemp(string name, string marker)
{
    var dir = Path.Combine(Path.GetTempPath(), "HugoerMediaAssetTests", "src", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, name);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, marker);
    return path;
}

static bool IsUnderStatic(string site, string path)
{
    var root = Path.GetFullPath(Path.Combine(site, "static"))
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var full = Path.GetFullPath(path);
    return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
