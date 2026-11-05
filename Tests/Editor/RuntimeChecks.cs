using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ChapterFlow.Tests
{
    // Shared by Unity EditMode test and the no-Unity PowerShell verification harness.
    public static class RuntimeChecks
    {
        public static int Run(string sample)
        {
            int checks = 0;
            Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception(message); checks++; };
            var c = ChapterDefinition.Parse(sample); var s = new ChapterSession(c);
            string evidence = ChapterDefinition.SymbolKey("evidence"), trust = ChapterDefinition.SymbolKey("trust");
            check(s.ActiveEventId == null, "Loading must not auto-start.");
            check(!s.ReceiveGameplaySignal("find_badge").Success, "No gameplay before start.");
            check(!s.RequestEventStart("start:control").Success, "Cannot skip first event.");
            check(!s.RequestEventStart("unknown").Success, "Unknown trigger rejected.");
            check(s.RequestEventStart("start:lobby").Success, "Start lobby.");
            check(!s.GetAvailableEvents().Any(e => e.Id == "control"), "Badge must gate control.");
            check(s.ReceiveGameplaySignal("find_badge").Success, "Badge gameplay succeeds.");
            check(s.ActiveEventId == "lobby", "Gameplay must not auto-advance.");
            check(s.GetAvailableEvents().Any(e => e.Id == "control"), "Eligibility preview updates immediately.");
            check(!s.RequestEventStart("start:control").Success, "Explicit end is required.");
            check(!s.ReceiveGameplaySignal("read_evidence").Success, "Actions scoped to active event.");
            check(!s.ReceiveGameplaySignal("find_badge").Success, "Once-per-visit applies.");
            check(!s.RequestEventEnd("control").Success, "Cannot end another event.");
            check(s.RequestEventEnd("lobby").Success && s.ActiveEventId == null, "End does not auto-start successor.");
            check(!s.ReceiveGameplaySignal("find_badge").Success, "No gameplay between events.");
            check(s.RequestEventStart("start:control").Success, "Start uses state, not legacy requiredSignal.");
            check(s.ReceiveGameplaySignal("read_evidence").Success && s.IsLocked(evidence), "Irreversible lock set.");
            check(s.ReceiveGameplaySignal("reassure").Success && s.ReceiveGameplaySignal("reassure").Success, "Repeatable number change.");
            check(s.State[trust].Number == .6, "Integer hundredths prevent floating-point drift.");
            check(s.GetAvailableEvents().Any(e => e.Id == "cooperate") && !s.GetAvailableEvents().Any(e => e.Id == "independent"), "Branches follow resulting state.");
            var saved = s.SaveJson(); var restored = new ChapterSession(c);
            check(restored.LoadJson(saved).Success && restored.SaveJson() == saved, "Save restores exact active state, receipts, locks, history.");
            check(!restored.ReceiveGameplaySignal("read_evidence").Success, "Once-per-visit preserved by save.");
            check(restored.RequestEventEnd("control").Success && restored.RequestEventStart("start:cooperate").Success, "Reach cooperative ending.");
            check(restored.EndingId == "cooperative", "Correct ending.");
            check(restored.RequestEventEnd("cooperate").Success && restored.EndingId == "cooperative", "Ending persists after explicit end.");
            check(!restored.RequestEventStart("start:lobby").Success, "Terminal cannot restart silently.");
            for (int i = 0; i < 20; i++) s.ReceiveGameplaySignal("reassure");
            check(s.State[trust].Number == 1, "Numeric addition clamps at one.");
            for (int i = 0; i < 20; i++) s.ReceiveGameplaySignal("pressure");
            check(s.State[trust].Number == 0, "Numeric addition clamps at zero.");
            check(s.RequestEventEnd("control").Success && s.RequestEventStart("start:independent").Success && s.EndingId == "independent", "Reach independent ending.");
            var early = new ChapterSession(c); early.RequestEventStart("start:lobby");
            check(early.RequestEventEnd("lobby").Success, "Ending event never requires gameplay.");
            check(!early.RequestEventStart("start:control").Success, "Conditions still checked after end.");
            check(early.RequestEventStart("start:leave").Success && early.EndingId == "leave", "Reach early ending.");

            Action<JObject, string> badSave = (bad, label) => { var before = restored.SaveJson(); check(!restored.LoadJson(bad.ToString()).Success && before == restored.SaveJson(), label); };
            var corrupt = JObject.Parse(saved); corrupt["fingerprint"] = "wrong"; badSave(corrupt, "Wrong chapter revision rejected atomically.");
            corrupt = JObject.Parse(saved); corrupt["state"][trust] = true; badSave(corrupt, "Save type mismatch rejected.");
            corrupt = JObject.Parse(saved); corrupt["state"][trust] = 1.01; badSave(corrupt, "Out-of-range save rejected.");
            corrupt = JObject.Parse(saved); corrupt["lockedSymbols"] = new JArray(); badSave(corrupt, "Cannot erase irreversible lock in save.");
            corrupt = JObject.Parse(saved); corrupt["activeEventId"] = "missing"; badSave(corrupt, "Unknown saved event rejected.");
            corrupt = JObject.Parse(saved); corrupt["completedEvents"] = new JArray("control"); corrupt["lastCompletedEventId"] = "control"; badSave(corrupt, "Invalid saved path rejected.");
            check(!restored.LoadJson("{broken").Success, "Malformed save rejected.");

            Action<JObject, string> badChapter = (bad, label) => { bool rejected = false; try { ChapterDefinition.Parse(bad.ToString()); } catch { rejected = true; } check(rejected, label); };
            var doc = JObject.Parse(sample); doc["gameplay"]["events"][0]["exits"][0]["to"] = "missing"; badChapter(doc, "Dangling exit rejected.");
            doc = JObject.Parse(sample); doc["gameplay"]["initialState"][1]["value"] = .333; badChapter(doc, "Invalid numeric precision rejected.");
            doc = JObject.Parse(sample); doc["gameplay"]["events"][1]["id"] = "lobby"; badChapter(doc, "Duplicate event rejected.");
            doc = JObject.Parse(sample); doc["gameplay"]["events"][1]["startTrigger"] = "start:lobby"; badChapter(doc, "Duplicate trigger rejected.");
            doc = JObject.Parse(sample); doc["gameplay"]["events"][1]["signals"][1]["effects"][0]["ref"]["symbol"] = "missing"; badChapter(doc, "Unknown effect symbol rejected.");
            doc = JObject.Parse(sample); doc.Remove("gameplay"); badChapter(doc, "Old source without gameplay rejected with re-export guidance.");

            doc = JObject.Parse(sample);
            var actions = (JArray)doc["gameplay"]["events"][1]["signals"];
            var reverse = (JObject)actions[0].DeepClone(); reverse["id"] = "reverse"; reverse["condition"] = JObject.Parse("{\"all\":[]}"); reverse["effects"][0]["value"] = false;
            ((JArray)reverse["effects"]).Add(JObject.Parse("{\"ref\":{\"symbol\":\"trust\",\"args\":[]},\"op\":\"set\",\"value\":1}")); actions.Add(reverse);
            var reverseSession = new ChapterSession(ChapterDefinition.Parse(doc.ToString()));
            // Fingerprint intentionally differs; prepare through real commands instead of modifying saves.
            reverseSession.RequestEventStart("start:lobby"); reverseSession.ReceiveGameplaySignal("find_badge"); reverseSession.RequestEventEnd("lobby"); reverseSession.RequestEventStart("start:control"); reverseSession.ReceiveGameplaySignal("read_evidence");
            var prior = reverseSession.SaveJson();
            check(!reverseSession.ReceiveGameplaySignal("reverse").Success && reverseSession.SaveJson() == prior, "Irreversible failure is atomic across all effects.");

            doc = JObject.Parse(sample);
            ((JArray)doc["gameplay"]["endings"]).Add(JObject.Parse("{\"endingId\":\"conflict\",\"condition\":{\"all\":[]}}"));
            var ambiguous = new ChapterSession(ChapterDefinition.Parse(doc.ToString())); ambiguous.RequestEventStart("start:lobby");
            check(ambiguous.ReceiveGameplaySignal("find_badge").Success, "Gameplay not prematurely gated by ending ambiguity."); ambiguous.RequestEventEnd("lobby");
            check(!ambiguous.RequestEventStart("start:leave").Success, "Ambiguous ending blocked at start.");

            doc = JObject.Parse(sample);
            doc["gameplay"]["events"][1]["entryAlternatives"] = JArray.Parse("[{\"all\":[{\"ref\":{\"symbol\":\"trust\",\"args\":[]},\"op\":\"eq\",\"value\":1}]},{\"all\":[]}]");
            var alternative = new ChapterSession(ChapterDefinition.Parse(doc.ToString())); alternative.RequestEventStart("start:lobby");
            check(alternative.GetEligibilityBlocker("control") != null, "OR does not bypass common entry condition."); alternative.ReceiveGameplaySignal("find_badge"); alternative.RequestEventEnd("lobby");
            check(alternative.RequestEventStart("start:control").Success, "One matching entry alternative is sufficient.");

            doc = JObject.Parse(sample); doc["gameplay"]["addedSymbols"][1]["changeMode"] = "irreversible";
            var numericLock = new ChapterSession(ChapterDefinition.Parse(doc.ToString()));
            numericLock.RequestEventStart("start:lobby"); numericLock.ReceiveGameplaySignal("find_badge"); numericLock.RequestEventEnd("lobby"); numericLock.RequestEventStart("start:control");
            check(numericLock.ReceiveGameplaySignal("reassure").Success && numericLock.IsLocked(trust), "Numeric symbol locks on first actual change.");
            check(!numericLock.ReceiveGameplaySignal("reassure").Success && numericLock.State[trust].Number == .4, "Irreversible means no second change, not just no decrease.");

            doc = JObject.Parse(sample);
            ((JArray)doc["gameplay"]["events"][4]["signals"]).Add(JObject.Parse("{\"id\":\"late_evidence\",\"label\":\"Late permitted evidence\",\"condition\":{\"all\":[]},\"effects\":[{\"ref\":{\"symbol\":\"evidence\",\"args\":[]},\"op\":\"set\",\"value\":true}],\"oncePerVisit\":true}"));
            var editedEnd = new ChapterSession(ChapterDefinition.Parse(doc.ToString()));
            editedEnd.RequestEventStart("start:lobby"); editedEnd.RequestEventEnd("lobby"); editedEnd.RequestEventStart("start:leave"); editedEnd.ReceiveGameplaySignal("late_evidence");
            check(editedEnd.EndingId == null && editedEnd.LoadJson(editedEnd.SaveJson()).Success, "Restore does not incorrectly re-check entry conditions after author-permitted changes.");
            return checks;
        }
    }
}
