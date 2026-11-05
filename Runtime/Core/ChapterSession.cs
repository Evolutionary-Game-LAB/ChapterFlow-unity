using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChapterFlow
{
    public sealed class CommandResult
    {
        public bool Success { get; private set; }
        public string Error { get; private set; }
        internal static CommandResult Ok() { return new CommandResult { Success = true, Error = "" }; }
        internal static CommandResult Fail(string error) { return new CommandResult { Error = error }; }
    }

    /// <summary>Deterministic, offline, main-thread runtime. No LLM or unrestricted state setters.</summary>
    public sealed class ChapterSession
    {
        public ChapterDefinition Chapter { get; private set; }
        public string ActiveEventId { get; private set; }
        public string LastCompletedEventId { get; private set; }
        private Dictionary<string, SymbolValue> state;
        private HashSet<string> usedSignals = new HashSet<string>();
        private Dictionary<string, Dictionary<string, SymbolValue>> partialResults = new Dictionary<string, Dictionary<string, SymbolValue>>();
        private static string PartialId(string id, string key) { return "symbol-play:" + JsonConvert.SerializeObject(new[] { id, key }); }
        private HashSet<string> lockedSymbols = new HashSet<string>();
        private List<string> completed = new List<string>();
        public IReadOnlyDictionary<string, SymbolValue> State { get { return new ReadOnlyDictionary<string, SymbolValue>(state); } }
        public IReadOnlyList<string> CompletedEvents { get { return completed.AsReadOnly(); } }
        public ChapterEvent ActiveEvent { get { return ActiveEventId == null ? null : Chapter.Events[ActiveEventId]; } }
        public bool IsLocked(string key) { return lockedSymbols.Contains(key); }
        public string EndingId
        {
            get {
                string current = ActiveEventId ?? LastCompletedEventId;
                if (current == null || Chapter.Events[current].EndingId == null) return null;
                var matches = Chapter.Endings.Where(e => e.Condition.Matches(state)).ToList();
                return matches.Count == 1 && matches[0].Id == Chapter.Events[current].EndingId ? matches[0].Id : null;
            }
        }
        public ChapterSession(ChapterDefinition chapter)
        {
            Chapter = chapter ?? throw new ArgumentNullException(nameof(chapter));
            state = chapter.Symbols.ToDictionary(s => s.Key, s => s.Value.Initial);
        }
        private string StartBlocker(string targetId, bool preview)
        {
            ChapterEvent target;
            if (!Chapter.Events.TryGetValue(targetId, out target)) return "등록되지 않은 사건입니다.";
            if (ActiveEventId != null && !preview) return "현재 사건을 먼저 종료해 주세요.";
            var previousId = preview ? (ActiveEventId ?? LastCompletedEventId) : LastCompletedEventId;
            if (previousId == null) {
                if (targetId != Chapter.StartEventId) return "챕터의 첫 사건부터 시작해 주세요.";
            } else {
                var previous = Chapter.Events[previousId];
                if (previous.EndingId != null) return "챕터가 종료되었습니다.";
                var exits = previous.Exits.Where(e => e.To == targetId).ToList();
                if (exits.Count == 0) return "이전 사건에서 연결되지 않은 사건입니다.";
                if (!exits.Any(e => e.Condition.Matches(state))) return "연결 경로의 진입 조건을 만족하지 않습니다.";
            }
            if (!target.EntryCondition.Matches(state) || (target.EntryAlternatives.Count > 0 && !target.EntryAlternatives.Any(c => c.Matches(state)))) return "사건 진입 조건을 만족하지 않습니다.";
            if (target.EndingId != null) {
                var matches = Chapter.Endings.Where(e => e.Condition.Matches(state)).ToList();
                if (matches.Count != 1 || matches[0].Id != target.EndingId) return "결말 조건이 미충족이거나 다른 결말과 충돌합니다.";
            }
            return null;
        }
        public string GetStartBlocker(string eventId) { return StartBlocker(eventId, false); }
        /// <summary>Preview eligibility after explicit end; does not end or start anything.</summary>
        public string GetEligibilityBlocker(string eventId) { return StartBlocker(eventId, true); }
        public IReadOnlyList<ChapterEvent> GetConnectedEvents()
        {
            var previous = ActiveEventId ?? LastCompletedEventId;
            return (previous == null ? new[] { Chapter.Events[Chapter.StartEventId] } : Chapter.Events[previous].Exits.Select(e => Chapter.Events[e.To]).Distinct()).ToList().AsReadOnly();
        }
        public IReadOnlyList<ChapterEvent> GetAvailableEvents() { return GetConnectedEvents().Where(e => GetEligibilityBlocker(e.Id) == null).ToList().AsReadOnly(); }
        public CommandResult RequestEventStart(string trigger)
        {
            var target = Chapter.Events.Values.FirstOrDefault(e => e.StartTrigger == trigger);
            if (target == null) return CommandResult.Fail("등록되지 않은 사건 시작 트리거입니다.");
            var reason = GetStartBlocker(target.Id); if (reason != null) return CommandResult.Fail(reason);
            ActiveEventId = target.Id; usedSignals.Clear(); partialResults.Clear(); return CommandResult.Ok();
        }
        public CommandResult RequestEventEnd(string eventId)
        {
            if (ActiveEventId == null || ActiveEventId != eventId) return CommandResult.Fail("현재 진행 중인 사건만 종료할 수 있습니다.");
            completed.Add(eventId); LastCompletedEventId = eventId; ActiveEventId = null; usedSignals.Clear(); partialResults.Clear(); return CommandResult.Ok();
        }
        private string PrepareSignal(string signalId, out Dictionary<string, SymbolValue> next, out HashSet<string> locks, string symbolKey = null)
        {
            next = null; locks = null;
            if (ActiveEvent == null) return "진행 중인 사건이 없습니다.";
            var signal = ActiveEvent.Signals.FirstOrDefault(s => s.Id == signalId);
            if (signal == null) return "현재 사건에서 허용되지 않은 게임플레이 신호입니다.";
            if (signal.OncePerVisit && usedSignals.Contains(signal.Id)) return "이미 처리한 신호입니다.";
            if (symbolKey != null && !signal.Effects.Any(e => e.Key == symbolKey)) return "이 행동에서 허용되지 않은 심볼입니다.";
            if (symbolKey != null && signal.OncePerVisit && usedSignals.Contains(PartialId(signalId, symbolKey))) return "이미 처리한 신호입니다.";
            var completedKeys = new HashSet<string>(signal.OncePerVisit ? signal.Effects.Select(e => e.Key).Where(k => usedSignals.Contains(PartialId(signalId, k))) : Enumerable.Empty<string>());
            foreach (var rule in signal.Condition.All) {
                Dictionary<string, SymbolValue> receipt;
                if (completedKeys.Contains(rule.Key) && partialResults.TryGetValue(PartialId(signalId, rule.Key), out receipt) && receipt.ContainsKey(rule.Key) && receipt[rule.Key].Equals(state[rule.Key])) continue;
                if (!rule.Matches(state)) return "게임플레이 실행 조건을 만족하지 않습니다.";
            }
            next = new Dictionary<string, SymbolValue>(state); locks = new HashSet<string>(lockedSymbols);
            foreach (var effect in signal.Effects.Where(e => symbolKey == null ? !completedKeys.Contains(e.Key) : e.Key == symbolKey)) {
                var old = next[effect.Key];
                next[effect.Key] = effect.Operator == "set" ? effect.Value : new SymbolValue(false, Math.Max(0, Math.Min(100, old.ticks + effect.Value.ticks)));
            }
            foreach (var symbol in Chapter.Symbols.Values) {
                if (!symbol.Irreversible || next[symbol.Key].Equals(state[symbol.Key])) continue;
                if (locks.Contains(symbol.Key)) return "되돌림 불가 심볼은 한 번 변경한 뒤 다시 변경할 수 없습니다.";
                locks.Add(symbol.Key);
            }
            return null;
        }
        public string GetSignalBlocker(string signalId, string symbolKey = null)
        {
            Dictionary<string, SymbolValue> next; HashSet<string> locks;
            return PrepareSignal(signalId, out next, out locks, symbolKey);
        }
        public CommandResult ReceiveGameplaySignal(string signalId, string symbolKey = null)
        {
            Dictionary<string, SymbolValue> next; HashSet<string> locks;
            var reason = PrepareSignal(signalId, out next, out locks, symbolKey);
            if (reason != null) return CommandResult.Fail(reason);
            state = next; lockedSymbols = locks;
            if (symbolKey == null) usedSignals.Add(signalId);
            else {
                var id = PartialId(signalId, symbolKey); usedSignals.Add(id);
                partialResults[id] = new Dictionary<string, SymbolValue> { [symbolKey] = state[symbolKey] };
                var action = ActiveEvent.Signals.First(a => a.Id == signalId);
                if (action.OncePerVisit && action.Effects.All(e => usedSignals.Contains(PartialId(signalId, e.Key)))) usedSignals.Add(signalId);
            }
            return CommandResult.Ok();
        }
        public string SaveJson()
        {
            return new JObject {
                ["format"] = "chapterflow-unity-save-v1", ["storyId"] = Chapter.StoryId, ["fingerprint"] = Chapter.Fingerprint,
                ["activeEventId"] = ActiveEventId, ["lastCompletedEventId"] = LastCompletedEventId,
                ["state"] = new JObject(state.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => new JProperty(s.Key, s.Value.ToToken()))),
                ["completedEvents"] = new JArray(completed), ["usedSignals"] = new JArray(usedSignals.OrderBy(x => x, StringComparer.Ordinal)),
                ["partialResults"] = new JObject(partialResults.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new JProperty(p.Key, new JObject(p.Value.Select(v => new JProperty(v.Key, v.Value.ToToken())))))),
                ["lockedSymbols"] = new JArray(lockedSymbols.OrderBy(x => x, StringComparer.Ordinal))
            }.ToString(Formatting.Indented);
        }
        // Validate into temporary structures. A rejected save never damages the live session.
        public CommandResult LoadJson(string json)
        {
            try {
                var data = ChapterDefinition.ReadObject(json);
                if ((string)data["format"] != "chapterflow-unity-save-v1" || (string)data["storyId"] != Chapter.StoryId || (string)data["fingerprint"] != Chapter.Fingerprint) throw new FormatException("다른 챕터 또는 다른 버전의 저장 데이터입니다.");
                var active = ChapterDefinition.NullableText(data["activeEventId"], "activeEventId");
                var last = ChapterDefinition.NullableText(data["lastCompletedEventId"], "lastCompletedEventId");
                if ((active != null && !Chapter.Events.ContainsKey(active)) || (last != null && !Chapter.Events.ContainsKey(last))) throw new FormatException("저장된 사건 ID가 유효하지 않습니다.");
                var raw = ChapterDefinition.Object(data["state"], "state"); var next = new Dictionary<string, SymbolValue>();
                if (raw.Count != state.Count) throw new FormatException("저장된 심볼 개수가 다릅니다.");
                foreach (var p in raw.Properties()) {
                    SymbolDefinition symbol; var value = SymbolValue.Parse(p.Value, false);
                    if (!Chapter.Symbols.TryGetValue(p.Name, out symbol) || symbol.Initial.IsBoolean != value.IsBoolean) throw new FormatException("저장된 심볼 형식이 다릅니다.");
                    next.Add(p.Name, value);
                }
                var history = ChapterDefinition.Array(data["completedEvents"], "completedEvents", 100000).Select(t => ChapterDefinition.Text(t, "completed event")).ToList();
                if (history.Any(id => !Chapter.Events.ContainsKey(id)) || history.LastOrDefault() != last) throw new FormatException("완료 이력과 마지막 사건이 다릅니다.");
                if (history.Count > 0 && history[0] != Chapter.StartEventId) throw new FormatException("완료 이력의 시작이 잘못되었습니다.");
                for (int i = 1; i < history.Count; i++) if (Chapter.Events[history[i-1]].EndingId != null || !Chapter.Events[history[i-1]].Exits.Any(x => x.To == history[i])) throw new FormatException("완료 이력의 연결이 잘못되었습니다.");
                if (active != null && (last == null ? active != Chapter.StartEventId : Chapter.Events[last].EndingId != null || !Chapter.Events[last].Exits.Any(e => e.To == active))) throw new FormatException("활성 사건과 완료 이력이 연결되지 않습니다.");
                var usedList = ChapterDefinition.Array(data["usedSignals"], "usedSignals").Select(t => ChapterDefinition.Text(t, "signal ID")).ToList();
                var permitted = active == null ? new List<GameplaySignal>() : Chapter.Events[active].Signals.ToList();
                var partialKeys = new Dictionary<string, string>();
                foreach (var action in permitted) foreach (var effect in action.Effects) partialKeys[PartialId(action.Id, effect.Key)] = effect.Key;
                if (usedList.Distinct().Count() != usedList.Count || usedList.Any(id => !permitted.Any(a => a.Id == id) && !partialKeys.ContainsKey(id))) throw new FormatException("저장된 게임플레이 이력이 유효하지 않습니다.");
                var restoredPartial = new Dictionary<string, Dictionary<string, SymbolValue>>();
                var partialRaw = data["partialResults"] == null ? new JObject() : ChapterDefinition.Object(data["partialResults"], "partialResults");
                foreach (var row in partialRaw.Properties()) {
                    string key;
                    if (!partialKeys.TryGetValue(row.Name, out key) || !usedList.Contains(row.Name)) throw new FormatException("잘못된 심볼 실행 이력입니다.");
                    var values = ChapterDefinition.Object(row.Value, "partial result");
                    if (values.Count != 1 || values[key] == null) throw new FormatException("잘못된 심볼 실행 결과입니다.");
                    var value = SymbolValue.Parse(values[key], false);
                    if (value.IsBoolean != Chapter.Symbols[key].Initial.IsBoolean) throw new FormatException("심볼 실행 결과 형식이 다릅니다.");
                    restoredPartial[row.Name] = new Dictionary<string, SymbolValue> { [key] = value };
                }
                if (usedList.Any(id => partialKeys.ContainsKey(id) && !restoredPartial.ContainsKey(id))) throw new FormatException("심볼 실행 결과가 누락되었습니다.");
                var lockList = ChapterDefinition.Array(data["lockedSymbols"], "lockedSymbols").Select(t => ChapterDefinition.Text(t, "locked symbol")).ToList();
                if (lockList.Distinct().Count() != lockList.Count || lockList.Any(key => !Chapter.Symbols.ContainsKey(key) || !Chapter.Symbols[key].Irreversible)) throw new FormatException("저장된 잠금 상태가 유효하지 않습니다.");
                foreach (var symbol in Chapter.Symbols.Values.Where(s => s.Irreversible)) if (lockList.Contains(symbol.Key) == next[symbol.Key].Equals(symbol.Initial)) throw new FormatException("되돌림 불가 심볼의 잠금과 값이 다릅니다.");
                if (active == null && last == null && (usedList.Count != 0 || next.Any(v => !v.Value.Equals(Chapter.Symbols[v.Key].Initial)))) throw new FormatException("시작 전 저장 상태가 초기값과 다릅니다.");
                // Entry predicates need not remain true after allowed gameplay, even in
                // an author-edited ending event. Do not re-enter/revalidate on restore.
                state = next; ActiveEventId = active; LastCompletedEventId = last; completed = history;
                usedSignals = new HashSet<string>(usedList); partialResults = restoredPartial; lockedSymbols = new HashSet<string>(lockList);
                return CommandResult.Ok();
            } catch (Exception e) when (e is FormatException || e is JsonException || e is ArgumentException || e is InvalidCastException) { return CommandResult.Fail(e.Message); }
        }
    }
}
