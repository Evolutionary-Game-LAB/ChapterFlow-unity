using UnityEngine;

namespace ChapterFlow.Unity
{
    public sealed class ChapterAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] private string sourceJson;
        public string SourceJson { get { return sourceJson; } }
        public ChapterDefinition CreateDefinition() { return ChapterDefinition.Parse(sourceJson); }
        public void Initialize(string json) { ChapterDefinition.Parse(json); sourceJson = json; }
    }
}
