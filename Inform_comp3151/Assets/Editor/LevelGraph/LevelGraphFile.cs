using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>
    /// Where the graph lives on disk and how it is read and written. One place so the path is not
    /// spelled in the window, validator, and project setup separately.
    ///
    /// The file is a plain .txt: Unity imports it as a TextAsset with no custom importer to write, the
    /// Inspector previews it, and git diffs it as text. That last one is the point — the topology used
    /// to live in GUID-laden LevelScene assets whose diffs could not be reviewed, and a format that is
    /// not reviewable is not a source of truth. SceneDirector reads this same file at runtime through
    /// a serialized TextAsset reference.
    /// </summary>
    public static class LevelGraphFile
    {
        public const string Path = "Assets/Scenes/LevelGraph.txt";

        public static bool Exists => File.Exists(Path);

        /// <summary>Reads and parses the graph. A missing file is an empty document, not an error —
        /// the window offers to seed one.</summary>
        public static LevelGraphDocument Load(out List<LevelGraphParser.ParseError> errors)
        {
            errors = new List<LevelGraphParser.ParseError>();
            if (!Exists) return new LevelGraphDocument();

            return LevelGraphParser.Parse(File.ReadAllText(Path), out errors);
        }

        public static LevelGraphDocument Load() => Load(out _);

        /// <summary>
        /// Writes the graph back and re-imports it. Skips the write when the text is byte-identical, so
        /// opening the window and pressing Apply out of habit does not touch the file's timestamp or
        /// show up in `git status`.
        /// </summary>
        public static void Save(LevelGraphDocument doc)
        {
            string text = LevelGraphParser.Serialize(doc);

            // Compare with line endings normalised. The serializer always emits LF, but git checks the
            // file out with CRLF on Windows (.gitattributes marks the repo text=auto), so a raw
            // comparison would never match after a fresh clone and every Apply would rewrite the file.
            if (Exists && Normalize(File.ReadAllText(Path)) == Normalize(text)) return;

            string dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(Path, text);
            AssetDatabase.ImportAsset(Path);
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

        public static string ScenePathFor(string roomName)
        {
            // Rooms live in two places: generated greybox under Generated/, hand-authored ones under
            // Level1/. Search rather than assume, so the graph file never has to carry a folder.
            string[] guids = AssetDatabase.FindAssets($"t:Scene {roomName}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == roomName) return path;
            }
            return null;
        }
    }
}
