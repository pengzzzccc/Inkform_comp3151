using System.Collections.Generic;
using Inkform.Interactable.Parts;
using Inkform.Level.CrystalMine;
using UnityEditor;
using UnityEngine;

namespace Inkform.EditorTools
{
    /// <summary>Validates Crystal Mine prefab compositions without requiring the scenes to exist yet.</summary>
    public static class CrystalMineSceneValidator
    {
        private static readonly string[] PrefabRoots =
        {
            "Assets/Prefabs/Interaction/CrystalMine",
            "Assets/Prefabs/Item/CrystalMine",
            "Assets/Prefabs/Hazard/CrystalMine",
            "Assets/Prefabs/Level/CrystalMine"
        };

        [MenuItem("Tools/Inkform/Validate Crystal Mine")]
        public static void Validate()
        {
            int warnings = 0;
            var doorIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", PrefabRoots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                foreach (CrystalDoorPart door in prefab.GetComponentsInChildren<CrystalDoorPart>(true))
                {
                    SerializedObject serialized = new SerializedObject(door);
                    string id = serialized.FindProperty("doorId").stringValue?.Trim();
                    int cost = serialized.FindProperty("crystalCost").intValue;
                    if (string.IsNullOrWhiteSpace(id)) warnings += Warn(path, "CrystalDoorPart has an empty stable door ID.");
                    else if (!doorIds.Add(id)) warnings += Warn(path, $"duplicate Crystal Door ID '{id}'.");
                    if (cost <= 0) warnings += Warn(path, "CrystalDoorPart cost must be positive.");
                }

                foreach (CrystalDropPart drop in prefab.GetComponentsInChildren<CrystalDropPart>(true))
                {
                    SerializedObject serialized = new SerializedObject(drop);
                    if (serialized.FindProperty("crystalShardPrefab").objectReferenceValue == null)
                        warnings += Warn(path, "CrystalDropPart has no CrystalShard prefab.");
                }

                bool isEncounterBomb = path.EndsWith("EncounterBomb.prefab", System.StringComparison.OrdinalIgnoreCase);
                if (isEncounterBomb && prefab.GetComponentInChildren<CarriablePart>(true) != null)
                    warnings += Warn(path, "EncounterBomb must not contain CarriablePart.");
                if (isEncounterBomb && prefab.GetComponentInChildren<ExplodePart>(true) == null)
                    warnings += Warn(path, "EncounterBomb requires ExplodePart.");

                foreach (WorldMapPart map in prefab.GetComponentsInChildren<WorldMapPart>(true))
                {
                    SerializedObject serialized = new SerializedObject(map);
                    if (serialized.FindProperty("focusPoint").objectReferenceValue == null)
                        warnings += Warn(path, "WorldMapPart requires a FocusPoint.");
                }

                foreach (ElevatorEncounterController encounter in prefab.GetComponentsInChildren<ElevatorEncounterController>(true))
                {
                    SerializedObject serialized = new SerializedObject(encounter);
                    if (serialized.FindProperty("elevatorPlatform").objectReferenceValue == null
                        || serialized.FindProperty("startPoint").objectReferenceValue == null
                        || serialized.FindProperty("endPoint").objectReferenceValue == null
                        || serialized.FindProperty("hazardRain").objectReferenceValue == null)
                        warnings += Warn(path, "ElevatorEncounter has a missing required reference.");
                    if (string.IsNullOrWhiteSpace(serialized.FindProperty("exitId").stringValue))
                        warnings += Warn(path, "ElevatorEncounter requires a LevelGraph exit ID.");
                }
            }

            var sceneDoorIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (CrystalDoorPart door in Object.FindObjectsByType<CrystalDoorPart>(FindObjectsInactive.Include))
            {
                if (!door.gameObject.scene.IsValid()) continue;
                SerializedObject serialized = new SerializedObject(door);
                string id = serialized.FindProperty("doorId").stringValue?.Trim();
                if (string.IsNullOrWhiteSpace(id)) warnings += Warn(door.gameObject.scene.path, "scene door has an empty stable ID.");
                else if (!sceneDoorIds.Add(id)) warnings += Warn(door.gameObject.scene.path, $"duplicate scene door ID '{id}'.");
            }

            foreach (ControlRoomSequenceController sequence in Object.FindObjectsByType<ControlRoomSequenceController>(FindObjectsInactive.Include))
            {
                if (!sequence.gameObject.scene.IsValid()) continue;
                SerializedObject serialized = new SerializedObject(sequence);
                if (serialized.FindProperty("intake").objectReferenceValue == null
                    || serialized.FindProperty("elevator").objectReferenceValue == null
                    || serialized.FindProperty("intakeFocusPoint").objectReferenceValue == null)
                    warnings += Warn(sequence.gameObject.scene.path, "ControlRoomSequence has a missing required reference.");
            }

            if (warnings == 0) Debug.Log("Crystal Mine validation passed.");
            else Debug.LogWarning($"Crystal Mine validation completed with {warnings} warning(s).");
        }

        private static int Warn(string path, string message)
        {
            Debug.LogWarning($"Crystal Mine: {path}: {message}");
            return 1;
        }
    }
}
