# 重构计划：语义统一 + 交互框架落地

> 状态：已定稿，未执行（2026-08-07）
> 背景：可接触物组件框架（`Assets/Code/Interactable/`，Interactable 主节点 + parts 组合）已建成，
> 本计划为全项目代码审查后整理的下一阶段改动。仅记录计划，本轮不做任何重构。

---

## 审查结论摘要（已完成）

三个代理对全项目 36 个文件交叉比对后，问题分三类：重复点（层掩码三拷贝、bounds→隐藏→碎裂序列 ×4、FX 守卫 ×2 等）、结构问题（RespawnDirector 与 Life 域割裂、Handler 命名混用等）、未抽象成语义（Bomb 未迁移、Checkpoint/RopeRangeZone 绕过框架、ItemSuper 半迁移残留等）。

本计划只覆盖第二批（语义统一）+ 第三批的 3-1。第一批（低风险收敛）与 Bomb 迁移明确搁置。

---

## 第二批：语义统一（5 项，低风险）

### 2-1 ItemCarrier 单源状态

**文件**：`Assets/Code/Player/ItemCarrier.cs`

**现状问题**：`ItemCarrier.heldItem` 字段与 `ItemBus.Held` 全局快照双份存储，死亡时手动同步两处（`:74-75`），注释自己承认双存。

**改动**：
- 删 `heldItem` 字段与 `OnItemEaten` 处理器（退订 `ItemBus.ItemEaten`）
- `IsEmpty => ItemBus.Held == null`
- `TryRelease`：`if (ItemBus.Held == null) return false;` → `RaiseItemReleased(ItemBus.Held, mouth, dir * spitSpeed)`（RaiseItemReleased 内部自清快照）→ return true
- `OnDied`：`if (ItemBus.Held == null) return;` → `ItemBus.Held.DropAt(...)` + `ClearHeld()`

**风险**：`ItemBus.Held` 为全局快照，多人/多载具场景会串——本项目单人，注释声明。零行为变化。

### 2-2 墙滑双阈值显式化（保留手感）

**文件**：`PlayerMotor.cs`、`AnimStateResolver.cs`

**现状问题**：重力（PlayerMotor.cs:163 `vy < 0f`）与动画（AnimStateResolver.cs:88 `vy < 1f`）两处独立判定墙滑，阈值不同且无解释。

**改动**（用户决策：保留双阈值，不统一）：
- `PlayerMotor` 加 `public bool IsWallSliding => contact.OnWall && !contact.OnGround && VelocityY < 0f`，`ApplyNonLinearGravity` 改读它（重力语义不变）
- `AnimStateResolver.cs:88` 的 `vy < 1f` 保留，加注释：**与 PlayerMotor.IsWallSliding（<0）刻意不同——上升尾段（vy 0~1）动画仍显示墙滑、重力不减速**，两处互引

### 2-3 PlayerBus.FaceSign

**文件**：`PlayerBus.cs` + 5 处调用点

**改动**：`PlayerBus` 加 `public static int FaceSign => Face == FaceDirection.R ? 1 : -1;`

替换 6 处：
- `PlayerHandler.cs:117`（Dash 方向）、`:146`（吐物兜底方向）
- `RopeGun.cs:168`（EffectiveFireDir）、`:716`（OnRespawned 准星）
- `CamHandler.cs:82`、`:104`（相机前瞻）

纯机械替换，零行为变化。

### 2-4 Shatter.BurstAndHide

**文件**：`Shatter.cs`、`ExplodePart.cs`

**改动**：`Shatter` 加 `public static void BurstAndHide(FragmentCue cue, Component source, Vector2 center, float force)`：
快照 bounds（gotcha 注释写死"Collider 禁用后 bounds 退化零尺寸"收进一处）→ 关 `Collider2D.enabled` + 所有 Renderer → `Burst`。

`ExplodePart.Explode()` 改调它。

**边界**：BreakableWall（走 `SetBroken` 统一开关，Restore 复用）与 ShatterDeathStrategy（走 `IDeathBody.Hide()` 接口）保持原样，gotcha 注释互引。Bomb.playExplode 不迁（Bomb 搁置）。

### 2-5 RespawnDirector 归 Life/

**文件**：`Assets/Code/Level/RespawnDirector.cs` → `Assets/Code/Life/`

**改动**：`git mv`（文件 + .meta 一起移，guid 保留 → GameManager.prefab 引用安全）；命名空间 `Inkform.Level` → `Inkform.Life`；加 `using Inkform.Level;`（Checkpoint 引用）。

**理由**：死亡/复活/检查点是同一生命域（同挂 GameManager 宿主），与 DeathDirector/LevelMemento 割裂在两个命名空间。

---

## 第三批：框架落地（仅 3-1）

### 3-1 Checkpoint / RopeRangeZone part 化

**新 part**（`Assets/Code/Interactable/Parts/`，命名空间 `Inkform.Interactable.Parts`）：

- `CheckpointPart`：
  - 字段：`isStartPoint` / `spawnOffset` / `active`（反复穿过去重）
  - `HandleContact(Enter)`：非玩家返回 false → `LifeBus.RaiseCheckpointSet(SpawnPos)` → true
  - 只读属性：`IsStartPoint` / `SpawnPos`
  - `OnDrawGizmos` 常显（现有 Checkpoint 逻辑全搬）
- `RangeZonePart`：
  - 字段：`maxRange`
  - `HandleContact`：Enter → `RopeGunBus.RaiseRangeOverride(maxRange)`；Exit → `RaiseRangeRestored()`
  - Gizmo 搬入

**改造**：
- `Checkpoint.prefab`：MonoBehaviour（脚本 guid `8f244e862675e264bb5eafdd5d797b45`）→ `Interactable` + `CheckpointPart`。已验证：场景 3 实例零字段覆写（无 isStartPoint/spawnOffset 覆写）→ 零场景改动；BoxCollider2D(IsTrigger) 已有，满足 RequireComponent
- `RespawnDirector.FindStartPoint`：`FindObjectsByType<Interactable>()` 循环 `TryGetPart<CheckpointPart>()`（加 `using Inkform.Interactable;`）
- 删 `Checkpoint.cs` / `RopeRangeZone.cs` + metas（RopeRangeZone 无场景实例，纯代码替换）
- `ContactPhase.cs` 注释里 "Checkpoint 只看 Enter" 的引用顺带更新

---

## 搁置项（明确不执行）

| 项 | 原因 |
|---|---|
| 第一批（Layers 常量、FxBus.Pulse、删死代码、注释修正等） | 用户指示"1 不动" |
| Bomb 代码迁移 + prefab 改造 | 用户决策：炸弹预制件生态位由组件化可接触物组合替代，预制体改造自行处理；`Bomb.cs` 原样保留 |
| ItemSuper 退役 | 依赖 Bomb 迁移（唯一继承者还在） |
| CarriablePart 事件 / ExplodeAfterDelay.Arm / HangingChain.CutAllChains 扩展 | 为 Bomb 迁移准备，无消费者不做（"只放当前用得到的成员"原则） |

---

## 验证清单（执行时用）

1. Unity 编译无报错。改动文件：
   - 第二批：`ItemCarrier.cs` / `PlayerMotor.cs` / `AnimStateResolver.cs` / `PlayerBus.cs` / `PlayerHandler.cs` / `RopeGun.cs` / `CamHandler.cs` / `Shatter.cs` / `ExplodePart.cs` / `RespawnDirector.cs`（移动）
   - 第三批：`CheckpointPart.cs` / `RangeZonePart.cs`（新增）+ `Checkpoint.prefab` + `RespawnDirector.cs` + 删 `Checkpoint.cs` / `RopeRangeZone.cs`
2. Play 回归：炸弹吞/吐/爆全流程（Bomb.cs 未动，行为应不变）；检查点踩点 + 复活 + 出生点（3 个场景实例）；尖刺/地雷组合；射程区域（如有）
3. `git mv` 后确认 GameManager.prefab 里 RespawnDirector 引用正常（guid 不变，靠 meta 保留）
4. Checkpoint.prefab 改后场景实例自动刷新（预制体资产变更），无需改场景文件
