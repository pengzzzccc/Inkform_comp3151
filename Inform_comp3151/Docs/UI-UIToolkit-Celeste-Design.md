# Inkform UI — UXML/USS 迁移 + 《蔚蓝》风格设计文档

> 分支 `feature/ui-uxml`。本文档是本次 UI 重构的设计基准与验收依据。
> 调研来源：Celeste 反编译源码 [TheCyndaquilDecompilers/Celeste_Decompiled](https://github.com/TheCyndaquilDecompilers/Celeste_Decompiled)（`TextMenu.cs` / `OuiMainMenu.cs` / `OuiTitleScreen.cs` / `OuiFileSelectSlot.cs` / `OuiOptions.cs` / `MenuOptions.cs` / `MountainModel.cs`，逐文件核实），以及本项目现有 uGUI 实现全量审读。

---

## 1. 迁移总览

| 维度 | 现状 | 目标 |
|---|---|---|
| 渲染 | uGUI（UIBuilder.cs 编辑器脚本生成 prefab） | UI Toolkit：UXML 布局 + USS 主题，UIBuilder 删除 |
| 面板 | `BasePanel : MonoBehaviour` + prefab 槽位 | `ToolkitPanel` 纯 C# 类 + `Resources.Load` 加载 UXML |
| 手柄 | 自研 GamepadCursor 虚拟光标（277 行，模拟鼠标） | **焦点导航**（`:focus` 高亮 + 方向键/摇杆移动，蔚蓝原版方式），GamepadCursor 删除 |
| 字体 | 菜单 BombSlimeFonts.ttf / 计时器 LiberationSans | **全部统一 BombSlimeFont**（TextCore 经 `style.unityFont` 继承注入） |
| 风格 | 深蓝扁平卡片（UiPalette） | 《蔚蓝》：夜空深蓝底 + 白字黑描边 + 绿色高亮 + 明信片存档卡 |
| 旧文件 | 7 个 prefab + 8 个 uGUI 脚本 | 全部删除（git 历史可找回）；SceneFader / CinematicBars / InteractionPromptPart 世界空间部分**保留 uGUI 不动** |

**架构不变量**（刻意保留）：UIManager 仍是唯一门面（暂停状态机、Escape 轮询、UiBus/ItemBus/LifeBus 订阅、SoundCue 播放）；面板按名字查控件；所有设置读写仍走 `SettingsStore`/`SaveStore`/`BindingTools`——本次只换 UI 层皮，不动游戏逻辑。

## 2. 设计 tokens（USS custom properties，蔚蓝源码值）

| Token | 值 | 出处 |
|---|---|---|
| `--c-night` | `rgb(1,8,23)` #010817 | MountainModel 默认夜空 |
| `--c-summit` | `rgb(19,32,62)` #13203E | MountainModel 峰会 |
| `--c-farewell` | `rgb(40,26,53)` #281A35 | MountainModel 告别 |
| `--c-green` | `rgb(132,255,84)` #84FF54 | TextMenu.HighlightColorA（选中高亮） |
| `--c-green-press` | `rgb(90,170,60)` | --c-green × 0.7（按下加深，本项目自定） |
| `--c-yellow` | `rgb(252,255,89)` #FCFF59 | HighlightColorB（备用于强调） |
| `--c-header` | `rgb(72,61,139)` #483D8B | Header 蓝紫外圈 / 版本号 |
| `--c-gray` | `rgb(128,128,128)` | 分组标题 SubHeader |
| `--c-disabled` | `rgb(47,79,79)` #2F4F4F | 禁用项 |
| `--c-pink` | `rgb(219,112,147)` | 警示/失败提示 |
| `--c-card` | `rgb(252,240,245)` | 存档明信片米白 |
| `--c-ink` | `rgb(24,20,26)` | 卡片上的黑字 |
| `--c-dim` | `rgba(0,0,0,0.4)` | 覆盖层压暗（MenuOptions 40% 黑幕） |

字号层级（蔚蓝 2.0 / 1.5 / 1.0 / 0.8 / 0.6 / 0.5 比例，基准 38px）：
`--fs-title: 76px`（界面大标题）· `--fs-begin: 57px`（BEGIN）· `--fs-item: 38px`（菜单项）· `--fs-value: 30px`（选项值/滑条读数）· `--fs-sub: 23px`（分组标题）· `--fs-corner: 19px`（角落提示/版本号）。

描边：正文与菜单项 2px 黑描边（`-unity-text-outline`，对应蔚蓝 `DrawOutline 2f`）；大标题 3px 蓝紫外圈（对应 `DrawEdgeOutline 4f`，Toolkit 单层描边取近似）。

## 3. 签名交互（用户定稿，覆盖蔚蓝原版的闪色/抖动）

**选中（hover / 手柄焦点）＝浮现 + 小高亮**
- 浮现：`translate 0→-8px` + `scale 1→1.06` + 文字色 白→`--c-green`，0.18s ease-out（USS transition）
- 小高亮：项底部 44×3px 绿色短下划线淡入（0.15s）
- 失焦：回落归位、颜色回白，0.15s

**点击＝按下手感**
- 鼠标按下（`:active`）：下压至 `-2px` + `scale 0.96` + 颜色加深至 `--c-green-press`，0.08s
- 松开/提交：`UiFx.Bounce` 过冲回弹（0.06s 压到 0.94 → 0.14s BackOut 弹回 1.06）+ click 音效（手柄 Submit 无 ：active，靠 Bounce 补手感）

**其余动效（沿蔚蓝参数，C# 驱动，USS 无 @keyframes）**

| 动效 | 参数 | 出处 |
|---|---|---|
| 屏幕滑入 | 0.25s CubeOut（自右 +1920px；主菜单自左 -500px） | OuiOptions `p += dt*4` / OuiMainMenu TweenFrom |
| 屏幕滑出 | 0.25s CubeIn | 同上 |
| 存档卡入场 | 0.25s CubeInOut，逐卡 stagger 0.06s | OuiFileSelectSlot（原 0.02s，肉眼几乎不可见，放大为 0.06s） |
| 选项值左右切换 | ±8px 余弦衰减滑动 0.25s（3Hz） | Option.Render + ValueWiggler |
| 结算大标题 | 逐字下落：每字延迟 0.02s，-100px 落至 +60px 过冲回 0，挤压 (0.75,1.5)→(1,1)，0.5s | AreaCompleteTitle |
| 存档 toast | 1s 保持 + 0.4s 淡出（原 SaveIndicator 不变） | SaveIndicator |

## 4. 逐屏设计

### 4.1 MainMenu（蔚蓝 OuiMainMenu 布局）
```
┌────────────────────────────────────────────┐
│ BOMB SLIME            (76px 左上)           │
│                                            │
│ BEGIN                 (57px, 大按钮)        │
│ OPTIONS                (38px                │
│ EXIT                     菜单列 x≈150)      │
│                                            │
│ v-comp3151(19px 左下, 蓝紫)   Confirm Enter │
│                                Back Esc ↓右下
└────────────────────────────────────────────┘
```
- 背景：透明（露出游戏场景/相机背景），不再自带面板底色
- 进入：整列自左 -500px 滑入 0.25s（蔚蓝主菜单从左，其余屏从右）
- Play→存档菜单、Options→设置、Exit→退出（逻辑与旧 `MainMenuPanel` 一致）

### 4.2 SaveMenu（蔚蓝 OuiFileSelect 明信片风）
```
┌────────────────────────────────────────────┐
│      ╭──────────────────────────╮          │
│      │ SLOT 1                   │  ← 三张   │
│      │ LevelName · 12:34 · 3 deaths │ 米白卡 │
│      ╰──────────────────────────╯          │
│      ╭─────── 空卡: New Game ────╮          │
│      ╰──────────────────────────╯          │
│      ╭──────────────────────────╮          │
│      ╰──────────────────────────╯          │
└────────────────────────────────────────────┘
```
- 卡 980×150px 居中垂直排（间距≈310px 蔚蓝行距的压缩版），入场自右 stagger
- 选中卡：浮现上移（签名交互），米白底 + 3px 深色描边 + 10px 圆角
- **tap = 继续/新游戏；hold 1s = 提出覆盖**（底部绿色进度条填充），3s 内二次 tap 确认——逻辑与旧 `SaveMenuPanel`/`UiHoldButton` 完全一致（手柄只能 tap，与旧版相同）
- 无 Back 按钮（与旧设计一致，Escape 逐层退出）

### 4.3 PauseMenu
- 40% 黑幕 + 居中列：`PAUSED`(76px 蓝紫外圈) + RESUME / OPTIONS / SAVE & QUIT
- 进入：自右滑入 0.25s；按钮全走签名交互

### 4.4 Settings（**交互变更**：3 tab → 蔚蓝式单页分组滚动）
```
┌────────────────────────────────────────────┐
│                 OPTIONS (76px)             │
│  ┌─ ScrollView ───────────────────────┐    │
│  │ SOUND (23px 灰 subheader)          │    │
│  │  Mute            < ON  >          │    │
│  │  Main Volume    [────●────] 80%    │    │
│  │  Music Volume   [──●──────] 50%    │    │
│  │  SFX Volume     [──────●──] 70%    │    │
│  │ GRAPHICS                            │    │
│  │  Resolution      < 1920 x 1080 >   │    │
│  │  Fullscreen      < ON >            │    │
│  │  FPS Cap         < 60 >            │    │
│  │  FX Intensity   [────●────]        │    │
│  │  Show FPS        < OFF >           │    │
│  │  Perf Recording  < OFF >           │    │
│  │  Perf Rate       < 1 Hz >          │    │
│  │ CONTROLS                            │    │
│  │  Device          < KeyboardMouse > │    │
│  │  Rumble          < ON >            │    │
│  │  Mouse Sensitivity [─●─────] 1.0   │    │
│  │  Controller Sensitivity [─●─] 1.0  │    │
│  │  Move Up          W      (rebind)  │    │
│  │  … (KBM 9 行 / 手柄 6 行，随设备切换) │    │
│  │  [Reset Bindings]  [Unstuck]       │    │
│  └────────────────────────────────────┘    │
│                    RESET   BACK   (右下)   │
└────────────────────────────────────────────┘
```
- **全部旧设置项保留**：静音、三音量、分辨率/全屏/FPS 上限/FX 强度/显示 FPS/性能录制+频率、设备切换、震动、双灵敏度、键位重绑全表、Unstuck、Reset、Reset Bindings；数据层调用与旧 `SettingsPanel` 一一对应
- 开关类=蔚蓝 Option 行 `< 值 >`（左右方向键/点击/手柄 Submit 切换，值文字 ±8px 滑动）；音量/灵敏度=蔚蓝化滑条（8px 圆角轨道 + 白色圆形把手 + 2px 黑边，聚焦时把手变绿）
- 设备行切换 KeyboardMouse/Gamepad 分组（对应旧 Dpd_Device，枚举顺序一致）
- 打开定位到顶部；关闭时 `BindingTools.CancelActive()`（与旧一致）

### 4.5 Tutorial
- 40% 黑幕 + 居中页图（scale-to-fit）+ `1 / 2` 页码 + `< PREV` / `NEXT >` / `CLOSE`
- 翻页/结尾置灰/舞台冻结（输入锁 + 世界冻结 scoped owner locks）逻辑与旧版一致；页图来自新建 `TutorialPages` ScriptableObject（bootstrap 生成，路径 `Assets/Resources/UI/TutorialPages.asset`）

### 4.6 EndPanel（蔚蓝 AreaComplete 风）
- 大标题 `RUN COMPLETE` 逐字下落弹跳（0.2s 起始延迟 + 每字 0.02s）
- `Deaths  N` / `Total time  M:SS`（打开时 `SaveStore.SaveNow()` 后读取，与旧一致）
- `CONFIRM` 按钮 → ReturnToMainMenu

### 4.7 HUD
- 计时器：顶部居中 38px 白字黑描边（**换 BombSlimeFont**，仅 Playing 且非过场时走表——旧 GameTimer 逻辑不变）
- FPS：右上角 19px 白 85%（SettingsStore.ShowFps 控制显隐）
- 物品栏：右下 132×86 深色圆角框（图标 54×54 scale-to-fit + `n/m`）
- 存档 toast：右下物品栏上方 `Saved`，1s+0.4s 淡出（SaveStore.Saved 驱动）
- 整棵 HUD `pickingMode: Ignore`，不挡任何游戏点击

## 5. 输入与音效

| 操作 | 鼠标 | 键盘/手柄 |
|---|---|---|
| 选中 | hover（浮现+高亮） | 方向键/左摇杆/十字键移动焦点（UI Toolkit 原生 NavigationEvent，`:focus` 同款视觉） |
| 确认 | 左键点击（按下回弹） | Enter / 手柄 A（`UiFx.Bounce` 补手感） |
| 选项行切值 | 点击/悬停箭头 | 行上按 ←/→（`NavigationMoveEvent` 被 OptionRow 截获，不移动焦点——蔚蓝同款） |
| 返回 | — | Esc / Start（UIManager 轮询，逐层退出，逻辑不变） |

音效沿用 `UIManager` 的 4 个 SoundCue 槽位（hover/click/toggleOn/toggleOff）：hover=浮现音、click=确认音、toggleOn/toggleOff=选项行右/左切值（对应蔚蓝 `button_toggle_on/off`）。Toolkit 控件通过 `UiBus.RaiseHovered/Clicked/Toggled` 打回同一管线，RumbleManager 等订阅方不受影响。

## 6. 文件布局

```
Inform_comp3151/Assets/
├── Docs/UI-UIToolkit-Celeste-Design.md          ← 本文档
├── Resources/UI/                                ← 运行时 Resources.Load
│   ├── RuntimeTheme.tss                         ← 主题入口（import Theme.uss；不能与 .uss 同名：Resources.Load<StyleSheet> 无法区分 ThemeStyleSheet 与其基类）
│   ├── Theme.uss                                ← 全部设计 tokens + 组件样式
│   ├── MainMenu.uxml  SaveMenu.uxml  PauseMenu.uxml
│   ├── Settings.uxml  Tutorial.uxml  EndPanel.uxml  Hud.uxml
│   ├── BombSlimeFonts.ttf                       ← 从 Art/UI 复制（Resources 可加载）
│   ├── MainPanel.asset                          ← bootstrap 生成（.asset 而非 .panelsettings：Unity 6 禁止 CreateAsset 该类型；缺失时 UIManager 运行时自建兜底）
│   └── TutorialPages.asset                      ← bootstrap 生成（教程页图）
├── Editor/UIToolkitBootstrap.cs                 ← Tools > Inkform > UIToolkit Bootstrap
└── Code/UI/
    ├── UIManager.cs                             ← 改造（UIDocument + 焦点导航）
    └── Toolkit/
        ├── Easing.cs  UiFx.cs  OptionRow.cs  ToolkitPanel.cs  Hud.cs
        ├── TutorialPages.cs (SO)
        └── MainMenuPanel.cs  SaveMenuPanel.cs  PausePanel.cs
            SettingsPanel.cs  TutorialPanel.cs  EndPanel.cs     ← 全部重写
```

## 7. 验收清单

- [ ] 打开 Unity（首次自动/手动跑 `Tools > Inkform > UIToolkit Bootstrap`）后进入 Play：主菜单自动出现，无红色报错
- [ ] 全部文本渲染为 BombSlimeFont（含 HUD 计时器）
- [ ] 鼠标悬停菜单项：浮现上移 + 变绿 + 底部短下划线；按下：下压回弹手感
- [ ] 手柄/方向键在菜单间移动焦点，视觉与 hover 一致；行上 ←/→ 切换选项值且焦点不跳走
- [ ] 主菜单 → 存档（三卡右侧滑入 stagger）→ 开新档进游戏；存档卡 hold 1s 出覆盖确认、3s 内二次确认
- [ ] 游戏中 Esc → 暂停菜单（右侧滑入、40% 黑幕）→ Options（单页分组滚动、全部设置项可用、键位重绑生效）→ Back 逐层返回
- [ ] 拾取能力 → 教程页（翻页、关闭、游戏冻结）；进终点房 → 结算页（逐字标题 + 死亡/时间）
- [ ] HUD：计时器走表、FPS 开关、物品栏计数、`Saved` toast
- [ ] UI 音效四种均有声；UIManager 序列化的 cue 槽位保留不丢
- [ ] SceneFader / CinematicBars / 世界空间交互提示（uGUI）工作正常

## 8. 已知取舍

- 蔚蓝的 0.1s 绿黄闪色、±8px 选中抖动被用户定稿的「浮现+小高亮」取代（保留蔚蓝配色与缓动曲线）
- Toolkit 单层文字描边 → 大标题的「4px 蓝紫外圈 + 2px 黑边」双层描边近似为 3px 蓝紫单层
- 存档卡 stagger 0.02s→0.06s（原值肉眼不可辨）
- 手柄键位重绑行、存档卡 hold 仅鼠标可用（与旧版限制一致）
- 手柄焦点导航依赖 Unity 6 InputForUI 自动桥接；若目标平台异常，兜底方案是显式监听 `InputSystem_Actions` UI map 合成 NavigationMoveEvent（见 UIManager 注释）
