using UnityEditor;
using UnityEngine;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>
    /// A modal "type one string" prompt. The editor API has DisplayDialog for yes/no and nothing at
    /// all for text, so this fills the gap: ShowModalUtility blocks until the window closes, which
    /// lets callers read the answer as a return value instead of threading a callback through.
    ///
    /// Returns null when cancelled, so "" stays a legitimate answer (clearing a display name).
    /// </summary>
    public class EditorInputDialog : EditorWindow
    {
        private string value = "";
        private string message = "";
        private bool accepted;
        private bool focusRequested;

        public static string Show(string title, string message, string initial)
        {
            var window = CreateInstance<EditorInputDialog>();
            window.titleContent = new GUIContent(title);
            window.message = message;
            window.value = initial ?? "";

            Vector2 size = new Vector2(400f, 116f);
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.center.x - size.x * 0.5f, main.center.y - size.y * 0.5f, size.x, size.y);
            window.minSize = size;
            window.maxSize = size;

            window.ShowModalUtility();
            return window.accepted ? window.value : null;
        }

        void OnGUI()
        {
            // Enter accepts, Escape cancels — handled before the controls draw so the keystroke is not
            // swallowed by the text field.
            Event e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { accepted = true; Close(); return; }
                if (e.keyCode == KeyCode.Escape) { accepted = false; Close(); return; }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(4f);

            GUI.SetNextControlName("EditorInputDialogField");
            value = EditorGUILayout.TextField(value);

            if (!focusRequested)
            {
                EditorGUI.FocusTextInControl("EditorInputDialogField");
                focusRequested = true;
            }

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(90f))) { accepted = false; Close(); }
                if (GUILayout.Button("OK", GUILayout.Width(90f))) { accepted = true; Close(); }
            }
        }
    }
}
