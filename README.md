# Hugoer

以 **Avalonia** 打造的 Hugo 桌面工作台：一鍵建立環境、圖形化設定、安裝主題（含 Stack）、Markdown 即時預覽，並推送到 GitHub、GitLab、Codeberg 或 Bitbucket。

## 功能

| 分頁 | 能力 |
|------|------|
| **環境** | 偵測／一鍵安裝 Hugo Extended、建立新站、開啟既有站、本機預覽、`hugo build` |
| **設定檔** | 網站基本欄位、**圖形化 params 表單**（Hugo / Stack 常用參數）、原始 TOML |
| **主題** | 一鍵安裝 **Stack** 及其他熱門主題、切換 theme、編輯主題設定 |
| **文章** | 只管理 `content/post` 等部落格文章、新增文章、**Markdown 即時預覽**、匯出 **Hexo／Jekyll 相容** Markdown |
| **遷移** | **Hexo／Jekyll → Hugo**、**Hugo → Hexo／Jekyll** 網站遷移（文章、頁面、靜態檔與基本設定） |
| **選單** | 與文章分開：圖形化編輯 `menu.main` / `menu.social`、網站頁面（關於／歸檔／搜尋） |
| **Git 部署** | GitHub、GitLab、Codeberg、Bitbucket 分別保存設定；推送、Pages／靜態網站提示、每 5 分鐘監控線上部署版本 |

## 系統需求

- Windows 10/11（亦可在 macOS / Linux 建置）
- 開發：[.NET 10 SDK](https://dotnet.microsoft.com/download)
- 建議： [Git](https://git-scm.com/)、[GitHub CLI (`gh`)](https://cli.github.com/)

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
.\scripts\publish.ps1 -Version 1.6.0
# 只要單一 exe、不要安裝程式
.\scripts\publish.ps1 -SkipInstaller
```

產出位置：

| 路徑 | 說明 |
|------|------|
| `dist\single\Hugoer.exe` | **單一可攜 EXE**（self-contained，免安裝 .NET） |
| `dist\publish\win-x64\` | publish 輸出目錄 |
| `dist\releases\velopack\` | Velopack **Setup.exe** 安裝程式（需 `vpk`） |
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
vpk pack --packId Hugoer --packVersion 1.6.0 --packDir .\dist\publish\win-x64 --mainExe Hugoer.exe --outputDir .\dist\releases\velopack
```

## 建議使用流程

1. **環境** → 一鍵安裝 Hugo Extended  
2. **環境** → 建立新網站（或開啟既有資料夾）  
3. **主題** → 一鍵安裝 Stack  
4. **設定檔** →「網站基本」改 baseURL / title；「Params 表單」調描述、色系、widgets 等  
5. **文章** → 新增／編輯部落格文章，右側即時預覽；可把單篇或全部文章匯出成 Hexo／Jekyll 相容的 `_posts` Markdown  
   **選單** → 編輯 Home／Archives／Search 等導覽，與文章分開  
6. **遷移** → 從 Hexo 或 Jekyll 匯入成 Hugo，或把目前 Hugo 網站輸出成 Hexo／Jekyll  
7. **Git 部署** → 從下拉選單選擇 GitHub／GitLab／Codeberg／Bitbucket；各平台設定分開保存，再連結或推送 repository。
8. **Git 部署** → 查看「線上版本監控」；Hugoer 每 5 分鐘確認 Pages／靜態網站是否已更新至本次推送版本

## 專案結構

```
Hugoer/
  Controls/     Markdown 即時預覽控制項
  Services/     Hugo、主題、內容、GitHub、Markdown、TOML params
  ViewModels/   MVVM
  Views/        Avalonia XAML
  scripts/      publish.ps1
  installer/    Inno Setup 腳本
```

## 授權

本專案為本機開發工具；Hugo 與各主題請遵循其各自授權條款。
