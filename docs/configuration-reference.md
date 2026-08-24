# Hugoer 設定檔編輯器參考

> 查證日期：2026-08-21。目標版本為 Hugo Extended 0.164.x 與 Hugo Theme Stack v4。本文只引用 Hugo 官方文件、Stack 官方儲存庫與官方 starter。Hugo 與主題會持續演進，實作時應保留未知欄位，且不要用整份範本覆寫使用者設定。

## 1. 命名空間與相容性

Hugo 設定可放在根目錄的 `hugo.toml`，也可拆分到 `config/_default/*.toml`。拆分檔案以根鍵命名，例如 `params.toml` 內容直接從 `mainSections`、`[sidebar]` 開始，不再包一層 `[params]`；`menus.toml` 也直接從 `[[main]]` 開始。不同環境可用 `config/production` 等目錄覆寫預設值。來源：[Hugo configuration introduction](https://gohugo.io/configuration/introduction/)、[Hugo directory structure](https://gohugo.io/getting-started/directory-structure/)。

編輯器應區分三類資料：

| 類別 | 範例 | 寫入位置 | 驗證來源 |
|---|---|---|---|
| Hugo 核心鍵 | `baseURL`、`pagination.pagerSize`、`markup` | 根設定或對應拆分檔 | Hugo 官方文件 |
| Stack v4 主題鍵 | `params.sidebar.avatar`、`params.article.toc` | 根設定的 `[params]`，或 `config/_default/params.toml` | Stack v4 官方預設值與 starter |
| 使用者自訂鍵 | 任意 `params.*`、模組提供的設定 | 原位置 | 不應刪除或臆測型別 |

### 1.1 現行鍵與舊別名

| 舊鍵 | 狀態 | 現行鍵／處理方式 |
|---|---|---|
| `languageCode` | Hugo 0.158.0 起棄用 | `locale` |
| `languages.<id>.languageCode` | 棄用 | `languages.<id>.locale` |
| `languages.<id>.languageName` | 棄用 | `languages.<id>.label` |
| `languages.<id>.languageDirection` | 棄用 | `languages.<id>.direction` |
| `paginate` | Hugo 0.128 起棄用，之後已移除 | `pagination.pagerSize` |
| `paginatePath` | 舊版分頁鍵；目前應遷移 | `pagination.path` |
| Stack `sidebar.avatar.enabled/local/src` | Stack v3 舊格式，v4 已移除 | `sidebar.avatar = "img/avatar.png"`；空字串代表無 avatar |
| Stack `featuredImageField` | Stack v4 已移除 | 文章 front matter 固定使用 `image` |
| Stack `defaultImage.*` | Stack v4 已移除 | 若需要，必須自行覆寫模板 |
| Stack `opengraph.local/src` | Stack v4 已移除 | 不再由主題提供預設 OG 圖 |
| Stack `imageProcessing.cover.enabled` | Stack v3 舊格式 | `imageProcessing.thumbnail.enabled` |
| front matter `hidden = true` | Stack v3 慣例，v4 不再使用 | Hugo front matter `build.list = "never"` |

Hugo 廢棄資訊來源：[All settings](https://gohugo.io/configuration/all/)、[Languages](https://gohugo.io/configuration/languages/)。Stack 遷移來源：[Stack v4 release notes](https://github.com/CaiJimmy/hugo-theme-stack/releases)、[Stack v4 default params](https://github.com/CaiJimmy/hugo-theme-stack/blob/master/config/_default/params.toml)。

## 2. 編輯器資料模型原則

1. 解析 TOML 後以 AST／保留節點方式修改；保留註解、未知鍵、大小寫與未編輯區段。
2. 同一 TOML table 中不得重複鍵。儲存前執行 `hugo config` 或至少 TOML parse，再執行 `hugo build`。
3. 使用者可以使用 YAML/JSON；若 Hugoer 僅支援 TOML，介面須明確顯示「唯讀／轉換」而非偷偷重寫。
4. 根設定與 `config/_default` 可能同時存在。應先解析 Hugo 合併結果，再把變更寫回原始來源；不能把合併結果平鋪回單一檔案。
5. 安全、模組掛載、自訂 output format 等進階區塊提供原始碼模式與 schema 驗證。不要用表單預設值覆蓋 Hugo 自身預設值。
6. 敏感欄位如 comment provider token、analytics ID、OAuth secret 應遮罩；避免寫入範例真實憑證。

## 3. Hugo 核心：網站基本資料與路徑

完整清單以 [Hugo All settings](https://gohugo.io/configuration/all/) 為準。

| 鍵 | 型別／預設 | 編輯器說明與驗證 |
|---|---|---|
| `baseURL` | string | 發佈站點的絕對 URL，必須含 scheme、host、可選 path，且以 `/` 結尾，例如 `https://example.org/blog/`。GitHub Pages 專案站通常含 repo path。 |
| `title` | string | 全站標題；多語站可在每個 `languages.<id>.title` 覆寫。 |
| `locale` | RFC 5646 language tag | 現行語系鍵，控制翻譯表選擇與日期、數字、貨幣等本地化；例如 `zh-Hant-TW`、`en-US`。不要再產生 `languageCode`。 |
| `timeZone` | string | 解析未帶 offset 日期的時區；接受 `UTC`、`Local` 或 IANA 名稱，如 `Asia/Taipei`。建議下拉選單使用 IANA ID。 |
| `contentDir` | string，預設 `content` | 內容目錄；亦可由 module mounts 提供更彈性的映射。相對於專案根目錄。 |
| `publishDir` | string，預設 `public` | 建置輸出目錄。修改前警示：清理或部署會影響此目錄。不得允許空值、根目錄或工作區外危險路徑。 |
| `theme` | string 或 string[] | 傳統 theme 目錄模式；多主題時由左到右優先。Stack v4 官方 starter 改用 Hugo Module import。 |
| `hasCJKLanguage` | bool，預設 false | 啟用 CJK 自動偵測，影響字數、閱讀時間與自動摘要。可保留於基本／語言設定。 |
| `copyright` | string | 版權文字；是否顯示取決於主題模板。 |
| `summaryLength` | int，預設 70 | 自動摘要的最少字數，於接近的段落邊界截斷。 |

推薦 TOML：

```toml
baseURL = "https://example.org/"
title = "我的網站"
locale = "zh-Hant-TW"
timeZone = "Asia/Taipei"
contentDir = "content"
publishDir = "public"
defaultContentLanguage = "zh-hant-tw"
hasCJKLanguage = true
```

注意：`locale` 是語言標籤；`defaultContentLanguage` 是 `languages` table 的鍵，兩者用途不同，不應強制相同字串。

## 4. 分頁 `pagination`

官方預設及語言覆寫方式見 [Configure pagination](https://gohugo.io/configuration/pagination/)。

| 鍵 | 型別／預設 | 用途 |
|---|---|---|
| `pagination.pagerSize` | int，10 | 每頁元素數，必須大於 0。Stack starter 使用 5。 |
| `pagination.path` | string，`page` | 分頁 URL 的路徑片段，例如 `/page/2/`。只填片段，不要放斜線模板。 |
| `pagination.disableAliases` | bool，false | 是否停用第一頁 alias。 |

```toml
[pagination]
pagerSize = 5
path = "page"
disableAliases = false
```

遷移時若讀到 `paginate = 5`，先顯示預覽，改寫為 `[pagination].pagerSize = 5`，並移除舊鍵，避免兩套設定並存。

## 5. Taxonomies 與 Permalinks

來源：[Taxonomies](https://gohugo.io/configuration/taxonomies/)、[Permalinks](https://gohugo.io/configuration/permalinks/)。

### 5.1 `taxonomies`

預設為單數鍵對複數 URL：`category = "categories"`、`tag = "tags"`。使用者一旦定義 `taxonomies`，應視為有意取代預設集合，不要自動補回已刪除項目。編輯器可提供「內容欄位名」「網址集合名」兩欄，驗證兩者非空且不重複。

```toml
[taxonomies]
category = "categories"
tag = "tags"
series = "series"
```

若不需要 taxonomy，可設定 `disableKinds = ["taxonomy", "term"]`；這是另一個 Hugo 根鍵，不等同刪除 mapping。

### 5.2 `permalinks`

可依 page kind 或 content section 設定 URL pattern。Stack starter：

```toml
[permalinks]
post = "/p/:slug/"
page = "/:slug/"
```

目前官方 map 形式可依 kind 與 section 分層，例如：

```toml
[permalinks.page]
post = "/p/:slug/"

[permalinks.section]
post = "/posts/"
```

Hugo 0.161 起另有規則陣列形式，每項含 `pattern` 與可選 `target`，由第一個符合 environment、kind、path、sites 等條件的規則勝出。`target.lang` 在 0.153 起棄用，改用 `target.sites`。

常用 token 包括日期 token（`:year`、`:month`、`:day`）、內容 token（`:slug`、`:title`、`:section`、`:sections`、`:contentbasename`、`:slugorcontentbasename`）。`:filename` 與 `:slugorfilename` 在 0.144 起棄用，分別改成 `:contentbasename` 與 `:slugorcontentbasename`。編輯器應顯示即時 URL 範例，保留 Hugo 文件允許的未知 token，並提醒 permalink 變更可能破壞既有連結。Stack starter 的簡寫 `[permalinks] post = ...` 應可讀取保留，但新 UI 優先使用官方 kind-aware 模型。

## 6. 輸出：`outputs`、`outputFormats`、`mediaTypes`

來源：[Outputs](https://gohugo.io/configuration/outputs/)、[Output formats](https://gohugo.io/configuration/output-formats/)、[Media types](https://gohugo.io/configuration/media-types/)。

### 6.1 `outputs`

指定各 page kind 要產生哪些格式。格式名稱不分大小寫，但介面建議顯示官方慣用大寫。

```toml
[outputs]
home = ["HTML", "RSS", "JSON"]
page = ["HTML"]
section = ["HTML", "RSS"]
taxonomy = ["HTML", "RSS"]
term = ["HTML", "RSS"]
```

JSON 搜尋通常同時需要：`home` 包含 `JSON`、存在相符的 output format，以及主題提供對應 layout。只勾選 JSON 不代表模板必然存在。

### 6.2 `outputFormats`

Hugo 有內建 HTML、RSS、JSON 等格式；同名自訂 table 可覆寫內建設定。重要欄位：`mediaType`、`baseName`、`path`、`rel`、`protocol`、`isHTML`、`isPlainText`、`noUgly`、`notAlternative`、`permalinkable`、`root`、`ugly`、`weight`。

```toml
[outputFormats.SearchIndex]
mediaType = "application/json"
baseName = "search"
isPlainText = true
notAlternative = true
```

表單應將「新增格式」視為進階操作：名稱不可與既有格式無意衝突；若覆寫內建格式要二次確認。

### 6.3 `mediaTypes`

自訂 MIME type 及副檔名，常見欄位為 `suffixes`、`delimiter`。table key 是 MIME type，包含 `/`，TOML 中應加引號。

```toml
[mediaTypes."application/manifest+json"]
suffixes = ["webmanifest"]
```

編輯器應驗證 MIME 型式、suffix 不含前導點，並確認 `outputFormats.*.mediaType` 有對應內建或自訂 media type。

## 7. `minify`

來源：[Configure minify](https://gohugo.io/configuration/minify/)。

`minify.minifyOutput` 是總開關，預設 false。`disableCSS`、`disableHTML`、`disableJS`、`disableJSON`、`disableSVG`、`disableXML` 可停用個別類型；細部設定位於 `minify.tdewolff.css/html/js/json/svg/xml`。`minify.tdewolff.html.keepConditionalComments` 已棄用，改用 `keepSpecialComments`；`css.inline` 是內部欄位，外部設定無效。建議 UI 提供「生產環境啟用」和各格式排除，細部選項放進階展開區。

```toml
[minify]
minifyOutput = true
disableXML = true
```

不要假設 minify 永遠安全：自訂模板、inline JS/CSS 或需要保留 whitespace 的輸出可能受影響，儲存後應跑 production build。

## 8. `build`

來源：[Configure build](https://gohugo.io/configuration/build/)。此處是全站資產建置設定，不是 front matter 的 page `build` options。

| 鍵 | 預設 | 用途 |
|---|---|---|
| `build.noJSConfigInAssets` | false | 使用 `js.Build` 時，是否禁止在 assets 產生協助編輯器導航的 `jsconfig.json`。 |
| `build.useResourceCacheWhen` | `fallback` | Sass 等轉譯何時使用資源檔案快取：`never`、`fallback`、`always`。 |
| `build.buildStats.enable` | false | 產生 `hugo_stats.json`，供 CSS pruning/Tailwind 使用。 |
| `build.buildStats.disableClasses/disableIDs/disableTags` | false | 從 build stats 排除對應 HTML 實體。 |
| `build.cacheBusters[]` | 規則陣列 | 每項含 regex `source`、`target`，決定來源變更時要失效哪些資源快取。 |

```toml
[build]
useResourceCacheWhen = "fallback"

[build.buildStats]
enable = false

[[build.cacheBusters]]
source = "(postcss|tailwind)\\.config\\.(js|mjs|cjs)"
target = "(css|styles|scss|sass)"
```

regex 欄位應提供測試與錯誤提示；錯誤 cache buster 可能讓開發伺服器無法正確重建。

## 9. `security`

來源：[Configure security](https://gohugo.io/configuration/security/)。這是高風險區塊，預設收合且需顯示警告。

Hugo 以 allowlist 限制外部程式、環境變數、遠端 HTTP 與 Node 權限。核心欄位：

- `allowContent`：允許的 content MIME regex；Hugo 0.162 起預設拒絕 `text/html` content，以避免原樣輸出任意 JavaScript。
- `enableInlineShortcodes`：預設 false。
- `exec.allow`、`exec.osEnv`：允許執行檔及其可存取環境變數的 regex。
- `funcs.getenv`：模板 `os.Getenv` 可讀的環境變數 regex，預設只允許 `HUGO_` 與 `CI` 類型。
- `http.methods`、`http.urls`、`http.mediaTypes`：`resources.GetRemote` 的限制。
- `node.permissions.disable`、`allowAddons`、`allowChildProcess`、`allowRead`、`allowWorker`、`allowWrite`：Node permission model。

否定規則以 `! ` 開頭，優先於 allow；全由否定規則組成時，未被拒絕者隱含允許；空清單拒絕全部；字串 `none` 完全停用該能力。編輯器不能把 regex 當一般 glob，也不能提供「允許全部」的一鍵預設。

## 10. `privacy` 與 `services`

來源：[Privacy](https://gohugo.io/configuration/privacy/)、[Services](https://gohugo.io/configuration/services/)。這些設定主要影響 Hugo 內嵌模板，第三方主題／模組可能不遵循。

### 10.1 Privacy

服務群組包括 `disqus`、`googleAnalytics`、`instagram`、`vimeo`、`x`、`youTube`。常見欄位：

- 各服務 `disable`：停用其內嵌模板。
- `googleAnalytics.respectDoNotTrack`：官方預設 true。
- Instagram 的 `simple`；Vimeo/X 的 `enableDNT`、`simple`；YouTube 的 `privacyEnhanced`。

設定只能協助合規，不能宣稱自動符合 GDPR/CCPA。Stack 自己的 comments provider 和 cookie consent 是 theme-specific，與 Hugo `privacy` 不同層。

### 10.2 Services

Hugo service 設定包含：

- `services.disqus.shortname`
- `services.googleAnalytics.id`
- `services.x.disableInlineCSS`
- `services.rss.limit`（`-1` 表示無限制）

舊式根鍵 `disqusShortname`、`googleAnalytics` 可能仍出現在舊主題範例；新編輯器應優先產生 `services.*` 結構，同時在遷移前檢查主題是否仍直接讀取舊根鍵。Stack starter 目前仍有 `disqusShortname`，因此 Hugoer 不應未經驗證就刪掉它。

## 11. `markup`

來源：[Configure markup](https://gohugo.io/configuration/markup/)、[Stack starter markup](https://github.com/CaiJimmy/hugo-theme-stack-starter/blob/master/config/_default/markup.toml)。

主要分三組；另有 `defaultMarkdownHandler`，推薦 `goldmark`，選擇 AsciiDoc、Org、Pandoc 或 reStructuredText 等外部 handler 時也必須滿足程式安裝與 security allowlist：

1. `goldmark`：Markdown parser/renderer。常用 `renderer.unsafe`（允許 Markdown 內 raw HTML）、`renderer.hardWraps`、`renderer.xhtml`；extensions 包括 CJK、definition list、footnote、linkify、passthrough、strikethrough、table、task list、typographer；parser 可設定 attribute、自動 heading ID、standalone image wrapping；render hooks 可設定 `image/link.useEmbedded`。舊 `renderHooks.*.enableDefault` 在 0.148 起棄用，改用 `useEmbedded`。
2. `highlight`：Chroma 語法高亮，例如 `noClasses`、`style`、`codeFences`、`guessSyntax`、`lineNos`、`lineNumbersInTable`、`tabWidth`。
3. `tableOfContents`：`startLevel`、`endLevel`、`ordered`。

Stack starter v4 開啟 Goldmark passthrough 支援數學：

```toml
[markup.goldmark.renderer]
unsafe = true

[markup.goldmark.extensions.passthrough]
enable = true

[markup.goldmark.extensions.passthrough.delimiters]
block = [["\\[", "\\]"], ["$$", "$$"]]
inline = [["\\(", "\\)"]]

[markup.tableOfContents]
startLevel = 2
endLevel = 4
ordered = true

[markup.highlight]
noClasses = false
codeFences = true
guessSyntax = true
lineNos = true
lineNumbersInTable = true
tabWidth = 4
```

`unsafe = true` 是內容信任決策，與 `security.allowContent` 不是同一控制項。Hugoer 會在建立網站、安裝 Stack、本機預覽與建置時為其管理的網站開啟此項，因為文章編輯器把縮放／排版後的圖片、音訊與影片存成 HTML；關閉時 Hugo Goldmark 會輸出 `<!-- raw HTML omitted -->`。設定仍顯示在「設定檔」。

## 12. `imaging`

來源：[Configure imaging](https://gohugo.io/configuration/imaging/)。

全域影像處理常用欄位：`resampleFilter`、`anchor`、`bgColor`；現行格式專屬設定包括 `avif.compression/encoderSpeed/hint/quality`、`jpeg.quality`、`webp.compression/hint/method/quality/useSharpYuv`。根層 `imaging.compression`、`imaging.hint`、`imaging.quality` 在 0.163 起棄用，應遷移至格式專屬 table。EXIF 位於 `imaging.exif`，可控制 `disableDate`、`disableLatLong`、`excludeFields`、`includeFields`。0.155 起另有 `imaging.meta.fields` 與 `imaging.meta.sources`（EXIF/IPTC/XMP）。色彩處理還可定義 `imaging.colors`。

編輯器應：

- quality 限制 1–100；anchor、resample filter、hint 使用官方允許值下拉。
- 明確提醒 EXIF 可能包含拍攝位置；發佈前提供隱私檢查。
- 將 Hugo `imaging` 與 Stack `params.imageProcessing` 分開顯示：前者是引擎設定，後者是主題何時／以何種寬度呼叫處理。

## 13. `caches`

來源：[Configure file caches](https://gohugo.io/configuration/caches/)。

Hugo 的現行標準檔案快取包含 `assets`、`getresource`、`images`、`misc`、`modulegitinfo`、`modulequeries`、`modules`。每區主要設定 `dir` 與 `maxAge`；`dir` 可使用 `:cacheDir`、`:resourceDir`、`:project` token，`maxAge` 使用 duration，`0` 停用快取，`-1` 表示永不過期。

編輯器可顯示實際解析後路徑，但儲存原 token。修改 cache dir 時要做路徑安全檢查；「清除快取」是獨立動作，不等同修改設定。

## 14. `module`

來源：[Configure modules](https://gohugo.io/configuration/module/)、[Stack starter module](https://github.com/CaiJimmy/hugo-theme-stack-starter/blob/master/config/_default/module.toml)。

Stack v4 官方 import：

```toml
[[module.imports]]
path = "github.com/CaiJimmy/hugo-theme-stack/v4"
disable = false
```

重要結構：

- `module.imports[]`：`path`、`disable`、`ignoreConfig`、`ignoreImports`、`noMounts`、`noVendor`、`usePackageJSON`（`auto|always|never`）、`version`。
- `module.mounts[]`：現行欄位為 `source`、`target`、`disableWatch`、`files`、`sites`；0.153 起 `includeFiles`／`excludeFiles` 改為 `files`，`lang` 改為 `sites`。自訂某 component 的 mount 會取代該 component 預設 mount。
- `module.hugoVersion`：現行相容範圍為 `min`、`max`；`extended` 在 0.153 起棄用，且 0.153.2 後不再檢查。
- `module.replacements`、`module.vendorClosest`、`module.workspace` 等進階模組解析設定。

Hugoer 應檢查 Go 是否可用、執行 `hugo mod graph`／`hugo mod tidy` 前顯示影響。Stack v4 module path 必須含 `/v4`，且官方 release notes 指出需 Hugo 0.157.0 以上。

## 15. `languages`

來源：[Configure languages](https://gohugo.io/configuration/languages/)。

根層：`defaultContentLanguage`、`defaultContentLanguageInSubdir`、`disableDefaultSiteRedirect`；舊 `disableDefaultLanguageRedirect` 已由更通用的 `disableDefaultSiteRedirect` 取代。每個 language 可定義：

| 鍵 | 說明 |
|---|---|
| `label` | UI 顯示名稱；取代 `languageName`。 |
| `locale` | RFC 5646 語言標籤；取代 `languageCode`。 |
| `direction` | `ltr` 或 `rtl`；取代 `languageDirection`。 |
| `title` | 該語言站點標題。 |
| `weight` | 越小排序越前；相同時按鍵名排序。 |
| `contentDir` | 該語言內容目錄。 |
| `disabled` | 是否在建置時停用。 |
| `pagination`、`menus`、`params` 等 | 可本地化設定。未定義時回退全域值。 |

```toml
defaultContentLanguage = "zh-hant-tw"

[languages.zh-hant-tw]
label = "繁體中文"
locale = "zh-Hant-TW"
direction = "ltr"
title = "我的網站"
weight = 1
contentDir = "content/zh-hant-tw"

[languages.en]
label = "English"
locale = "en-US"
direction = "ltr"
title = "My Site"
weight = 2
contentDir = "content/en"
```

Stack v4 release notes 對其內建 i18n key 有額外遷移：`zh-cn` → `zh`、`zh-hk` → `zh-hant-hk`、`zh-tw` → `zh-hant-tw`。這是 Stack 的相容性需求，不是說所有 Hugo 專案的 language map key 都必須如此。

## 16. `menus`

來源：[Configure menus](https://gohugo.io/configuration/menus/)、[Stack starter menu](https://github.com/CaiJimmy/hugo-theme-stack-starter/blob/master/config/_default/menu.toml)。

每個 menu 是 entry 陣列；常用欄位：`identifier`、`name`、`pageRef`、`url`、`parent`、`weight`、`pre`、`post`、`params`。內部內容優先使用 `pageRef`，外部連結才使用 `url`。`parent` 應指向同 menu 內另一 entry 的 identifier。

Stack 提供 `main` 與 `social`，其 `params.icon` 對應主題 assets 中的 icon；`params.newtab` 可讓項目在新分頁開啟。

```toml
[[menus.social]]
identifier = "github"
name = "GitHub"
url = "https://github.com/example"
weight = 10

[menus.social.params]
icon = "brand-github"
newtab = true
```

若使用拆分的 `config/_default/menu.toml`，官方 Stack starter 直接寫 `[[social]]`，不包 `[menus]`。編輯器需要辨識檔案語境。

## 17. `params`

來源：[Configure params](https://gohugo.io/configuration/params/)。`params` 是站點／主題／模組的自訂參數容器，Hugo 不替任意鍵定義 schema。可於 `languages.<id>.params` 覆寫本地化值。Hugoer 應提供：

- 通用樹狀編輯器，支援 string、int、float、bool、datetime、array、table、array-of-tables。參數名建議 camelCase 或 snake_case；kebab-case 雖可儲存，但 Hugo template 不能以一般 chained identifier 直接存取。
- 根 `hugo.toml` 中顯示 `[params]`；獨立 `params.toml` 中省略外層。
- 安裝已知主題時套用對應 schema，但仍保留 schema 外鍵。
- 區分 `params` 與 front matter params，前者是站點設定，後者是單頁 metadata。

## 18. Stack v4 主題參數

本節以 [Stack v4 default params](https://github.com/CaiJimmy/hugo-theme-stack/blob/master/config/_default/params.toml) 與 [官方 starter params](https://github.com/CaiJimmy/hugo-theme-stack-starter/blob/master/config/_default/params.toml) 為準。下列全部是 **theme-specific**，不是 Hugo 核心鍵。

### 18.1 全域與 footer/dateFormat

| 鍵 | 預設／範例 | 用途 |
|---|---|---|
| `mainSections` | `["post"]` | 首頁與 archive 主要列出的 section。應提供多選並比對實際 content sections。 |
| `rssFullContent` | true | RSS 是否輸出全文。 |
| `favicon` | starter `img/favicon.png` | v4 從 `assets` 解析；不再假設 `static`。 |
| `SortBy` | `default` 或 `lastmod` | 列表排序；注意官方預設鍵目前是大寫 `S`，保留原大小寫。 |
| `footer.since` | 年份 int | footer 起始年份。 |
| `footer.customText` | string | 自訂 footer 文字；是否允許 HTML應依主題模板實際 escape 行為。 |
| `dateFormat.published` | `:date_full` | 發佈日期格式，可用 Hugo 本地化 layout token。 |
| `dateFormat.lastUpdated` | `:date_full` | 更新日期格式。 |

### 18.2 Sidebar

| 鍵 | 型別 | 說明 |
|---|---|---|
| `sidebar.compact` | bool | 緊湊側欄。主題預設 false。 |
| `sidebar.emoji` | string | starter 示範 `🍥`。 |
| `sidebar.subtitle` | string | 側欄副標題。 |
| `sidebar.avatar` | string | v4 是單一路徑，例如 `img/avatar.png`；空字串停用。資產應放在 `assets`。 |

不得再輸出：

```toml
# Stack v3 舊格式，v4 不可用
[params.sidebar.avatar]
enabled = true
local = true
src = "img/avatar.png"
```

### 18.3 Article

| 鍵 | 預設 | 說明 |
|---|---|---|
| `article.headingAnchor` | false | 標題 anchor。 |
| `article.math` | false | 數學功能；仍需 markup passthrough 等相容設定。 |
| `article.toc` | true | 文章 TOC。starter 未明列時由 theme default 繼承。 |
| `article.readingTime` | true | 顯示閱讀時間。 |
| `article.list.showTags` | false | 列表頁顯示文章 tags。 |
| `article.license.enabled` | false（starter true） | 顯示授權資訊。 |
| `article.license.default` | CC BY-NC-SA 4.0 文案 | 預設授權字串。 |

v4 default 另含 `article.mermaid`（`look`、light/dark theme、theme variables、`securityLevel`、`htmlLabels`、`transparentBackground`）及 `article.alertIcon` 五種 alert icon。這些屬進階區；`mermaid.securityLevel = "loose"` 會允許 HTML labels，應提示風險。

### 18.4 Color scheme

```toml
[params.colorScheme]
toggle = true
default = "auto" # auto | light | dark
```

Stack v4 要求 `colorScheme` 是 table；把它寫成字串會造成 theme template 取值失敗。UI 用 enum 並另設 toggle。

### 18.5 Widgets

`widgets.homepage`、`widgets.page` 是 widget 物件陣列。官方 starter：

```toml
[params.widgets]
homepage = [
  { type = "search" },
  { type = "archives", params = { limit = 5 } },
  { type = "categories", params = { limit = 10 } },
  { type = "tag-cloud", params = { limit = 10 } },
]
page = [{ type = "toc" }]
```

編輯器應支援拖曳排序、每種 widget 的 `params.limit`，並保留未知 widget。`search` widget 需要相容的 JSON output/template；啟用時應做建置檢查。

### 18.6 Comments

總開關：`comments.enabled`；provider 名稱由 `comments.provider` 選擇。v4 官方 default 目前包含：`artalk`、`disqusjs`、`utterances`、`beaudar`、`remark42`、`vssue`、`waline`、`twikoo`、`cactus`、`giscus`、`gitalk`、`cusdis`、`comentario`。

實作原則：

- provider 下只顯示相符設定，但不能刪除其他 provider 的既有設定。
- repo、repoID、categoryID、client ID/secret、API key 等分別驗證；secret 遮罩並警告公開靜態站的設定最終可能可見。
- Giscus 欄位包括 `repo`、`repoID`、`category`、`categoryID`、`mapping`、`lightTheme`、`darkTheme`、`reactionsEnabled`、`emitMetadata`、`inputPosition`、`lang`、`strict`、`loading`。
- Waline 具有陣列 `emoji`、`requiredMeta` 與巢狀 `locale`。
- Comentario 是 v4 新支援之一；欄位應從官方 default 動態建立，避免硬編碼很快過期。

### 18.7 Open Graph

目前 theme default 只定義：

```toml
[params.opengraph.twitter]
site = ""
card = "summary_large_image"
```

`site` 是 Twitter/X 帳號，`card` 常用 `summary` 或 `summary_large_image`。v4 已移除 `opengraph.local`、`opengraph.src` 及舊 `defaultImage` fallback；表單不可再顯示成有效 v4 欄位。

### 18.8 Image processing

```toml
[params.imageProcessing]
autoOrient = false

[params.imageProcessing.external]
timeout = "5s"

[params.imageProcessing.content]
enabled = true
widths = [800, 1600, 2400]

[params.imageProcessing.thumbnail]
enabled = true
```

`widths` 應為遞增正整數、去重；`timeout` 驗證 Go duration。舊 `cover.enabled` 必須遷移為 `thumbnail.enabled`。此區與 Hugo 核心 `[imaging]` 分開。

### 18.9 Cookies

v4 default 包含 `cookies.enabled`、`cookies.showSettings`，及 `cookies.categories.analytics`、`functional`。這是 Stack 的 GDPR cookie consent UI，不等於 Hugo `[privacy]`；啟用分析服務時應聯動提醒使用者檢查 consent 與隱私政策。

## 19. Stack v4 完整基線範例

以下是適合 Hugoer 產生新 Stack v4 站點的「最小且清楚」基線；未列出的 theme defaults 交由主題提供，不複製整份 190 行預設檔。

```toml
baseURL = "https://example.org/"
title = "我的網站"
locale = "zh-Hant-TW"
defaultContentLanguage = "zh-hant-tw"
hasCJKLanguage = true
timeZone = "Asia/Taipei"

[pagination]
pagerSize = 5

[permalinks]
post = "/p/:slug/"
page = "/:slug/"

[params]
mainSections = ["post"]
rssFullContent = true
favicon = "img/favicon.png"

[params.footer]
since = 2026
customText = ""

[params.dateFormat]
published = ":date_full"
lastUpdated = ":date_full"

[params.sidebar]
emoji = "🍥"
subtitle = ""
avatar = "img/avatar.png"

[params.article]
headingAnchor = false
math = false
toc = true
readingTime = true

[params.colorScheme]
toggle = true
default = "auto"

[params.widgets]
homepage = [
  { type = "search" },
  { type = "archives", params = { limit = 5 } },
  { type = "categories", params = { limit = 10 } },
  { type = "tag-cloud", params = { limit = 10 } },
]
page = [{ type = "toc" }]

[params.comments]
enabled = false
provider = "giscus"

[params.opengraph.twitter]
site = ""
card = "summary_large_image"

[[module.imports]]
path = "github.com/CaiJimmy/hugo-theme-stack/v4"
```

若存成 `config/_default/params.toml`，必須移除上述所有 `params.` 前綴；若存成 `module.toml`，則 `[[module.imports]]` 改為 `[[imports]]`。

## 20. Hugoer UI 分層建議

| 分頁 | 基礎欄位 | 進階欄位 |
|---|---|---|
| 網站 | baseURL、title、locale、timeZone | contentDir、publishDir、CJK、summary |
| 語言 | default language、語言清單 | 語言層 pagination/menus/params、redirect |
| 內容與 URL | taxonomies、permalinks、pagination | outputs、output formats、media types |
| Markdown | raw HTML、TOC、highlight | Goldmark extensions、passthrough delimiters |
| 圖片 | quality、anchor、EXIF privacy | filters/colors；Stack responsive widths |
| 主題 Stack | sidebar、article、colors、widgets、footer | comments、Mermaid、cookies、Open Graph |
| 建置 | publishDir、minify | build stats、cache busters、caches |
| 模組 | Stack v4 安裝／版本 | imports、mounts、replacements |
| 安全與隱私 | privacy toggles | security regex allowlists、services |
| 原始設定 | syntax-highlighted TOML | unknown keys、diff、來源檔定位 |

儲存流程：

1. 顯示結構化 diff，特別標出 deprecated migration 與刪除。
2. 建立同目錄可復原備份。
3. 原子寫入暫存檔，再取代目標檔。
4. 執行 `hugo config` 驗證合併設定。
5. 執行 `hugo build --panicOnWarning` 可作嚴格驗證選項；一般模式至少執行 production build。
6. 失敗時保留原檔、顯示檔名／行列／Hugo 原始訊息，不能只顯示「建置失敗」。

## 21. 一手來源索引

### Hugo 官方

- [Configuration overview](https://gohugo.io/configuration/)
- [All settings](https://gohugo.io/configuration/all/)
- [Introduction and configuration merge rules](https://gohugo.io/configuration/introduction/)
- [Pagination](https://gohugo.io/configuration/pagination/)
- [Taxonomies](https://gohugo.io/configuration/taxonomies/)
- [Permalinks](https://gohugo.io/configuration/permalinks/)
- [Outputs](https://gohugo.io/configuration/outputs/)
- [Output formats](https://gohugo.io/configuration/output-formats/)
- [Media types](https://gohugo.io/configuration/media-types/)
- [Minify](https://gohugo.io/configuration/minify/)
- [Build](https://gohugo.io/configuration/build/)
- [Security](https://gohugo.io/configuration/security/)
- [Privacy](https://gohugo.io/configuration/privacy/)
- [Services](https://gohugo.io/configuration/services/)
- [Markup](https://gohugo.io/configuration/markup/)
- [Imaging](https://gohugo.io/configuration/imaging/)
- [Caches](https://gohugo.io/configuration/caches/)
- [Modules](https://gohugo.io/configuration/module/)
- [Languages](https://gohugo.io/configuration/languages/)
- [Menus](https://gohugo.io/configuration/menus/)
- [Params](https://gohugo.io/configuration/params/)

### Stack 官方

- [Stack repository](https://github.com/CaiJimmy/hugo-theme-stack)
- [Stack v4 default params](https://github.com/CaiJimmy/hugo-theme-stack/blob/master/config/_default/params.toml)
- [Stack release notes and v4 migration notes](https://github.com/CaiJimmy/hugo-theme-stack/releases)
- [Official Stack starter](https://github.com/CaiJimmy/hugo-theme-stack-starter)
- [Starter config](https://github.com/CaiJimmy/hugo-theme-stack-starter/blob/master/config/_default/config.toml)
- [Starter params](https://github.com/CaiJimmy/hugo-theme-stack-starter/blob/master/config/_default/params.toml)
- [Starter markup](https://github.com/CaiJimmy/hugo-theme-stack-starter/blob/master/config/_default/markup.toml)
- [Starter menus](https://github.com/CaiJimmy/hugo-theme-stack-starter/blob/master/config/_default/menu.toml)
- [Starter module import](https://github.com/CaiJimmy/hugo-theme-stack-starter/blob/master/config/_default/module.toml)
- [Starter permalinks](https://github.com/CaiJimmy/hugo-theme-stack-starter/blob/master/config/_default/permalinks.toml)
