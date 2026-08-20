# Inkform（Bomb Slime）项目评估报告

> 评估日期：2026-08-14
> 评估口径：完整游戏标准（加权多维度评分，满分 10）
> 评估方式：全项目只读调研（代码、资源、工程配置、git 历史），结论均附文件路径证据
> 对比基线：2026-07-29 历史评估 4.6/10（见 `.workbuddy/memory/2026-07-29.md`）

---

## 一、项目概览与技术栈

**Inkform** 是 COMP3151 课程 2D 平台游戏。玩家角色能吞下并吐出物体（炸弹等）引发连锁爆炸、用绳枪勾住地形摆荡移动、贴墙攀爬与吸附天花板；关卡由可爆破的墙、悬挂的链条、巡逻与旋转的机关、以及死亡后复原的检查点构成（`README.md:1-7`）。

| 项 | 值 |
|---|---|
| 引擎 | Unity **6000.4.10f1**（版本强约束，见 `README.md:13` 警告） |
| 渲染 | URP 17.4 · 2D Renderer（`Packages/manifest.json:16`） |
| 输入 | Unity Input System 1.19（`Packages/manifest.json:14`） |
| 目标平台 | Windows 独立版（1920×1080），构建配置 `Assets/Settings/Build Profiles/Windows.asset` |
| 可运行构建 | `Build/Inform_comp3151.exe`（2026-08-13 构建，含 D3D12 数据目录） |
| 体量 | C# 脚本 91 个（`Assets/Code` 89 + `Assets/Editor` 2）约 12,091 行；Prefab 24；Scene 7；贴图 39；SFX 16 + 9 SoundCue 资产；动画 22 个；Tile 资产 115 个 |
| 资源加载 | 无 Addressables / 无 Resources.Load，全为场景内引用 + 拖拽绑定 |

## 二、代码关系逻辑（架构）

### 2.1 入口与管理器

无 `GameManager.cs` 类——`GameManager` 是预制体宿主（`Prefabs/Control/GameManager.prefab`），挂载 8 个管理器：

- UIManager、SceneDirector、RespawnDirector、DeathDirector、LevelMemento、RumbleManager、AudioManager、InputHandler

其中 AudioManager 调用 `DontDestroyOnLoad`（`Assets/Code/Audio/AudioManager.cs:74`），使整套管理器跨场景存活。

### 2.2 事件总线（8 个静态类）

PlayerBus / LifeBus / ItemBus / HazardBus / FxBus / RopeGunBus / LevelBus / UiBus —— 项目解耦的核心机制。各系统不直接互相引用，而是发事件、订阅事件；"Director"类负责把总线事件翻译成具体效果：

```
事件源（玩家/机关/关卡对象）
   → 静态 Bus（PlayerBus / LevelBus / LifeBus / …）
      → Director 层（FxDirector / AudioDirector / DeathDirector / RumbleManager）
         → 具体效果（特效 / 音效 / 死亡 / 震动）
```

### 2.3 设计模式

| 模式 | 应用 | 证据 |
|---|---|---|
| 策略模式 | 死亡处理：DeathDirector 按 DeathCause 选 DeathStrategy | `Assets/Code/Life/DeathDirector.cs:39-51` |
| 备忘录模式 | 重生恢复：LevelMemento 捕获/恢复 IRestorable 对象快照，解决"碎墙死亡后不回来"的关卡问题 | `Assets/Code/Life/LevelMemento.cs:65-107` |
| 数据驱动 | 关卡拓扑：LevelFlow（SO 资产 `Assets/Scenes/All_level_Con.asset`）定义场景图与出口目标 | `Assets/Code/Level/LevelFlow.cs`、`SceneDirector.cs:29` |
| 单点门禁 | `SceneManager.LoadScene` 全项目唯一调用点，场景切换全部经由 SceneDirector | `Assets/Code/Level/SceneDirector.cs:125-128` |

关键流程：LevelExit 触发 `LevelBus.Completed` → SceneDirector 按 LevelFlow 拓扑解析出口目标加载下一关，死胡同则回主菜单（`SceneDirector.cs:137-155`）。

### 2.4 组件化框架

Interactable 节点 + 16 个 Parts 组件（HarmOnTouch、ExplodePart、ExplodeOnContact/Impact/Blast/AfterDelay、BreakablePart、RestorablePart、CarriablePart、FloatablePart、TimedVisibility、HangingChain、PatrolMover、Spinner、SolidSurface 等）组合出任意机关。近期提交已将原来的 HangWall / BreakableWall 单体脚本重构进该框架。

### 2.5 UI 与数据流

- **UI**：`UIBuilder`（`Assets/Editor`）程序化生成菜单场景与 UI 预制体；UIManager 是菜单唯一入口，四面板（MainMenu/Pause/Settings/SaveMenu）只回调查 UIManager。
- **运行时数据**：PlayerBus、LifeBus 与 LevelBus 发布玩家、死亡及关卡事实；四格 `InventoryStore` 保存稳定物品 ID 和选中格，ItemBus 只发布存入/吐出事实。
- **设置持久化**：PlayerPrefs（SettingsStore，键前缀 `Inkform.`，包含按键重绑定 JSON；旧小地图键会在加载时清理）。
- **进度存档**：SaveStore v2 保存关卡、出生点、死亡数与背包，使用临时文件、备份和原子替换；新游戏覆盖在入口场景首次记录进度后才提交。

## 三、质量信号

### 3.1 优点

- 无 TODO/FIXME/HACK 注释；注释异常详尽且解释 WHY（如 SceneDirector 延迟一帧广播的原因 `SceneDirector.cs:60-63`、LevelMemento 菜单首场景 bug 的来龙去脉 `LevelMemento.cs:44-52`）。
- 使用 Unity 6000.4 新 API（`FindObjectsByType` 的 includeInactive 重载替代已废弃的 sort 版本，`LevelMemento.cs:71-75`）。
- 空方法均为刻意设计（防叠音、占位扩展点）。
- 目录按域组织（`Assets/Code/` 13 个域），命名空间 Inkform.<域> 与目录对应。
- 版本控制规范：`.gitattributes` 配置 Unity YAML 合并策略与 Git LFS 二进制管理；`.meta` 随文件移动的约定写入 README 必读。

### 3.2 债务与风险

| 类别 | 说明 | 证据 |
|---|---|---|
| 文档死链 | README 引用的 `Docs/ProjectStructure.md`、`Docs/RefactorPlan.md` 已被提交 4a98141 删除；`Docs/` 现仅剩一张 UI 截图 | `README.md:51-53` |
| 文档过时 | README 称 Build Settings 首场景为 AnimationTest，实际为 MainMenu | `README.md:23-24` vs `EditorBuildSettings.asset:8-9` |
| 测试覆盖有限 | 已有 LevelGraph 纯逻辑测试与运行时边界 EditMode 测试；完整 PlayMode/压力测试仍依赖有效 Unity 许可证会话 | `Assets/Tests/Editor/`、`Assets/Editor/InkformRuntimeEdgeTests.cs` |
| 无 CI/CD | 无 `.github/`、无 pipeline 配置 | — |
| 死数据 | DeathCause.Blast/Void 无发布者；MusicVolume 无音乐系统；PlayerState.Eat/Release/Swing 已无对应动画分支，但 PlayerAnimBuilder 仍在生成这些剪辑 | `DeathCause.cs:14-15`、`SettingsStore.cs`、`AniHandler.cs` |
| 硬编码 | 图层号 6/11/13 分散多处；1920×1080 CanvasScaler 参数在 UIManager/GamepadCursor/FpsDisplay 手写重复（注释承认）；DiscSprite 生成逻辑两处重复 | `ContactSensor.cs:27`、`RopeGun.cs:47`、`SolidSurface.cs:23-24` |
| 构建滞后 | `Build/` 仅含 level0 单场景，未含当前 4 场景配置（MainMenu/AnimationTest/B2/Level1）；Build 目录被 .gitignore 忽略 | `EditorBuildSettings.asset:8-19` |

## 四、当前前进目标

- **进行中**：UI 系统 + 关卡流程/场景管理。最近提交 `4a98141`（2026-08-13）"Update : UI And base Level Control" 新增 UiBus/LevelBus/SceneDirector/LevelFlow/SettingsStore(413行)/SettingsPanel(461行)/UIBuilder(886行)/GamepadCursor/LevelExit；`852848b`（2026-08-14）"quick back up" 完成 Prefabs 重组（Control/Item/env）与关卡图资产重命名。
- **发展脉络**：地图完成 → Interactable 框架化重构（删单体脚本）→ UI/关卡流程。玩法内核已稳定，正在补"游戏外壳"。
- **待办线索**（代码与文档推断）：
  1. 真实存档系统（SaveMenu 桩 → 实现；`SceneDirector.cs:114` 注释 "future save" 的关卡选择入口）
  2. 重建被删的 ProjectStructure / RefactorPlan 文档（README 死链）
  3. 重新出包使 Build 包含全部当前场景
  4. 音乐系统接入（MusicVolume 设置位已就绪）
  5. 清理死数据与硬编码图层号

## 五、评分（完整游戏标准，加权满分 10）

| 维度 | 权重 | 2026-07-29 | 当前 | 主要依据 |
|---|---|---|---|---|
| 手感 / 核心玩法 | 20% | 8.5 | **8.5** | trauma 抖屏 / hitstop / URP punch / 13 态动画状态机 / 绳枪 Verlet 摆荡 / 输入优先级朝向修复 |
| 架构与代码质量 | 20% | 8.0 | **8.5** | 8 总线 + Director 翻译层、备忘录/策略/SO 数据驱动、组件化 Interactable、单点场景门禁；死代码与硬编码仍存 |
| 内容量 | 30% | 1.5 | **3.0** | 3 正式关卡（Level1/B1/B2）+ 菜单、24 Prefab、22 动画；无 Boss / 剧情 / 关卡选择 |
| 系统完整度 | 20% | 2.0 | **4.0** | 设置 / 按键重绑定 / 手柄光标 / UI 面板齐全；存档缺失、音乐未接入 |
| 工程规范（文档/测试/CI/构建卫生） | 10% | — | **4.5** | README 质量高但死链+过时；无测试、无 CI、构建滞后 |
| **加权总分** | 100% | **4.6** | **≈ 5.6** | 8.5×0.2 + 8.5×0.2 + 3.0×0.3 + 4.0×0.2 + 4.5×0.1 |

### 结论

较 2026-07-29（4.6 分）提升约 1 分，主要来自架构（Interactable 框架化、关卡流程、UI 系统落地）与内容（3 关卡成型）。**手感与架构达准专业水准（8.5）**，瓶颈在**内容量与系统完整性**——完成存档系统、重建文档、补内容、重新出包是提分最快的四条路径。按课程项目口径，当前处于"玩法与框架完成、游戏外壳（UI/流程/设置）刚就位、存档与内容量待补"的中期阶段。

## 六、可选跟进项（按优先级）

1. 重建 `Docs/ProjectStructure.md` / `Docs/RefactorPlan.md` 并修正 README 过时描述（低风险，恢复文档卫生）
2. 实现真实存档系统（LevelMemento 的 IRestorable 快照体系可扩展为持久化；打通 SettingsStore 键体系）
3. 接入音乐（MusicVolume 设置位已就绪）+ 清理死数据（DeathCause.Blast/Void、PlayerState 遗留枚举、PlayerAnimBuilder 冗余剪辑）
4. 用 Unity Test Framework 为 Bus / Director / LevelFlow 拓扑补冒烟测试
5. 重新出包使 Build 包含全部 4 场景，确认 Build Settings 首场景为 MainMenu

---

*本报告由全项目只读调研生成；文件路径证据基于 2026-08-14 仓库状态。*
