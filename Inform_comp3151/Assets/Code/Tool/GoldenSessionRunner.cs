using Inkform.Bus;
using Inkform.Life;
using Inkform.Player;
using System.IO;
using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// 黄金会话驱动：用确定性脚本输入逐帧驱动 PlayerHandler（并同步写入 SampleInputSource），
    /// 顺序录制 6 段黄金会话。不需要人工操作——输入与帧号一一对应，录制内容仅取决于脚本与场景。
    ///
    /// 由编辑器菜单（Tools/Refactor/Record Golden Sessions）创建并进入播放模式；
    /// 全部完成后写 _runner_done.flag，编辑器侧轮询该文件后退出播放模式。
    /// </summary>
    [DefaultExecutionOrder(-1500)]   // 先于 SessionRecorder 的 FixedUpdate 采样
    public class GoldenSessionRunner : MonoBehaviour
    {
        public const string FlagPath = "Assets/Recordings/_runner_done.flag";

        public static bool BatchMode;               // 批处理模式（由编辑器菜单注入）
        public GameObject bombPrefab;               // 由编辑器菜单注入

        private enum Phase { Settle, Recording }

        private static readonly string[] ScenarioNames =
        {
            "jump-run",         // 右移 + 一次跳跃
            "wall-slide",       // 左移撞墙 + 墙跳
            "ceiling-stick",    // 原地起跳顶天花板
            "dash",             // 右移 + 冲刺
            "death-respawn",    // 右移 + 刺死 + 复活
            "bomb-eat-spit",    // 生成炸弹 + 挂绳抓取 + 吐出
            "rope-fire",        // 右移 + 发射绳索（拉取/收绳轨迹基线）
        };

        private static readonly int[] ScenarioDurations =
        {
            700, 500, 420, 320, 650, 800, 700,
        };

        private const int SettleFrames = 40;    // 复位/开局稳定帧数（不录制）

        private struct InputCmd
        {
            public Vector2 move;
            public bool jumpPressed;
            public bool jumpHeld;
            public bool dashPressed;
            public bool fire;       // RopeFire 按下沿
            public bool spit;       // SpitBomb 按下沿
        }

        private SessionRecorder recorder;
        private Transform player;
        private Rigidbody2D body;
        private PlayerHandler handler;

        private Vector2 spawn;
        private bool spawnCaptured;

        private int scenarioIndex;
        private int frame;
        private Phase phase = Phase.Settle;
        private bool running = true;
        private bool prevJumpHeld;

        void Awake()
        {
            GameObject go = GameObject.FindGameObjectWithTag(Tags.Player);
            if (go == null)
            {
                Debug.LogError("[GoldenSessionRunner] 找不到玩家，会话录制中止");
                FinishAll();
                return;
            }

            player = go.transform;
            body = go.GetComponent<Rigidbody2D>();
            handler = go.GetComponent<PlayerHandler>();

            recorder = gameObject.AddComponent<SessionRecorder>();
            recorder.player = player;
            recorder.OverwriteFile = true;
        }

        void FixedUpdate()
        {
            if (!running) return;

            if (!spawnCaptured)
            {
                spawnCaptured = true;
                spawn = player.position;
            }

            switch (phase)
            {
                case Phase.Settle:
                    ResetInputOnly();
                    if (++frame >= SettleFrames)
                    {
                        frame = 0;
                        phase = Phase.Recording;
                        recorder.SessionName = ScenarioNames[scenarioIndex];
                        recorder.StartRecording();
                    }
                    break;

                case Phase.Recording:
                    if (frame >= ScenarioDurations[scenarioIndex])
                    {
                        recorder.StopRecording();
                        scenarioIndex++;
                        if (scenarioIndex >= ScenarioNames.Length)
                        {
                            FinishAll();
                            return;
                        }
                        ResetPlayer();
                        frame = 0;
                        phase = Phase.Settle;
                        return;
                    }
                    DriveScenario();
                    frame++;
                    break;
            }
        }

        // 确定性脚本：输入只由 (场景, 帧号) 决定
        private void DriveScenario()
        {
            InputCmd cmd = default;
            cmd.move = new Vector2(1f, 0f);     // 大多数场景默认右移

            switch (scenarioIndex)
            {
                case 0:     // jump-run
                    if (frame == 120) { cmd.jumpPressed = true; cmd.jumpHeld = true; }
                    else if (frame > 120 && frame <= 134) cmd.jumpHeld = true;
                    break;

                case 1:     // wall-slide
                    cmd.move = new Vector2(-1f, 0f);
                    if (frame == 60) { cmd.jumpPressed = true; cmd.jumpHeld = true; }
                    else if (frame > 60 && frame <= 74) cmd.jumpHeld = true;
                    break;

                case 2:     // ceiling-stick
                    cmd.move = Vector2.zero;
                    if (frame == 0) { cmd.jumpPressed = true; cmd.jumpHeld = true; }
                    else if (frame >= 1 && frame <= 129) cmd.jumpHeld = true;
                    break;

                case 3:     // dash
                    if (frame == 90) cmd.dashPressed = true;
                    break;

                case 4:     // death-respawn：强制刺死一次，之后自然复活
                    if (frame == 260 && !LifeBus.IsDead)
                    {
                        LifeBus.RaiseDied(new DeathContext(
                            player.gameObject, (Vector2)player.position, DeathCause.Spike));
                    }
                    break;

                case 5:     // bomb-eat-spit
                    if (frame == 10 && bombPrefab != null)
                    {
                        Instantiate(bombPrefab, (Vector2)player.position + new Vector2(2.5f, 0f), Quaternion.identity);
                    }
                    if (frame == 150) cmd.fire = true;
                    if (frame == 560) cmd.spit = true;
                    break;

                case 6:     // rope-fire：无瞄准 → 移动方向上倾发射；记录飞行/拉取/收绳全过程的玩家轨迹
                    if (frame == 150) cmd.fire = true;
                    if (frame == 400) cmd.fire = true;      // 第二次按下 = 取消/松绳（视相位而定）
                    break;
            }

            ApplyCommand(cmd);
        }

        private void ApplyCommand(in InputCmd c)
        {
            SampleInputSource.Move = c.move;
            SampleInputSource.JumpPressed = c.jumpPressed;
            SampleInputSource.JumpHeld = c.jumpHeld;
            SampleInputSource.DashPressed = c.dashPressed;

            if (handler == null) return;
            handler.Move(c.move);
            if (c.jumpPressed) handler.JumpPressed();
            if (prevJumpHeld && !c.jumpHeld) handler.JumpReleased();
            if (c.dashPressed) handler.Dash();
            if (c.fire) handler.RopeFire();
            if (c.spit) handler.SpitBomb();
            prevJumpHeld = c.jumpHeld;
        }

        private void ResetInputOnly()
        {
            SampleInputSource.ResetAll();
            prevJumpHeld = false;
            if (handler != null) handler.Move(Vector2.zero);
        }

        private void ResetPlayer()
        {
            ResetInputOnly();

            // 走复活路径清理死亡/持有状态（保留总线语义）
            if (player != null)
            {
                LifeBus.RaiseRespawned(player.gameObject, spawn);
                ItemBus.ClearHeld();
            }

            if (body != null)
            {
                body.simulated = true;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.position = spawn;          // AutoSyncTransforms=0：transform 与刚体双写
            }
            if (player != null) player.position = spawn;
        }

        private void FinishAll()
        {
            if (!running) return;
            running = false;
            Debug.Log("[GoldenSessionRunner] 全部会话录制完成");
            if (!Application.isEditor) return;  // 编辑器录制专用
            File.WriteAllText(FlagPath, "done");
        }
    }
}
