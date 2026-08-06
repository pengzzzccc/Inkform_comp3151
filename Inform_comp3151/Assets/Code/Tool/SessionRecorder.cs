using Inkform.Bus;
using Inkform.Player;
using Inkform.Replay;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Inkform.Tool
{
    /// <summary>
    /// 黄金会话录制器：每物理帧采集「输入 + 接触 + 速度 + 状态 + 位置」，
    /// 停止时写 JSON 到 Assets/Recordings/。内容见 ReplaySchema。
    ///
    /// 用法：
    /// 手动录制 —— 挂到任意物体（推荐 GameManager），拖入 player，运行中按 F9 开始/停止；
    /// 脚本录制 —— GoldenSessionRunner 直接驱动 StartRecording/StopRecording。
    /// </summary>
    [DefaultExecutionOrder(1000)]   // 晚于输入源（DeviceInputHook / GoldenSessionRunner）采样
    public class SessionRecorder : MonoBehaviour
    {
        public const string RecordingsDir = "Assets/Recordings";

        [Tooltip("被录制的玩家。留空 = 启动时按 Player 标签查找")]
        public Transform player;

        [SerializeField] private string sessionName = "session";
        [SerializeField] private bool startOnPlay = false;          // 播放即开始（手动调试用）
        [SerializeField] private bool overwriteFile = false;        // true = 固定文件名覆盖（黄金会话用）
        [SerializeField] private Key toggleKey = Key.F9;

        /// <summary>录制名（黄金会话用：逐会话设置）。</summary>
        public string SessionName { set => sessionName = value; }

        /// <summary>true = 固定文件名覆盖（黄金会话用）。</summary>
        public bool OverwriteFile { set => overwriteFile = value; }

        private Rigidbody2D body;
        private ContactSensor sensor;
        private readonly List<ReplayFrame> frames = new List<ReplayFrame>();
        private bool recording;

        public bool IsRecording => recording;

        void Awake()
        {
            ResolvePlayer();
        }

        void Start()
        {
            if (startOnPlay) StartRecording();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            {
                if (recording) StopRecording();
                else StartRecording();
            }
        }

        void FixedUpdate()
        {
            if (!recording || body == null) return;

            frames.Add(new ReplayFrame
            {
                frame = frames.Count,
                moveX = SampleInputSource.Move.x,
                moveY = SampleInputSource.Move.y,
                jumpPressed = SampleInputSource.JumpPressed,
                jumpHeld = SampleInputSource.JumpHeld,
                dashPressed = SampleInputSource.DashPressed,
                aimX = SampleInputSource.Aim.x,
                aimY = SampleInputSource.Aim.y,
                contactFlags = ReadContact(),
                vx = body.linearVelocity.x,
                vy = body.linearVelocity.y,
                state = (int)PlayerBus.State,
                px = body.position.x,
                py = body.position.y,
            });
        }

        private int ReadContact()
        {
            int flags = 0;
            if (sensor != null)
            {
                if (sensor.OnGround) flags |= ReplaySchema.ContactGround;
                if (sensor.OnLeftWall) flags |= ReplaySchema.ContactLeftWall;
                if (sensor.OnRightWall) flags |= ReplaySchema.ContactRightWall;
                if (sensor.OnCeiling) flags |= ReplaySchema.ContactCeiling;
            }
            return flags;
        }

        public void StartRecording()
        {
            if (recording) return;
            frames.Clear();
            recording = true;
            Debug.Log($"[SessionRecorder] 开始录制：{sessionName}");
        }

        public void StopRecording()
        {
            if (!recording) return;
            recording = false;

            string fileName = overwriteFile
                ? $"{sessionName}.json"
                : $"{sessionName}_{System.DateTime.Now:yyyyMMdd_HHmmss}.json";
            string path = $"{RecordingsDir}/{fileName}";

            ReplayFile file = new ReplayFile
            {
                header = new ReplayFileHeader
                {
                    sessionName = sessionName,
                    sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    fixedDeltaTime = Time.fixedDeltaTime,
                    gravityY = Physics2D.gravity.y,
                    frameCount = frames.Count,
                },
                frames = frames.ToArray(),
            };

            ReplayIO.Save(path, file);
            Debug.Log($"[SessionRecorder] 已保存 {path}（{frames.Count} 帧）");
        }

        private void ResolvePlayer()
        {
            if (player == null)
            {
                GameObject go = GameObject.FindGameObjectWithTag(Tags.Player);
                if (go != null) player = go.transform;
            }
            if (player != null)
            {
                body = player.GetComponent<Rigidbody2D>();
                sensor = player.GetComponent<ContactSensor>();
            }
            if (body == null) Debug.LogWarning("[SessionRecorder] 找不到玩家刚体，录制无效", this);
        }
    }
}
