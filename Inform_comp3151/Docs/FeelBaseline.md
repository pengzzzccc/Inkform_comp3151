# 手感参数基线（Feel Baseline）

> 目的：在重构（M0 起）之前锁定当前版本的手感参数快照，供 M2 迁移后比对。
> 来源：`Assets/Prefabs/Player.prefab`、`Assets/Life/DeathStrategy_Spike.asset`、ProjectSettings 实配值（2026-08-06，commit `c131001`，标签 `refactor-p0-baseline`）。

## 物理与时间（ProjectSettings）

| 项 | 值 | 文件 |
|---|---|---|
| Fixed Timestep | 0.02 s（50Hz） | TimeManager |
| Maximum Allowed Timestep | 0.33333334 | TimeManager |
| Physics2D 重力 | (0, -9.81) | Physics2DSettings |
| QueriesStartInColliders | 1（开） | Physics2DSettings |
| AutoSyncTransforms | 0（关 → transform 与刚体需双写） | Physics2DSettings |
| DefaultContactOffset | 0.01 | Physics2DSettings |
| LinearSleepTolerance | 0.01 | Physics2DSettings |

## 层与 Tag

| 层索引 | 名称 |
|---|---|
| 3 | Player |
| 6 | Terrain |
| 9 | Wall |
| 10 | Celling |
| 11 | Breakable |
| 12 | Debris |
| 13 | Hazard |

- 自定义 Tag：`BreakAble`
- `ContactSensor.terrainMask = (1<<6)|(1<<11)`（Terrain|Breakable）；`RopeGun.hitMask` 同值；`RopeGun.ropeCollisionMask` 同值。

## PlayerMotor（Player.prefab 实配值）

| 字段 | 值 | 备注 |
|---|---|---|
| movingSpeed | 10 | |
| jumpSpeed | 12 | |
| fallCutMultiplier | 0.12 | |
| jumpBuffer | 0.15 | |
| jumpTimes | 1 | 单段跳 + 墙跳补次数 |
| gravity | 2.5 | 写进 Rigidbody2D.gravityScale |
| fallGravityMultiplier | 1.9 | |
| onWallGravityMultiplier | 0.1 | |
| wallJumpTime | 0.15 | |
| wallKickMultiplier | 0.3 | |
| attackMultiplier | 2 | dash 冲刺 |
| attackTime | 0.22 | |
| knockbackTime | 0.35 | |
| 贴顶反相重力 | -5 | 吸顶时 gravityScale |

## ContactSensor

| 字段 | 值 |
|---|---|
| checkRadius | 0.1 |
| ceilingStickTime | 0.5 |

## AnimStateResolver

| 字段 | 值 |
|---|---|
| landAnimTime | 1 |
| jumpUpAnimTime | 0.3 |
| ceilingAttachTime | 0.25 |

## ItemCarrier

| 字段 | 值 |
|---|---|
| spitOffset | 0.9 |
| spitSpeed | 24 |

## RopeGun（Player.prefab 实配值）

| 字段 | 值 |
|---|---|
| maxRange | 6 |
| launchSpeed | 22 |
| bulletGravityScale | 1 |
| bulletRadius | 0.12 |
| muzzleOffset | 0.5 |
| bombDetectRadius | 0.55 |
| mouseAimSensitivity | 1 |
| stickAimSpeed | 15 |
| mouseShowThreshold | 0.5 |
| stickShowThreshold | 0.1 |
| aimShowTime | 0.15 |
| aimFallbackElevation | 30 |
| crosshairSize | 0.6 |
| dashWidth | 0.05 / dashUnitScale 0.5 / dashSortingOrder 10 |
| previewStepDt | 0.033333335 |
| ropeSettings | segmentLength 0.25 / maxSegments 32 / solverIterations 6 / gravityScale 1 / damping 0.99 / attachStiffness 0.6 / attachDamping 0.25 / maxCorrectionSpeed 20 / collisionRadius 0.04 |
| ropeGravityScale | 2.5 |
| ropeWidth | 0.06 / ropeSortingOrder -5 |
| chainCutRadius | 0.35 |
| pullSpeed | 12 |
| arrivalDistance | 0.7 |
| stuckTime | 0.25 |
| tautRopeGravity | 0.15 |
| swallowDistance | 0.75 |
| detachDistance | 0.5 |
| reelTimeout | 2.5 |
| reelSpeed | 5 |
| hitStopTime | 0.06 |

## DeathStrategy_Spike

| 字段 | 值 |
|---|---|
| cause | Spike(0) |
| respawnDelay | 0.9 |
| burstForce | 14 |
| trauma | 0.7 |
| hitStop | 0.12 |
| zoom | -0.5 |
| zoomTime | 0.4 |
| punch | 0.8 |
| punchTime | 0.5 |

## 其他关键配置

| 项 | 值 | 备注 |
|---|---|---|
| ScreenFx.maxHitStop | 0.25 | 死亡 hitStop 上限 |
| FxDirector.falloffRange | 20 | 爆炸衰减范围 |
| CamHandler.traumaDecay / maxShakeOffset / shakeFrequency | 1.6 / 0.6 / 22 | |
| AudioManager.poolSize | 24 | |

> 对比方法：M2 迁移后，用本表 + `Assets/Recordings/` 黄金会话逐项核对。
