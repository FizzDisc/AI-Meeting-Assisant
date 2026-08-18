import unittest
from speaker_reconciliation import reconcile

class ReconciliationTests(unittest.TestCase):
 def test_known_microphone_is_you(self):
  item=reconcile([{"start":0,"end":1,"text":"hello","source":"microphone"}],[])[0]
  self.assertEqual("You",item["speaker"]);self.assertEqual("known-source",item["speakerAssignment"])
 def test_system_speaker_uses_greatest_overlap(self):
  turns=[{"start":0,"end":2,"speaker":"SPEAKER_00"},{"start":2,"end":3,"speaker":"SPEAKER_01"}]
  item=reconcile([{"start":.2,"end":1.8,"text":"hello","source":"system_audio"}],turns)[0]
  self.assertEqual("SPEAKER_00",item["speaker"]);self.assertEqual("assigned",item["speakerAssignment"])
 def test_close_overlap_is_explicitly_ambiguous(self):
  turns=[{"start":0,"end":1,"speaker":"SPEAKER_00"},{"start":1,"end":2,"speaker":"SPEAKER_01"}]
  item=reconcile([{"start":0,"end":2,"text":"overlap","source":"system_audio"}],turns)[0]
  self.assertIsNone(item["speaker"]);self.assertEqual("ambiguous",item["speakerAssignment"])
 def test_missing_turn_is_unassigned(self):
  item=reconcile([{"start":5,"end":6,"text":"noise","source":"system_audio"}],[])[0]
  self.assertEqual("unassigned",item["speakerAssignment"])

if __name__ == "__main__": unittest.main()
