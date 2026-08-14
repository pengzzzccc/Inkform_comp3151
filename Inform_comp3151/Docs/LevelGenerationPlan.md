# 关卡生成·分步执行文档（Level Generation Plan）

> 文档日期：2026-08-14
> 适用范围：Inkform（Bomb Slime）2D 平台游戏，Unity 6000.4.10f1 / URP 17.4，分支 `dev`
> 用途：按本文档步骤，为 3 个关卡共 **22 个房间**生成场景（每个房间含简易平台跳跃挑战、房间名牌、指路门），实现一键配置工具，并完成关卡图完整接线，使玩家可从菜单一路逐门穿越全部房间。
> 执行方式：本文档为**分步执行清单**——每步给出目标、具体操作、验收标准；产物为 `Assets/Editor/RoomBuilder.cs` 工具与 `Assets/Scenes/Generated/` 下的生成内容。

---

## 一、背景与目标

- 现有手工关卡场景（`Assets/Scenes/Level1/Level1.unity`、`B1.unity`、`B2.unity`）为旧体系遗留产物，**本轮保留不动**，不并入新关卡图。
- 关卡图框架已就绪但未接线：`LevelFlow` 资产（`Assets/Scenes/All_level_Con.asset`）为空（`entryLevel: {fileID: 0}`、`levels: []`），`GameManager.prefab` 上 `SceneDirector.flow` 未赋值。
- 本轮目标产物：
  1. **一键配置工具** `Assets/Editor/RoomBuilder.cs`（`Tools/Inkform/Room Builder/` 菜单组）；
  2. **22 个房间场景**（`Assets/Scenes/Generated/`），每个房间布局相同、可独立在编辑器中 Play 测试；
  3. **关卡图接线**：22 个 `LevelScene` 资产 + `All_level_Con.asset` 填充 + 全部场景进入 Build Settings + `GameManager` 的 `SceneDirector.flow` 指向关卡图。

每个房间必须满足三条测试要求：

| 要求 | 实现方式 |
|---|---|
| 可玩（平台跳跃挑战） | 统一模板：平地带 → 跳过尖刺 → 两级台阶（高差 ≤ 跳高）→ 出口门在高台上 |
| 知道这是哪个房间 | 房间名牌（场景名 + 关卡名，世界空间 Canvas + uGUI Text） |
| 知道去往哪个房间 | 每邻居一个指路门，门上标注目标场景名 |

## 二、现有框架如何承接房间图

### 2.1 关卡图数据流（已核实，勿绕过）

```
LevelFlow (SO 资产 Assets/Scenes/All_level_Con.asset)
  ├─ mainMenuSceneName = "MainMenu"        （菜单场景名，index 0）
  ├─ entryLevel = L1_Player                （新游戏落点）
  └─ levels[] = 全部 22 个 LevelScene
        └─ LevelScene.sceneName + connections[]
              └─ LevelConnection { id, target }   （id 与场景内 LevelExit.exitId 拼写一致）
场景内: LevelExit 触发器 → LevelBus.Completed(exitId) → SceneDirector 解析 current.TargetOf(exitId) → LoadScene
```

- 关卡图类型：`Assets/Code/Level/LevelFlow.cs`（`levels`、`FindBySceneName`）、`LevelScene.cs`（`connections`、`TargetOf`）、`LevelExit.cs`（`exitId` 序列化字段）、`SceneDirector.cs`（全项目唯一 `SceneManager.LoadScene` 调用点，`GameManager.prefab` 上的单例）。
- `LevelExit.exitId` 必须与 `LevelConnection.id` 逐字节一致（`LevelExit.cs:13-14`）。本方案让二者**都由同一份邻居字符串生成**，天然一致。
- 场景名全局唯一：`SceneManager.LoadScene` 按名字匹配，且跨关存在重名节点（L1.B1 与 L2.B1），因此统一加关卡前缀：`L1_` / `L2_` / `L3_`。

### 2.2 关键几何事实（模板坐标的依据，勿凭印象改动）

| 项 | 值 | 证据 |
|---|---|---|
| 色块模型（默认图形） | 中心 (cx,cy)、尺寸 (w,h) ⇒ 顶面 y = cy+h/2，占位 x∈[cx-w/2, cx+w/2]；SpriteRenderer（白色精灵资产 tint 着色）+ BoxCollider2D（非 trigger） | — |
| 白精灵资产 | 工具首次运行时自动生成 `Assets/Art/Generated/WhitePixel.png`（2×2 白，PPU=2 ⇒ 1×1 世界单位）；**必须是持久化资产**——临时 `Sprite.Create` 精灵无法序列化进场景，重开后引用变 Missing、色块消失。勿用 `Resources.GetBuiltinResource<Sprite>`（Unity 6000 已移除 UI/Skin 内建精灵） | — |
| 标签（名牌/门牌） | 世界空间 **Canvas + uGUI Text**：世界高度 ≈ fontSize × canvasScale；uGUI 运行时自动解析字体材质。**勿用 legacy TextMesh**——`font.material` 序列化为内建默认材质引用（`{fileID: 10100, guid: 000…000}`），场景重开后文字消失，须手动重选字体 | 已核实 `L1_Player.unity` 的 `m_Materials` |
| 地面层 | 色块放 **layer 6 (Terrain)**——ContactSensor 地面掩码 `(1<<6)|(1<<11)`，否则玩家无法站立/起跳 | `Assets/Code/Player/ContactSensor.cs:24-27` |
| 玩家满跳高度 | `jumpSpeed=12`、`gravity=3` ⇒ 峰值 ≈ **2.45 单位** | `Assets/Code/Player/PlayerMotor.cs:21,27` |
| 台阶高差约束 | **任何台阶高差 ≤ 2**（留 0.45 余量） | 由跳高推导 |
| 相机 | MainCamera.prefab ortho size 5，z=-10；房间名义 20×11 | `Assets/Prefabs/Control/MainCamera.prefab` |

### 2.3 可复用预制体清单（工具按此路径加载）

| 用途 | 路径 |
|---|---|
| 管理器宿主（每房间一个实例） | `Assets/Prefabs/Control/GameManager.prefab`（guid 757e4b69744e90f429887cdce0fa416e） |
| 玩家（tag `Player`） | `Assets/Prefabs/Control/Player.prefab` |
| 相机（ortho 5，含 CamHandler/ScreenFx/FxDirector） | `Assets/Prefabs/Control/MainCamera.prefab` |
| 后处理（ScreenFx.volume 引用） | `Assets/Prefabs/env/GlobalVolume.prefab` |
| 检查点/出生点（`isStartPoint=true` 即出生点） | `Assets/Prefabs/env/Checkpoint.prefab` |

> 地面/台阶/尖刺**不再用预制体**（WallBase / Spike 已被默认色块取代，见 §4.1），仅由 `MakeBlock` 生成。

> **场景内接线（必做，`WirePlayerReferences`）**：GameManager 实例需场景覆盖 `InputHandler.player` → 本场景 Player 的 PlayerHandler、`AniHandler.animations` → 本场景 Player 的 Animator——预制体内的跨预制体引用（指向 Player.prefab 内部组件）加载后**不会自动重映射**到场景实例，否则 AniHandler 从不播放动画（动画机"坏"）；相机须为 Player 的**子对象**且 `CamHandler.target` = Player Transform，否则相机不跟随（对照可工作场景 `Level1.unity:22342-22349,22666`）。

> 注：`Assets/Editor/UIBuilder.cs:37` 的 `GameManagerPrefabPath = "Assets/Prefabs/GameManager.prefab"` 已过时，真实路径在 `Control/` 子目录下，**不要照抄该常量**。

## 三、房间清单与连接图（数据表）

### 3.1 关卡与命名

| 关卡 | 英文名 | 场景名前缀 | 房间 |
|---|---|---|---|
| Level 1 | a laboratory in a cave | `L1_` | Player（入口）、Boss、B1–B6、S1–S4 |
| Level 2 | Mountain tunnel | `L2_` | Boss、B1–B6、S1、S2 |
| Level 3 | Battle Corridor | `L3_` | LongFight |

**入口关卡：`L1_Player`**（`LevelFlow.entryLevel`）。

### 3.2 邻接表（sceneName → neighbors[]；门数 = 邻居数）

> 生成规则：以下每条无向边展开为双向两条有向连接；跨级边（加粗）已双向展开。共 22 房间、**58 条有向连接**（L1 14 边 + L2 12 边 + 跨级 3 边，各 ×2）。

| 房间（sceneName） | neighbors[]（同为 LevelScene.sceneName） |
|---|---|
| `L1_Player` | `L1_S1`, `L1_B1`, `L1_B5` |
| `L1_Boss` | `L1_B5`, `L1_B6`, **`L2_B1`** |
| `L1_B1` | `L1_Player`, `L1_B2`, `L1_B3`, `L1_S2` |
| `L1_B2` | `L1_B1`, `L1_S3` |
| `L1_B3` | `L1_B1`, `L1_B4`, `L1_B5`, `L1_B6` |
| `L1_B4` | `L1_B3`, `L1_S4` |
| `L1_B5` | `L1_Player`, `L1_B3`, `L1_Boss` |
| `L1_B6` | `L1_S3`, `L1_B3`, `L1_Boss` |
| `L1_S1` | `L1_Player` |
| `L1_S2` | `L1_B1` |
| `L1_S3` | `L1_B2`, `L1_B6` |
| `L1_S4` | `L1_B4`, **`L2_S1`** |
| `L2_Boss` | `L2_B6`, `L2_S2`, **`L3_LongFight`** |
| `L2_B1` | `L2_B3`, `L2_S1`, **`L1_Boss`** |
| `L2_B2` | `L2_S1`, `L2_S2` |
| `L2_B3` | `L2_B1`, `L2_B4` |
| `L2_B4` | `L2_B3`, `L2_B5`, `L2_S1`, `L2_S2` |
| `L2_B5` | `L2_B4`, `L2_B6`, `L2_S2` |
| `L2_B6` | `L2_B5`, `L2_Boss` |
| `L2_S1` | `L2_B1`, `L2_B4`, `L2_B2`, **`L1_S4`** |
| `L2_S2` | `L2_B4`, `L2_B5`, `L2_B2`, `L2_Boss` |
| `L3_LongFight` | `L2_Boss` |

最大邻居数 = 4（`L1_B3`、`L2_B4`、`L2_S2`），门位公式按此上限设计。

## 四、房间模板（共享骨架 + 挑战模板轮换 + 方向门位）

> 每个房间共享同一骨架（地面 + 出生点 + 4 个方向门位），但**挑战模板按房间序号 % 3 轮换**（V0/V1/V2），门的方位固定（右地面 / 右高台 / 左地面 / 顶部）。难度统一"简单"，便于识别与遍历测试。

### 4.1 共享骨架（世界坐标，房间原点 (0,0)）

| 对象 | 图形 | 尺寸 @ 中心坐标 | 说明 |
|---|---|---|---|
| `Floor` | 灰块 (0.5,0.5,0.5) | 22×1 @ (11, 1.5) | 顶面 y=2，地面带覆盖 x∈[0,22] |
| `Checkpoint` | Checkpoint（**isStartPoint=true**） | — @ (4.5, 2) | 出生点；RespawnDirector 开局传送到 `SpawnPos=(4.5, 2.5)` |
| `Player` | Player.prefab | — @ (4.5, 2.5) | 与出生点一致；tag `Player` |
| `GameManager` | GameManager.prefab | — @ (0, 0, 0) | 每房间一个实例（理由见 §8.4） |
| `GlobalVolume` | GlobalVolume.prefab | — @ (10, 5.5, 0) | 后处理 |
| `Main Camera` | MainCamera.prefab | — @ (10, 5.5, -10) | ortho size 5 |
| `RoomLabel` | Canvas + uGUI Text | — @ (2.5, 9.5, -1) | 房间名牌（见 §4.3） |

> 色块 = SpriteRenderer（工具生成的白色精灵资产 tint 着色）+ BoxCollider2D（非 trigger，尺寸与块一致）+ **layer 6 (Terrain)**；台阶灰 (0.65,0.65,0.65)，尖刺红 (0.85,0.2,0.2)。

### 4.1b 挑战模板（房间序号 % 3 轮换：0→V0，1→V1，2→V2）

| 模板 | 布局（M/RP/TP = 台阶，SP = 尖刺，全部 +2/级 ≤ 跳高 2.45） | 高/顶门位 |
|---|---|---|
| V0 单尖刺·三阶塔 | SP 3×2 @ (7,3)（x∈[5.5,8.5] 须跳过）；M1 4×1 @ (15,3.5)→y=4；RP 6×1 @ (19,5.5)→y=6；TP 4×1 @ (10,7.5)→y=8 | S1 (20.5,6.5)；S3 (10,8.5) |
| V1 双尖刺·三阶塔 | SP @ (6.5,3)（x∈[5,8]）+ SP @ (11.5,3)（x∈[10,13]，落点间隙 2）；塔同 V0 | 同 V0 |
| V2 台尖·四阶塔 | 无地面尖刺；M1 4×1 @ (14,3.5)→y=4；M2 4×1 @ (17,5.5)→y=6，其上 SP 3×1 @ (17.5,6)（攀登中跳过）；RP 6×1 @ (20,7.5)→y=8；TP 4×1 @ (10,9.5)→y=10 | S1 (20.5,8.5)；S3 (10,10.5) |

**平台跳跃挑战**：出生在平地带 → 跳过尖刺（V0/V1 在地面，V2 在台阶上）→ 登台阶塔 → 右高门；塔顶跳上顶部平台 → 顶部门。S0/S2 两扇地面门平走可达。

### 4.2 指路门（每邻居一个，按方向分布）

| 门位 | 位置 | 到达方式 | 门牌位置 |
|---|---|---|---|
| S0 右墙地面 | (20.5, 2.5) | 塔架下平走 | 门上 +2 |
| S1 右墙高台 | (20.5, 6.5 或 8.5，随模板) | 登台阶塔 | 门上 +2 |
| S2 左墙地面 | (1.5, 2.5) | 平走 | 门上 +2 |
| S3 顶部高台 | (10, 8.5 或 10.5，随模板) | 塔顶跳上 | **门下 -2**（留在屏幕内） |

- 邻居按 §3.2 表顺序取前 N 个门位（N = 邻居数 ≤ 4）。
- 每个门 = 空 GameObject `Door_<邻居场景名>`，挂：
  - `BoxCollider2D`：`isTrigger = true`，`size = (1.2, 2)`；
  - `LevelExit`：`exitId = 邻居场景名`（经 `SerializedObject.FindProperty("exitId")` 设置，`LevelExit.cs:22`）。
- 门牌：子对象 **Canvas + uGUI Text**，文本 = **纯场景名**（如 `L1_B1`，无箭头——BombSlimeFonts.ttf 无 `→` 字形），fontSize 36 / canvasScale 0.025（≈0.9 世界单位），z=-1。
- 门由 `LevelExit.OnTriggerEnter2D` 触发，`SceneDirector.OnCompleted` 解析后加载邻居场景；未知出口（图未接线时）回落主菜单并告警（`SceneDirector.cs:148-151`）。

### 4.3 房间名牌与门牌（世界空间 Canvas + uGUI Text）

- 标签 = 根对象挂 **Canvas（RenderMode.WorldSpace）**、`localScale = canvasScale`，子对象挂 **uGUI Text**：`font = LegacyFont`、`fontSize`、`alignment = MiddleCenter`、横纵 `Overflow`、`raycastTarget = false`、`color = white`，`RectTransform.sizeDelta = (600, 100)`（给足区域，文本居中）。
- 世界高度 ≈ **fontSize × canvasScale**：房间名牌 **48 / 0.05**（≈2.4 世界单位），门牌 **36 / 0.025**（≈0.9）。
- 文本两行 `"<场景名>\n<关卡英文名>"`（如 `L1_B1\na laboratory in a cave`），位置 (2.5, 9.5, -1)（z=-1 防遮挡）。
- 字体：`LegacyFont`（`Assets/Art/UI/BombSlimeFonts.ttf` → 回退 `LegacyRuntime.ttf` → `Arial.ttf`，同 `UIBuilder.cs:1041-1060`）。
- **为何不用 legacy TextMesh**：`font.material` 在编辑器返回非资产材质，保存场景时序列化为内建引用，重开场景文字消失、须手动重选字体；uGUI Text 运行时自动解析字体材质，无此问题（项目 UI 全部采用 uGUI Text 即为此机制）。
- 一律**纯 ASCII**：BombSlimeFonts.ttf 无非 ASCII 字形（如 `→`），缺字形渲染为空/方块。

## 五、一键配置工具规格（RoomBuilder.cs）

- 位置：`Assets/Editor/RoomBuilder.cs`；命名空间：`Inkform.EditorTools`；**全部 `public static`**（支持 `-batchmode -quit -executeMethod` 无头执行）。
- 风格对齐 `Assets/Editor/UIBuilder.cs`：AssetDatabase 按路径加载、`PrefabUtility`/`SerializedObject` 写序列化字段、`AssignIfEmpty` 保护手改赋值。

### 5.1 菜单项

| 菜单 | 方法 |
|---|---|
| `Tools/Inkform/Room Builder/Build All Rooms` | `BuildAllRooms()` |
| `Tools/Inkform/Room Builder/Build Level Graph` | `BuildLevelGraph()` |
| `Tools/Inkform/Room Builder/Wire GameManager Flow` | `WireGameManagerFlow()` |
| `Tools/Inkform/Room Builder/Fix Level Build Settings` | `FixBuildSettings()` |
| `Tools/Inkform/Room Builder/Build All` | `BuildAll()`（串联以上四个） |

### 5.2 房间数据表（工具内静态数据）

```csharp
private sealed class RoomData
{
    public string sceneName;   // 场景名 + LevelScene.sceneName + 门牌目标，三处共用
    public string levelName;   // 关卡英文名（名牌第二行）
    public string[] neighbors; // LevelScene.sceneName 数组（= 门数 = 连接数）
}
private static readonly RoomData[] Rooms = { /* §3.2 表逐行录入，共 22 行 */ };
```

### 5.3 BuildAllRooms（生成 22 个房间场景）

对 `Rooms` 每一行：

1. `EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()`，用户取消则整体 `return`；
2. `EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)`；
3. 逐个 `PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene)` 并按 §4.1 设置 `transform.position`：GameManager / Player / MainCamera / GlobalVolume / Checkpoint；地面/台阶/尖刺由 `MakeBlock` 生成默认色块（SpriteRenderer + BoxCollider2D + layer 6，§4.1b 三模板轮换）；实例化后调用 `WirePlayerReferences(gameManager, player, camera)` 完成场景内接线（§2.3）；
4. Checkpoint 实例：`SerializedObject.FindProperty("isStartPoint").boolValue = true` + `ApplyModifiedPropertiesWithoutUndo()`（同 UIBuilder.cs:920-935 模式）；
5. 按 §4.2 生成每个门（含 `LevelExit.exitId` 与门牌）、按 §4.3 生成房间名牌；
6. `EditorSceneManager.SaveScene(scene, $"Assets/Scenes/Generated/{sceneName}.unity")`；
7. 全部完成后 `AssetDatabase.SaveAssets()` + 打印 22 个场景路径。

> 场景就地覆盖保存（.meta/guid 保留）；**不改动**旧 `Assets/Scenes/Level1/` 下的手工场景。

### 5.4 BuildLevelGraph（生成 LevelScene 资产并填充 LevelFlow）

1. 确保目录 `Assets/Scenes/Generated/Levels/`（`AssetDatabase.IsValidFolder/CreateFolder`）；
2. 对每房间：**create-or-load** `LevelScene` 资产 `Assets/Scenes/Generated/Levels/<sceneName>.asset`（`ScriptableObject.CreateInstance<LevelScene>()` + `AssetDatabase.CreateAsset`；已存在则 `LoadAssetAtPath` 复用）。**`sceneName` 必须经 `SerializedObject` 写入**（直接字段赋值 + `SetDirty` 曾丢失该字符串，导致 `FindBySceneName` 永不匹配、关卡切换失效）；新建资产在 `CreateAsset` **之前**先赋值；
3. `connections` **每轮整体重建**：对每个邻居 N 生成 `new LevelConnection { id = N, target = LoadAssetAtPath<LevelScene>($"Assets/Scenes/Generated/Levels/{N}.asset") }`；
4. `LevelFlow`（create-or-load `Assets/Scenes/All_level_Con.asset`）：`entryLevel = L1_Player` 资产、`levels = Rooms 表序全部 22 个资产`、**不改 `mainMenuSceneName`**；
5. `EditorUtility.SetDirty` + `AssetDatabase.SaveAssets()`；
6. **自校验**：`SaveAssets` 后重载每个资产核对 `sceneName`，不符则 `Debug.LogError` 列出——任何持久化失败立刻可见，不再静默。

### 5.5 WireGameManagerFlow（GameManager → 关卡图）

```csharp
GameObject root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Control/GameManager.prefab"); // 勿用 UIBuilder 的过时常量
SceneDirector sd = root.GetComponent<SceneDirector>();
SerializedObject so = new SerializedObject(sd);
// AssignIfEmpty("flow", <All_level_Con 资产>) —— 空槽才填，手改赋值存活（同 UIBuilder.cs:965-971）
so.ApplyModifiedPropertiesWithoutUndo();
PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Control/GameManager.prefab");
PrefabUtility.UnloadPrefabContents(root);
```

### 5.6 FixBuildSettings（场景进 Build Settings）

- 目标列表：`MainMenu` 保持 **index 0**，其余既有条目按 path 去重保留，再**追加**缺失的 22 个 `Assets/Scenes/Generated/*.unity`。
- **不要调用 `UIBuilder.SetBuildSettings()`**（`UIBuilder.cs:1017-1034`：它会重建列表、只复制"当时的条目"，会丢新场景）；自行实现 append 式更新，保证与 UIBuilder 任意顺序运行都幂等。
- **Build Profile（Unity 6000）**：本项目 `Assets/Settings/Build Profiles/Windows.asset` 为 `m_OverrideGlobalSceneList: 1`，**覆盖全局场景列表**——只写 `EditorBuildSettings.scenes` 不够，`LoadScene` 仍报 "has not been added to the build settings"。`AppendRoomsToBuildProfile()` 需把 22 场景按 `m_path` 去重追加进每个覆盖型 profile 的 `m_Scenes`（`m_enabled/m_path/m_guid=AssetPathToGUID`）。

### 5.7 幂等性汇总

| 产物 | 幂等策略 |
|---|---|
| 房间场景 | `SaveScene` 就地覆盖（.meta/guid 不变） |
| LevelScene 资产 | load-then-mutate，缺失才重建 |
| connections | 每轮整体重建（数据表为准） |
| GameManager.flow | `AssignIfEmpty`（手改赋值存活） |
| Build Settings | 按 path 去重追加 |

文档口径（同 UIBuilder 类文档）：**生成物上的手改会被下一次构建覆盖**。

## 六、分步执行清单

| 步骤 | 操作 | 验收 |
|---|---|---|
| ① | 按 §5 实现 `Assets/Editor/RoomBuilder.cs`（数据表逐行抄录 §3.2） | Unity 编译通过，菜单出现 `Tools/Inkform/Room Builder/` 五项 |
| ② | 运行 `Build All Rooms` | `Assets/Scenes/Generated/` 出现 22 个场景；无报错 |
| ③ | 运行 `Build Level Graph` | `Assets/Scenes/Generated/Levels/` 22 个 asset；`All_level_Con.asset` 的 entryLevel/levels 已填 |
| ④ | 运行 `Wire GameManager Flow` | `GameManager.prefab` 的 SceneDirector.flow 指向 `All_level_Con` |
| ⑤ | 运行 `Fix Level Build Settings` | Build Settings：MainMenu 在 index 0，22 场景全部在列且无重复 |
| ⑥ | 验证（§7） | 全部通过 |

## 七、验证方案

1. **单房间可玩**：打开 `Assets/Scenes/Generated/L1_Player.unity` 按 Play → 玩家出现在出生点 → 名牌显示 `L1_Player / a laboratory in a cave`（**文字直接显示，无需重选字体**）→ 跳过障碍、登台阶塔 → 走进任意方向的门 → 对应邻居场景加载（如 `L1_B1`），新场景名牌正确。
2. **完整流程**：从 `MainMenu.unity` 开始 Play → New Game（存档槽任意）→ 落到 `L1_Player`。
3. **全图遍历**：按 §3.2 表逐门走通 22 房间；跨级门（`L1_Boss→L2_B1`、`L1_S4→L2_S1`、`L2_Boss→L3_LongFight` 及反向）双向可达。
4. **障碍与变体**：尖刺为实心红块，玩家不可穿过、须跳跃通过；相邻房间布局应不同（V0/V1/V2 按序号轮换）；S0/S2 地面门平走可达、S1/S3 需登塔。模板暂无敌意元素，**死亡重生验证待后续加入机关后补测**。
5. **幂等**：重跑 `Build All Rooms` 与 `Build Level Graph` → 无重复、无报错、`All_level_Con.asset` 连接数仍为 58。
6. **回程兜底**：若某门目标未接线（图异常），进入该门应回落主菜单而非卡死（`SceneDirector.cs:148-151` 行为）。

## 八、注意事项（坑）

1. **.meta 纪律**：不手删 `.meta`；场景/资产**就地覆盖**以保 guid，否则引用变 Missing（README 明确警告，`Inform_comp3151/README.md:109-113`）。
2. **场景必须进 Build Settings**：`SceneManager.LoadScene(name)` 按名匹配，未入 Build 的场景加载报 "has not been added to the build settings"（§5.6 解决）。
3. **exitId 拼写契约**：`LevelExit.exitId` 与 `LevelConnection.id` 必须逐字节一致（`LevelExit.cs:13-14`）。二者由同一 `neighbors` 字符串生成 ⇒ 天然一致；**改数据表后必须整跑 `Build All`**（场景与图同步重建）。
4. **每房间放 GameManager 实例（刻意为之）**：现有场景均含其实例；`SceneDirector.Awake` 单例去重（`SceneDirector.cs:34-38`）会在从菜单进入房间时自毁房间内副本，无冲突；而**编辑器直接打开房间 F5 测试时**，本房间的 GameManager 成为唯一实例，UIManager/Respawn 才能工作——验证步骤①依赖此设计。切场景瞬间短暂双实例靠 Awake 自毁，无害。
5. **InputHandler 不烘焙玩家引用**：`InputHandler` 在 Awake 与 sceneLoaded 时 `FindAnyObjectByType<PlayerHandler>()` 重绑玩家（`InputHandler.cs:90-93`），工具**绝不**在 GameManager 上写 player 引用。
6. **标签用 uGUI 而非 legacy TextMesh**：`font.material` 序列化为内建引用，场景重开文字消失、须手动重选字体（§4.3）；世界空间 Canvas + uGUI Text 是项目 UI 同款机制，运行时自动解析字体材质。
7. **遗留场景**：旧 `Level1/B1/B2.unity` 不并入新图；`B1.unity` 有历史 Missing 脚本引用，勿触碰。
8. **Build Settings 清洁**：`AnimationTest` 调试场景仍在 Build Settings，建议在出包前移除（README.md:103-105 已提示）。
9. **纯 ASCII 文本**：门牌/名牌一律 ASCII（自定义字体无 `→` 等非 ASCII 字形），需要指示符号时用 ASCII 替代。
10. **sceneName 持久化与 Build Profile**（关卡切换失效的两大隐蔽原因）：① `LevelScene.sceneName` 直接字段赋值曾丢失（全部资产空 sceneName，`FindBySceneName` 永不匹配）——必须经 SerializedObject 写入并保留工具自校验；② Unity 6000 下 `m_OverrideGlobalSceneList: 1` 的 Build Profile 覆盖全局场景列表——`LoadScene` 按名加载必须在 profile 的 `m_Scenes` 中也存在。
