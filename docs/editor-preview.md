# 編輯器預覽

Hugoer Mobile 的文章編輯器在「預覽」模式會以原生元件呈現文章內容，不需要先發布到 GitHub Pages。

支援的內容：

- Markdown 標題、段落、引用、清單與 fenced code block
- Markdown 圖片：`![替代文字](image.jpg)`
- Markdown 媒體連結：影片（MP4／M4V／MOV／WebM）與 PDF
- HTML `<img>`、`<video>`／`<source>`、`<iframe>`／`<embed>`／`<object>` 媒體標籤
- Hugo `figure`、`image`、`img`、`video`、`pdf` shortcodes（具名 `src`／`url`／`data`／`file` 或第一個引號參數）

相對路徑會以目前站點的 Pages URL 解析；絕對網址必須是 HTTP(S)，本機檔案可使用 `file://`（Android 也接受 `content://`）。媒體載入失敗時會保留來源與重試按鈕，離線時不會阻塞文章編輯。影片必須是可直接串流的媒體網址，YouTube 頁面網址不會被當成影片檔案。

Android 會用 `PdfRenderer` 顯示 PDF 前三頁，iOS 會用 `PDFKit` 顯示可捲動的完整文件；兩個平台都不會在預覽時自動播放影片。
