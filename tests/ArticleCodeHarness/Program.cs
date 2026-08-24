using Hugoer.Helpers;

var day = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.FromHours(8));

Assert(ArticleCode.Format(day, 1) == "20260823-1", "First code of the day is yyyyMMdd-1.");
Assert(ArticleCode.NextFromNames([], day) == "20260823-1", "Empty folder starts at -1.");
Assert(ArticleCode.NextFromNames(["hello-world.md", "notes.md"], day) == "20260823-1",
    "Unrelated names do not consume the sequence.");
Assert(ArticleCode.NextFromNames(["20260823-1.md", "20260823-2.md"], day) == "20260823-3",
    "Sequence continues after existing codes.");
Assert(ArticleCode.NextFromNames(["20260823-1.md", "20260823-4.md"], day) == "20260823-5",
    "Sequence uses max+1 rather than filling gaps.");
Assert(ArticleCode.NextFromNames(["20260822-9.md", "20260823-1.md"], day) == "20260823-2",
    "Other days do not affect today's sequence.");
Assert(ArticleCode.NextFromNames(["20260823-01.md"], day) == "20260823-2",
    "Zero-padded numbers still count toward the max.");
Assert(ArticleCode.NextFromNames(["20260823-1"], day) == "20260823-2",
    "Leaf-bundle folder names are counted.");
Assert(ArticleCode.NextInDirectory(null, day) == "20260823-1",
    "Missing directory starts at -1.");

var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hugoer-article-code-" + Guid.NewGuid().ToString("N")));
try
{
    File.WriteAllText(Path.Combine(temp.FullName, "20260823-1.md"), "x");
    Directory.CreateDirectory(Path.Combine(temp.FullName, "20260823-2"));
    Assert(ArticleCode.NextInDirectory(temp.FullName, day) == "20260823-3",
        "Directory scan includes files and folders.");
}
finally
{
    temp.Delete(true);
}

Console.WriteLine("ARTICLE_CODE_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
