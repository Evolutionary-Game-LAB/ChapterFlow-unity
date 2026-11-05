using UnityEngine;
using UnityEngine.Events;

namespace ChapterFlow.Unity
{
    /// <summary>Bind one chapter event to gameplay objects, dialogue, or cutscenes without writing an ID filter.</summary>
    [AddComponentMenu("ChapterFlow/Event Binding")]
    public sealed class ChapterEventBinding : MonoBehaviour
    {
        [SerializeField] private ChapterRunner runner;
        [SerializeField] private string eventId;
        [SerializeField] private UnityEvent onStarted = new UnityEvent();
        [SerializeField] private UnityEvent onEnded = new UnityEvent();
        private ChapterRunner bound;
        private void OnEnable()
        {
            bound = runner;
            if (bound == null) return;
            bound.OnEventStarted.AddListener(Started); bound.OnEventEnded.AddListener(Ended);
        }
        private void OnDisable()
        {
            if (bound == null) return;
            bound.OnEventStarted.RemoveListener(Started); bound.OnEventEnded.RemoveListener(Ended); bound = null;
        }
        private void Started(string id) { if (id == eventId) onStarted.Invoke(); }
        private void Ended(string id) { if (id == eventId) onEnded.Invoke(); }
    }
}
