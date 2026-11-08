# 螢幕翻譯

在畫面上圈一塊區域，程式每隔約 250 毫秒擷取一次。文字沒變就略過；有變就用本機 [Ollama](https://ollama.com) 譯成繁體中文，再把譯文蓋回原處。遮罩可以點穿，也不會被自己擷取進去。

同一套行為有兩個獨立程式，沒有共用程式碼：

| | Mac | Windows |
| --- | --- | --- |
| 程式 | Swift 6、AppKit 選單列 | C#、WPF，系統匣 |
| 系統 | macOS 14 以上 | Windows 10 2004 以上，含 Windows 11 |
| 擷取 | ScreenCaptureKit | 螢幕 BitBlt |
| 辨識 | Apple Vision | Windows.Media.Ocr |
| 設定 | UserDefaults | `%AppData%\ScreenTranslator\settings.json` |

預設連到 `http://127.0.0.1:11434`，模型是 `qwen2.5:7b`。目標語言固定是繁體中文。

## 使用

先讓 Ollama 在本機跑起來，並拉下模型：

```bash
ollama pull qwen2.5:7b
```

然後：

1. 開啟程式。Mac 圖示在選單列；Windows 圖示在系統匣，左鍵打開選單。
2. 選「圈選區域」，在同一個螢幕上拖出範圍。按 Esc 取消。範圍太小（不到約 24 點）不會採用。
3. 選「開始」。選單會改成「暫停」。
4. 「設定…」可改來源語言、模型名稱、Ollama 位址和遮罩不透明度。

來源語言有：自動、日文、英文、簡體中文、繁體中文。

- Mac 的「自動」會依序嘗試日文、英文、繁體中文、簡體中文，並依辨識信心取用。
- Windows 的「自動」只用日文光學字元辨識。Windows OCR 一次只能指定一種語言，也沒有信心值。

翻譯結果會快取。畫面短時間變成空白時，遮罩會再留約 0.4 秒才清掉，避免字幕閃爍。

### Mac 權限

第一次擷取要允許螢幕錄製。若系統沒有跳出詢問，到「系統設定 › 隱私權與安全性 › 螢幕錄製」打開這個 App，然後重新開啟。

### Windows 光學字元辨識

辨識用的是 Windows 語言元件，不是程式自己附帶的套件。到「設定 › 時間與語言 › 語言與地區」，打開該語言的「語言選項」，安裝對應的光學字元辨識。

日文要先裝「基本鍵入」，「光學字元辨識」才會出現。英文、簡體中文、繁體中文也是各自的語言元件。沒裝時，設定視窗會提示要裝哪一種。

## 建置

Mac 需要 Swift 6 工具鏈：

```bash
swift test
scripts/build-app.sh
```

產出是 `dist/ScreenTranslator.app`。

Windows 需要 .NET 8 SDK（`Windows/global.json` 指定 8.0.406，可往後相容到同系列較新的 8.x）。在存放庫根目錄執行：

```powershell
dotnet test Windows\ScreenTranslator.sln
scripts\build-windows.ps1
```

產出在 `dist\windows\`。這是依賴本機執行環境的發佈，目標機器要安裝 **.NET 8 Desktop Runtime**。請帶走整個資料夾：`ScreenTranslator.exe` 還需要旁邊的 dll 和 json。兩個 `.pdb` 只是除錯符號，可以不拷。

`dist/`、`.build/`、`bin/`、`obj/` 都已忽略，不會進 git。

## 目錄

```
App/                         Mac 介面、擷取、遮罩
Core/                        Mac 翻譯流程與 Ollama 用戶端
Tests/TranslatorCoreTests/   Mac 流程測試
Support/Info.plist
scripts/build-app.sh
scripts/build-windows.ps1
Windows/
  ScreenTranslator.Core/         同一套翻譯流程的 C# 實作
  ScreenTranslator.Core.Tests/
  ScreenTranslator.App/          系統匣、設定、擷取與遮罩
```

## 限制

- 沒有安裝程式，也沒有全域快速鍵。
- 獨佔全螢幕的遊戲在 Windows 上可能擷到黑畫面。視窗化或無邊框的影片與字幕可以擷。
- 遮罩是點擊穿透的，所以設定視窗不要設成永遠在最上層，否則會被遮罩擋住卻點不到。
