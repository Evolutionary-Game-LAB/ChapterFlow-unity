using System.Linq;
using ChapterFlow.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChapterFlow.Samples.Editor
{
    public static class QuickStartScene
    {
        [MenuItem("Tools/ChapterFlow/Create Quick Start Scene")]
        public static void Create()
        {
            var paths = AssetDatabase.FindAssets("signal-lab t:TextAsset").Select(AssetDatabase.GUIDToAssetPath).Where(p => p.StartsWith("Assets/")).ToArray();
            if (paths.Length != 1) { EditorUtility.DisplayDialog("ChapterFlow", "샘플을 한 번 임포트하고 signal-lab.json이 Assets에 하나만 있는지 확인하세요.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var root = new GameObject("ChapterFlow Demo");
            var runner = root.AddComponent<ChapterRunner>(); runner.SetJson(AssetDatabase.LoadAssetAtPath<TextAsset>(paths[0]));
            root.AddComponent<ChapterFlowDemoPanel>().runner = runner;
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("ChapterFlow sample ready. Save the scene, then press Play.");
        }
    }
}
