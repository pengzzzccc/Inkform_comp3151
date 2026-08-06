# Inkform 程序架构文档

> 状态：草案（活文档，逐节评审修改后再进入实施）
>
> 目标：把本项目重构为「**一个与游戏引擎解耦的可扩展角色控制与游戏流程控制框架**」。
> 本作（Inkform）是该框架的第一个落地实现；未来可将其中的框架部分抽取为独立可复用包。

---

## 目录

1. [背景与现状](#1-背景与现状)
2. [框架愿景与设计原则](#2-框架愿景与设计原则)
3. [目标架构总览](#3-目标架构总览)
4. [核心层 Inkform.Core](#4-核心层-inkformcore)
5. [适配层 Unity Shell](#5-适配层-unity-shell)
6. [组件化协议](#6-组件化协议)
7. [数据驱动资产](#7-数据驱动资产)
8. [引擎迁移策略](#8-引擎迁移策略)
9. [测试策略](#9-测试策略)
10. [设计模式决策清单](#10-设计模式决策清单)
11. [实施路线图](#11-实施路线图)
12. [风险与对策](#12-风险与对策)
13. [开放问题（待讨论）](#13-开放问题待讨论)
14. [目标目录结构](#14-目标目录结构)
15. [重构实施计划](#15-重构实施计划)

---

## 1. 背景与现状

### 1.1 项目现状摘要

Unity 2D 平台跳跃游戏（绳索枪 + 炸弹解谜）。代码组织：`Assets/Code` 下分 `Bus / Player / Item / Level / Life / Fx / Audio / Input / Tool` 九个命名空间，约 55 个脚本。

当前架构为「事件驱动 + 分层 + 数据驱动」：

```
Input层   InputHandler ──(方法调用)──▶ PlayerHandler(门面/协调者)
                                          │ 按序驱动
                                          ▼
玩家子系统  ContactSensor → PlayerMotor → AnimStateResolver → (AniHandler/ColliderHandler/ItemCarrier/RopeGun)
                │                │              │
                ▼                ▼              ▼
总线层    PlayerBus  LifeBus  ItemBus  HazardBus  FxBus  RopeGunBus  ◀── 静态事件总线(带快照+去重)
                │                │              │
                ▼                ▼              ▼
导演层    DeathDirector  RespawnDirector  FxDirector  AudioDirector  ──▶ CamHandler/ScreenFx/AudioManager
                │
            DeathStrategy(SO资产)
数据层    SoundCue / FragmentCue / DeathStrategy / Chain.Settings / VerletRope.Settings (ScriptableObject/嵌套配置)
工具层    Timer/UnscaledTimer / VerletRope / Dir8 / RandomPick / Tags (纯逻辑，无生命周期)
```

优点：总线解耦干净（发布方不持有订阅者引用）、导演层职责单一（游戏事实→表现命令的翻译）、SO 资产集中调参、策略/备忘录扩展点成熟。

痛点：

| 编号 | 问题 | 具体表现 |
|---|---|---|
| P1 | PlayerHandler 是第二 God 类 | 加能力必须改它的输入入口 + InputHandler + 预制体接线；`SpitBomb` 已出现三层方向兜底链 |
| P2 | RopeGun 751 行 | 瞄准/弹道/预览/绳索/拉取/收绳/断链/抓炸弹 八项职责一锅炖 |
| P3 | 动画映射两份手工同步表 | `AniHandler` 的 switch 与 `PlayerAnimBuilder.Map` 必须手工一致 |
| P4 | 加一个 PlayerState 改 4 处 | 枚举、AnimStateResolver、AniHandler switch、ColliderHandler overrides |
| P5 | 静态总线全局化 | 6 个手工 Bus + 各自 ResetStatics；无法替换、难以测试 |
| P6 | 服务定位散落 | Bomb.Player / AudioManager.Listener / RespawnDirector.Deaths / AudioManager.Instance 四处手写懒缓存 |
| P7 | 场景扫描耦合 | LevelMemento 用 FindObjectsByType 收集 IRestorable，运行期不可增删 |
| P8 | 逻辑与引擎耦合 | 规则全部内嵌 MonoBehaviour，无法脱离 Unity 测试与复用 |

### 1.2 已识别的设计模式

**显式应用**：策略（DeathStrategy/DeathDirector）、备忘录（IMemento/IRestorable/LevelMemento）、外观（PlayerHandler）、对象池（AudioManager）、单例（AudioManager.Instance）、模板方法（ItemSuper.DropAt）、类型对象（SO 资产）、中介者/观察者（6 个静态 Bus）。

**隐式出现**：服务定位器（四处懒缓存）、轻量状态机（RopePhase/BombPhase/PlayerState）、责任链（动画优先级链、策略线性查表）、空对象（槽位留空静默跳过）、工厂（Chain/钩子/碎块的运行时创建）、原型（Instantiate）、适配器（InputHandler 包装 InputSystem）、命令（FxBus 命令语义）、值对象（DeathContext/Timer struct）、接口隔离（IDeathBody/IMemento/IRestorable）、构建者（编辑器期 PlayerAnimBuilder/RefactorMigration）。

**刻意规避**：动画推导用优先级链而非转移式状态机（AnimStateResolver 注释有论证，见开放问题 G）。

---

## 2. 框架愿景与设计原则

### 2.1 框架定位

一个「**与游戏引擎解耦的可扩展角色控制与游戏流程控制框架**」：

- **角色控制框架**：角色运动模拟、状态推导、能力/武器扩展、生命与死亡规则——与输入设备和渲染引擎无关。
- **游戏流程控制框架**：关卡加载、检查点/死亡/复活、关卡目标与切换、流程状态机——与场景管理方式无关。
- **引擎无关**：上述一切为纯 C# 核心，Unity 只是其中一个适配壳。
- **可扩展**：新能力、新武器、新角色、新相机行为、新关卡类型都是「新增组件/资产」，不改框架代码。

### 2.2 设计原则

1. **引擎无关**：Core 零 UnityEngine 依赖，编译为独立程序集（netstandard），由静态扫描/CI 强制校验。
2. **消息驱动**：规则层与表现层之间只走类型化事件/命令消息，不持有对象引用。
3. **组件化**：能力即组件、注册即接入；组合优于继承。
4. **数据驱动**：数值、映射、流程配置全部资产化；扩展以新建资产/组件为主，改代码为辅。
5. **可测试**：Core 为纯逻辑，输入可录制回放，行为可逐帧比对。
6. **框架与内容分离**：框架层（可复用）与游戏内容层（本作规则）在程序集层面分开。
7. **行为优先于结构**：每个重构里程碑独立可验证、可回滚，手感（行为）不漂移。

---

## 3. 目标架构总览

```
┌─────────────────────── Unity Shell (适配层, 可整层替换) ───────────────────────┐
│  InputAdapter   PlayerShell/EnemyShell(角色壳)   GameRoot(组合根/装配)          │
│  PhysicsPort    FxPort   AudioPort   CameraRig(行为组件组)   LevelShell         │
└──────┬──────────────────────────────────┬──────────────────────────────────────┘
       │ 输入结构体(引擎无关)               │ 命令/事件消息(引擎无关)
       ▼                                  ▼
┌─────────────────────── Inkform.Core (纯逻辑, 引擎无关) ────────────────────────┐
│  Framework(框架, 可复用):                                                       │
│    Ports(ITimePort/IRngPort/IPhysicsPort/IEventChannel...)                      │
│    Events(EventChannel<T>)   FlowMachine(流程状态机)                            │
│    CharacterProtocol(角色协议)   IAbility/IWeapon   LifeRules(死亡策略)         │
│  Gameplay(本作内容):                                                           │
│    PlayerSim(运动学+状态推导)   RopeSim(Verlet去Unity化)   BombSim/ChainRules   │
│    LevelConfig 流程配置    AnimProfile 数据                                   │
└─────────────────────────────────────────────────────────────────────────────────┘
```

### 3.1 消息流

- **上行（事实）**：Shell 采集输入/接触/碰撞 → 转成 Core 输入结构体 → 发给 Core 模拟。
- **下行（命令）**：Core 模拟产出状态与事件 → 经 `EventChannel<T>` 广播 → Shell 表现组件翻译成渲染/音频/特效/相机命令。
- 规则决策（跳不跳得起来、炸不炸、死不死、流程切不切）全部在 Core 内完成；Shell 不写规则。

### 3.2 程序集布局

| 程序集 | 命名空间 | 依赖 | 用途 |
|---|---|---|---|
| `Inkform.Core.Framework` | `Inkform.Core` | 仅 .NET（System.Numerics） | 框架：端口、事件、流程机、角色协议、能力/武器接口、生命规则 |
| `Inkform.Core.Gameplay` | `Inkform.Core.Gameplay` | 仅 Framework | 本作内容：PlayerSim/RopeSim/BombSim/ChainRules/关卡配置数据 |
| `Inkform.Shell`（现 Assets/Code） | `Inkform.Shell.*` | Unity + 两个 Core | 适配层：MonoBehaviour 壳、端口实现、组合根 |
| `Inkform.Core.Tests` | — | Core + NUnit | 单元测试（Unity Test Runner 内运行；Core 无引擎依赖，将来可加 csproj 用 dotnet test 纯 CLI 运行） |

> 框架与内容分离的意义：将来把 `Inkform.Core.Framework` 提为独立可复用包时，本作的 Gameplay 与 Shell 都不受影响；另一个游戏可直接依赖 Framework 实现自己的 Gameplay + Shell。

---

## 4. 核心层 Inkform.Core

### 4.1 基础设施：端口（Ports）

所有引擎能力经端口访问，Core 只认识接口。端口由 Shell 实现、由组合根装配。

```csharp
namespace Inkform.Core
{
    // 时间端口：可缩放/非缩放双时钟（hitstop 语义依赖）
    public interface ITimePort
    {
        float Time { get; }
        float UnscaledTime { get; }
        float DeltaTime { get; }
    }

    // 随机端口：可注入种子，便于测试复现
    public interface IRngPort
    {
        float Next01();
        float Range(float min, float max);
        int Next(int max);
    }

    // 物理端口：引擎物理能力的抽象（2D 平台跳跃所需的最小面）
    public interface IPhysicsPort
    {
        ContactFlags Probe(ContactQuery query);              // 四向接触检测
        void SetVelocity(Vector2 v);
        void AddVelocity(Vector2 dv);
        void SetGravityScale(float scale);
        void SetSimulated(bool on);
        void Teleport(Vector2 pos);
        bool CastCircle(Vector2 from, float radius, Vector2 dir, float dist,
                        LayerKey mask, out Vector2 hitPoint); // 绳索/射线检测
    }

    // 其他候选端口：ISavePort（存档）、IDebugPort（调试绘制）——按需后补
}
```

> 开放问题 E：物理端口的面（探测/射线/碰撞回调事件）需要多细，见 13 节。

### 4.2 事件系统 EventChannel\<T\>

替代现有 6 个手工静态总线。保留全部既有语义：**去重、快照（供迟到订阅者首次同步）、订阅令牌（IDisposable）、ResetStatics（防 Domain Reload 残留）**。

```csharp
namespace Inkform.Core
{
    /// <summary>瞬时事件通道：去重由 Raise 侧保证或由事件自身相等性决定。</summary>
    public static class EventChannel<T>
    {
        public static IDisposable Subscribe(Action<T> handler);
        public static void Raise(in T ev);
    }

    /// <summary>带快照的事件：订阅即读当前值完成首次同步。</summary>
    public interface ISnapshotEvent<T>
    {
        T Snapshot { get; }
    }
    public static class SnapshotChannel<T> where T : ISnapshotEvent<T> { /* ... */ }
}
```

迁移方式：现有 `LifeBus.Died` 等改为 `EventChannel<DeathContext>.Died` 的静态别名，调用点一字不改（见里程碑 M1）。

> 开放问题 B：用泛型静态通道还是实例化聚合器（可替换、可注入、可测），见 13 节。

### 4.3 角色控制框架（Framework 部分）

#### 4.3.1 角色数据模型（引擎无关的结构体）

```csharp
public enum PlayerState { Idle, Move, JumpUp, Rise, Fall, Land, Eat, Release,
                          CeilingStick, CeilingMove, WallSlideL, WallSlideR, CeilingIdle, Swing }
public enum FaceDirection { L, R }

[Flags] public enum ContactFlags { Ground = 1, LeftWall = 2, RightWall = 4, Ceiling = 8 }

public readonly struct InputState
{
    public readonly Vector2 Move;
    public readonly bool JumpPressed;   // 按下沿
    public readonly bool JumpHeld;
    public readonly bool DashPressed;
    public readonly Vector2 AimDelta;   // 瞄准（绳索枪等武器使用）
}

public readonly struct CharacterSnapshot
{
    public readonly Vector2 Position, Velocity;
    public readonly ContactFlags Contact;
    public readonly PlayerState State;
    public readonly FaceDirection Face;
}
```

#### 4.3.2 角色模拟接口与实现

```csharp
/// <summary>角色模拟器：引擎无关的运动学 + 状态推导。实现者不接触任何引擎类型。</summary>
public interface ICharacterSim
{
    CharacterSnapshot Tick(in InputState input, float dt);
    void Teleport(Vector2 pos);
    void Reset();                       // 复活/关卡重开
}

/// <summary>玩家实现：跳跃缓冲、可变跳高、墙跳、冲刺、贴顶反相计时、
/// 推导式动画优先级链 —— 现有 PlayerMotor + AnimStateResolver 的判定逻辑整体平移。</summary>
public sealed class PlayerSim : ICharacterSim { /* ... */ }
```

行为验证：现有 PlayerHandler 按「感知 → 运动 → 动画」顺序驱动；Core 化后由 `PlayerShell` 按「Sense → Simulate → Present」三相位驱动（见 6.1）。

### 4.4 游戏流程控制框架（Framework 部分）

#### 4.4.1 流程状态机

```csharp
public enum FlowState { Boot, Menu, LoadLevel, Playing, Respawning, Paused, Goal }

/// <summary>流程机：引擎无关。输入为事实事件，输出为流程命令。</summary>
public sealed class FlowMachine
{
    public FlowState State { get; }
    public void Tick(float dt);                                    // 超时/条件推进
    public void OnDied(in DeathContext ctx);                       // 死亡 → Respawning
    public void OnCheckpointSet(Vector2 pos);                      // 记录复活点
    public void OnRespawnComplete();                               // 复活完成 → Playing
    public void OnGoalReached(string levelId);                     // 过关 → 下一关
    public event Action<FlowCommand> CommandIssued;                // 命令下行
}
```

命令示例：`LoadLevelCommand`、`RespawnCommand`、`PauseCommand`、`GameOverCommand`。Shell 的 GameRoot 订阅命令并执行实际的场景/预制体操作。

#### 4.4.2 关卡配置（数据驱动）

```csharp
public sealed class LevelConfig   // 纯数据，Shell 侧用 ScriptableObject 承载同一内容
{
    public string LevelId;
    public Vector2 StartSpawn;
    public string NextLevelId;
    public Bounds2D CameraBounds;      // 相机边界
    public string[] RestorableTags;    // 参与快照还原的物件类型（可选，替代全量扫描）
}
```

#### 4.4.3 生命与死亡规则

- 死亡策略（Strategy 模式）：`DeathStrategy`（`Cause`、`RespawnDelay`、`Execute(ctx, IDeathBody)`）从现有代码平移为纯 C# 抽象 + 本作实现（碎裂演出参数化为表现命令，而非直接调引擎 API）。
- 策略执行产出「表现命令」列表（碎块规格、震屏参数、音效 Cue 引用、hitstop 时长），由 Shell 翻译执行。

#### 4.4.4 可还原（Memento）

- `IMemento` / `IRestorable` 接口保留；原发者改为 OnEnable/OnDisable 自注册（对齐 `Chain.Active` 既有模式），`LevelMemento`（Caretaker）从注册表收集，不再场景扫描。

### 4.5 模拟服务（本作内容层）

| 模块 | 内容 | 去 Unity 化方式 |
|---|---|---|
| `VerletRope` | 绳索解算 | `Vector2`→`System.Numerics`；`Rigidbody2D`→`IAttachedBody`；`Physics2D.CircleCast`→`IPhysicsPort.CastCircle` |
| `RopeSim` | 绳索枪状态机（瞄准/飞行/拉取/收绳/抓炸弹）+ 弹道解算 | 现有 RopeGun 的 RopePhase 与数值逻辑整体平移 |
| `BombSim` | 引信/免疫期/速度爆炸/连锁引信规则 | 纯规则；接触判定输入由 Shell 提供 |
| `ChainRules` | 断链判定（爆炸波及/钩子近距） | 纯几何 + 输入事件 |
| `Shatter` | 网格切分 | 纯函数，输出碎块描述数组 |

---

## 5. 适配层 Unity Shell

### 5.1 端口实现

| 端口 | Shell 实现 | 说明 |
|---|---|---|
| `ITimePort` | `UnityTimePort` | 包 `Time.time / unscaledTime / deltaTime` |
| `IRngPort` | `UnityRngPort` | 包 `UnityEngine.Random` |
| `IPhysicsPort` | `RigidbodyPort` | 包 Rigidbody2D + Physics2D.OverlapCircle + CircleCast；挂角色物体上 |

### 5.2 组合根 GameRoot（替代 GameManager）

职责：
- 构建并持有 `ServiceRegistry`（Time/Rng/Physics 端口、FlowMachine、EventChannel 实例）。
- 装配场景（找到角色壳、关卡壳并注入端口）。
- 订阅流程命令（加载关卡、复活、暂停）。
- 负责生命周期与场景切换。

`ServiceRegistry` 集中现有四处手写懒缓存（Bomb.Player / AudioManager.Listener / RespawnDirector.Deaths / AudioManager.Instance），并提供统一的「懒查找 + fake-null 自动重找」语义。

### 5.3 角色壳

```csharp
/// <summary>角色壳：持有 ICharacterSim 实例，把 Unity 输入翻译成 InputState 并驱动三相位。</summary>
public class PlayerShell : MonoBehaviour
{
    private ICharacterSim sim;                      // PlayerSim，由组合根注入
    private readonly List<IPlayerModule> modules;   // 能力模块（自注册）
    private IPhysicsPort physics;

    void Update() { CollectInput(); DrivePhases(); BroadcastState(); }
}
```

- 敌人的壳 = `EnemyShell` + 不同的 `ICharacterSim`/控制器（AI），共享同一套生命/死亡/复活管线（见 6.2）。

### 5.4 表现壳

| 壳组件 | 职责 | 对应现状 |
|---|---|---|
| `FxPort` | 订阅 Core 特效命令 → FxBus（或直接调 CameraRig/ScreenFx） | FxDirector |
| `AudioPort` | 订阅 Core 音频命令 → AudioManager | AudioDirector |
| `CameraRig` | 行为组件组（见 6.4） | CamHandler + ScreenFx |
| `AniPresenter` | 订阅状态广播 → 按 AnimProfile 播放动画 | AniHandler |
| `BodyPresenter` | 订阅状态广播 → 设置碰撞体 size/offset | ColliderHandler |
| `ItemShell` | 物品外壳（ItemSuper 等），规则归 Core | Item 命名空间 |

---

## 6. 组件化协议

### 6.1 能力模块协议（玩家能力扩展点）

```csharp
public enum PlayerPhase { Sense, Simulate, Present }   // 保持现有「感知→运动→动画」顺序语义

public interface IPlayerModule
{
    void OnPhase(PlayerPhase phase, float dt);          // 模块按注册序在相位内执行
    bool ConsumeAction(PlayerAction action, in ActionArgs args);  // 输入分发
}
```

`PlayerShell` 只做：收集输入 → 依次执行三相位 → 广播状态。`RopeGun`、`ItemCarrier`、未来新能力全部改为实现该协议并自注册——**加能力零改动 PlayerShell**。

### 6.2 角色通用协议（敌人/NPC/多角色）

```csharp
public interface ICharacter
{
    ICharacterSim Sim { get; }         // 运动模拟（玩家=PlayerSim，敌人=EnemySim/AI 驱动）
    IHealth Health { get; }            // 生命：血量/死亡事件（策略分发到 IDeathBody）
    IContact Contact { get; }          // 四向接触（共享组件）
    IPresenter Presenter { get; }      // 表现：AnimProfile 驱动的动画/体型
}
```

- `Spike` 等危险物改为对 `ICharacter` 生效（不再 CompareTag("Player")）。
- 敌人行为差异用「控制器」注入：`PlayerController`（输入）/ `IAIController`（AI，开放问题 D 讨论抽象程度）。

### 6.3 武器协议

```csharp
public readonly struct AimInfo { public Vector2 Origin; public Vector2 Direction; public Vector2 Target; }

public interface IWeapon
{
    string Id { get; }
    bool TryFire(in AimInfo aim);
    void Cancel();
    void Tick(float dt);               // 冷却/飞行动画等
}
```

- `RopeGunShell`（瞄准/预览 + RopeSim 驱动 + 渲染）与 `SpitBomb` 都是武器实现。
- `WeaponSlot` 组件管理切换、冷却与空手兜底（替换现有 SpitBomb 的三层方向兜底链）。
- RopeGun 按此拆为：`RopeAimer`（瞄准/预览）→ `RopeSim`（Core 状态机）→ `RopeRenderer`（LineRenderer 渲染壳）。

### 6.4 相机行为组件（相机移动控制）

```csharp
/// <summary>相机行为：可插拔，Inspector 顺序即优先级。</summary>
public abstract class CameraBehavior : MonoBehaviour
{
    public abstract void Apply(in CameraContext ctx);
}

public readonly struct CameraContext
{
    public Vector2 TargetPos;          // 跟随目标（角色）
    public FaceDirection Face;
    public float DeltaTime;
    public bool Dead;                  // 死亡/复活瞬移等状态
}
```

`CameraRig` 持有行为列表：`FollowBehavior`（跟随+前瞻）、`ShakeBehavior`（trauma 抖动）、`ZoomBehavior`（punch）、`ClampToLevelBoundsBehavior`（关卡边界）、`SnapBehavior`（复活瞬移吸附）。增删相机玩法 = 增删组件，不碰 CameraRig。

### 6.5 其他既有协议（平移保留）

| 接口 | 模式 | 说明 |
|---|---|---|
| `IDeathBody` | 接口隔离 | 尸体操作面（VisualBounds/Hide/Show） |
| `IMemento` / `IRestorable` | 备忘录 | 改自注册表收集 |
| `IPlayerModule` / `IWeapon` / `ICharacter` | 组件化 | 新引入 |
| `IAbility`（可选） | — | 通用非武器能力（如二段跳升级），武器视为一种能力（开放问题） |

---

## 7. 数据驱动资产

| 资产 | 现状 | 目标 |
|---|---|---|
| `SoundCue` | ✓ | 平移为纯数据（播放命令由 AudioPort 翻译） |
| `FragmentCue` | ✓ | 平移为纯数据（碎块规格由 FxPort 翻译） |
| `DeathStrategy`（SO） | ✓ | Core 抽象 + Shell 承载（SO 序列化）+ 表现命令化 |
| `AnimProfile`（新） | ✗ | 合并三张表：`PlayerState → clip 基名/方向规则/flip 规则/碰撞体 size+offset`。AniPresenter / BodyPresenter / 动画构建工具读同一资产。加状态或换皮 = 新资产 |
| `LevelConfig`（新） | ✗ | 关卡流程配置（出生点/相机边界/下一关/可还原清单） |
| `FlowSettings`（新） | ✗ | 流程机参数（死亡停顿、加载时长等） |

---

## 8. 引擎迁移策略

1. **程序集隔离**：`Inkform.Core.Framework` / `Inkform.Core.Gameplay` 独立 asmdef，目标 netstandard；CI/编辑器脚本扫描断言 Core 内无 `UnityEngine` 引用。
2. **行为回归保险（录制回放）**：录制玩家输入序列；迁移每个模块后回放比对轨迹与状态序列，防手感漂移（跳跃缓冲、贴顶反相、Verlet 细节是高风险区）。
3. **移植模板**：Shell 内每处引擎 API 映射到端口：

   | 引擎 API | 端口/消息 |
   |---|---|
   | `Rigidbody2D` 读写 | `IPhysicsPort` |
   | `Physics2D.OverlapCircle/Raycast/CircleCast` | `IPhysicsPort` |
   | `Time.time / unscaledTime` | `ITimePort` |
   | `UnityEngine.Random` | `IRngPort` |
   | `SpriteRenderer/Animator` | 表现命令消息 → 新引擎的渲染壳 |
   | `OnTrigger/OnCollision` 回调 | 碰撞事实消息 → Core 规则输入 |

4. **迁移演练（M6）**：用 Godot 或 MonoGame 搭最小壳，复用 Core（输入→模拟→简单渲染），验证「换引擎只换 Shell」。

---

## 9. 测试策略

| 层 | 测试方式 | 覆盖对象 |
|---|---|---|
| Core 单元测试 | NUnit（Unity Test Runner 内跑；Core 无引擎依赖，将来可加 csproj 用 dotnet test 纯 CLI 跑） | Timer 时钟语义、跳跃缓冲/墙跳/贴顶推导、Verlet 收敛、Bomb 引信/连锁、FlowMachine 状态迁移、Shatter 网格 |
| 行为回归 | 输入录制回放比对 | PlayerSim 迁移后的轨迹/状态序列与旧版一致 |
| Shell 集成 | 现有手动玩法验证 + 场景回归清单 | 端口桥接正确性 |
| 引擎迁移演练 | M6 双引擎跑通 | 框架层的可移植性 |

---

## 10. 设计模式决策清单

**沿用**（已验证有效）：策略（死亡演出）、备忘录（关卡还原）、类型对象（SO）、对象池（音频/数组预分配）、模板方法（ItemSuper）、接口隔离、空对象（槽位留空静默跳过）、值对象（struct 事件 + in 传参）、责任链（动画优先级、查表）。

**改造**：

| 现状 | 目标 | 理由 |
|---|---|---|
| 6 个手写静态 Bus | `EventChannel<T>` 泛型通道 | 新事件不再新建类；保留去重/快照/Reset 语义；可注入实例（测试） |
| 4 处散落懒缓存 | `ServiceRegistry` | 单一查找点，组合根统一装配 |
| FindObjectsByType 扫描 | 自注册表 | 与 Chain.Active 统一，运行期可增删 |
| AniHandler switch + AnimBuilder 两份表 | `AnimProfile` 单表 | 消灭手工同步；支持换皮/多角色 |
| PlayerHandler God 门面 | 模块注册 + 三相位 | 新能力零改动 |
| 规则内嵌 MonoBehaviour | Core 纯逻辑 + 端口 | 引擎解耦、可测试、可复用 |

**新引入**：端口-适配器（Hexagonal）、组合根（GameRoot）、能力模块（IPlayerModule）、武器协议（IWeapon）、角色协议（ICharacter）、相机行为组件（CameraBehavior）、流程状态机（FlowMachine）、框架/内容程序集分离。

---

## 11. 实施路线图

> 每里程碑独立可验证、可回滚；M0→M1→M2 为硬依赖，M3 之后可并行/调整顺序。

| 里程碑 | 内容 | 验证门 |
|---|---|---|
| **M0** | 建 Core.Framework/Core.Gameplay asmdef + 测试骨架；Timer/Dir8/RandomPick 入 Core；VerletRope 去 Unity 化 | Core 单测全绿；游戏运行无行为变化 |
| **M1** | `EventChannel<T>` 泛型事件；现有 6 个 Bus 改为别名保持调用点不变 | 功能回归 |
| **M2** | 抽取 `PlayerSim`（运动学 + 状态推导入 Core）；`PhysicsPort` 桥接；PlayerShell 三相位 | 输入录制回放轨迹/状态比对一致 |
| **M3** | `IPlayerModule` 注册；`IWeapon`（RopeGun 拆分、SpitBomb 武器化）；ServiceRegistry | 新增假武器 demo 零改动接入 |
| **M4** | `ICharacter` 通用化；`AnimProfile` 资产；敌人原型 | 敌人走同一生命/死亡管线 |
| **M5** | `FlowMachine` + `LevelConfig`；`CameraRig` 行为组件 | 双关卡流程 + 关卡相机边界 |
| **M6** | 引擎迁移演练：Godot/MonoGame 最小壳复用 Core | Core 在第二引擎内跑通角色模拟与流程 |

---

## 12. 风险与对策

| 风险 | 对策 |
|---|---|
| Verlet/物理手感迁移漂移 | 输入录制回放 + 逐模块行为比对（M2 建立） |
| hitstop 与缩放/非缩放时钟交互出错 | 时钟统一走 ITimePort，Unscaled 语义单测覆盖 |
| 模块化导致调用关系隐晦 | 三相位纪律 + GameRoot 显式装配；模块仅依赖端口与消息 |
| 泛型事件通道性能退化（装箱/分配） | 事件结构体 + in 传参；参照现有 struct 事件经验，基准测试把关 |
| 重构范围失控 | 每里程碑独立可验证、可回滚；Core 永远先于 Shell 落地 |
| 框架与内容边界过界 | 程序集依赖单向（Shell→Gameplay→Framework），用 asmdef 强制 |

---

## 13. 开放问题（待讨论）

| 编号 | 问题 | 备选方案 | 倾向（可改） |
|---|---|---|---|
| A | Core 数学类型 | ① System.Numerics.Vector2；② 自建 Vector2 结构体 | System.Numerics（引擎无关、无开发成本）；若手感/互操作需要再自建 |
| B | 事件通道形态 | ① 泛型静态通道（EventChannel\<T\>）；② 实例化聚合器（可注入、可替换） | 静态通道为主 + 聚合器包装（两者并用） |
| C | 角色控制与流程控制的关系 | ① 同一 Core 两个子模块；② 两个独立子框架 | 同一 Core 的两个子框架（目录/程序集上分开） |
| D | 敌人 AI 抽象程度 | ① IAIController 简单接口；② 行为树/决策树框架；③ 不做，敌人复用玩家能力 | 先 IAIController，按需求演进 |
| E | IPhysicsPort 的面 | ① 仅 2D 平台跳跃所需（探测/速度/射线）；② 全面物理抽象 | 先最小面，引擎迁移演练后按需加 |
| F | 多关卡实现 | ① 单场景复用 + 数据驱动切换；② 多场景加载 | 待定（影响 LevelShell 与 GameRoot 设计） |
| G | 动画状态推导 | ① 保持推导式优先级链；② 迁转移式状态机 | 保持推导式（现有论证充分） |
| H | 相机控制归属 | ① Shell 行为组件（CameraBehavior）；② Core 相机模拟 + Shell 渲染 | ①（表现层，不涉规则） |
| I | 能力与武器关系 | ① IWeapon 是 IAbility 的特例；② 平级两个接口 | 平级（先不引入 IAbility 基类，避免过度设计） |
| J | 版本与分支策略 | ① 每里程碑一个分支 + 可回滚点；② 主分支渐进 | 待定（配合团队习惯） |

---

## 14. 目标目录结构

> 目标代码目录（尚未创建）。`Assets/Core` 为新增的纯逻辑核心；`Assets/Code` 为现有代码重构后的 Shell 适配层；`Assets/Tests` 为新增测试程序集。现有 `Assets/Life`（策略资产）、`Assets/Fx`、`Assets/Audio/SFX` 等**资产目录保持原位不动**。

```
Assets/
├── Core/                                    ★ 纯逻辑核心（新目录，引擎无关，零 UnityEngine）
│   ├── Framework/                           ◆ asmdef: Inkform.Core.Framework（框架，可复用）
│   │   ├── Ports/                           （端口接口 = 引擎能力抽象）
│   │   │   ├── ITimePort.cs                    双时钟（Time/UnscaledTime/DeltaTime）
│   │   │   ├── IRngPort.cs                     可注入随机源
│   │   │   ├── IPhysicsPort.cs                 SetVelocity/Probe/CastCircle/Teleport…
│   │   │   ├── ContactQuery.cs + ContactFlags  四向接触查询与标志
│   │   │   ├── LayerKey.cs                     层掩码抽象
│   │   │   └── Bounds2D.cs
│   │   ├── Events/
│   │   │   ├── EventChannel.cs              ◆ EventChannel<T> 泛型瞬时事件
│   │   │   ├── SnapshotChannel.cs           ◆ 带快照事件（迟到订阅者首次同步）
│   │   │   └── Subscription.cs                 IDisposable 订阅令牌
│   │   ├── Character/                        （角色控制框架）
│   │   │   ├── ICharacterSim.cs                角色模拟器接口（Tick/Teleport/Reset）
│   │   │   ├── CharacterSnapshot.cs             位置/速度/接触/状态/朝向
│   │   │   ├── InputState.cs                    输入结构体（Move/Jump/Dash/AimDelta）
│   │   │   ├── PlayerState.cs + FaceDirection.cs  状态枚举（现文件平移）
│   │   │   ├── PlayerPhase.cs                   Sense/Simulate/Present 三相位
│   │   │   ├── IWeapon.cs + AimInfo.cs          武器协议
│   │   │   └── IController.cs                   PlayerController/IAIController 抽象
│   │   ├── Flow/                            （游戏流程控制框架）
│   │   │   ├── FlowMachine.cs                 流程状态机（纯逻辑）
│   │   │   ├── FlowState.cs                    Boot/Menu/LoadLevel/Playing/Respawning/Paused/Goal
│   │   │   ├── FlowCommand.cs                  LoadLevel/Respawn/Pause/GameOver 命令
│   │   │   ├── LevelConfig.cs                  关卡配置数据
│   │   │   └── FlowSettings.cs
│   │   ├── Life/
│   │   │   ├── DeathContext.cs + DeathCause.cs   现文件平移
│   │   │   ├── DeathStrategy.cs                抽象策略（纯 C#，不再继承 ScriptableObject）
│   │   │   ├── IDeathBody.cs + IHealth.cs       角色生命协议
│   │   │   ├── IMemento.cs + IRestorable.cs     现文件平移
│   │   │   ├── RestorableRegistry.cs           OnEnable/OnDisable 自注册表
│   │   │   └── LevelMemento.cs                 Caretaker（改读注册表，不再场景扫描）
│   │   ├── Simulation/
│   │   │   ├── VerletRope.cs                   去 Unity 化（Vector2→System.Numerics）
│   │   │   ├── IAttachedBody.cs                附刚体抽象（替代 Rigidbody2D）
│   │   │   ├── Timer.cs + UnscaledTimer.cs     走 ITimePort
│   │   │   └── Shatter.cs                      网格切分纯函数（输出碎块描述数组）
│   │   ├── Utilities/
│   │   │   ├── Dir8.cs + RandomPick.cs          现文件平移
│   │   │   └── ServiceRegistry.cs               统一服务查找
│   │   └── Inkform.Core.Framework.asmdef
│   │
│   └── Gameplay/                             ◆ asmdef: Inkform.Core.Gameplay（本作内容）
│       ├── Character/
│       │   └── PlayerSim.cs                   运动学+状态推导（PlayerMotor+AnimStateResolver 逻辑平移）
│       ├── Rope/
│       │   ├── RopeSim.cs                    绳索枪状态机（RopePhase 迁移）
│       │   └── Ballistics.cs                  弹道解算（SolveBallistic 平移）
│       ├── Items/
│       │   ├── BombSim.cs                     引信/免疫期/速度爆炸/连锁规则
│       │   └── ChainRules.cs                  断链判定（纯几何）
│       ├── Life/
│       │   └── ShatterDeathStrategy.cs        本作死法实现（产出表现命令，不碰引擎 API）
│       └── Inkform.Core.Gameplay.asmdef

├── Code/                                    ★ Shell 适配层（现有 Assets/Code 重构于此）
│   ├── Shell.asmdef                         ◆ asmdef: Inkform.Shell（依赖 Unity + 两个 Core）
│   ├── Root/
│   │   ├── GameRoot.cs                       组合根（装配端口/流程机/模块，替代 GameManager 逻辑）
│   │   └── SceneBootstrapper.cs             场景入口（找到角色/关卡壳并注入）
│   ├── Ports/                                （端口实现）
│   │   ├── UnityTimePort.cs
│   │   ├── UnityRngPort.cs
│   │   └── RigidbodyPort.cs                  Rigidbody2D + OverlapCircle/CircleCast 桥接
│   ├── Character/
│   │   ├── PlayerShell.cs                     三相位驱动 + 模块自注册（替代 PlayerHandler）
│   │   ├── EnemyShell.cs                     敌人壳（复用生命/死亡管线）
│   │   ├── PlayerController.cs               输入→InputState 翻译
│   │   ├── ContactSensor.cs                  接触检测壳（现文件改造）
│   │   ├── Health.cs                         IHealth 实现
│   │   ├── AniPresenter.cs                   订阅状态→AnimProfile 播放（替代 AniHandler）
│   │   └── BodyPresenter.cs                  订阅状态→碰撞体 size/offset（替代 ColliderHandler）
│   ├── Modules/                              （能力/武器模块，自注册）
│   │   ├── WeaponSlot.cs                     武器切换/冷却/空手兜底（替代 SpitBomb 方向链）
│   │   ├── ItemCarrier.cs                    现文件改 IPlayerModule
│   │   ├── RopeGun/                          RopeGun 拆分（751 行 → 4 组件）
│   │   │   ├── RopeGun.cs                     模块入口（实现 IWeapon）
│   │   │   ├── RopeAimer.cs                   瞄准/准星/抛物线预览
│   │   │   ├── RopeRenderer.cs                LineRenderer 渲染壳
│   │   │   └── GrappleHook.cs                 钩子运行时物体
│   │   └── SpitBombWeapon.cs                 IWeapon 化
│   ├── Items/                                （物品外壳，规则归 Core）
│   │   ├── ItemSuper.cs + Bomb.cs + Chain.cs + HangingPoint.cs + HangWall.cs   现文件瘦身
│   ├── Level/
│   │   ├── LevelShell.cs                     关卡装配/流程命令执行
│   │   ├── Spike.cs                          改为对 ICharacter 生效
│   │   ├── Checkpoint.cs + BreakableWall.cs + Spawner.cs + RopeRangeZone.cs    现文件改造
│   ├── Presentation/
│   │   ├── CameraRig.cs                      行为组件组宿主（替代 CamHandler）
│   │   ├── Behaviors/                        （相机行为，可插拔）
│   │   │   ├── FollowBehavior.cs + ShakeBehavior.cs + ZoomBehavior.cs
│   │   │   ├── ClampToLevelBoundsBehavior.cs + SnapBehavior.cs
│   │   ├── ScreenFx.cs                       现文件平移
│   │   ├── FxPort.cs                         订阅 Core 特效命令（替代 FxDirector）
│   │   ├── Fragment.cs                       现文件平移
│   │   └── ShatterExecutor.cs                把 Core 碎块描述翻译成 Unity 对象
│   ├── Audio/
│   │   ├── AudioPort.cs                      订阅 Core 音频命令（替代 AudioDirector）
│   │   ├── AudioManager.cs                   对象池，现文件平移
│   │   └── SoundCue.cs                       SO 资产类型，现文件平移
│   ├── Data/                                 （ScriptableObject 资产类型）
│   │   ├── AnimProfile.cs                    ★ 新：状态→clip/体型映射单表
│   │   ├── FragmentCue.cs                    现文件平移
│   │   ├── DeathStrategyAsset.cs             SO 承载 Core 策略（数据 + 工厂方法）
│   │   ├── LevelConfigAsset.cs               ★ 新
│   │   └── RopeSettingsAsset.cs              ★ 新（可选）
│   └── Input/
│       └── InputAdapter.cs                   替代 InputHandler（转发到 PlayerController）

└── Tests/                                    ◆ asmdef: Inkform.Core.Tests（NUnit）
    ├── PlayerSimTests.cs                    跳跃缓冲/墙跳/贴顶推导
    ├── FlowMachineTests.cs                  状态迁移/超时
    ├── EventChannelTests.cs                 去重/快照/订阅令牌
    ├── VerletRopeTests.cs                   收敛/附刚体
    ├── BombSimTests.cs                      引信/连锁/速度爆炸
    ├── ShatterTests.cs                      网格切分
    └── ReplayComparisonTests.cs             输入录制回放比对（防手感漂移）
```

要点：

- **依赖单向**：`Shell → Gameplay → Framework`，由 asmdef 强制；Core 内禁 `UnityEngine`（CI 扫描校验）。
- **迁移统计**：现有 55 个脚本中约 20 个「逻辑平移入 Core」，约 25 个「瘦身为 Shell 壳」，6 个平移不动（`AudioManager`/`ScreenFx`/`Fragment`/`SoundCue`/`FragmentCue`/`HangingPoint`）。
- **两个关键拆解**：`DeathStrategy` 从 SO 抽象改为「Core 纯 C# 抽象 + Shell SO 承载（`DeathStrategyAsset`）」；`RopeGun` 拆成 4 个组件。
- **开发位置**：日常添加功能只动 `Shell`（表现/组件）与 `Gameplay`（本作规则），Framework 仅在新契约/新端口需要时扩展（详见开放问题区后续的「功能扩展指南」）。

---

## 15. 重构实施计划

> 基于第 11 节路线图的执行细化版。每阶段独立可验证、可回滚；M2 为风险集中区（手感验证），使用双模对比 + 录制回放双保险。

### 15.1 执行纪律

1. **行为优先**：每阶段结束游戏可运行；手感通过「录制回放比对」验证不漂移。
2. **Core 先于 Shell**：先让逻辑落 Core + 单测绿，再拆 Shell。
3. **双模对比（shadow mode）**：M2 关键阶段，新 `PlayerSim` 与旧 `PlayerMotor` 并行跑，逐帧比对位置/状态，差异清零后再切换——比事后回放更早发现问题。
4. **每阶段独立提交**：以 git 标签为回滚点；asmdef 依赖单向（Shell→Gameplay→Framework）。
5. **不兼容底线**：Core 内零 `UnityEngine`，脚本扫描校验（M0 建立）。

### 15.2 阶段计划

| 阶段 | 内容 | 验证门 |
|---|---|---|
| **P0 准备** | 打 git 基线标签；确认路线决策（见 15.4-①）；搭建**输入录制工具**（FixedUpdate 粒度录制 InputState 流 + 输出快照） | 录制工具可复现一段操作序列 |
| **M0 程序集与纯逻辑下沉** | 建 `Core.Framework / Core.Gameplay / Core.Tests` 三个 asmdef + 无 UnityEngine 扫描脚本；`Timer/UnscaledTimer`（走 `ITimePort`）、`Dir8`、`RandomPick` 入 Core；`VerletRope` 去 Unity 化（`System.Numerics` + `IAttachedBody` + `IPhysicsPort.CastCircle`）；核心单测（Timer 时钟语义、Verlet 收敛） | 单测全绿；游戏运行无行为变化 |
| **M1 泛型事件通道** | `EventChannel<T>`/`SnapshotChannel<T>`/`Subscription`；6 个 Bus 改静态别名（调用点一字不改），保留去重/快照/ResetStatics 语义；`EventChannelTests` | 功能回归；新增测试事件通道验证「新事件不新建文件」 |
| **M2a 玩家模拟抽取** | `IPhysicsPort` 桥（`RigidbodyPort`：速度/重力/Teleport/接触探测）；`PlayerSim` 骨架 + `PlayerShell` 三相位；双模对比跑通 | 双模并行无差异 |
| **M2b** | 运动学平移（重力曲线、跳跃缓冲、可变跳高、墙跳、冲刺、击退锁定、贴顶反相）→ 单测 + 双模比对 | 轨迹比对一致 |
| **M2c** | 状态推导平移（`AnimStateResolver` 优先级链）；广播契约不变，`AniHandler/ColliderHandler` 照旧订阅 | 状态序列比对一致 |
| **M2d** | 切换正式模式（旧 Motor 退役），重建 Player 预制体；录制回放全量回归（跳跃/贴墙/贴顶/冲刺/死亡/复活场景各录一段） | 轨迹与状态序列比对一致；游戏全流程可玩 |
| **M3 模块化与武器** | `IPlayerModule` 注册协议；`ServiceRegistry`（吸收 4 处懒缓存）；`IWeapon` + `WeaponSlot`（吸收 SpitBomb 方向兜底链）；RopeGun 拆分（`RopeGun` 入口 / `RopeAimer` / `RopeRenderer` / `GrappleHook`，逻辑归 `RopeSim`）；`SpitBombWeapon` | 新增假武器（如鞭炮 demo）零改动接入并生效 |
| **M4 多角色与数据驱动** | `ICharacter`/`IHealth` 通用化；`Spike` 等危险物改为对 `ICharacter` 生效；`AnimProfile` 资产（AniPresenter/BodyPresenter/动画构建工具三表合一）；敌人原型（`EnemyShell` + 简单 `IAIController`） | 敌人走同一生命/死亡/复活管线；换皮角色仅换资产 |
| **M5 流程控制与相机** | `FlowMachine`/`FlowState`/`FlowCommand` + `LevelConfig`/`LevelConfigAsset`；`RestorableRegistry`（替代场景扫描）；`CameraRig` + 行为组件（Follow/Shake/Zoom/Clamp/Snap，吸收 CamHandler+ScreenFx） | 双关卡流程切换；关卡相机边界生效 |
| **M6 引擎迁移演练**（可选） | Godot 或 MonoGame 最小壳（输入→`PlayerSim`→简单渲染），复用 Core 编译产物 | Core 在第二引擎内跑通角色模拟与流程；产出一份端口实现清单（固化「换引擎成本」答案） |

### 15.3 依赖关系与工作量

```
P0 → M0 → M1 → M2(硬依赖链) ─┬→ M3 ─→ M4 ─→ M5 ─→ M6(可选)
                              └─ 每步独立可验证可回滚
```

总估：2–3 周单人全职；M2 为风险集中区（手感验证），已用双模对比 + 录制回放双保险。

### 15.4 决策记录

> 已确认（2026-08-06，P0）：执行期间如需调整，回本表修改并注明原因。

| 编号 | 决策 | 结论（已确认） |
|---|---|---|
| ① | 引擎解耦路线 | **C→A 渐进**（M0/M1 纯逻辑下沉，M2 才引入物理端口） |
| ② | 执行会话粒度 | **分阶段执行，每阶段停下检查**；首个会话交付 P0 |
| ③ | 录制工具形态 | **双模对比为主、离线回放兜底**（P0 先建录制 + 黄金会话） |
| ④ | 旧 Bus 文件去留 | **M1 迁移后直接删除**（git 历史可回退） |
| ⑤ | 分支策略 | **主分支渐进 + 阶段标签**（refactor-p0-baseline 已建） |
