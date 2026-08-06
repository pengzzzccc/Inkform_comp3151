#if UNITY_EDITOR
using Inkform.Replay;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Inkform.EditorTools
{
    /// <summary>
    /// 黄金会话校验：读取 Assets/Recordings/ 下全部 JSON，按 ReplaySchema 完整性规则逐一校验。
    /// </summary>
    public static class ValidateRecordingsMenu
    {
        const string RecordingsDir = "Assets/Recordings";

        [MenuItem("Tools/Refactor/Validate Recordings")]
        public static void ValidateAll()
        {
            int ok = RunValidation(out int fail);
            Debug.Log($"[ValidateRecordings] 完成：{ok} 通过 / {fail} 失败");
        }

        /// <summary>批处理入口：-executeMethod Inkform.EditorTools.ValidateRecordingsMenu.ValidateBatch</summary>
        public static void ValidateBatch()
        {
            RunValidation(out int fail);
            EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        private static int RunValidation(out int failCount)
        {
            int ok = 0;
            failCount = 0;

            if (!Directory.Exists(RecordingsDir))
            {
                Debug.LogWarning($"[ValidateRecordings] 目录不存在：{RecordingsDir}");
                return 0;
            }

            foreach (string path in Directory.GetFiles(RecordingsDir, "*.json"))
            {
                try
                {
                    ReplayFile file = ReplayIO.Load(path);
                    if (ReplayIO.Validate(file, out string error))
                    {
                        ok++;
                        Debug.Log($"[ValidateRecordings] OK {Path.GetFileName(path)}：{file.header.frameCount} 帧");
                    }
                    else
                    {
                        failCount++;
                        Debug.LogError($"[ValidateRecordings] FAIL {Path.GetFileName(path)}：{error}");
                    }
                }
                catch (System.Exception e)
                {
                    failCount++;
                    Debug.LogError($"[ValidateRecordings] FAIL {Path.GetFileName(path)}：{e.Message}");
                }
            }

            return ok;
        }
    }
}
#endif
