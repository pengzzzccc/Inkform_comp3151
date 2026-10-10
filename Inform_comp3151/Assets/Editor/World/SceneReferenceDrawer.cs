using System.IO;
using Inkform.Level;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;   // SceneUtility lives here, not in UnityEditor

namespace Inkform.WorldTools
{
    /// <summary>
    /// Inspector face of SceneReference: a SceneAsset drag field plus a status line naming the
    /// scene and whether it is in the Build Settings. The status line is the point — "referenced
    /// but not in the build" used to be invisible until a door refused to load at runtime.
    /// </summary>
    [CustomPropertyDrawer(typeof(SceneReference))]
    public sealed class SceneReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty asset = property.FindPropertyRelative("sceneAsset");
            SerializedProperty path = property.FindPropertyRelative("scenePath");

            EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            Rect field = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginChangeCheck();
            Object picked = EditorGUI.ObjectField(field, GUIContent.none, asset.objectReferenceValue, typeof(SceneAsset), false);
            if (EditorGUI.EndChangeCheck())
                asset.objectReferenceValue = picked;

            string status;
            int buildIndex = -1;
            if (asset.objectReferenceValue != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(asset.objectReferenceValue);
                buildIndex = SceneUtility.GetBuildIndexByScenePath(assetPath);
                status = $"{Path.GetFileNameWithoutExtension(assetPath)}   {(buildIndex >= 0 ? $"in build (#{buildIndex})" : "NOT in build")}";
            }
            else if (!string.IsNullOrEmpty(path.stringValue))
            {
                status = $"{Path.GetFileNameWithoutExtension(path.stringValue)}   (scene asset missing)";
            }
            else
            {
                status = "no scene assigned";
            }

            Rect statusRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + 2f,
                position.width, EditorGUIUtility.singleLineHeight);
            var style = new GUIStyle(EditorStyles.miniLabel);
            if (asset.objectReferenceValue != null && buildIndex < 0) style.normal.textColor = new Color(0.95f, 0.35f, 0.25f);
            GUI.Label(statusRect, status, style);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight * 2f + 4f;
    }
}
