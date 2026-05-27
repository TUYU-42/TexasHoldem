# 德州撲克 Texas Hold'em

> 視窗程式設計 (II) 作業二：棋牌類遊戲
> 1123305 范宸瑋

一款使用 C# WinForms (.NET Framework 4.7.2) 開發的單人 vs 5 個 AI 的完整 6 人桌 No-Limit 德州撲克遊戲。

---

## 專案簡介

完整實作了德州撲克的核心規則與流程，包含：

- **完整下注流程**：Fold / Check / Call / Raise / All-In，含合法最小加注 (min-raise) 規則
- **多街投注**：Pre-flop → Flop → Turn → River → Showdown，含 burn cards
- **盲注機制**：自動切換 Dealer Button，含 Small Blind / Big Blind，並支援 Heads-up 規則 (莊家為 SB)
- **All-In 與 Side Pot**：依各家累積投注切分主池與多個邊池，正確處理棄牌玩家的籌碼歸屬
- **7-card hand evaluator**：從 7 張牌 (2 手牌 + 5 公牌) 中找出最佳 5 張牌組合，正確處理同花順、輪子順 (A-2-3-4-5)、葫蘆、踢腳比較等
- **AI 策略**：基於手牌強度估算 + 三種性格 (緊、平衡、鬆兇)，會偶爾詐唬
- **籌碼動畫與音效**：發牌、下注、棄牌、All-In、勝利、失敗皆有對應音效；籌碼會動畫飛入底池
- **自訂繪製的撲克桌面**：使用 GDI+ 繪製圓角綠色桌面、座位、籌碼堆、莊家鈕

---

## 執行說明

### 環境需求

- Windows 10 / 11
- Visual Studio 2019 或更高版本
- .NET Framework 4.7.2

### 編譯與執行

1. 用 Visual Studio 開啟 `TexasHoldem.sln`
2. 直接按 F5 (Debug) 或 Ctrl+F5 (Release without debugging) 執行
3. 視窗會自動開啟，發給玩家 2 張手牌即可開始遊玩

### 操作說明

| 按鈕 | 功能 |
|---|---|
| **Fold 棄牌** | 放棄這手牌，失去已下注的籌碼 |
| **Check / Call** | 沒有人下注時 Check；否則 Call 到目前最高注 |
| **Raise 加注** | 透過下方滑桿選擇加注到的總額，然後按下 |
| **All-In 全下** | 把手上所有籌碼推入底池 |
| **New Hand 新局** | 一手結束後按此開始下一局 |
| **Sound 音效** | 勾選/取消音效 |

右側面板會即時顯示遊戲日誌 (誰下注多少、誰贏了底池與牌型)。

---

## 遊戲畫面

> 第一次提交時請補上實際遊玩截圖，並放到 `docs/` 資料夾。

```
docs/screenshot_table.png      ← 整個牌桌畫面
docs/screenshot_showdown.png   ← 結算 (亮牌) 畫面
docs/screenshot_allin.png      ← All-In 與 Side Pot
```

---

## 專案結構

```
TexasHoldem/
├── Card.cs                  # 撲克牌資料模型
├── Deck.cs                  # 52 張牌組 + Fisher-Yates 洗牌
├── HandEvaluator.cs         # 7 張牌找最佳 5 張組合 + 比較
├── Player.cs                # 玩家狀態 (籌碼、手牌、本街/本局已投注)
├── AiPlayer.cs              # AI 決策邏輯 (手牌強度 + 性格)
├── GameEngine.cs            # 遊戲狀態機 (盲注、下注街、Side Pot、Showdown)
├── SoundManager.cs          # 音效播放
├── CardImageProvider.cs     # 牌面圖片載入與快取
├── MainForm.cs              # 主視窗 UI (自訂繪製牌桌)
├── Program.cs               # 進入點
└── Resources/
    ├── Cards/   *.png       # 52 張牌面 + back.png
    └── Sounds/  *.wav       # 8 個音效檔
```

---

## 技術重點

- **狀態驅動架構**：`GameEngine` 透過 `OnEvent` 事件對 UI 廣播每個關鍵時刻 (HandStart, FlopDealt, PlayerActed, Showdown...)，UI 不直接呼叫引擎內部，便於除錯與單元測試
- **雙緩衝面板**：自訂 `DoubleBufferedPanel` 避免重繪閃爍
- **Hand Evaluator**：從 7 張牌中以 C(7,5)=21 組合枚舉所有可能的 5 張牌，回傳含 tie-breakers 的 `HandValue`，可正確比較同牌型踢腳大小
- **Side Pot 演算法**：依各家 `TotalCommitted` 的「不同金額層」切分，每一層的金額 = (本層 - 前一層) × 仍在此層的玩家數，再過濾棄牌者作為該層 eligible

---

## 開發過程 (Git Commit)

請查看 commit history，主要分為幾個階段：

1. `feat: card model and deck` - 撲克牌與牌組
2. `feat: hand evaluator with tie-breakers` - 7 張牌最佳手牌評估
3. `feat: game engine with betting streets` - 完整下注流程
4. `feat: side pot for all-in` - 邊池計算
5. `feat: ai strategy with personalities` - AI 決策
6. `feat: winforms ui with custom drawing` - 自訂繪製牌桌
7. `feat: chip animations and sounds` - 動畫與音效
8. `docs: readme and report` - 文件
