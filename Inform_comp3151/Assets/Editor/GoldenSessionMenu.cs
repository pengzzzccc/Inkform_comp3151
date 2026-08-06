#if UNITY_EDITOR
using Inkform.Tool;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Inkform.EditorTools
{
    /// <summary>
    /// 黄金会话录制编排：打开测试场景 → 创建驱动对象 → 进入播放模式，
    /// 轮询 _runner_done.flag，完成后退出播放模式（批处理模式随即退出进程）。
    /// </summary>
    public static class GoldenSessionMenu
    {
        const string ScenePath = "Assets/Scenes/Test/Player_test.unity";
        const string BombPrefabPath = "Assets/Prefabs/Bomb.prefab";
        const string HarnessName = "GoldenSessionHarness";

        [MenuItem("Tools/Refactor/Record Golden Sessions")]
        public static void RecordFromMenu() => Record(false);

        /// <summary>批处理入口：Unity.exe -batchmode -projectPath ... -executeMethod
        /// Inkform.EditorTools.GoldenSessionMenu.RecordBatch</summary>
        public static void RecordBatch() => Record(true);

        private static void Record(bool batch)
        {
            Directory.CreateDirectory("Assets/Recordings");
            if (File.Exists(GoldenSessionRunner.FlagPath)) File.Delete(GoldenSessionRunner.FlagPath);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[GoldenSession] 打开场景失败：{ScenePath}");
                if (batch) EditorApplication.Exit(1);
                return;
            }

            // 清理上次残留的驱动对象（用户中途退出播放模式时可能残留）
            var old = GameObject.Find(HarnessName);
            if (old != null) Object.DestroyImmediate(old);

            var go = new GameObject(HarnessName);
            var runner = go.AddComponent<GoldenSessionRunner>();
            runner.bombPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BombPrefabPath);
            GoldenSessionRunner.BatchMode = batch;

            EditorApplication.update += PollForDone;
            EditorApplication.isPlaying = true;
        }

        private static void PollForDone()
        {
            if (!File.Exists(GoldenSessionRunner.FlagPath)) return;
            File.Delete(GoldenSessionRunner.FlagPath);
            EditorApplication.update -= PollForDone;

            if (!Application.isPlaying)
            {
                Finish();
                return;
            }

            EditorApplication.update += WaitForPlayExit;
            EditorApplication.isPlaying = false;
        }

        private static void WaitForPlayExit()
        {
            if (EditorApplication.isPlaying) return;
            EditorApplication.update -= WaitForPlayExit;
            Finish();
        }

        private static void Finish()
        {
            // 清掉驱动对象，避免残留到下一次手动进入播放模式
            var harness = GameObject.Find(HarnessName);
            if (harness != null) Object.DestroyImmediate(harness);

            Debug.Log("[GoldenSession] 全部黄金会话录制完成，输出于 Assets/Recordings/");
            if (GoldenSessionRunner.BatchMode) EditorApplication.Exit(0);
        }
    }
}
#endif
