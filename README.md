# Hugoer

以 **Avalonia** 打造的 Hugo 桌面工作台：一鍵建立環境、圖形化設定、安裝主題（含 Stack）、Markdown 即時預覽，並推送到 GitHub、GitLab、Codeberg 或 Bitbucket。

> Mobile companions 位於 [`mobile/`](mobile/)：Android Jetpack Compose 與 iOS SwiftUI
> 以 repository 為資料邊界，提供行動端瀏覽／編輯；本機 Hugo 預覽、批次遷移與完整 Pages
> 建置仍由桌面版處理。架構與 APK 發布契約請參閱
> [`docs/mobile-architecture.md`](docs/mobile-architecture.md)。

## 功能

| 分頁 | 能力 |
|------|------|
| **環境** | 偵測／一鍵安裝 Hugo Extended、建立新站、開啟既有站、本機預覽、`hugo build`（離線備援） |
| **設定檔** | 網站基本欄位、**圖形化 params 表單**（Hugo / Stack 常用參數）、原始 TOML |
| **主題** | 一鍵安裝 **Stack** 及其他熱門主題、切換 theme、編輯主題設定 |
| **文章** | 只管理 `content/post` 等部落格文章、新增文章、**Markdown 即時預覽**、匯出 **Hexo／Jekyll 相容** Markdown |
| **遷移** | **Hexo／Jekyll → Hugo**、**Hugo → Hexo／Jekyll** 網站遷移（文章、頁面、靜態檔與基本設定） |
| **選單** | 與文章分開：圖形化編輯 `menu.main` / `menu.social`、網站頁面（關於／歸檔／搜尋） |
| **Git 部署** | 預設直接推送 GitHub Pages／遠端 Pages 工作流程；遠端失敗時可自動或手動本機備援；GitHub、GitLab、Codeberg、Bitbucket 分別保存設定；每 5 分鐘監控線上部署版本 |

## 系統需求

- Windows 10/11（亦可在 macOS / Linux 建置）
- 開發：[.NET 10 SDK](https://dotnet.microsoft.com/download)
- 建議： [Git](https://git-scm.com/)；GitHub 可選 [GitHub CLI (`gh`)](https://cli.github.com/)，也可使用 Git Credential Manager／SSH

Hugo 可在應用程式內一鍵安裝。

## 開發執行

```powershell
dotnet restore
dotnet run
```

## 打包：單一 EXE 與安裝程式

一鍵發布腳本（建議）：

```powershell
.\scripts\publish.ps1
# 或指定版本
.\scripts\publish.ps1 -Version 1.7.0 -Runtime win-x64
# 只產生免安裝單檔 EXE、ZIP 與校驗資訊
.\scripts\publish.ps1 -Version 1.7.0 -SkipInstaller
```

產出位置：

| 路徑 | 說明 |
|------|------|
| `dist\single\Hugoer.exe` | **單一可攜 EXE**（self-contained，免安裝 .NET） |
| `dist\publish\win-x64\` | publish 輸出目錄 |
| `dist\releases\Hugoer-<版本>-win-x64-portable.zip` | 可攜式 Windows x64 壓縮包 |
| `dist\releases\SHA256SUMS.txt` | 所有 release 檔案的 SHA-256 校驗碼 |
| `dist\releases\release-manifest.json` | 版本、runtime、檔案大小與雜湊資訊 |
| `dist\releases\velopack\` | Velopack **Setup.exe** 安裝程式（需 `vpk`；可用 `-InstallTools` 安裝） |
| `dist\releases\inno\` | Inno Setup 安裝程式（若已安裝 Inno Setup 6） |

手動 publish：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o .\dist\publish\win-x64
```

Velopack CLI（可選，產生 Setup.exe）：

```powershell
dotnet tool install -g vpk
vpk pack --packId Hugoer --packVersion 1.7.0 --packDir .\dist\publish\win-x64 --mainExe Hugoer.exe --outputDir .\dist\releases\velopack
```

## 建議使用流程

1. **環境** → 一鍵安裝 Hugo Extended  
2. **環境** → 建立新網站（或開啟既有資料夾）  
3. **主題** → 一鍵安裝 Stack  
4. **設定檔** →「網站基本」改 baseURL / title；「Params 表單」調描述、色系、widgets 等  
5. **文章** → 新增／編輯部落格文章，右側即時預覽；可把單篇或全部文章匯出成 Hexo／Jekyll 相容的 `_posts` Markdown  
   **選單** → 編輯 Home／Archives／Search 等導覽，與文章分開  
6. **遷移** → 從 Hexo 或 Jekyll 匯入成 Hugo，或把目前 Hugo 網站輸出成 Hexo／Jekyll  
7. **Git 部署** → 從下拉選單選擇 GitHub／GitLab／Codeberg／Bitbucket；各平台設定分開保存，再連結或推送 repository。主要路徑預設為「GitHub Pages（遠端優先）」。
8. 遠端模式會直接讀取／合併並提交 repository，由 GitHub Actions／平台工作流程建置 Pages，不要求本機 Hugo；GitHub 可使用 `gh` 或 Git Credential Manager／SSH；遠端失敗時可自動或手動執行「本機部署備援」產生 `public/`。
9. **Git 部署** → 查看「線上版本監控」；Hugoer 每 5 分鐘確認 Pages／靜態網站是否已更新至本次推送版本

## GitHub Pages 工作流程與權限

第一次使用 GitHub Pages 時，Hugoer 會在網站 repository 寫入
`.github/workflows/hugo.yml`。範本依照 [Hugo 官方 GitHub Pages 指南](https://gohugo.io/host-and-deploy/host-on-github-pages/)產生，包含：

- `push`（`main`／`master`）及 `workflow_dispatch` 觸發器。
- `contents: read`、`pages: write`、`id-token: write` 最小部署權限與不可取消的 `pages` concurrency。
- recursive submodule、完整 Git 歷史、條件式 Go／Node.js、Dart Sass 與 Hugo Extended 工具安裝。
- Hugo `--gc --minify --baseURL "${{ steps.pages.outputs.base_url }}/"` production build、cache restore/save，以及 Pages artifact 上傳與部署。

GitHub REST Pages API 的自動設定需要 repository 的 `admin`、`maintain` 或 Pages 管理權限；只有推送權限的協作者仍可正常提交網站，但必須由 repository 擁有者在 **Settings → Pages → Build and deployment → Source** 選擇 **GitHub Actions**。若 API 回傳 404，這通常代表 Pages 尚未啟用或目前帳號沒有管理權限，不代表網站檔案沒有推送成功。

GitHub Pages 的 `source[branch]` 會以 repository 預設分支（若 API 回應沒有提供才退回目前分支），`source[path]` 使用根目錄 `/` 傳給 API；個人網站與專案網站的 `baseURL` 則由 `configure-pages` 在 Actions 執行時提供，避免子路徑網站的 CSS、圖片及連結失效。詳細 API 欄位請參閱 [GitHub Pages REST API](https://docs.github.com/en/rest/pages/pages?apiVersion=2022-11-28)。

## 專案結構

```
Hugoer/
  Controls/     Markdown 即時預覽控制項
  Services/     Hugo、主題、內容、GitHub、Markdown、TOML params
  ViewModels/   MVVM
  Views/        Avalonia XAML
  scripts/      publish.ps1
  installer/    Inno Setup 腳本
  mobile/       Android + iOS companion apps
```

## 授權

本專案為本機開發工具；Hugo 與各主題請遵循其各自授權條款。
