using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tomlyn;
using Tomlyn.Model;

namespace Hugoer.Services;

public sealed partial class ConfigFieldItem : ObservableObject
{
    public required string Path { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Group { get; init; }
    public required ParamFieldKind Kind { get; init; }
    public string DefaultValue { get; init; } = string.Empty;
    public string Example { get; init; } = string.Empty;
    public string DocumentationUrl { get; init; } = string.Empty;
    public bool IsKnown { get; init; } = true;

    [ObservableProperty]
    public partial bool IsConfigured { get; set; }

    [ObservableProperty]
    public partial string StringValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool BoolValue { get; set; }

    public bool IsBool => Kind == ParamFieldKind.Bool;
    public bool IsText => !IsBool;
    public string KindLabel => Kind switch
    {
        ParamFieldKind.Bool => "布林 bool",
        ParamFieldKind.Number => "數字 number",
        ParamFieldKind.Array => "陣列 array（逗號分隔）",
        ParamFieldKind.Nested => "物件 table",
        _ => "文字 string"
    };
    public string DefaultLabel => string.IsNullOrWhiteSpace(DefaultValue) ? "未設定" : DefaultValue;
    public string SearchText => $"{Path} {DisplayName} {Description} {Group}";

    [RelayCommand]
    private void OpenDocumentation()
    {
        if (string.IsNullOrWhiteSpace(DocumentationUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DocumentationUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // The URL remains visible in the UI when no browser handler is available.
        }
    }
}

public sealed class HugoConfigService
{
    private const string Docs = "https://gohugo.io/configuration";

    public static IReadOnlyList<ConfigFieldDefinition> Definitions { get; } =
    [
        // Site identity and URLs
        F("baseURL", "網站網址", "發佈網站的絕對網址；包含協定、主機、子路徑及結尾斜線。", ParamFieldKind.String, "網站與網址", "", "https://example.org/", $"{Docs}/all/#baseurl"),
        F("title", "網站標題", "網站的全域標題。", ParamFieldKind.String, "網站與網址", "", "My Hugo Site", $"{Docs}/all/#title"),
        F("copyright", "版權文字", "網站層級的版權文字，可由主題顯示。", ParamFieldKind.String, "網站與網址", "", "© 2026 Example", $"{Docs}/all/"),
        F("locale", "語系", "用於翻譯、日期及數字格式的語系識別碼；新版 Hugo 取代 languageCode。", ParamFieldKind.String, "網站與網址", "en-US", "zh-TW", $"{Docs}/all/#locale"),
        F("timeZone", "時區", "解析不含時區偏移日期時使用的 IANA 時區。", ParamFieldKind.String, "網站與網址", "", "Asia/Taipei", $"{Docs}/all/#timezone"),
        F("defaultContentLanguage", "預設內容語言", "多語網站的預設語言代碼。", ParamFieldKind.String, "網站與網址", "en", "zh-tw", $"{Docs}/languages/"),
        F("defaultContentLanguageInSubdir", "預設語言使用子目錄", "讓預設語言也使用語言子目錄。", ParamFieldKind.Bool, "網站與網址", "false", "", $"{Docs}/languages/"),
        F("hasCJKLanguage", "啟用 CJK 字數計算", "針對中日韓內容使用適合的字數與摘要計算。", ParamFieldKind.Bool, "網站與網址", "false", "", $"{Docs}/all/#hascjklanguage"),
        F("enableRobotsTXT", "產生 robots.txt", "使用內建或自訂範本產生 robots.txt。", ParamFieldKind.Bool, "網站與網址", "false", "", $"{Docs}/all/#enablerobotstxt"),
        F("canonifyURLs", "絕對化內容 URL", "將產生內容中的相對 URL 轉為絕對 URL；通常優先保留 false。", ParamFieldKind.Bool, "網站與網址", "false", "", $"{Docs}/all/#canonifyurls"),
        F("relativeURLs", "使用相對 URL", "讓產生的 URL 相對於目前內容；不適合所有部署情境。", ParamFieldKind.Bool, "網站與網址", "false", "", $"{Docs}/all/#relativeurls"),
        F("uglyURLs", "使用副檔名 URL", "以 /about.html 形式輸出，而非 /about/。", ParamFieldKind.Bool, "網站與網址", "false", "", $"{Docs}/ugly-urls/"),
        F("disablePathToLower", "保留路徑大小寫", "停用 Hugo 將路徑轉為小寫的行為。", ParamFieldKind.Bool, "網站與網址", "false", "", $"{Docs}/all/#disablepathtolower"),
        F("removePathAccents", "移除路徑重音符號", "從自動產生的 URL 路徑移除重音符號。", ParamFieldKind.Bool, "網站與網址", "false", "", $"{Docs}/all/#removepathaccents"),
        F("titleCaseStyle", "標題大小寫規則", "自動標題的大小寫規則：ap、chicago、go、firstupper 或 none。", ParamFieldKind.String, "網站與網址", "ap", "none", $"{Docs}/all/#title-case-style"),

        // Project layout and build behavior
        F("contentDir", "內容目錄", "內容檔案目錄。", ParamFieldKind.String, "目錄與建置", "content", "content", $"{Docs}/all/#contentdir"),
        F("publishDir", "輸出目錄", "建置完成網站的輸出目錄。", ParamFieldKind.String, "目錄與建置", "public", "public", $"{Docs}/all/#publishdir"),
        F("staticDir", "靜態資源目錄", "一個或多個靜態資源目錄。", ParamFieldKind.Array, "目錄與建置", "static", "static, public-assets", $"{Docs}/all/#staticdir"),
        F("assetDir", "Assets 目錄", "全域資源與 Hugo Pipes 的來源目錄。", ParamFieldKind.String, "目錄與建置", "assets", "assets", $"{Docs}/all/#assetdir"),
        F("dataDir", "資料目錄", "Hugo data 檔案所在目錄。", ParamFieldKind.String, "目錄與建置", "data", "data", $"{Docs}/all/#datadir"),
        F("layoutDir", "版型目錄", "自訂 layout 範本目錄。", ParamFieldKind.String, "目錄與建置", "layouts", "layouts", $"{Docs}/all/#layoutdir"),
        F("archetypeDir", "Archetype 目錄", "新增內容時使用的 archetype 目錄。", ParamFieldKind.String, "目錄與建置", "archetypes", "archetypes", $"{Docs}/all/#archetypedir"),
        F("themesDir", "主題目錄", "Hugo 主題的父目錄。", ParamFieldKind.String, "目錄與建置", "themes", "themes", $"{Docs}/all/#themesdir"),
        F("resourceDir", "資源快取目錄", "產生或快取處理後資源的目錄。", ParamFieldKind.String, "目錄與建置", "resources", "resources", $"{Docs}/all/#resourcedir"),
        F("cacheDir", "快取目錄", "Hugo 檔案快取位置。", ParamFieldKind.String, "目錄與建置", "系統快取", "", $"{Docs}/all/#cachedir"),
        F("theme", "主題", "使用的主題名稱；多主題時可使用陣列。", ParamFieldKind.String, "目錄與建置", "", "Stack", $"{Docs}/all/#theme"),
        F("timeout", "建置逾時", "避免範本遞迴或遠端資源無限等待的逾時時間。", ParamFieldKind.String, "目錄與建置", "60s", "120s", $"{Docs}/all/#timeout"),
        F("buildDrafts", "建置草稿", "正式建置時包含 draft 內容。", ParamFieldKind.Bool, "目錄與建置", "false", "", $"{Docs}/all/#builddrafts"),
        F("buildFuture", "建置未來內容", "包含 publishDate 在未來的內容。", ParamFieldKind.Bool, "目錄與建置", "false", "", $"{Docs}/all/#buildfuture"),
        F("buildExpired", "建置過期內容", "包含 expiryDate 已過期的內容。", ParamFieldKind.Bool, "目錄與建置", "false", "", $"{Docs}/all/#buildexpired"),
        F("disableKinds", "停用頁面種類", "不產生指定 kind，例如 taxonomy、term、RSS。", ParamFieldKind.Array, "目錄與建置", "", "taxonomy, term, RSS", $"{Docs}/all/#disablekinds"),
        F("ignoreFiles", "忽略檔案規則", "建置時忽略符合正規表示式的檔案。", ParamFieldKind.Array, "目錄與建置", "", "\\.tmp$", $"{Docs}/all/#ignorefiles"),
        F("cleanDestinationDir", "清理輸出目錄", "刪除目的地中不再由 static 提供的檔案。", ParamFieldKind.Bool, "目錄與建置", "false", "", $"{Docs}/all/#cleandestinationdir"),
        F("enableGitInfo", "啟用 Git 資訊", "從最後提交取得作者與日期等頁面資訊。", ParamFieldKind.Bool, "目錄與建置", "false", "", $"{Docs}/all/#enablegitinfo"),
        F("build.noJSConfigInAssets", "不產生 jsconfig.json", "使用 js.Build 時不在 assets 產生編輯器導覽用 jsconfig.json。", ParamFieldKind.Bool, "目錄與建置", "false", "", $"{Docs}/build/"),
        F("build.useResourceCacheWhen", "資源快取使用時機", "Sass 等轉譯使用檔案快取的時機：never、fallback 或 always。", ParamFieldKind.String, "目錄與建置", "fallback", "always", $"{Docs}/build/"),
        F("build.buildStats.enable", "產生建置統計", "產生 hugo_stats.json，供 Tailwind 或 CSS pruning 使用。", ParamFieldKind.Bool, "目錄與建置", "false", "", $"{Docs}/build/"),
        F("build.buildStats.disableClasses", "統計排除 class", "不在 build stats 記錄 HTML class。", ParamFieldKind.Bool, "目錄與建置", "false", "", $"{Docs}/build/"),
        F("build.buildStats.disableIDs", "統計排除 ID", "不在 build stats 記錄 HTML id。", ParamFieldKind.Bool, "目錄與建置", "false", "", $"{Docs}/build/"),
        F("build.buildStats.disableTags", "統計排除 tag", "不在 build stats 記錄 HTML tag。", ParamFieldKind.Bool, "目錄與建置", "false", "", $"{Docs}/build/"),

        // Content, page and pagination
        F("summaryLength", "自動摘要長度", "未指定摘要分隔符時使用的字數。", ParamFieldKind.Number, "內容與頁面", "70", "120", $"{Docs}/all/#summarylength"),
        F("pluralizeListTitles", "複數化列表標題", "自動產生列表標題時使用複數形式。", ParamFieldKind.Bool, "內容與頁面", "true", "", $"{Docs}/all/#pluralizelisttitles"),
        F("capitalizeListTitles", "列表標題首字大寫", "自動將列表頁標題套用 titleCaseStyle。", ParamFieldKind.Bool, "內容與頁面", "true", "", $"{Docs}/all/#capitalizelisttitles"),
        F("pagination.pagerSize", "每頁文章數", "分頁器每頁顯示的項目數。", ParamFieldKind.Number, "內容與頁面", "10", "12", $"{Docs}/pagination/"),
        F("pagination.path", "分頁路徑", "分頁 URL 的路徑片段。", ParamFieldKind.String, "內容與頁面", "page", "page", $"{Docs}/pagination/"),
        F("pagination.disableAliases", "停用舊分頁別名", "不為先前分頁 URL 建立 alias。", ParamFieldKind.Bool, "內容與頁面", "false", "", $"{Docs}/pagination/"),
        F("taxonomies.category", "分類 Taxonomy", "分類 taxonomy 的單數名稱對應。", ParamFieldKind.String, "內容與頁面", "categories", "categories", $"{Docs}/taxonomies/"),
        F("taxonomies.tag", "標籤 Taxonomy", "標籤 taxonomy 的單數名稱對應。", ParamFieldKind.String, "內容與頁面", "tags", "tags", $"{Docs}/taxonomies/"),
        F("permalinks.page.posts", "文章固定網址", "posts 頁面種類的 permalink 模式。", ParamFieldKind.String, "內容與頁面", "", "/posts/:year/:month/:slug/", $"{Docs}/permalinks/"),
        F("refLinksErrorLevel", "無效引用錯誤層級", "ref/relref 找不到目標時的紀錄層級。", ParamFieldKind.String, "內容與頁面", "ERROR", "WARNING", $"{Docs}/all/#reflinkserrorlevel"),
        F("refLinksNotFoundURL", "無效引用替代網址", "ref/relref 找不到目標時使用的 URL。", ParamFieldKind.String, "內容與頁面", "", "/404.html", $"{Docs}/all/#reflinksnotfoundurl"),

        // Outputs and sitemap
        F("outputs.home", "首頁輸出格式", "首頁要產生的輸出格式。", ParamFieldKind.Array, "輸出與 Sitemap", "HTML, RSS", "HTML, RSS, JSON", $"{Docs}/outputs/"),
        F("outputs.page", "內容頁輸出格式", "一般內容頁要產生的輸出格式。", ParamFieldKind.Array, "輸出與 Sitemap", "HTML", "HTML", $"{Docs}/outputs/"),
        F("outputs.section", "Section 輸出格式", "Section 列表頁要產生的格式。", ParamFieldKind.Array, "輸出與 Sitemap", "HTML, RSS", "HTML, RSS", $"{Docs}/outputs/"),
        F("outputs.taxonomy", "Taxonomy 輸出格式", "Taxonomy 列表頁要產生的格式。", ParamFieldKind.Array, "輸出與 Sitemap", "HTML, RSS", "HTML, RSS", $"{Docs}/outputs/"),
        F("outputs.term", "Term 輸出格式", "Term 頁面要產生的格式。", ParamFieldKind.Array, "輸出與 Sitemap", "HTML, RSS", "HTML, RSS", $"{Docs}/outputs/"),
        F("outputFormats.RSS.baseName", "RSS 檔名", "RSS 輸出的基本檔名。", ParamFieldKind.String, "輸出與 Sitemap", "index", "feed", $"{Docs}/output-formats/"),
        F("outputFormats.RSS.mediaType", "RSS 媒體類型", "RSS 輸出使用的 media type。", ParamFieldKind.String, "輸出與 Sitemap", "application/rss+xml", "application/rss+xml", $"{Docs}/output-formats/"),
        F("sitemap.filename", "Sitemap 檔名", "Sitemap 輸出檔名。", ParamFieldKind.String, "輸出與 Sitemap", "sitemap.xml", "sitemap.xml", $"{Docs}/sitemap/"),
        F("sitemap.changeFreq", "Sitemap 更新頻率", "提供給搜尋引擎的更新頻率提示。", ParamFieldKind.String, "輸出與 Sitemap", "", "weekly", $"{Docs}/sitemap/"),
        F("sitemap.priority", "Sitemap 優先度", "提供給搜尋引擎的預設優先度。", ParamFieldKind.Number, "輸出與 Sitemap", "-1", "0.5", $"{Docs}/sitemap/"),
        F("sitemap.disable", "停用 Sitemap", "不產生 sitemap.xml。", ParamFieldKind.Bool, "輸出與 Sitemap", "false", "", $"{Docs}/sitemap/"),

        // Markdown and highlighting
        F("markup.defaultMarkdownHandler", "Markdown 處理器", "Markdown 預設處理器；官方建議 goldmark。", ParamFieldKind.String, "Markdown 與語法", "goldmark", "goldmark", $"{Docs}/markup/"),
        F("markup.goldmark.renderer.unsafe", "允許 Markdown 原始 HTML", "允許內容中的原始 HTML；僅在信任內容來源時啟用。", ParamFieldKind.Bool, "Markdown 與語法", "false", "", $"{Docs}/markup/#renderer"),
        F("markup.goldmark.renderer.hardWraps", "換行轉 br", "將段落內換行轉為 br。", ParamFieldKind.Bool, "Markdown 與語法", "false", "", $"{Docs}/markup/#renderer"),
        F("markup.goldmark.parser.autoHeadingID", "自動產生標題 ID", "為 Markdown 標題自動產生 id。", ParamFieldKind.Bool, "Markdown 與語法", "true", "", $"{Docs}/markup/#parser"),
        F("markup.goldmark.parser.autoIDType", "標題 ID 規則", "github、github-ascii 或 blackfriday。", ParamFieldKind.String, "Markdown 與語法", "github", "github", $"{Docs}/markup/#parser"),
        F("markup.goldmark.parser.attribute.block", "區塊屬性", "允許在 Markdown 區塊元素加入屬性。", ParamFieldKind.Bool, "Markdown 與語法", "false", "", $"{Docs}/markup/#parser"),
        F("markup.goldmark.parser.attribute.title", "標題屬性", "允許在 Markdown 標題加入屬性。", ParamFieldKind.Bool, "Markdown 與語法", "true", "", $"{Docs}/markup/#parser"),
        F("markup.goldmark.extensions.table", "Markdown 表格", "啟用 GFM 表格語法。", ParamFieldKind.Bool, "Markdown 與語法", "true", "", $"{Docs}/markup/#extensions"),
        F("markup.goldmark.extensions.taskList", "Markdown 工作清單", "啟用工作清單語法。", ParamFieldKind.Bool, "Markdown 與語法", "true", "", $"{Docs}/markup/#extensions"),
        F("markup.goldmark.extensions.strikethrough", "Markdown 刪除線", "啟用刪除線語法。", ParamFieldKind.Bool, "Markdown 與語法", "true", "", $"{Docs}/markup/#extensions"),
        F("markup.goldmark.extensions.typographer", "排版替換", "將引號、省略號等字元組合轉成排版實體。", ParamFieldKind.Bool, "Markdown 與語法", "true", "", $"{Docs}/markup/#extensions"),
        F("markup.highlight.style", "程式碼配色", "Chroma 語法醒目提示樣式。", ParamFieldKind.String, "Markdown 與語法", "monokai", "github-dark", $"{Docs}/markup/#highlight"),
        F("markup.highlight.lineNos", "顯示程式碼行號", "為程式碼區塊顯示行號。", ParamFieldKind.Bool, "Markdown 與語法", "false", "", $"{Docs}/markup/#highlight"),
        F("markup.highlight.guessSyntax", "自動猜測語法", "未提供語言時嘗試猜測 lexer。", ParamFieldKind.Bool, "Markdown 與語法", "false", "", $"{Docs}/markup/#highlight"),
        F("markup.highlight.noClasses", "使用行內樣式", "使用行內 style 而非 CSS class。", ParamFieldKind.Bool, "Markdown 與語法", "true", "", $"{Docs}/markup/#highlight"),
        F("markup.highlight.tabWidth", "Tab 寬度", "語法醒目提示中的 Tab 空格數。", ParamFieldKind.Number, "Markdown 與語法", "4", "4", $"{Docs}/markup/#highlight"),
        F("markup.tableOfContents.startLevel", "目錄起始層級", "目錄包含的最小標題層級。", ParamFieldKind.Number, "Markdown 與語法", "2", "2", $"{Docs}/markup/#table-of-contents"),
        F("markup.tableOfContents.endLevel", "目錄結束層級", "目錄包含的最大標題層級。", ParamFieldKind.Number, "Markdown 與語法", "3", "4", $"{Docs}/markup/#table-of-contents"),
        F("markup.tableOfContents.ordered", "有序目錄", "使用 ol 而非 ul 產生文章目錄。", ParamFieldKind.Bool, "Markdown 與語法", "false", "", $"{Docs}/markup/#table-of-contents"),

        // Images and minification
        F("imaging.resampleFilter", "圖片縮放濾鏡", "圖片縮放使用的重採樣濾鏡。", ParamFieldKind.String, "圖片與最佳化", "Box", "Lanczos", $"{Docs}/imaging/"),
        F("imaging.anchor", "圖片裁切錨點", "裁切及填滿時使用的錨點。", ParamFieldKind.String, "圖片與最佳化", "Smart", "Center", $"{Docs}/imaging/"),
        F("imaging.bgColor", "預設背景色", "透明圖片轉為不支援透明的格式時使用的背景色。", ParamFieldKind.String, "圖片與最佳化", "#ffffff", "#ffffff", $"{Docs}/imaging/"),
        F("imaging.jpeg.quality", "JPEG 品質", "JPEG 編碼品質；Hugo 0.163 起使用格式專屬設定。", ParamFieldKind.Number, "圖片與最佳化", "75", "85", $"{Docs}/imaging/"),
        F("imaging.webp.quality", "WebP 品質", "WebP 編碼品質；Hugo 0.163 起使用格式專屬設定。", ParamFieldKind.Number, "圖片與最佳化", "75", "85", $"{Docs}/imaging/"),
        F("imaging.webp.compression", "WebP 壓縮模式", "WebP 編碼的壓縮模式。", ParamFieldKind.String, "圖片與最佳化", "lossy", "lossless", $"{Docs}/imaging/"),
        F("imaging.webp.hint", "WebP 編碼提示", "photo、picture、drawing、icon 或 text。", ParamFieldKind.String, "圖片與最佳化", "photo", "photo", $"{Docs}/imaging/"),
        F("imaging.webp.method", "WebP 編碼方法", "WebP 壓縮速度與品質取捨。", ParamFieldKind.Number, "圖片與最佳化", "4", "6", $"{Docs}/imaging/"),
        F("imaging.webp.useSharpYuv", "WebP Sharp YUV", "使用較精確的 RGB 至 YUV 轉換。", ParamFieldKind.Bool, "圖片與最佳化", "false", "", $"{Docs}/imaging/"),
        F("imaging.avif.quality", "AVIF 品質", "AVIF 編碼品質。", ParamFieldKind.Number, "圖片與最佳化", "60", "70", $"{Docs}/imaging/"),
        F("imaging.avif.compression", "AVIF 壓縮模式", "AVIF 編碼的壓縮設定。", ParamFieldKind.String, "圖片與最佳化", "lossy", "lossless", $"{Docs}/imaging/"),
        F("imaging.avif.encoderSpeed", "AVIF 編碼速度", "編碼速度與壓縮效率取捨。", ParamFieldKind.Number, "圖片與最佳化", "6", "8", $"{Docs}/imaging/"),
        F("imaging.avif.hint", "AVIF 編碼提示", "photo、picture、drawing、icon 或 text。", ParamFieldKind.String, "圖片與最佳化", "photo", "photo", $"{Docs}/imaging/"),
        F("imaging.exif.disableDate", "停用 EXIF 日期", "不解碼 EXIF 日期欄位。", ParamFieldKind.Bool, "圖片與最佳化", "false", "", $"{Docs}/imaging/"),
        F("imaging.exif.disableLatLong", "停用 EXIF 座標", "不解碼 EXIF GPS 座標。", ParamFieldKind.Bool, "圖片與最佳化", "false", "", $"{Docs}/imaging/"),
        F("minify.minifyOutput", "壓縮輸出", "對支援的輸出格式執行壓縮。", ParamFieldKind.Bool, "圖片與最佳化", "false", "", $"{Docs}/minify/"),
        F("minify.disableCSS", "停用 CSS 壓縮", "即使啟用 minifyOutput 也不壓縮 CSS。", ParamFieldKind.Bool, "圖片與最佳化", "false", "", $"{Docs}/minify/"),
        F("minify.disableHTML", "停用 HTML 壓縮", "即使啟用 minifyOutput 也不壓縮 HTML。", ParamFieldKind.Bool, "圖片與最佳化", "false", "", $"{Docs}/minify/"),
        F("minify.disableJS", "停用 JavaScript 壓縮", "即使啟用 minifyOutput 也不壓縮 JavaScript。", ParamFieldKind.Bool, "圖片與最佳化", "false", "", $"{Docs}/minify/"),
        F("minify.disableJSON", "停用 JSON 壓縮", "即使啟用 minifyOutput 也不壓縮 JSON。", ParamFieldKind.Bool, "圖片與最佳化", "false", "", $"{Docs}/minify/"),
        F("minify.disableSVG", "停用 SVG 壓縮", "即使啟用 minifyOutput 也不壓縮 SVG。", ParamFieldKind.Bool, "圖片與最佳化", "false", "", $"{Docs}/minify/"),
        F("minify.disableXML", "停用 XML 壓縮", "即使啟用 minifyOutput 也不壓縮 XML。", ParamFieldKind.Bool, "圖片與最佳化", "false", "", $"{Docs}/minify/"),

        // Privacy, services and security
        F("privacy.disqus.disable", "停用 Disqus", "停用內建 Disqus 範本。", ParamFieldKind.Bool, "隱私與服務", "false", "", $"{Docs}/privacy/"),
        F("privacy.googleAnalytics.disable", "停用 Google Analytics", "停用內建 Google Analytics 範本。", ParamFieldKind.Bool, "隱私與服務", "false", "", $"{Docs}/privacy/"),
        F("privacy.googleAnalytics.respectDoNotTrack", "尊重 Do Not Track", "瀏覽器啟用 DNT 時不載入分析。", ParamFieldKind.Bool, "隱私與服務", "true", "", $"{Docs}/privacy/"),
        F("privacy.instagram.disable", "停用 Instagram 嵌入", "停用 Instagram shortcode。", ParamFieldKind.Bool, "隱私與服務", "false", "", $"{Docs}/privacy/"),
        F("privacy.instagram.simple", "簡化 Instagram 嵌入", "使用較少追蹤功能的簡化嵌入。", ParamFieldKind.Bool, "隱私與服務", "false", "", $"{Docs}/privacy/"),
        F("privacy.x.disable", "停用 X 嵌入", "停用 Hugo 內建 X shortcode。", ParamFieldKind.Bool, "隱私與服務", "false", "", $"{Docs}/privacy/"),
        F("privacy.x.enableDNT", "X Do Not Track", "為 X 嵌入內容啟用 Do Not Track。", ParamFieldKind.Bool, "隱私與服務", "true", "", $"{Docs}/privacy/"),
        F("privacy.x.simple", "簡化 X 嵌入", "使用簡化的 X 嵌入。", ParamFieldKind.Bool, "隱私與服務", "false", "", $"{Docs}/privacy/"),
        F("privacy.vimeo.disable", "停用 Vimeo 嵌入", "停用 Vimeo shortcode。", ParamFieldKind.Bool, "隱私與服務", "false", "", $"{Docs}/privacy/"),
        F("privacy.vimeo.enableDNT", "Vimeo Do Not Track", "為 Vimeo 嵌入啟用 DNT。", ParamFieldKind.Bool, "隱私與服務", "true", "", $"{Docs}/privacy/"),
        F("privacy.youTube.disable", "停用 YouTube 嵌入", "停用 YouTube shortcode。", ParamFieldKind.Bool, "隱私與服務", "false", "", $"{Docs}/privacy/"),
        F("privacy.youTube.privacyEnhanced", "YouTube 隱私增強", "使用 youtube-nocookie.com 嵌入。", ParamFieldKind.Bool, "隱私與服務", "false", "", $"{Docs}/privacy/"),
        F("services.disqus.shortname", "Disqus Shortname", "Hugo 內建 Disqus 範本使用的站點 shortname。", ParamFieldKind.String, "隱私與服務", "", "example", $"{Docs}/services/"),
        F("services.googleAnalytics.id", "Google Analytics ID", "內建 Analytics 範本使用的追蹤 ID。", ParamFieldKind.String, "隱私與服務", "", "G-XXXXXXXXXX", $"{Docs}/services/"),
        F("services.x.disableInlineCSS", "停用 X Inline CSS", "不讓內建 X 範本輸出 inline CSS。", ParamFieldKind.Bool, "隱私與服務", "false", "", $"{Docs}/services/"),
        F("services.rss.limit", "RSS 項目上限", "RSS feed 最多包含的項目數；-1 表示不限制。", ParamFieldKind.Number, "隱私與服務", "-1", "20", $"{Docs}/services/"),
        F("security.enableInlineShortcodes", "允許 Inline Shortcode", "啟用內嵌 shortcode；請只對可信內容開啟。", ParamFieldKind.Bool, "安全性", "false", "", $"{Docs}/security/"),
        F("security.allowContent", "允許內容 MIME", "可直接發佈的內容 MIME regex 白名單；不要用一鍵允許全部。", ParamFieldKind.Array, "安全性", "", "text/plain", $"{Docs}/security/"),
        F("security.exec.allow", "允許執行程式", "Hugo 可執行的外部程式正規表示式白名單。", ParamFieldKind.Array, "安全性", "dart-sass, go, git, node, postcss", "^git$, ^node$", $"{Docs}/security/"),
        F("security.exec.osEnv", "外部程式環境變數", "允許外部程式取得的環境變數 regex 白名單。", ParamFieldKind.Array, "安全性", "(?i)^(PATH|PATHEXT|APPDATA|TMP|TEMP|TERM)$", "^PATH$", $"{Docs}/security/"),
        F("security.funcs.getenv", "允許讀取環境變數", "範本 getenv 函式可讀取的環境變數白名單。", ParamFieldKind.Array, "安全性", "^HUGO_, ^CI$", "^HUGO_, ^CI$", $"{Docs}/security/"),
        F("security.http.methods", "允許 HTTP 方法", "遠端資源請求可使用的方法白名單。", ParamFieldKind.Array, "安全性", "GET, POST", "GET, POST", $"{Docs}/security/"),
        F("security.http.urls", "允許遠端網址", "resources.GetRemote 等功能可存取的網址正規表示式白名單。", ParamFieldKind.Array, "安全性", "受限的 HTTP(S)", "(?i)^https://", $"{Docs}/security/"),
        F("security.http.mediaTypes", "允許遠端媒體類型", "遠端資源回應允許的 MIME regex 白名單。", ParamFieldKind.Array, "安全性", "受限的常見類型", "^image/", $"{Docs}/security/"),
        F("security.node.permissions.disable", "停用 Node 權限模型", "停用 Hugo 對 Node 的權限限制，風險較高。", ParamFieldKind.Bool, "安全性", "false", "", $"{Docs}/security/"),
        F("security.node.permissions.allowAddons", "Node 原生 Addon", "允許 Node 載入原生 addon。", ParamFieldKind.Bool, "安全性", "false", "", $"{Docs}/security/"),
        F("security.node.permissions.allowChildProcess", "Node 子程序", "允許 Node 建立子程序。", ParamFieldKind.Bool, "安全性", "false", "", $"{Docs}/security/"),
        F("security.node.permissions.allowWorker", "Node Worker", "允許 Node 建立 worker thread。", ParamFieldKind.Bool, "安全性", "false", "", $"{Docs}/security/"),

        // Cache and modules
        F("caches.assets.dir", "Assets 快取目錄", "資產快取的目錄或 :cacheDir 代號。", ParamFieldKind.String, "快取與模組", ":resourceDir/_gen", ":cacheDir/assets", $"{Docs}/caches/"),
        F("caches.assets.maxAge", "Assets 快取期限", "資產快取有效時間。", ParamFieldKind.String, "快取與模組", "-1", "24h", $"{Docs}/caches/"),
        F("caches.images.dir", "圖片快取目錄", "處理後圖片快取位置。", ParamFieldKind.String, "快取與模組", ":resourceDir/_gen", ":cacheDir/images", $"{Docs}/caches/"),
        F("caches.images.maxAge", "圖片快取期限", "圖片快取有效時間。", ParamFieldKind.String, "快取與模組", "-1", "24h", $"{Docs}/caches/"),
        F("caches.getresource.dir", "遠端資源快取目錄", "resources.GetRemote 等資源快取位置。", ParamFieldKind.String, "快取與模組", ":cacheDir/:project", ":cacheDir/:project", $"{Docs}/caches/"),
        F("caches.getresource.maxAge", "遠端資源快取期限", "遠端資源快取有效時間。", ParamFieldKind.String, "快取與模組", "none", "24h", $"{Docs}/caches/"),
        F("caches.misc.dir", "其他快取目錄", "Hugo 其他檔案快取位置。", ParamFieldKind.String, "快取與模組", ":cacheDir/:project", ":cacheDir/:project", $"{Docs}/caches/"),
        F("caches.misc.maxAge", "其他快取期限", "其他檔案快取有效時間。", ParamFieldKind.String, "快取與模組", "-1", "24h", $"{Docs}/caches/"),
        F("caches.modules.dir", "Module 快取目錄", "下載的 Hugo Modules 快取位置。", ParamFieldKind.String, "快取與模組", ":cacheDir/modules", ":cacheDir/modules", $"{Docs}/caches/"),
        F("caches.modules.maxAge", "Module 快取期限", "Hugo Modules 快取有效時間。", ParamFieldKind.String, "快取與模組", "-1", "24h", $"{Docs}/caches/"),
        F("module.proxy", "Go Module Proxy", "Hugo Modules 使用的 proxy。", ParamFieldKind.String, "快取與模組", "direct", "https://proxy.golang.org", $"{Docs}/modules/"),
        F("module.noProxy", "不使用 Proxy 的模組", "以 glob 指定不經 module proxy 的模組。", ParamFieldKind.String, "快取與模組", "", "github.com/example/*", $"{Docs}/modules/"),
        F("module.private", "私人模組", "以 glob 指定私人 Hugo Modules。", ParamFieldKind.String, "快取與模組", "", "github.com/example/*", $"{Docs}/modules/"),
    ];

    public ObservableCollection<ConfigFieldItem> LoadForm(string tomlText)
    {
        var root = Parse(tomlText);
        var flat = new Dictionary<string, (ParamFieldKind Kind, string Value, bool Bool)>(StringComparer.OrdinalIgnoreCase);
        FlattenTable(root, string.Empty, flat);
        var result = new ObservableCollection<ConfigFieldItem>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in Definitions)
        {
            used.Add(definition.Path);
            var configured = flat.TryGetValue(definition.Path, out var existing);
            result.Add(new ConfigFieldItem
            {
                Path = definition.Path,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                Group = definition.Group,
                Kind = definition.Kind,
                DefaultValue = definition.DefaultValue,
                Example = definition.Example,
                DocumentationUrl = definition.DocumentationUrl,
                IsConfigured = configured,
                StringValue = configured ? existing.Value : definition.DefaultValue,
                BoolValue = configured ? existing.Bool : ParseBool(definition.DefaultValue)
            });
        }

        foreach (var (path, value) in flat.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (used.Contains(path) || path.StartsWith("params.", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(new ConfigFieldItem
            {
                Path = path,
                DisplayName = path,
                Description = "設定檔中既有的自訂或尚未收錄欄位；套用時會保留。",
                Group = "自訂／其他",
                Kind = value.Kind,
                DocumentationUrl = $"{Docs}/",
                IsKnown = false,
                IsConfigured = true,
                StringValue = value.Value,
                BoolValue = value.Bool
            });
        }

        return result;
    }

    public string ApplyToToml(string tomlText, IEnumerable<ConfigFieldItem> fields)
    {
        var root = Parse(tomlText);
        foreach (var field in fields)
        {
            if (field.Path.StartsWith("params.", StringComparison.OrdinalIgnoreCase)) continue;
            if (!field.IsConfigured)
            {
                RemovePath(root, field.Path);
                continue;
            }

            SetPathValue(root, field.Path, field);
        }
        return TomlSerializer.Serialize(root);
    }

    private static TomlTable Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new TomlTable();
        try
        {
            return TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"TOML 格式無效，請先在原始編輯器修正：{ex.Message}", ex);
        }
    }

    private static void FlattenTable(
        TomlTable table,
        string prefix,
        IDictionary<string, (ParamFieldKind Kind, string Value, bool Bool)> flat)
    {
        foreach (var (key, raw) in table)
        {
            var path = string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";
            switch (raw)
            {
                case string value:
                    flat[path] = (ParamFieldKind.String, value, false);
                    break;
                case bool value:
                    flat[path] = (ParamFieldKind.Bool, value ? "true" : "false", value);
                    break;
                case int value:
                    flat[path] = (ParamFieldKind.Number, value.ToString(CultureInfo.InvariantCulture), false);
                    break;
                case long value:
                    flat[path] = (ParamFieldKind.Number, value.ToString(CultureInfo.InvariantCulture), false);
                    break;
                case double value:
                    flat[path] = (ParamFieldKind.Number, value.ToString(CultureInfo.InvariantCulture), false);
                    break;
                case TomlArray value:
                    flat[path] = (ParamFieldKind.Array, string.Join(", ", value.Select(FormatScalar)), false);
                    break;
                case TomlTable nested:
                    FlattenTable(nested, path, flat);
                    break;
            }
        }
    }

    private static void SetPathValue(TomlTable root, string path, ConfigFieldItem field)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;
        var table = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (!table.TryGetValue(parts[index], out var value) || value is not TomlTable nested)
            {
                nested = new TomlTable();
                table[parts[index]] = nested;
            }
            table = nested;
        }

        table[parts[^1]] = field.Kind switch
        {
            ParamFieldKind.Bool => field.BoolValue,
            ParamFieldKind.Number when long.TryParse(field.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) => integer,
            ParamFieldKind.Number when double.TryParse(field.StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            ParamFieldKind.Array => ParseArray(field.StringValue),
            _ => field.StringValue
        };
    }

    private static void RemovePath(TomlTable root, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;
        var tables = new List<(TomlTable Parent, string Key)>();
        var current = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (!current.TryGetValue(parts[index], out var value) || value is not TomlTable nested) return;
            tables.Add((current, parts[index]));
            current = nested;
        }
        current.Remove(parts[^1]);
        for (var index = tables.Count - 1; index >= 0; index--)
        {
            var (parent, key) = tables[index];
            if (parent[key] is TomlTable table && table.Count == 0)
                parent.Remove(key);
            else
                break;
        }
    }

    private static TomlArray ParseArray(string value)
    {
        var result = new TomlArray();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            result.Add(part);
        return result;
    }

    private static string FormatScalar(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private static bool ParseBool(string value) => bool.TryParse(value, out var result) && result;

    private static ConfigFieldDefinition F(
        string path,
        string name,
        string description,
        ParamFieldKind kind,
        string group,
        string defaultValue,
        string example,
        string documentationUrl) =>
        new(path, name, description, kind, group, defaultValue, example, documentationUrl);
}

public sealed record ConfigFieldDefinition(
    string Path,
    string DisplayName,
    string Description,
    ParamFieldKind Kind,
    string Group,
    string DefaultValue,
    string Example,
    string DocumentationUrl);
