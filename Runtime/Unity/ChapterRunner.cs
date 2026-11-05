using System;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace ChapterFlow.Unity
{
    [Serializable] public sealed class ChapterStringEvent : UnityEvent<string> { }

    [AddComponentMenu("ChapterFlow/Chapter Runner")]
    public sealed class ChapterRunner : MonoBehaviour
    {
        [SerializeField] private ChapterAsset chapter;
        [Tooltip("Optional: a JSON Export dragged directly from the Project window.")]
        [SerializeField] private TextAsset chapterJson;
        [Tooltip("Load the chapter on Start; does not start its first event.")]
        [SerializeField] private bool initializeOnStart = true;
        [Tooltip("Optional explicit trigger for the first event on Start.")]
        [SerializeField] private bool startFirstEventOnStart;
        [SerializeField] private string saveSlot = "chapterflow-save.json";
        [SerializeField] private ChapterStringEvent onEventStarted = new ChapterStringEvent();
        [SerializeField] private ChapterStringEvent onEventEnded = new ChapterStringEvent();
        [SerializeField] private ChapterStringEvent onGameplayReceived = new ChapterStringEvent();
        [SerializeField] private ChapterStringEvent onEndingReached = new ChapterStringEvent();
        [SerializeField] private ChapterStringEvent onError = new ChapterStringEvent();
        [SerializeField] private UnityEvent onStateChanged = new UnityEvent();
        [SerializeField] private UnityEvent onSessionRestored = new UnityEvent();
        public ChapterSession Session { get; private set; }
        public string LastError { get; private set; }
        public ChapterStringEvent OnEventStarted { get { return onEventStarted; } }
        public ChapterStringEvent OnEventEnded { get { return onEventEnded; } }
        public ChapterStringEvent OnGameplayReceived { get { return onGameplayReceived; } }
        public ChapterStringEvent OnEndingReached { get { return onEndingReached; } }
        public ChapterStringEvent OnError { get { return onError; } }
        public UnityEvent OnStateChanged { get { return onStateChanged; } }
        public UnityEvent OnSessionRestored { get { return onSessionRestored; } }
        public string SavePath { get { return Path.Combine(Application.persistentDataPath, Path.GetFileName(string.IsNullOrWhiteSpace(saveSlot) ? "chapterflow-save.json" : saveSlot)); } }

        private void Start() { if (initializeOnStart && InitializeChapter() && startFirstEventOnStart) StartFirstEvent(); }
        public void SetChapter(ChapterAsset asset) { chapter = asset; chapterJson = null; }
        public void SetJson(TextAsset json) { chapterJson = json; chapter = null; }
        private bool Fail(string error) { LastError = error; Debug.LogWarning("ChapterFlow: " + error, this); onError.Invoke(error); return false; }
        private bool Ready() { return Session != null || Fail("먼저 챕터를 로드해 주세요."); }
        private bool Accepted(CommandResult result) { if (!result.Success) return Fail(result.Error); LastError = ""; return true; }
        public bool InitializeChapter()
        {
            try {
                if (chapter == null && chapterJson == null) return Fail("Chapter Asset 또는 JSON을 지정해 주세요.");
                var definition = chapter != null ? chapter.CreateDefinition() : ChapterDefinition.Parse(chapterJson.text);
                Session = new ChapterSession(definition); LastError = "";
            } catch (Exception e) { return Fail(e.Message); }
            onStateChanged.Invoke(); return true;
        }
        // Void wrappers are selectable by Unity UI Button / UnityEvent in the Inspector.
        public void LoadChapter() { InitializeChapter(); }
        public void StartFirstEvent() { if (Ready()) StartEvent(Session.Chapter.Events[Session.Chapter.StartEventId].StartTrigger); }
        public void StartEvent(string startTrigger) { TryStartEvent(startTrigger); }
        public bool TryStartEvent(string startTrigger)
        {
            if (!Ready() || !Accepted(Session.RequestEventStart(startTrigger))) return false;
            var id = Session.ActiveEventId; var ending = Session.EndingId;
            onEventStarted.Invoke(id);
            if (ending != null) onEndingReached.Invoke(ending);
            onStateChanged.Invoke(); return true;
        }
        public void SendGameplay(string signalId) { TrySendGameplay(signalId); }
        public bool TrySendGameplay(string signalId)
        {
            if (!Ready() || !Accepted(Session.ReceiveGameplaySignal(signalId))) return false;
            onGameplayReceived.Invoke(signalId); onStateChanged.Invoke(); return true;
        }
        /// <summary>Execute only the declared effects of one symbol, like the web playtest.</summary>
        public bool TrySendSymbolGameplay(string signalId, string symbolKey)
        {
            if (!Ready() || !Accepted(Session.ReceiveGameplaySignal(signalId, symbolKey))) return false;
            onGameplayReceived.Invoke(signalId); onStateChanged.Invoke(); return true;
        }
        public void EndCurrentEvent() { if (Ready()) TryEndEvent(Session.ActiveEventId); }
        public void EndEvent(string eventId) { TryEndEvent(eventId); }
        public bool TryEndEvent(string eventId)
        {
            if (!Ready() || !Accepted(Session.RequestEventEnd(eventId))) return false;
            onEventEnded.Invoke(eventId); onStateChanged.Invoke(); return true;
        }
        public string CaptureSaveJson() { if (!Ready()) return null; return Session.SaveJson(); }
        public bool RestoreSaveJson(string json)
        {
            if (!Ready() || !Accepted(Session.LoadJson(json))) return false;
            // Restore never replays gameplay/cutscene start events. Reconcile world state here.
            onSessionRestored.Invoke(); onStateChanged.Invoke(); return true;
        }
        public void SaveProgress()
        {
            if (!Ready()) return;
            try {
                string path = SavePath; Directory.CreateDirectory(Path.GetDirectoryName(path));
                string pending = path + ".tmp"; File.WriteAllText(pending, Session.SaveJson());
                if (File.Exists(path)) File.Replace(pending, path, path + ".bak"); else File.Move(pending, path);
                LastError = "";
            } catch (Exception e) { Fail("저장 실패: " + e.Message); }
        }
        public void LoadProgress()
        {
            if (!Ready()) return;
            try { RestoreSaveJson(File.ReadAllText(SavePath)); } catch (Exception e) { Fail("불러오기 실패: " + e.Message); }
        }
    }
}
