using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChapterFlow
{
    // Numbers use integer hundredths, matching the web runtime (no float equality drift).
    public struct SymbolValue : IEquatable<SymbolValue>
    {
        public bool IsBoolean { get; private set; }
        public bool Boolean { get { if (!IsBoolean) throw new InvalidOperationException("Not a boolean."); return ticks != 0; } }
        public double Number { get { if (IsBoolean) throw new InvalidOperationException("Not a number."); return ticks / 100d; } }
        internal int ticks;
        internal SymbolValue(bool boolean, int value) { IsBoolean = boolean; ticks = value; }
        public static SymbolValue FromBoolean(bool value) { return new SymbolValue(true, value ? 1 : 0); }
        public static SymbolValue FromNumber(double value) { return Parse(new JValue(value), false); }
        internal static SymbolValue Parse(JToken value, bool effect)
        {
            if (value != null && value.Type == JTokenType.Boolean) return FromBoolean((bool)value);
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) throw new FormatException("Symbol value must be boolean or number.");
            double n = (double)value;
            if (double.IsNaN(n) || double.IsInfinity(n) || n < (effect ? -1 : 0) || n > 1 || Math.Abs(n * 100 - Math.Round(n * 100)) > 1e-8)
                throw new FormatException("Numeric values must have at most two decimals in range " + (effect ? "-1..1." : "0..1."));
            return new SymbolValue(false, (int)Math.Round(n * 100));
        }
        internal JToken ToToken() { return IsBoolean ? new JValue(Boolean) : new JValue(Number); }
        public bool Equals(SymbolValue other) { return IsBoolean == other.IsBoolean && ticks == other.ticks; }
        public override bool Equals(object obj) { return obj is SymbolValue && Equals((SymbolValue)obj); }
        public override int GetHashCode() { return (IsBoolean ? 1000 : 0) + ticks; }
        public override string ToString() { return IsBoolean ? (Boolean ? "예" : "아니오") : Number.ToString("0.00", CultureInfo.InvariantCulture); }
    }

    public sealed class SymbolDefinition
    {
        public string Key { get; internal set; }
        public string Name { get; internal set; }
        public string Meaning { get; internal set; }
        public SymbolValue Initial { get; internal set; }
        public bool Irreversible { get; internal set; }
    }
    public sealed class Rule
    {
        public string Key { get; internal set; }
        public string Operator { get; internal set; }
        public SymbolValue Value { get; internal set; }
        internal bool Matches(IReadOnlyDictionary<string, SymbolValue> state)
        {
            SymbolValue v;
            if (!state.TryGetValue(Key, out v) || v.IsBoolean != Value.IsBoolean) return false;
            if (v.IsBoolean && Operator != "eq" && Operator != "ne") return false;
            switch (Operator) {
                case "eq": return v.Equals(Value); case "ne": return !v.Equals(Value);
                case "lt": return v.ticks < Value.ticks; case "lte": return v.ticks <= Value.ticks;
                case "gt": return v.ticks > Value.ticks; case "gte": return v.ticks >= Value.ticks;
                default: return false;
            }
        }
    }
    public sealed class Condition
    {
        public IReadOnlyList<Rule> All { get; internal set; }
        public bool Matches(IReadOnlyDictionary<string, SymbolValue> state) { return All.All(r => r.Matches(state)); }
    }
    public sealed class Effect
    {
        public string Key { get; internal set; }
        public string Operator { get; internal set; }
        public SymbolValue Value { get; internal set; }
    }
    public sealed class GameplaySignal
    {
        public string Id { get; internal set; }
        public string Label { get; internal set; }
        public Condition Condition { get; internal set; }
        public IReadOnlyList<Effect> Effects { get; internal set; }
        public bool OncePerVisit { get; internal set; }
    }
    public sealed class EventExit
    {
        public string To { get; internal set; }
        // Legacy witness ID is retained for inspection, never required to start an event.
        public string RequiredSignal { get; internal set; }
        public Condition Condition { get; internal set; }
    }
    public sealed class ChapterEvent
    {
        public string Id { get; internal set; }
        public string Title { get; internal set; }
        public string Summary { get; internal set; }
        public string StartTrigger { get; internal set; }
        public string EndingId { get; internal set; }
        public Condition EntryCondition { get; internal set; }
        public IReadOnlyList<Condition> EntryAlternatives { get; internal set; }
        public IReadOnlyList<GameplaySignal> Signals { get; internal set; }
        public IReadOnlyList<EventExit> Exits { get; internal set; }
    }
    public sealed class EndingRule
    {
        public string Id { get; internal set; }
        public Condition Condition { get; internal set; }
    }

    public sealed class ChapterDefinition
    {
        public string StoryId { get; private set; }
        public string Title { get; private set; }
        public string Fingerprint { get; private set; }
        public string StartEventId { get; private set; }
        public IReadOnlyDictionary<string, SymbolDefinition> Symbols { get; private set; }
        public IReadOnlyDictionary<string, ChapterEvent> Events { get; private set; }
        public IReadOnlyList<EndingRule> Endings { get; private set; }

        public static string SymbolKey(string symbol, params string[] args) { return JsonConvert.SerializeObject(new[] { symbol }.Concat(args)); }
        internal static JObject Object(JToken token, string name) { return token as JObject ?? throw new FormatException(name + " must be an object."); }
        internal static JArray Array(JToken token, string name, int max = 256) {
            var a = token as JArray; if (a == null || a.Count > max) throw new FormatException(name + " must be an array (max " + max + ")."); return a;
        }
        internal static string Text(JToken token, string name) {
            if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token)) throw new FormatException(name + " must be a nonempty string.");
            return (string)token;
        }
        internal static string NullableText(JToken token, string name) { return token == null || token.Type == JTokenType.Null ? null : Text(token, name); }
        internal static JObject ReadObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 16 * 1024 * 1024) throw new FormatException("Missing or oversized JSON.");
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 64 }) {
                var obj = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new FormatException("Unexpected content after JSON object.");
                return obj;
            }
        }
        private static string Ref(JToken token)
        {
            var r = Object(token, "ref");
            var symbol = Text(r["symbol"], "ref.symbol");
            var args = Array(r["args"], "ref.args", 2).Select(a => Text(a, "ref.args[]")).ToArray();
            return SymbolKey(symbol, args);
        }
        private static JToken Canonical(JToken t) {
            if (t is JObject) return new JObject(((JObject)t).Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Canonical(p.Value))));
            if (t is JArray) return new JArray(((JArray)t).Select(Canonical));
            return t.DeepClone();
        }
        private static string Hash(JToken t) {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Canonical(t).ToString(Formatting.None)))).Replace("-", "").ToLowerInvariant();
        }
        private static Condition ParseCondition(JToken token, Dictionary<string, SymbolDefinition> symbols)
        {
            var rules = new List<Rule>();
            foreach (var r in Array(Object(token, "condition")["all"], "condition.all", 72)) {
                var key = Ref(r["ref"]); var op = Text(r["op"], "comparison.op"); var v = SymbolValue.Parse(r["value"], false);
                SymbolDefinition s;
                if (!symbols.TryGetValue(key, out s) || s.Initial.IsBoolean != v.IsBoolean) throw new FormatException("Unknown symbol or mismatched comparison type: " + key);
                if (!new[] { "eq", "ne", "lt", "lte", "gt", "gte" }.Contains(op)) throw new FormatException("Unknown comparison: " + op);
                rules.Add(new Rule { Key = key, Operator = op, Value = v });
            }
            return new Condition { All = rules.AsReadOnly() };
        }
        public static ChapterDefinition Parse(string json)
        {
            var root = ReadObject(json);
            if (root["schemaVersion"] != null && (root["gameplay"] == null || root["gameplay"].Type == JTokenType.Null)) throw new FormatException("gameplay 데이터가 없습니다. 최신 웹툴에서 챕터를 불러온 뒤 JSON Export로 다시 저장해 주세요.");
            bool envelope = root["gameplay"] != null;
            if (envelope && Text(root["schemaVersion"], "schemaVersion") != "0.6") throw new FormatException("Supported story schema: 0.6.");
            var g = envelope ? Object(root["gameplay"], "gameplay") : root;
            if (Text(g["format"], "gameplay.format") != "chapter-gameplay-v1") throw new FormatException("Supported gameplay format: chapter-gameplay-v1.");
            var fingerprint = Hash(g);
            var symbols = new Dictionary<string, SymbolDefinition>(StringComparer.Ordinal);
            foreach (var item in Array(g["initialState"], "initialState")) {
                var key = Ref(item["ref"]);
                if (symbols.ContainsKey(key)) throw new FormatException("Duplicate initial symbol: " + key);
                symbols.Add(key, new SymbolDefinition { Key = key, Name = (string)item["ref"]["symbol"], Meaning = "", Initial = SymbolValue.Parse(item["value"], false) });
            }
            var addedKeys = new HashSet<string>();
            foreach (var item in g["addedSymbols"] == null ? new JArray() : Array(g["addedSymbols"], "addedSymbols", 80)) {
                var key = Ref(item["ref"]); SymbolDefinition symbol;
                if (!symbols.TryGetValue(key, out symbol) || !addedKeys.Add(key)) throw new FormatException("Unknown or duplicate added symbol: " + key);
                var kind = Text(item["type"], "symbol.type"); var mode = Text(item["changeMode"], "changeMode");
                if ((kind != "boolean" && kind != "number") || (kind == "boolean") != symbol.Initial.IsBoolean || !SymbolValue.Parse(item["initial"], false).Equals(symbol.Initial)) throw new FormatException("Added symbol type/initial value mismatch.");
                if (mode != "free" && mode != "irreversible") throw new FormatException("Unknown change mode.");
                symbol.Irreversible = mode == "irreversible"; symbol.Name = Text(item["name"], "symbol.name"); symbol.Meaning = (string)item["meaning"] ?? "";
            }
            // Metadata is optional; gameplay is the only authoritative executable contract.
            var labels = root.SelectToken("symbolic.approval.contract.stateSymbols") as JArray;
            if (labels != null) foreach (var s in symbols.Values.Where(s => !addedKeys.Contains(s.Key))) {
                var id = (string)JArray.Parse(s.Key)[0]; var label = labels.FirstOrDefault(l => (string)l["id"] == id);
                if (label != null) { s.Name = (string)label["name"] ?? (string)label["meaning"] ?? s.Name; s.Meaning = (string)label["meaning"] ?? ""; }
            }
            var events = new Dictionary<string, ChapterEvent>(StringComparer.Ordinal); var triggers = new HashSet<string>();
            foreach (var e in Array(g["events"], "events", 64)) {
                var id = Text(e["id"], "event.id"); var trigger = Text(e["startTrigger"], "startTrigger");
                if (events.ContainsKey(id) || !triggers.Add(trigger)) throw new FormatException("Duplicate event ID or start trigger.");
                var signals = new List<GameplaySignal>(); var ids = new HashSet<string>();
                foreach (var s in Array(e["signals"], "signals")) {
                    var sid = Text(s["id"], "signal.id"); if (!ids.Add(sid)) throw new FormatException("Duplicate signal: " + sid);
                    if (s["oncePerVisit"] == null || s["oncePerVisit"].Type != JTokenType.Boolean) throw new FormatException("oncePerVisit must be boolean.");
                    var effects = new List<Effect>();
                    foreach (var effect in Array(s["effects"], "effects", 24)) {
                        var key = Ref(effect["ref"]); var op = Text(effect["op"], "effect.op"); var value = SymbolValue.Parse(effect["value"], true); SymbolDefinition symbol;
                        if (!symbols.TryGetValue(key, out symbol) || value.IsBoolean != symbol.Initial.IsBoolean) throw new FormatException("Unknown symbol or mismatched effect type: " + key);
                        if ((op != "set" && op != "add") || (op == "add" && value.IsBoolean) || (op == "set" && !value.IsBoolean && value.ticks < 0)) throw new FormatException("Invalid effect operation/value.");
                        effects.Add(new Effect { Key = key, Operator = op, Value = value });
                    }
                    signals.Add(new GameplaySignal { Id = sid, Label = Text(s["label"], "signal.label"), Condition = ParseCondition(s["condition"], symbols), Effects = effects.AsReadOnly(), OncePerVisit = (bool)s["oncePerVisit"] });
                }
                var exits = new List<EventExit>();
                foreach (var x in Array(e["exits"], "exits")) exits.Add(new EventExit { To = Text(x["to"], "exit.to"), RequiredSignal = Text(x["requiredSignal"], "requiredSignal"), Condition = ParseCondition(x["condition"], symbols) });
                var alternatives = new List<Condition>();
                if (e["entryAlternatives"] != null) {
                    var a = Array(e["entryAlternatives"], "entryAlternatives"); if (a.Count == 0) throw new FormatException("entryAlternatives cannot be empty.");
                    alternatives.AddRange(a.Select(c => ParseCondition(c, symbols)));
                }
                var node = (root.SelectToken("graph.nodes") as JArray)?.FirstOrDefault(n => (string)n["id"] == "n_" + id);
                var scene = root.SelectToken("symbolic.details.scenes") as JObject;
                var edits = root.SelectToken("chapterEdits.events") as JObject;
                events.Add(id, new ChapterEvent { Id = id, Title = (string)node?["title"] ?? id, Summary = (string)edits?[id]?["summary"] ?? (string)scene?[id]?["summary"] ?? (string)node?["sceneGoal"] ?? "", StartTrigger = trigger, EndingId = NullableText(e["endingId"], "endingId"), EntryCondition = ParseCondition(e["entryCondition"], symbols), EntryAlternatives = alternatives.AsReadOnly(), Signals = signals.AsReadOnly(), Exits = exits.AsReadOnly() });
            }
            var start = Text(g["startEventId"], "startEventId");
            if (!events.ContainsKey(start) || events[start].EndingId != null) throw new FormatException("Start must reference a non-ending event.");
            var endings = new List<EndingRule>(); var endingIds = new HashSet<string>();
            foreach (var e in Array(g["endings"], "endings", 6)) {
                var id = Text(e["endingId"], "endingId"); if (!endingIds.Add(id)) throw new FormatException("Duplicate ending ID.");
                endings.Add(new EndingRule { Id = id, Condition = ParseCondition(e["condition"], symbols) });
            }
            foreach (var e in events.Values) {
                if (e.EndingId != null && (!endingIds.Contains(e.EndingId) || e.Exits.Count > 0)) throw new FormatException("Invalid terminal event: " + e.Id);
                if (e.Exits.Any(x => !events.ContainsKey(x.To))) throw new FormatException("Dangling exit from " + e.Id);
            }
            return new ChapterDefinition { StoryId = envelope ? Text(root["storyId"], "storyId") : fingerprint, Title = envelope ? Text(root["title"], "title") : "ChapterFlow", Fingerprint = fingerprint, StartEventId = start, Symbols = new ReadOnlyDictionary<string, SymbolDefinition>(symbols), Events = new ReadOnlyDictionary<string, ChapterEvent>(events), Endings = endings.AsReadOnly() };
        }
    }
}
