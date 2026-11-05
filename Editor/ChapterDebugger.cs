using System.Linq;
using ChapterFlow.Unity;
using UnityEditor;
using UnityEngine;

namespace ChapterFlow.Editor
{
    public sealed class ChapterDebugger : EditorWindow
    {
        private ChapterRunner runner;
        private Vector2 scroll;
        [MenuItem("Tools/ChapterFlow/Runtime Debugger")]
        public static void ShowWindow() { GetWindow<ChapterDebugger>("ChapterFlow"); }
        private void OnInspectorUpdate() { Repaint(); }
        private void OnGUI()
        {
            runner = (ChapterRunner)EditorGUILayout.ObjectField("Chapter Runner", runner, typeof(ChapterRunner), true);
            if (runner == null && GUILayout.Button("Use selected GameObject")) runner = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<ChapterRunner>() : null;
            if (!Application.isPlaying) { EditorGUILayout.HelpBox("Play 모드에서 사용하세요. 상태 변경 / 사건 종료 / 다음 사건 시작은 각각 별도 명령입니다.", MessageType.Info); return; }
            if (runner == null) return;
            if (runner.Session == null) { if (GUILayout.Button("Load chapter")) runner.LoadChapter(); return; }
            var session = runner.Session;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField(session.Chapter.Title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Current event", session.ActiveEvent == null ? "종료됨 / 시작 대기" : session.ActiveEvent.Title);
            if (session.ActiveEvent != null) EditorGUILayout.LabelField(session.ActiveEvent.Summary, EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrEmpty(runner.LastError)) EditorGUILayout.HelpBox(runner.LastError, MessageType.Warning);
            if (session.EndingId != null) EditorGUILayout.HelpBox("결말: " + session.EndingId, MessageType.Info);
            EditorGUILayout.Space(); EditorGUILayout.LabelField("허용된 게임플레이", EditorStyles.boldLabel);
            if (session.ActiveEvent != null) foreach (var signal in session.ActiveEvent.Signals) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.SelectableLabel(signal.Id, GUILayout.Height(18));
                foreach (var effect in signal.Effects) EditorGUILayout.LabelField(session.Chapter.Symbols[effect.Key].Name + " : " + session.State[effect.Key] + (effect.Operator == "add" ? " + " : " → ") + effect.Value, EditorStyles.wordWrappedLabel);
                var reason = session.GetSignalBlocker(signal.Id);
                using (new EditorGUI.DisabledScope(reason != null)) if (GUILayout.Button(signal.Label)) runner.SendGameplay(signal.Id);
                if (reason != null) EditorGUILayout.LabelField(reason, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }
            using (new EditorGUI.DisabledScope(session.ActiveEventId == null)) if (GUILayout.Button("현재 사건 종료")) runner.EndCurrentEvent();
            EditorGUILayout.Space(); EditorGUILayout.LabelField("연결된 사건 / 시작 트리거", EditorStyles.boldLabel);
            foreach (var next in session.GetConnectedEvents()) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                string reason = session.GetEligibilityBlocker(next.Id);
                EditorGUILayout.LabelField(next.Title + (reason == null ? " · 조건 충족" : " · 잠김"), EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(next.StartTrigger, GUILayout.Height(18));
                foreach (var c in next.EntryCondition.All) RuleLabel(session, c);
                foreach (var path in next.EntryAlternatives) {
                    EditorGUILayout.LabelField("대체 진입 조건 (OR)", EditorStyles.miniBoldLabel);
                    foreach (var c in path.All) RuleLabel(session, c);
                }
                var previousId = session.ActiveEventId ?? session.LastCompletedEventId;
                if (previousId != null) foreach (var exit in session.Chapter.Events[previousId].Exits.Where(e => e.To == next.Id)) {
                    foreach (var c in exit.Condition.All) RuleLabel(session, c);
                }
                if (reason != null) EditorGUILayout.LabelField(reason, EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(session.GetStartBlocker(next.Id) != null)) if (GUILayout.Button("사건 시작")) runner.StartEvent(next.StartTrigger);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("현재 심볼", EditorStyles.boldLabel);
            foreach (var symbol in session.Chapter.Symbols.Values) EditorGUILayout.LabelField(symbol.Name + (session.IsLocked(symbol.Key) ? " 🔒" : ""), session.State[symbol.Key].ToString(), EditorStyles.wordWrappedLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("진행 저장")) runner.SaveProgress();
            if (GUILayout.Button("진행 불러오기")) runner.LoadProgress();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.SelectableLabel(runner.SavePath, GUILayout.Height(36));
            if (GUILayout.Button("새 세션으로 초기화") && EditorUtility.DisplayDialog("ChapterFlow", "현재 메모리의 진행을 초기화할까요? 저장 파일은 유지됩니다.", "초기화", "취소")) runner.LoadChapter();
            EditorGUILayout.EndScrollView();
        }
        private static void RuleLabel(ChapterSession session, Rule rule)
        {
            EditorGUILayout.LabelField(session.Chapter.Symbols[rule.Key].Name + " " + rule.Operator + " " + rule.Value + " (현재 " + session.State[rule.Key] + ")", EditorStyles.wordWrappedMiniLabel);
        }
    }
}
