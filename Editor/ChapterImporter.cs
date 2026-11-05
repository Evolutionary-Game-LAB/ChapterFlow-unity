using System;
using System.IO;
using ChapterFlow.Unity;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace ChapterFlow.Editor
{
    // Ordinary .json files remain Unity TextAssets. Only the opt-in extension is owned.
    [ScriptedImporter(1, "chapterflow")]
    public sealed class ChapterImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var asset = ScriptableObject.CreateInstance<ChapterAsset>();
            try {
                asset.Initialize(File.ReadAllText(context.assetPath));
                context.AddObjectToAsset("chapter", asset); context.SetMainObject(asset);
            } catch (Exception e) { DestroyImmediate(asset); context.LogImportError("ChapterFlow: " + e.Message); }
        }
        [MenuItem("Tools/ChapterFlow/Import Web JSON...")]
        public static void ImportJson()
        {
            string input = EditorUtility.OpenFilePanel("ChapterFlow JSON Export", "", "json");
            if (string.IsNullOrEmpty(input)) return;
            try {
                string json = File.ReadAllText(input); var definition = ChapterDefinition.Parse(json);
                string output = EditorUtility.SaveFilePanelInProject("Save ChapterFlow asset", Path.GetFileNameWithoutExtension(input), "chapterflow", "웹툴 JSON 원본과 별도로 임포트합니다.");
                if (string.IsNullOrEmpty(output)) return;
                File.WriteAllText(output, json); AssetDatabase.ImportAsset(output);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<ChapterAsset>(output);
                Debug.Log("ChapterFlow imported: " + definition.Title + " (" + definition.Events.Count + " events)");
            } catch (Exception e) { EditorUtility.DisplayDialog("ChapterFlow import failed", e.Message, "OK"); }
        }
    }
    [CustomEditor(typeof(ChapterAsset))]
    public sealed class ChapterAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            try {
                var chapter = ((ChapterAsset)target).CreateDefinition();
                EditorGUILayout.LabelField(chapter.Title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Events / Symbols / Endings", chapter.Events.Count + " / " + chapter.Symbols.Count + " / " + chapter.Endings.Count);
                EditorGUILayout.SelectableLabel(chapter.StoryId, GUILayout.Height(20));
                EditorGUILayout.HelpBox("Chapter Runner에 이 에셋을 연결하세요. 실행 중 Tools > ChapterFlow > Runtime Debugger에서 상태와 조건을 확인할 수 있습니다.", MessageType.Info);
            } catch (Exception e) { EditorGUILayout.HelpBox(e.Message, MessageType.Error); }
        }
    }
}
