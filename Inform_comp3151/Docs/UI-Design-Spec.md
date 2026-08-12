# Bomb Slime - UI 设计规格文档

> 来源：`Docs/Bomb Slime UI.png`
> 整理日期：2026-08-12

---

## 一、整体导航流程图

```
                        ┌─────────────┐
                        │ Loading Page│ (Loading...)
                        └──────┬──────┘
                               ↓
┌──────────┐          ┌──────────────┐          ┌──────────────┐
│ Loading  │ ←─────── │  Save Menu   │ ←─────── │  Main Menu   │
│  Page    │  (存档后) │  (3个存档槽)  │  (Play)  │  Bomb slime  │
└──────────┘          └──────────────┘          └──────┬───────┘
                                                       │
                                                ┌──────┴───────┐
                                                │              │
                                         ┌──────┴──────┐ ┌─────┴─────┐
                                         │  Settings   │ │ Pause Menu│
                                         │             │ │  Resume   │
                                         │  Sound      │ │  Settings │
                                         │  Graphics   │ │ Save&Quit │
                                         │  Controls   │ └───────────┘
                                         └──────┬──────┘
                                                │
                                   ┌────────────┼────────────┐
                                   ↓            ↓            ↓
                              ┌────────┐  ┌──────────┐  ┌──────────┐
                              │ Sound  │  │ Graphics │  │ Controls │
                              │ Main Vol│ │ Resolution│ │ Input Dev│
                              │ Music Vol│ │ FullScreen│ │ Mouse Sen│
                              │ SFX Vol│  │ FPS      │  │ Ctrl Sen │
                              └────────┘  │ VSync    │  │ Key Bind │
                                          └──────────┘  └──────────┘
```

---

## 二、各面板详细规格

### 1. Main Menu（主菜单）

| 元素 | 内容 | 说明 |
|------|------|------|
| **标题** | "Bomb slime" | 游戏名称，居中大字 |
| **按钮 1** | Play | 进入 Save Menu 选择存档 |
| **按钮 2** | Settings | 打开设置面板 |
| **按钮 3** | Exit | 退出游戏 `Application.Quit` |

**布局**：标题居中上方，三个按钮垂直排列在下方

---

### 2. Save Menu（存档菜单）

| 元素 | 内容 | 说明 |
|------|------|------|
| **标题** | Save Menu | |
| **存档槽 ×3** | "No Records" | 空存档显示占位文字，有存档显示关卡/时间等 |
| **左右箭头** | ◀ ▶ | 翻页/选择存档槽 |
| **点击存档** | → 进入 Loading Page | 加载后进入游戏 |

**布局**：标题在上，3个存档槽垂直堆叠，左右两侧有导航箭头

---

### 3. Loading Page（加载页面）

| 元素 | 内容 | 说明 |
|------|------|------|
| **加载提示** | "Loading..." | 底部文字 |
| **进度** | （可选）进度条或 spinner | 设计图未明确，但隐含加载状态 |

**布局**：全屏或半屏，底部显示 "Loading..."

---

### 4. Pause Menu（暂停菜单）

| 元素 | 内容 | 说明 |
|------|------|------|
| **背景** | 游戏画面（半透明覆盖） | 暂停时游戏画面冻结/变暗 |
| **按钮 1** | Resume | 返回游戏 |
| **按钮 2** | Settings | 打开设置面板 |
| **按钮 3** | Save & Quit | 保存并返回主菜单 |

**布局**：弹窗形式，垂直排列三个按钮

**注意**：暂停时需要：
- `Time.timeScale = 0` 暂停游戏
- 显示鼠标 `Cursor.visible = true`
- 解除鼠标锁定 `Cursor.lockState = CursorLockMode.None`

---

### 5. Settings（设置面板 - 主入口）

| 元素 | 内容 | 说明 |
|------|------|------|
| **标题** | Settings | |
| **左侧导航** | Sound / Graphics / Controls | 当前选中项高亮 |
| **右侧内容** | 随左侧选中切换 | |
| **底部按钮** | Reset Settings | 恢复默认 |
| **底部按钮** | Back | 返回上级菜单 |

**布局**：左右分栏（左导航 30%，右内容 70%）

---

### 6. Sound（声音设置）

| 控件 | 说明 | 默认值（推测） |
|------|------|--------------|
| **Main Volume** | 主音量滑块 | 100% |
| **Music Volume** | 音乐音量滑块 | 100% |
| **SFX Volume** | 音效音量滑块 | 100% |

**与现有 AudioManager 对接**：
- `AudioManager.masterVolume`
- `AudioManager.musicVolume`（需确认）
- `AudioManager.sfxVolume`（需确认）

---

### 7. Graphics（图形设置）

| 控件 | 选项 | 说明 |
|------|------|------|
| **Resolution** | ◀ 2560×1440 ▶ | 分辨率切换，左右箭头轮询 |
| **Full Screen** | ◀ OFF ▶ | 全屏开关 |
| **FPS** | ◀ 60 ▶ | 帧率上限 |
| **VSync** | ◀ Low ▶ (slider?) | 垂直同步，滑块或选项 |

**注意**：VSync 那个看起来像滑块（Low→High），但通常 VSync 是开关。可能需要确认具体实现。

---

### 8. Controls（控制设置）

| 控件 | 说明 | 默认值（推测） |
|------|------|--------------|
| **Input Device** | Controller/Keyboard 切换 | 根据当前输入自动检测 |
| **Mouse Sensitivity** | 鼠标灵敏度滑块 | 1.0 |
| **Controller Sensitivity** | 手柄灵敏度滑块 | 1.0 |
| **Key Bindings** | 按键重映射区域 | 参考 InputSystem_Actions |

**与现有系统对接**：
- `InputHandler` 已有键盘/手柄识别逻辑
- 需要扩展 `InputSystem_Actions` 支持运行时 rebinding

---

### 9. Map（地图界面）

| 元素 | 说明 |
|------|------|
| **关卡结构** | 显示当前关卡的房间布局（矩形=房间，线=通道） |
| **玩家位置** | 红色圆点 = 玩家当前位置 |
| **已探索/未探索** | 可能需要 fog of war 或颜色区分 |

**说明**：设计图左下角单独画了一个 Map，可能是游戏中按 Tab/M 打开的独立面板，不属于 Settings 体系。

---

## 三、状态转换总结

| 当前状态 | 触发 | 目标状态 |
|---------|------|---------|
| **启动** | 游戏启动 | Main Menu |
| Main Menu → Play | 点击 Play | Save Menu |
| Main Menu → Settings | 点击 Settings | Settings |
| Main Menu → Exit | 点击 Exit | 退出应用 |
| Save Menu → 选择存档 | 点击存档槽 | Loading Page → Game |
| Settings → Back | 点击 Back | 返回 Main Menu / Pause Menu |
| Settings → Reset | 点击 Reset Settings | 恢复默认并刷新 UI |
| Settings → Sound/Graphic/Ctrl | 点击左侧导航 | 切换右侧内容 |
| **游戏中** | 按 Esc / Start | Pause Menu |
| Pause Menu → Resume | 点击 Resume | 返回游戏 |
| Pause Menu → Settings | 点击 Settings | Settings（Back 回 Pause Menu）|
| Pause Menu → Save & Quit | 点击 Save & Quit | Main Menu |

---

## 四、与现有项目的集成点

| 现有系统 | 对接方式 |
|---------|---------|
| `InputHandler` (InputSystem_Actions) | 菜单需要切换到 UI 输入模式（`UIInputModule`），暂停时禁用游戏输入 |
| `AudioManager` / `AudioDirector` | 音量设置写入，读取当前值 |
| `SceneLoader` | 存档加载、回主菜单需要场景切换 |
| `Cursor` 控制 | 游戏内 `Cursor.visible = false`，菜单需要 `true` |
| `DontDestroyOnLoad` (GameManager) | 菜单状态需要跨场景持久化（设置、存档）|
| `LevelMemento` / `RespawnDirector` | 存档保存/加载对接（现有 LevelMemento 已有存档基础）|
