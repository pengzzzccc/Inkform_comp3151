# Inkform

一款 2D 平台游戏。玩家角色能吞下并吐出物体（炸弹等）、用绳枪勾住地形摆荡移动、
贴墙攀爬与吸附天花板；关卡由可爆破的墙、悬挂的链条、巡逻与旋转的机关、
以及死亡后复原的检查点构成。

COMP3151 项目仓库。

## 环境

| | |
|---|---|
| Unity | **6000.4.10f1**（版本需一致，否则场景与预制体可能被自动升级并产生大量无谓 diff） |
| 渲染管线 | URP 17.4 · 2D Renderer |
| 输入 | Unity Input System 1.19 |

## 打开项目

1. Unity Hub → Add → 选择本仓库下的 `Inform_comp3151/` 目录（**不是**仓库根目录）
2. 首次打开会重新导入资产，需要几分钟
3. 从 `Assets/Scenes/Level1/Level1.unity` 开始

> 注意：Build Settings 当前的首个场景是 `Scenes/Test/AnimationTest.unity`。
> 正式出包前需把 `Level1.unity` 调到索引 0。

## 目录速查

```
Assets/
├── Animation/   动画片段与状态机
├── Art/         贴图 · 材质 · Tile · UI
├── Audio/       音频源与 SoundCue 资产
├── Code/        运行时脚本，按域分目录（命名空间 Inkform.<域> 与之对应）
├── Editor/      编辑器工具
├── Fx/          特效数据资产
├── Life/        死亡策略数据资产
├── Physics/     物理材质
├── Prefabs/     所有预制体
├── Scenes/      Level1/ 正式关卡 · Test/ 调试场景
└── Settings/    URP 渲染管线配置
```

## 开工前必读

**移动或重命名 `Assets/` 下的任何文件时，`.meta` 必须跟着一起走。**
Unity 靠 `.meta` 里的 GUID 解析引用，丢下 `.meta` 会让所有引用它的场景和预制体
变成 Missing，而且不打开对应场景就不会报错。用 Unity 的 Project 窗口拖动，
或 `git mv` 本体和 `.meta` 两个文件。

详细的目录职责、命名约定、待接入功能清单与已知问题，见
**[`Docs/ProjectStructure.md`](Docs/ProjectStructure.md)**。

代码层的重构待办见 [`Docs/RefactorPlan.md`](Docs/RefactorPlan.md)（已定稿，尚未执行）。
