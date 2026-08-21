# Inkform

A 2D platformer built with Unity. The player can swallow and spit objects (bombs!) for chain explosions,
pull themselves across gaps with a rope gun, slide along walls, and stick to ceilings. Levels are built
around breakable walls, hanging chains, patrolling and spinning hazards, and checkpoints that bring the
world back together after you die.

COMP3151 course project.

## Gameplay Features

- **Swallow & spit bombs** — grab bombs and spit them to trigger chain explosions
- **Rope gun** — grapple onto terrain and pull yourself across gaps; aim with the mouse or right stick
- **Wall sliding & ceiling sticking** — slide down walls and hang upside down from ceilings
- **Breakable walls & hanging chains** — destructible terrain and swinging chain links
- **Hazards** — patrolling movers and rotating spinners
- **Checkpoints with state restore** — dying resets not just the player but the level itself
  (shattered walls come back), so puzzles can always be retried fresh
- **Game feel polish** — screen shake, hit stop and camera punch on impactful moments
- **Full settings** — rebindable controls, keyboard/gamepad device selection, and a gamepad cursor

## Controls

| Action | Keyboard & Mouse | Gamepad |
|---|---|---|
| Move | WASD / Arrow keys | Left stick |
| Aim | Mouse | Right stick |
| Jump | Space | Right trigger |
| Dash | Left Shift | X (west button) |
| Rope fire | Left mouse button | Left trigger |
| Spit bomb | Q | Right stick press |
| Pause / menu | Esc | — |

All bindings can be remapped in-game from the pause menu → **Settings → Controls**.
With a gamepad, menus are driven by a virtual cursor — the left stick moves it and the X (west)
button clicks.

## Requirements

| | |
|---|---|
| Unity | **6000.4.10f1** (must match exactly — opening the project with another version auto-upgrades scenes and prefabs, generating a large amount of spurious diffs) |
| Render pipeline | URP 17.4 · 2D Renderer |
| Input | Unity Input System 1.19 |

## Getting Started

1. In Unity Hub, click **Add** and select the `Inform_comp3151/` directory (the project folder inside this
   repository — **not** the repository root).
2. The first import of assets takes a few minutes.
3. The game boots into the main menu (`Assets/Scenes/Menu/MainMenu.unity`). **New Game** loads the entry
   level `L1_Player` through the level-flow graph (`Assets/Scenes/All_level_Con.asset`); for direct
   testing you can also open any generated room under `Assets/Scenes/Generated/` or the legacy hand-made
   levels under `Assets/Scenes/Level1/`.

## Project Structure

```
Assets/
├── Animation/   Animation clips and state machines
├── Art/         Sprites · materials · tiles · UI
├── Audio/       Audio sources and SoundCue assets
├── Code/        Runtime scripts, organized by domain (namespace Inkform.<domain>)
├── Editor/      Editor tooling (procedural UI/menu builder, animation clip builder)
├── Fx/          Visual effect data assets
├── Life/        Death strategy data assets
├── Physics/     Physics materials
├── Prefabs/     All prefabs (player, managers, level objects)
├── Scenes/      Menu/ · Level1/ (legacy hand-made levels) · Generated/ (22 procedurally built rooms) · Test/ (debug scenes)
└── Settings/    URP pipeline config and build profiles
```

Key architecture pieces in `Assets/Code/`:

- **Event buses** — eight static event buses (`PlayerBus`, `LifeBus`, `ItemBus`, `HazardBus`,
  `FxBus`, `RopeGunBus`, `LevelBus`, `UiBus`) keep gameplay systems decoupled; "director" classes
  translate bus events into effects (FX, audio, death, rumble).
- **SceneDirector** — the single gatekeeper for scene transitions, routing through a `LevelFlow`
  ScriptableObject (`Assets/Scenes/All_level_Con.asset`) that defines the level graph: menu scene,
  entry level (`L1_Player`), and per-exit targets. Designers connect levels by dragging asset
  references — no code.
- **Interactable framework** — a composable `Interactable` node with interchangeable "parts"
  (explode, break, restore, carry, patrol, spin, …) used to build every hazard and destructible.
- **LevelMemento** — captures and restores the state of every restorable object on checkpoint/death,
  which is what makes shattered walls come back after respawn.

## Scenes

| Scene | Purpose |
|---|---|
| `Scenes/Menu/MainMenu.unity` | Main menu (first scene in Build Settings) |
| `Scenes/Generated/L1_Player.unity` | Entry room — where New Game starts |
| `Scenes/Generated/L1_B1 … L3_LongFight.unity` | 21 more procedurally generated rooms (Level 1 / Level 2 / long fight), wired through the level-flow graph |
| `Scenes/Level1/Level1.unity` | Legacy hand-made level 1 (not part of the generated flow) |
| `Scenes/Level1/B1.unity` | Legacy hand-made level B1 (not part of the generated flow) |
| `Scenes/Level1/B2.unity` | Legacy hand-made level B2 (not part of the generated flow) |
| `Scenes/Test/PlayerTest.unity` | Debug scene: player sandbox |
| `Scenes/Test/AnimationTest.unity` | Debug scene: animation state machine (not in Build Settings) |
| `Scenes/Test/TileMapTest.unity` | Debug scene: tilemap test |

## Building

Target platform is **Windows standalone** (1920×1080). A build profile lives at
`Assets/Settings/Build Profiles/Windows.asset`.

> Current Build Settings scene order: `MainMenu` → the 22 generated rooms → legacy `B1`/`B2`/`Level1`.
> The debug scene `AnimationTest` was removed from the build list — keep it out unless you are testing
> the animation state machine.

## Important Notes

- **`.meta` files must move with their asset.** Unity resolves references by GUID stored in `.meta`;
  leaving a `.meta` behind turns every referencing scene/prefab into a Missing reference (and no error
  is reported until the scene is opened). Move/rename via the Unity Project window, or `git mv` both
  the asset and its `.meta`.
- **Keep the Unity version pinned to 6000.4.10f1.** Upgrading scenes/prefabs under another version
  silently rewrites files and floods the diff.
