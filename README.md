# Garmin Music Studio 🎵 (Garmin 手錶音樂管理工具)

專為 Garmin 智慧手錶（Forerunner 255/265/965、Fēnix、Venu 等系列）打造的現代化 Windows 桌面音樂管理軟體。

![Garmin Music Studio](app.png)

## ✨ 主要功能特點

1. **MTP 智慧手錶隨插即用**
   - 自動偵測 USB 連接的 Garmin 手錶裝置。
   - 支援手錶內部儲存容量即時監測與剩餘空間統計。
   - 支援依資料夾（如 `180/`、`跑步歌單/` 等）進行分類瀏覽與批次刪除（一次性確認，不反覆彈窗）。

2. **中文與萬國碼歌名亂碼自動修復**
   - 解決 Windows MTP 傳輸時中文曲名經常變成問號或亂碼的編碼問題（UTF-8 / Big5 智慧識別）。

3. **雙向播放清單產生器 (.m3u8)**
   - 支援勾選手錶內部歌曲或本機歌曲，一鍵建立 Garmin 手錶原生支援的 UTF-8 相對路徑 M3U8 播放清單。

4. **YouTube 音樂高速下載**
   - 輸入 YouTube 影片或播放清單網址，自動下載並轉換為 Garmin 最相容之 192kbps MP3 格式。

5. **Podcast 訂閱與單集下載**
   - 支援 RSS 廣播訂閱，一鍵下載最新單集傳輸至手錶。

6. **Garmin Subsonic 無線伺服器 (WiFi 同步)**
   - 內建相容 Subsonic API 的本機伺服器，配合手錶 SubMusic 應用程式，可透過 WiFi 無線串流與下載手錶離線音樂。

7. **深色主題與 1.5 倍高解析大字體設計**
   - 精緻沉浸式深色介面，字體與圖示清晰易讀。

---

## 🚀 下載與使用

前往 [Releases 頁面](https://github.com/joely1101/garmin-music-suite/releases) 下載最新版本：
- **`GarminMusicStudio.exe`**: 單一獨立執行檔，無需安裝 .NET Runtime，直接點擊開啟。
- **`GarminMusicStudio-win-x64-bundle.zip`**: 完整便攜包，內含 MTP 傳輸支援組件與音訊下載核心。

---

## 🛠️ 開發與編譯

- 開發環境：.NET 8.0 SDK (Windows Desktop WPF)
- 編譯單一執行檔指令：
  ```powershell
  dotnet publish GarminMusicStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
  ```

---

## 📄 License
MIT License
