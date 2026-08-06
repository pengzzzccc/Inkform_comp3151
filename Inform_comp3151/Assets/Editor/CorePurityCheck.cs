#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Inkform.EditorTools
{
    /// <summary>
    /// Core 纯净度校验：扫描 Assets/Core 下所有 .cs 源码，报告任何引擎引用。
    /// （noEngineReferences 已在编译期拦截绝大多数，本工具作双保险与 CI 用。）
    /// </summary>
    public static class CorePurityCheck
    {
        const string CoreRoot = "Assets/Core";

        static readonly string[] Forbidden =
        {
            "using UnityEngine",
            "using UnityEditor",
            "UnityEngine.",
            "UnityEditor.",
            "Unity.Jobs",
            "Unity.Collections",
        };

        [MenuItem("Tools/Framework/Check Core Purity")]
        public static void Run()
        {
            if (!Directory.Exists(CoreRoot))
            {
                Debug.LogWarning($"[CorePurity] {CoreRoot} 不存在");
                return;
            }

            int violations = 0;
            foreach (string file in Directory.GetFiles(CoreRoot, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;
                    foreach (string token in Forbidden)
                    {
                        if (lines[i].Contains(token))
                        {
                            violations++;
                            Debug.LogError($"[CorePurity] 违规 {file}:{i + 1} 含引擎引用：{token}\n  {lines[i].Trim()}");
                        }
                    }
                }
            }

            Debug.Log($"[CorePurity] 校验完成：{(violations == 0 ? "通过（零引擎引用）" : violations + " 处违规")}");
        }
    }
}
#endif
