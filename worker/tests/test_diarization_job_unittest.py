import unittest
from diarization_job import extract
class Turn:
 def __init__(self,start,end):self.start,self.end=start,end
class Output:
 exclusive_speaker_diarization=[(Turn(0.2,1.5),"SPEAKER_00"),(Turn(1.8,3.0),"SPEAKER_01")]
 speaker_diarization=[]
class Tests(unittest.TestCase):
 def test_exclusive_turns_are_serialized(self):
  self.assertEqual(["SPEAKER_00","SPEAKER_01"],[x["speaker"] for x in extract(Output())])
if __name__=="__main__":unittest.main()
