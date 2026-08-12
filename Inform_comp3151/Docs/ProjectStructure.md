# 项目结构与约定

> 最后整理：2026-08-12 · Unity 6000.4.10f1 · URP 2D

本文档描述 `Assets/` 的组织方式与团队约定。新增文件前先看一眼「该放哪」，
移动资产前务必看一眼「移动资产的铁律」。

---

## 目录职责

| 目录 | 放什么 | 不放什么 |
|---|---|---|
| `Animation/` | `.anim` 动画片段、`.controller` 状态机，以及生成它们的 `Sources/` 精灵图 | 非动画用的贴图 |
| `Art/` | 所有美术源文件：贴图、材质、Tile 资产、UI 图 | 预制体 |
| `Audio/` | `.wav` 音频源，以及引用它们的 `SoundCue` 资产 | 播放逻辑（在 `Code/Audio/`） |
| `Code/` | 全部运行时 C# 脚本，按域分子目录 | 编辑器专用脚本 |
| `Editor/` | 编辑器专用脚本（`UnityEditor` 依赖必须待在这里，否则出包编译失败） | 运行时逻辑 |
| `Fx/` | 特效数据资产：`FragmentCue`（碎裂外观）、屏幕特效配置 | 特效代码（在 `Code/Fx/`） |
| `Life/` | 死亡策略数据资产（`DeathStrategy_*`） | 生死逻辑（在 `Code/Life/`） |
| `Physics/` | `.physicsMaterial2D` 物理材质 | — |
| `Prefabs/` | **所有**预制体，无一例外 | 数据资产 |
| `Scenes/` | 场景。`Level*/` 为正式关卡，`Test/` 为调试场景 | — |
| `Settings/` | **仅** URP 渲染管线配置 | 任何非渲染配置 |

`Fx/` `Life/` `Physics/` 目前只有 1~4 个文件，看起来偏碎，但它们与 `Code/` 下的同名域
一一镜像——找「碎裂效果的数据」和找「碎裂效果的代码」路径对称。刻意不合并成
统一的 `Data/` 层，那会切断这层对应关系。

---

## 命名约定

**代码**：`Assets/Code/<域>/` 与命名空间 `Inkform.<域>` 严格一一对应。新增脚本必须
落在某个域目录下并声明对应命名空间。

| 目录 | 命名空间 | 职责 |
|---|---|---|
| `Code/Audio/` | `Inkform.Audio` | 事件→音效的翻译层 |
| `Code/Bus/` | `Inkform.Bus` | 静态事件总线，系统间解耦的唯一通道 |
| `Code/Fx/` | `Inkform.Fx` | 碎裂、震屏、相机 |
| `Code/Input/` | `Inkform.Input` | 输入系统（`InputSystem_Actions.cs` 为生成文件，勿手改） |
| `Code/Interactable/` | `Inkform.Interactable` | 可接触物框架主节点与接口 |
| `Code/Interactable/Parts/` | `Inkform.Interactable.Parts` | 可组合的行为 part |
| `Code/Item/` | `Inkform.Item` | 炸弹与链条（框架前的旧实现，见下方待办） |
| `Code/Level/` | `Inkform.Level` | 检查点、复活、生成器 |
| `Code/Life/` | `Inkform.Life` | 死亡策略、关卡快照复原 |
| `Code/Player/` | `Inkform.Player` | 玩家移动、动画、绳枪、搬运 |
| `Code/Tool/` | `Inkform.Tool` | 无依赖工具类 |
| `Editor/` | `Inkform.EditorTools` | 编辑器工具 |

**资产**：PascalCase，不含空格与下划线。`MovablePlatform.prefab` ✅ ／
`moveAblePlatfrom.prefab` ❌ ／ `Main Camera.prefab` ❌。

**音效资产**：`SFX_<主体><动作>_<序号>.wav`，如 `SFX_BombBlast_01.wav`。

---

## 移动资产的铁律

**`.meta` 必须与本体成对移动。**

Unity 靠 `.meta` 里的 GUID 而非路径来解析引用。只移动本体、丢下 `.meta`，
Unity 会为新位置生成一个全新 GUID，所有引用它的场景和预制体当场变成
"Missing (Mono Script)" —— 而且这种损坏在打开对应场景之前不会报错。

正确做法二选一：

1. **在 Unity 编辑器的 Project 窗口里拖动** —— Unity 自动处理 `.meta`
2. **`git mv` 两个文件**：
   ```bash
   git mv Assets/Old/Thing.prefab Assets/New/Thing.prefab
   git mv Assets/Old/Thing.prefab.meta Assets/New/Thing.prefab.meta
   ```

**绝不要在文件资源管理器里拖动 `Assets/` 下的任何东西。**

移动后自检：`git status` 里每一项都应显示为 `R`（rename）而非 `D`+`A`。

另外，`ProjectSettings/EditorBuildSettings.asset` 里硬编码了场景路径，
`Assets/Editor/PlayerAnimBuilder.cs:17-19` 硬编码了 `Assets/Animation/Player*`——
改动这些路径时要同批更新。

---

## 待接入清单

以下代码完整、注释详细，但**全项目零引用**（没挂在任何预制体/场景上）。不是垃圾，
是写完还没接进关卡的功能。清理时请先确认，别顺手删。

| 文件 | 是什么 |
|---|---|
| `Code/Player/ColliderHandler.cs` | 按 `PlayerState` 切换碰撞体尺寸/偏移，事件驱动无抖动 |
| `Code/Level/RopeRangeZone.cs` | 绳枪射程覆盖区域。**注意**：它是 `RopeGunBus.RangeOverride`/`RangeRestored` 的唯一生产者，删它会连带让 `Bus/RopeGunBus.cs` 整个类无人触发，且 `Player/RopeGun.cs` 的订阅与两个回调变成死代码 |
| `Code/Interactable/Parts/ExplodeAfterDelay.cs` | 延时引爆 part，为炸弹迁移预留（见 `RefactorPlan.md` 搁置项） |

零引用资产（同样只登记不删）：

| 资产 | 说明 |
|---|---|
| `Prefabs/Spike.prefab` | 无场景实例 |
| `Prefabs/WallBase.prefab` | 无场景实例 |
| `Prefabs/MainCamera.prefab` | 无场景实例（场景各自持有自己的相机） |
| `Prefabs/LongPrefabs/Bomb.prefab` | 无场景实例，同目录其余 6 个均在用 |
| `Scenes/Level1/B1.unity` | 不在 Build Settings，也没有任何 `SceneLoader` 指向它 |

`Scenes/Test/` 下的场景不在 Build Settings 属正常——它们供编辑器内手动调试用。

---

## 已知偏离

这些是明知不理想但本轮**刻意未动**的地方，改动前需与相关队友沟通。

**1. 作者命名的目录**

`Code/LongCode/` 与 `Prefabs/LongPrefabs/` 以队友名字（Viet Phi Long Le）命名，
而非按语义。应分别并入 `Code/Level/` 和 `Prefabs/`。

**2. 预制体资产分裂（优先级最高）**

`Prefabs/LongPrefabs/` 里的 `AllinoneBomb`、`Bomb`、`BreakableWall` 与根目录同名
预制体是**各自独立的分叉**——GUID 不同、内容已出现差异（`Bomb.prefab` 根目录版
209 行 vs 分叉版 254 行），并非副本。分叉版被 `Scenes/Level1/B2.unity` 实际引用。

后果：改根目录的炸弹不会影响 B2 关卡，反之亦然。两套资产会持续分歧。合并是
玩法层决策，需要在 Unity 里逐个核对字段差异，不是纯文件操作。

**3. `Code/LongCode/SceneLoader.cs` 无命名空间**

全项目唯一游离在 `Inkform.*` 之外的脚本，位于全局命名空间。归入 `Inkform.Level`
即可（改命名空间对场景引用安全，Unity 按 GUID 解析）。

**4. Build Settings 首个场景是测试场景**

`EditorBuildSettings.asset` 索引 0 是 `Scenes/Test/AnimationTest.unity`，意味着出包后
游戏从测试场景启动。正式出包前需把 `Level1.unity` 调到索引 0。

---

## 相关文档

- [`RefactorPlan.md`](RefactorPlan.md) —— **未执行的有效待办**，不是过期文档。
  其第二批 5 项（`ItemCarrier` 单源状态、墙滑双阈值显式化、`PlayerBus.FaceSign`、
  `Shatter.BurstAndHide`、`RespawnDirector` 归 `Life/`）与第三批 3-1
  （`Checkpoint`/`RopeRangeZone` part 化）截至本次整理**一项都未落地**，
  已逐条核实。
