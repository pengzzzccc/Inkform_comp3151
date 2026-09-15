# Inkform UI 系统重做设计稿（Blueprint）

> 版本：v1.0（2026-09-15）
> 前置文档：[UI-UIToolkit-Celeste-Design.md](UI-UIToolkit-Celeste-Design.md)（迁移期设计，本文取代其布局部分，保留其视觉语言）
> 坐标系：PanelSettings 参考分辨率 **1920 x 1080**（ScaleWithScreenSize, match 0.5）

---

## 1. 目标与范围

### 1.1 问题清单与处置

| # | 问题 | 证据 | 处置 |
|---|------|------|------|
| A | 面板视口高度塌陷 `root=1920x0`，所有锚定层随之坍缩 | Editor.log `[UIManager] first layout` | P0：视口加固（见 §6.3） |
| B | 样式链路三路冗余（TSS / `<Style src>` / root 直挂） | Theme 同时经三条路径进入 | P1：收敛为单一路径 + 显式回退 |
| C | Settings 无遮罩，与底层 MainMenu 视觉叠字 | 运行截图（OPTIONS 与 BEGIN 重叠） | P1：Settings 加 `.dim` |
| D | 诊断代码 / 命名残留 | UIManager 一次性日志 | P1 清理 |

### 1.2 明确保留（架构约束，重做不得破坏）

- **UIManager 单一门卫**：面板永不直接触碰游戏系统，一切经 `UIManager` 路由（开面板、进关、暂停、退出）。
- **ToolkitPanel 生命周期**：纯 C# 对象（非 MonoBehaviour），`Open/Close/Tick/Teardown`，UIManager.Update 驱动。
- **事件总线**：`UiBus`(悬停/点击/切换音效)、`ItemBus.AbilityUnlocked`(教程)、`LifeBus.Died`(教程兜底)、
  `InventoryStore.Changed` / `SettingsStore.Changed` / `SaveStore.Changed/Saved`(HUD/存档菜单)。
- **场景策略归 SceneDirector**：`IsMenuScene/IsEndScene/StartNewGame/ContinueGame/ReturnToMainMenu/IsTransitioning`。
- **设置持久化归 SettingsStore**（SettingsPanel 是唯一写者）；存档归 SaveStore（3 槽）。
- **SceneFader 保持 uGUI**（sortingOrder 200，位于 Toolkit 面板 100 之上）——转场黑幕不属于本次范围。
- **字体约束**：BombSlimeFonts.ttf 仅含拉丁字形 → 所有 UI 文案 ASCII。
- **音效**：四条 SoundCue（hover/click/toggleOn/toggleOff），经 UiBus 触发，面板不认识音频系统。

---

## 2. 视觉设计语言（Celeste 风）

沿用 Theme.uss 已验证的 token，P1 重组为三层：

```
:root
├── 色彩：--c-night/--c-summit 背景 · --c-green 悬停高亮 · --c-yellow 强调
│         --c-header 标题描边 · --c-card 明信片米白 · --c-dim 40% 遮罩
├── 字阶：76(标题) 57(BEGIN/终局数字) 38(菜单项) 30(值) 23(小节) 19(角标)
└── 交互签名：--float-y -8px 悬浮 · --press-y -2px 按压 · 0.18s/0.08s 过渡
```

**交互签名（所有可选项共享）**：悬浮/聚焦 → 上浮 8px + 放大 1.06 + 文字变绿 + 底部 44px 绿色下划线淡入；
按下 → 回落 2px + 缩小 0.96；键盘/游戏柄 Submit 无 :active，由 C# `UiFx.Bounce` 补一次回弹。

**全屏切换签名**：菜单页自右滑入（0.25s，CubeOut）+ 淡入；主菜单列表例外，自左滑入（Celeste OuiMainMenu）；
关闭反向滑出。存档槽额外做 0.06s 级联。

---

## 3. 面板设计稿

以下线框均以 1920x1080 参考分辨率绘制。

### 3.1 MainMenu（主菜单）

```
1920x1080, 背景为场景相机画面（透出，不加遮罩）
┌────────────────────────────────────────────────────────────┐
│ (150,64)                                                   │
│ ┌──────────────┐                                           │
│ │ BOMB SLIME   │  76px 灰字 + 蓝色描边(--c-header)          │
│ └──────────────┘                                           │
│ (150,210)                                                  │
│ ┌───────────────────┐                                      │
│ │ ▸ BEGIN           │  57px ← 当前焦点态(上浮+绿+下划线)     │
│ ├───────────────────┤                                      │
│ │   OPTIONS         │  38px                                │
│ ├───────────────────┤                                      │
│ │   EXIT            │  38px                                │
│ └───────────────────┘  列宽 640px, 自左滑入(-500px)         │
│                                                            │
│ (80, bottom-28)                            (right-40, b-28)│
│  v0.4 (版本号, 19px 蓝字)          Confirm - Enter  Back - Esc│
└────────────────────────────────────────────────────────────┘
```

| 元素 | 命名 | 功能 | 激发 |
|---|---|---|---|
| 开始 | `Btn_Begin` | 打开存档选择页 | → `UI.Open<SaveMenuPanel>()` |
| 选项 | `Btn_Settings` | 打开设置页（本页**保持打开**在底层） | → `UI.OpenSettings()` |
| 退出 | `Btn_Exit` | 退出游戏 | → `UI.Quit()` |
| 版本号 | `Lbl_Version`（新增，复用已有 `.version-label` 类） | `v{Application.version}` | 无交互 |

### 3.2 SaveMenu（存档选择 · Celeste OuiFileSelect）

```
1920x1080, 全屏 40% 黑遮罩(.dim)
┌────────────────────────────────────────────────────────────┐
│                  SAVE SLOTS (38px 标题, 顶部居中 y=110)      │
│                                                            │
│         ┌──────────────────────────────────────┐           │
│         │ ████ (HoldFill: 绿色渐进填充层)        │           │
│         │  Slot 1 - Moon Chapter    (30px 深灰)  │ 980x150  │
│         │  Moon_1  1:23:45  12 deaths  2026-..  │  19px 黑  │
│         └──────────────────────────────────────┘  圆角10    │
│         ┌──────────────────────────────────────┐  每卡间距14 │
│         │  Slot 2 - New Game                    │           │
│         └──────────────────────────────────────┘           │
│         ┌──────────────────────────────────────┐           │
│         │  Slot 3 - New Game                    │           │
│         └──────────────────────────────────────┘           │
│              Confirm - Enter          Back - Esc           │
└────────────────────────────────────────────────────────────┘
三卡自右级联滑入（0.25s + 0.06s/卡）
```

| 交互 | 条件 | 行为 |
|---|---|---|
| 点按槽位 | 有存档 | `UI.ContinueGame(slot)` → SceneDirector.ContinueGame |
| 点按槽位 | 空槽 | `UI.StartNewGame(slot)` → BeginNewRun + StartNewGame |
| **长按 1s** | 有存档 | 进入"覆盖待确认"态：文案变 `Overwrite with a new game? Tap again to confirm`，绿填充铺满触发 |
| 再点一次 | 覆盖待确认态（3s 内） | 确认覆盖 → StartNewGame |
| 3s 超时 / 点其他卡 | — | 自动回到常态 |
| Esc | — | 回到 MainMenu（SaveMenu 关闭） |

键盘/游戏柄限制（保留既有取舍）：Submit 无长按语义 → 只能"继续/新开"，覆盖仅鼠标可用。

### 3.3 Settings（设置 · Celeste OuiOptions 单页滚动）

```
1920x1080, 全屏 40% 黑遮罩(.dim) ← 本次新增, 修复叠字
┌────────────────────────────────────────────────────────────┐
│ y=32 居中: OPTIONS (76px 灰字蓝描边)                         │
│ ┌─ScrollView (y 140→960)──────────────────────────────────┐│
│ │  SOUND (23px 小节头)                                     ││
│ │    Mute               < OFF >      Main Volume  ───●── 80%││
│ │    Music Volume ──●── 60%          SFX Volume   ──●── 100%││
│ │  GRAPHICS                                                ││
│ │    Resolution < 1920 x 1080 >      Fullscreen  < ON >    ││
│ │    FPS Cap   < 60 >                FX Intensity ──●── 70% ││
│ │    Show FPS  < ON >                Perf Recording < OFF > ││
│ │    Perf Rate < 10 Hz >                                   ││
│ │  CONTROLS                                                ││
│ │    Device < Keyboard + Mouse >     Rumble < ON >         ││
│ │    Mouse Sens ──●── 1.0            Controller Sens ──●──  ││
│ │    ┌ KBM 重绑定表 (Device 切换) ┐   ┌ Gamepad 表 ┐        ││
│ │    │ Move Up/Down/Left/Right │   │ Jump/Dash/...  │     ││
│ │    │ Jump Dash RopeFire ...  │   │ (Move/Aim 只读) │     ││
│ │    Unstuck (游戏中可用)       Reset Bindings              ││
│ └─────────────────────────────────────────────────────────┘│
│                                     Reset   Back (30px 右下)│
└────────────────────────────────────────────────────────────┘
```

| 分组 | 行 | 控件类型 | 写入 |
|---|---|---|---|
| SOUND | Mute | OptionRow(ON/OFF) | SettingsStore.SetMuted |
| | Main/Music/SFX Volume | Slider 0-1 + 百分比读数 | SetMaster/Music/SfxVolume |
| GRAPHICS | Resolution | OptionRow 循环(按值匹配) | SetResolution |
| | Fullscreen / Show FPS / Perf Recording / Perf Rate / FPS Cap | OptionRow / 循环 | 对应 Set |
| | FX Intensity | Slider 0-1 | SetFxIntensity |
| CONTROLS | Device | OptionRow 循环，切换下方重绑定表 | SetDevice |
| | Rumble | ON/OFF | SetRumble |
| | Mouse/Controller Sensitivity | Slider + 倍数读数 | SetMouse/StickSensitivity |
| | 重绑定表 KBM 9 行 / Pad 6 行 | 整行按钮，点击进入监听 | BindingTools.StartRebind |
| | Unstuck | 动作行(菜单场景禁用) | UI.Unstuck → RespawnDirector |
| | Reset Bindings | 动作行 | BindingTools.ResetAllBindings |
| 底部 | Reset | 恢复全部默认 + 重绑表刷新 | SettingsStore.ResetToDefaults |
| | Back | 返回底层页（暂停→PauseMenu，主菜单→MainMenu） | UI.CloseSettings |

数据纪律（保留）：OnOpen/Reset 时**拉取** SettingsStore 刷新全部行；行控件变更时**推送**回 Store；滑块用
`SetValueWithoutNotify` 防自激。打开时滚动条归零。

### 3.4 PauseMenu（暂停）

```
1920x1080, 全屏 40% 黑遮罩 → 下方游戏画面被压暗
┌────────────────────────────────────────────────────────────┐
│                       PAUSED (76px)                        │
│                     (margin-bottom 40)                     │
│                  ┌──────────────────┐                      │
│                  │     RESUME       │  38px 居中列 720px    │
│                  │    OPTIONS       │                      │
│                  │   SAVE & QUIT    │                      │
│                  └──────────────────┘                      │
│              Confirm - Enter          Back - Esc           │
└────────────────────────────────────────────────────────────┘
```

- 打开（Esc/Start）：`UI.OpenPause()` → timeScale=0、输入关闭、光标释放、GameState=Paused。
- `Btn_Resume` → `UI.Resume()`；`Btn_Settings` → `UI.OpenSettings()`（本页保持打开，Back 时恢复）；
  `Btn_SaveAndQuit` → `UI.QuitToMainMenu()` → SceneDirector 归档 + 回主菜单。
- Esc 在 Pause 打开时 = Resume（栈底翻转）。

### 3.5 Tutorial（能力教程 · 拾取触发）

```
1920x1080, 全屏 40% 黑遮罩; 打开期间: 游戏输入锁定 + 世界冻结(scoped lock)
┌────────────────────────────────────────────────────────────┐
│ y 110→930:                                                 │
│   ┌───────────────────────────────────────────────┐        │
│   │        Page (能力教程图, scale-to-fit)          │        │
│   └───────────────────────────────────────────────┘        │
│ bottom-40 居中页脚:                                         │
│   ◀ PREV        1 / 3        NEXT ▶    (30px, 页首/页尾禁用) │
│                                  CLOSE (右下)               │
└────────────────────────────────────────────────────────────┘
```

- 激发：`ItemBus.AbilityUnlocked(pos, abilityId)` → `UIManager.OpenTutorial(abilityId)`；
  仅当 `showOnPickup && HasPages(abilityId)`（Checkpoint→checkpointPages，RopeGun→ropeGunPages）。
- 每条关闭路径（按钮/Esc/死亡兜底/UIManager 销毁）都必须释放 scoped lock——不可破坏的安全约束。
- 页脚按钮**禁用而非隐藏**（不回流布局，FocusFirst 跳过禁用项）。

### 3.6 EndPanel（终局结算 · Celeste AreaCompleteTitle）

```
1920x1080, 无遮罩(终局场景本身就是收尾画面)
┌────────────────────────────────────────────────────────────┐
│                       (垂直居中列)                          │
│              R U N   C O M P L E T E                       │
│              (逐字母下落动画 UiFx.DropTitle, 76px)           │
│                                                            │
│                  Deaths  12        (57px)                  │
│                  Total time  1:23:45                       │
│                                                            │
│                       CONFIRM (38px)                       │
└────────────────────────────────────────────────────────────┘
```

- 激发：终局场景加载 → `ApplySceneState` → Open。OnOpen 先 `SaveStore.SaveNow()` 再读数（含最后一房间的时长）。
- `Btn_Back` / Esc → `UI.ReturnToMainMenu()` → SceneDirector.ReturnToMainMenu。

### 3.7 HUD（游戏中常显 · 全部 picking-mode Ignore）

```
1920x1080, 透传所有点击
┌────────────────────────────────────────────────────────────┐
│                12:34 (38px 顶部居中, outline)               │
│                                             144 FPS (19px) │
│                                          ┌──────────────┐  │
│                                          │ ▣      3/5   │  │
│                                          │ 图标   计数    │  │
│                                          └──────────────┘  │
│                                        Saved (19px, 淡出)  │
└────────────────────────────────────────────────────────────┘
```

| 元素 | 规则 |
|---|---|
| TimerLabel | 仅 `GameState==Playing && !IsTransitioning` 时累加（缩放时间）；关卡加载时归零 |
| FpsLabel | `SettingsStore.ShowFps` 门控；0.5s 节流 + 0.1 指数平滑；非缩放时间 |
| Inventory | `InventoryStore.Changed` 驱动：FIFO 队首道具图标 + `Count/Capacity` |
| SaveToast | `SaveStore.Saved` 触发：停 1s → 0.4s 淡出（非缩放时间，暂停中也能闪） |

---

## 4. 连接拓扑

### 4.1 页面导航图

```
                       SceneDirector (场景策略唯一所有者)
        ┌──────────────────────┼───────────────────────┐
        ▼                      ▼                       ▼
   MainMenu.unity         Level1/*.unity            End.unity
   (ApplySceneState)      (ApplySceneState)         (ApplySceneState)
        │                      │                       │
   ┌────▼─────┐           ┌────▼────┐            ┌─────▼─────┐
   │ MainMenu │           │   HUD   │            │ EndPanel  │
   └─┬───┬───┬┘           └─┬───────┘            └─────┬─────┘
     │   │   │              │ Esc/Start                │ Back/Esc
     │   │   │         ┌────▼─────┐                    │
     │   │   │         │ PauseMenu│◄── Esc 再按=Resume ─┤
     │   │   │         └─┬───────┬┘                    │
     │   │   │           │       │Save&Quit ──────→ 回主菜单
     │   │   │      OPTIONS     │
     │   │   │           ▼       ▼
     │   │   └──────►┌──────────┐
     │   │           │ Settings │  Back → Pause(暂停链) 或 留在MainMenu
     │   │           └──────────┘
     │ BEGIN          ▲
     ▼                │OPTIONS
┌─────────┐           │
│ SaveMenu├───────────┘ (MainMenu 保持打开在底层)
└────┬────┘
     │ 选中槽位(继续/新开/覆盖确认)
     ▼
  进入 Level1 (HUD 重置计时)

  [关卡内] 拾取能力 ──ItemBus.AbilityUnlocked──► Tutorial(冻结世界) ──CLOSE/Esc──► 回到游戏
```

### 4.2 Escape/Start 退出栈（UIManager.Update，由内向外）

```
Tutorial → Settings → SaveMenu → EndPanel(=回主菜单) → Pause(开/关翻转)
※ IsTransitioning 期间输入被 SceneFader/RoomIntro 持有，一律忽略
```

### 4.3 数据流（谁读谁写）

```
SettingsPanel ──写──► SettingsStore ──Changed──► Hud(FPS可见性)
       ▲                     │
       └──────拉取(OnOpen/Reset)──────
SaveMenuPanel ◄─Get/HasSave/Changed─ SaveStore ◄─SaveNow/EndRun─ EndPanel/SceneDirector
Hud ◄─InventoryStore.Changed          Hud ◄─SaveStore.Saved(SaveToast)
TutorialPanel ◄─TutorialPages(SO)     UIManager ◄─LifeBus.Died(兜底关教程)
全部面板 ──UiBus.Hovered/Clicked/Toggled──► UIManager ──► SoundCue×4
```

---

## 5. 激发关系矩阵

| # | 触发源 | 类型 | 结果 |
|---|--------|------|------|
| 1 | 场景加载（菜单/关卡/终局） | ApplySceneState | 开 MainMenu / 关全部+HUD / 开 EndPanel |
| 2 | Esc / Start 手柄 | 每帧轮询 | 按退出栈逐层退；无面板时翻转型暂停 |
| 3 | 拾取能力解锁 | ItemBus 事件 | 开 Tutorial（条件门控），冻结世界 |
| 4 | 玩家死亡 | LifeBus 事件 | 兜底关 Tutorial |
| 5 | BEGIN | MainMenu 按钮 | 开 SaveMenu |
| 6 | OPTIONS | MainMenu/Pause 按钮 | 开 Settings（底层页保持） |
| 7 | 槽位点按/长按 | SaveMenu 指针 | 继续 / 新开 / 覆盖确认流程 |
| 8 | RESUME / Esc | Pause 按钮 | 关 Pause，恢复 timeScale/输入/光标 |
| 9 | SAVE & QUIT | Pause 按钮 | 归档 → 场景转场 → MainMenu |
| 10 | Unstuck | Settings 动作行 | RespawnNow → 关 Settings → Resume |
| 11 | 存档写入 | SaveStore.Saved | HUD "Saved" toast |
| 12 | 悬停/点击/切换 | UiBus | 四条 SoundCue 之一（空槽静默跳过） |

---

## 6. 技术架构

### 6.1 文档结构（收敛样式来源）

```
Assets/Resources/UI/
├── RuntimeTheme.tss        ← 全局主题入口：@import unity-theme://default（必须用此 URI——按文件名
│                              import UnityDefaultRuntimeTheme.tss 无法解析且静默失败）+ @import Theme.uss；
│                              缺默认主题会让 ScrollView/Slider 等内置控件失去内部布局、内容叠压
├── Theme.uss               ← 共享基座：token、.screen/.dim、描边、.floaty、.menu-item、.confirm-hint
├── MainMenu.uss  SaveMenu.uss  Settings.uss
├── PauseMenu.uss  Tutorial.uss  EndPanel.uss  Hud.uss   ← 每面板一个样式文件
├── MainPanel.asset         ← PanelSettings(1920x1080, match 0.5, sortingOrder 100)
├── *.uxml ×7               ← 每张面板一个模板（结构见 §6.2）
├── TutorialPages.asset     ← 教程页图
└── BombSlimeFonts.ttf  LiberationSans.ttf   ← Resources 可加载的字体副本
```

**样式规则**：共享基座经 PanelSettings.themeUss 全局加载；每张 UXML 恰好一行 `<Style src="自己的面板.uss"/>`，
面板私有布局只进自己的文件（跨面板覆写一律不允许——PauseMenu 用独立类 .pause-column 而非覆写
.menu-column）。UIManager 保留 root 直挂 Theme.uss 作为**显式回退**。

### 6.2 UXML 结构规范（每张面板统一骨架）

```
<ui:UXML>
  <ui:VisualElement name="<PanelName>" class="screen">
      ↑ .screen: absolute 0/0/0/0
    <ui:VisualElement class="dim" picking-mode="Ignore"/>   ← 需要压暗背景的页才有
    ...内容(锚定/flex 列)...
    <ui:VisualElement class="confirm-hint" picking-mode="Ignore">... </ui:VisualElement>
  </ui:VisualElement>
</ui:UXML>
```

命名规范：`Btn_*` 按钮、`Lbl_*` 标签、`Slot{i}/SlotTitle{i}/SlotInfo{i}/HoldFill{i}` 存档槽、
语义名其余（`MenuColumn`、`Scroll`、`Rows`）。全部交互控件加 `.floaty`，文字加 `.outline`。
HUD 树所有节点 `picking-mode="Ignore"`。

### 6.3 视口加固（P0，针对 root=1920x0）

已确认：样式链路已通（`screenPos=Absolute`），剩余问题**仅为面板视口高度**。首要怀疑：
**Game 视图 Scale 缩放滑块 ≠ 1x**（截图为 0.55x）——UI Toolkit 运行时面板在 Game 视图缩放下
取到退化尺寸，是编辑器已知问题（不影响真机）。

落地三件事：
1. **验证**：Game 视图 Scale 拖回 1x，复测 `[UIManager] first layout` 应输出 `1920x1080`。
2. **守卫**：CreateDocument 的首布局回调升级——`root.layout.height < 1 && Screen.height > 1` 时输出
   可操作告警（"Game view Scale 必须为 1x，运行时面板高度为 0"），一次 Play 只报一次。
3. **不变式**：层结构保持"root 拉伸 + 子层 absolute 拉伸"（正确支持任意宽高比），不引入固定像素尺寸——
   真机上 ScaleWithScreenSize 的面板坐标空间恒等于参考分辨率按 match 混合的结果，拉伸语义最正确。

### 6.4 面板基类与动画（保留现状，微调）

- `ToolkitPanel`：`Open/Close/Tick/Teardown`，`BringToFront` + 焦点首个 `.floaty`。
- `UiFx`：`SlideIn/SlideOut/Staggered/Bounce/Nudge/DropTitle`，全部非缩放时间、元素调度器驱动。
- 关闭-打开竞态由"读活状态"的守卫处理（保留）。

---

## 7. 落地计划

| 阶段 | 内容 | 工作量 | 验收标准 |
|---|---|---|---|
| **P0 视口加固** | §6.3 三件事：1x 复测、守卫告警、删除旧探针 | 0.5h | `first layout: root=1920x1080`；缩放时出现可操作告警 |
| **P1 骨架** | Theme.uss 分层重组；7 张 UXML 按 §6.2 重写；删 `<Style src>`；Settings 加 `.dim`；MainMenu 加版本号 | 1 天 | Builder 预览正常；编译零警告；样式单路来源 |
| **P2 HUD** | Hud.uxml + Hud.cs 校准（结构不变，微调布局与 outline） | 0.5 天 | 关卡内计时/FPS/背包/toast 全部按 §3.7 规则工作 |
| **P3 菜单链** | MainMenu / SaveMenu / Pause 三面板 + 级联动画 + 覆盖确认流 | 1 天 | §4.1 导航图全路径可走通；动画符合 §2 签名 |
| **P4 Settings** | 全部行重建（行工厂保留）、双设备重绑定表、Reset/Unstuck | 1 天 | 每行读写 SettingsStore 正确；重绑定即时刷新；无自激 |
| **P5 Tutorial + End** | 页组/冻结/锁定不变，视觉按 §3.5/§3.6；DropTitle 结算 | 0.5 天 | 拾取→教程→关闭全路径锁释放；终局数字与存档一致 |
| **P6 回归** | 更新 EditMode 测试（资源断言、Tutorial 锁、面板元素存在性）；全场景手测 | 0.5 天 | 测试全绿；MainMenu/Level1/End 三场景手测通过 |

执行顺序强依赖：P0 → P1 → 其余（P2-P5 可并行）→ P6。P0 完成前不要动任何布局代码——视口高度问题
会让一切布局验证失真。

风险与回退：若 1x 下 root 高度仍为 0（编辑器与真机皆然），则问题在面板创建时序而非 Game 视图，
届时把 UIDocument 从运行时 AddComponent 改为 GameManager.prefab 上预挂组件（序列化 panelSettings），
其余计划不变。
