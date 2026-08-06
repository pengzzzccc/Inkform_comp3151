using System;
using System.IO;
using UnityEngine;

namespace Inkform.Replay
{
    /// <summary>
    /// 录制 Schema（黄金会话格式，v1）。
    ///
    /// 录制端（SessionRecorder，Shell 侧）按本 Schema 序列化 JSON；
    /// 解析/校验端（ReplayIO）供 M2 回放比对测试与编辑器校验工具共用。
    /// 字段名是跨端契约，改动需两端同步。
    /// </summary>
    public static class ReplaySchema
    {
        public const string FormatVersion = "inkform-replay-v1";

        // 接触标志位（与 Core 规划中的 ContactFlags 一致）
        public const int ContactGround = 1;
        public const int ContactLeftWall = 2;
        public const int ContactRightWall = 4;
        public const int ContactCeiling = 8;
    }

    /// <summary>文件头：回放所需的确定性环境参数。</summary>
    [Serializable]
    public class ReplayFileHeader
    {
        public string format = ReplaySchema.FormatVersion;
        public string sessionName;
        public string sceneName;
        public float fixedDeltaTime;    // Fixed Timestep（0.02）
        public float gravityY;          // Physics2D 重力 Y（-9.81）
        public int frameCount;
    }

    /// <summary>单帧记录：输入 + 决策层输出（接触/速度/状态/位置）。</summary>
    [Serializable]
    public class ReplayFrame
    {
        public int frame;               // 会话内物理帧序号（0 起，步进 1）
        public float moveX, moveY;      // 移动输入
        public bool jumpPressed;        // 按下沿
        public bool jumpHeld;           // 按住
        public bool dashPressed;        // 冲刺按下沿
        public float aimX, aimY;        // 瞄准（绳索枪）
        public int contactFlags;        // 位标志，见 ReplaySchema.Contact*
        public float vx, vy;            // 刚体速度（决策层输出）
        public int state;               // PlayerState 枚举索引
        public float px, py;            // 刚体位置（参考）
    }

    /// <summary>录制文件本体。</summary>
    [Serializable]
    public class ReplayFile
    {
        public ReplayFileHeader header;
        public ReplayFrame[] frames;
    }

    /// <summary>保存 / 解析 / 校验。</summary>
    public static class ReplayIO
    {
        /// <summary>序列化为 JSON 文本（编辑器录制用）。</summary>
        public static string ToJson(ReplayFile file) => JsonUtility.ToJson(file, true);

        /// <summary>从 JSON 文本解析。</summary>
        public static ReplayFile FromJson(string json) => JsonUtility.FromJson<ReplayFile>(json);

        public static ReplayFile Load(string path) => FromJson(File.ReadAllText(path));

        public static void Save(string path, ReplayFile file)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson(file));
        }

        /// <summary>完整性校验。失败时 error 给出原因。</summary>
        public static bool Validate(ReplayFile file, out string error)
        {
            if (file == null) { error = "文件为空或解析失败"; return false; }
            if (file.header == null) { error = "缺少 header"; return false; }
            if (file.header.format != ReplaySchema.FormatVersion)
            {
                error = $"格式版本不符：{file.header.format}（期望 {ReplaySchema.FormatVersion}）";
                return false;
            }
            if (file.frames == null || file.frames.Length == 0)
            {
                error = "frames 为空";
                return false;
            }
            if (file.header.frameCount != file.frames.Length)
            {
                error = $"header.frameCount({file.header.frameCount}) != frames.Length({file.frames.Length})";
                return false;
            }
            if (file.header.fixedDeltaTime <= 0f)
            {
                error = "fixedDeltaTime 非法";
                return false;
            }

            for (int i = 0; i < file.frames.Length; i++)
            {
                ReplayFrame f = file.frames[i];
                if (f == null) { error = $"frames[{i}] 为空"; return false; }
                if (f.frame != i) { error = $"frames[{i}].frame = {f.frame}，应为 {i}"; return false; }
                if (!IsFinite(f.moveX) || !IsFinite(f.moveY) || !IsFinite(f.aimX) || !IsFinite(f.aimY)
                    || !IsFinite(f.vx) || !IsFinite(f.vy) || !IsFinite(f.px) || !IsFinite(f.py))
                {
                    error = $"frames[{i}] 含非有限浮点值"; return false;
                }
                if (f.state < 0 || f.state > 13) { error = $"frames[{i}].state = {f.state} 超出 PlayerState 范围"; return false; }
                if ((f.contactFlags & ~0xF) != 0) { error = $"frames[{i}].contactFlags = {f.contactFlags} 含未知位"; return false; }
            }

            error = null;
            return true;
        }

        private static bool IsFinite(float v) => float.IsFinite(v);
    }
}
