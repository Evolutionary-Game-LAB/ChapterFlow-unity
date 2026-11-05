using ChapterFlow.Unity;
using UnityEngine;

namespace ChapterFlow.Samples
{
    public sealed class ChapterFlowDemoPanel : MonoBehaviour
    {
        public ChapterRunner runner;
        private Vector2 scroll;
        private string note = "Load chapter → start → gameplay → end → start next event";
        private void OnGUI()
        {
            if (runner == null) return;
            GUILayout.BeginArea(new Rect(16, 16, Mathf.Min(720, Screen.width - 32), Screen.height - 32), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("ChapterFlow · Runtime prototype");
            if (runner.Session == null) { if (GUILayout.Button("Load chapter")) runner.LoadChapter(); }
            else {
                var s = runner.Session;
                GUILayout.Label(s.Chapter.Title);
                GUILayout.Label("Current: " + (s.ActiveEvent == null ? "Waiting for event start" : s.ActiveEvent.Title));
                if (s.ActiveEvent != null) {
                    GUILayout.Label(s.ActiveEvent.Summary);
                    GUILayout.Label("1. Allowed gameplay (updates state only)");
                    foreach (var signal in s.ActiveEvent.Signals) {
                        string blocked = s.GetSignalBlocker(signal.Id); GUI.enabled = blocked == null;
                        if (GUILayout.Button(signal.Label)) { runner.SendGameplay(signal.Id); note = "Gameplay: " + signal.Id; }
                        GUI.enabled = true;
                        if (blocked != null) GUILayout.Label(blocked);
                    }
                    if (GUILayout.Button("2. End current event")) runner.EndCurrentEvent();
                }
                GUILayout.Label("3. Connected events (explicit start)");
                foreach (var e in s.GetConnectedEvents()) {
                    string blocked = s.GetEligibilityBlocker(e.Id);
                    GUI.enabled = s.ActiveEventId == null && blocked == null;
                    if (GUILayout.Button("Start: " + e.Title)) runner.StartEvent(e.StartTrigger);
                    GUI.enabled = true;
                    if (blocked != null) GUILayout.Label(blocked);
                }
                GUILayout.Space(12); GUILayout.Label("Symbols");
                foreach (var symbol in s.Chapter.Symbols.Values) GUILayout.Label(symbol.Name + " = " + s.State[symbol.Key] + (s.IsLocked(symbol.Key) ? " [locked]" : ""));
                if (s.EndingId != null) GUILayout.Label("Ending reached: " + s.EndingId);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save")) { runner.SaveProgress(); note = "Save file: " + runner.SavePath; }
                if (GUILayout.Button("Load save")) runner.LoadProgress();
                if (GUILayout.Button("Reset session")) runner.LoadChapter();
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(note);
            if (!string.IsNullOrEmpty(runner.LastError)) GUILayout.Label(runner.LastError);
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
    }
}
