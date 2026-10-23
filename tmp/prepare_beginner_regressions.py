from pathlib import Path
root=Path(__file__).resolve().parents[1]
s=(root/'tmp/QaUiAudioProbe.cs').read_text(encoding='utf-8-sig')
s=s.replace('QaUiAudioProbe','QaBeginnerUiAudioProbe').replace('--qa-ui-audio','--qa-beginner-ui-audio').replace('Stage_02','Stage_01').replace('stage02','stage01')
(root/'tmp/QaBeginnerUiAudioProbe.cs').write_text(s,encoding='utf-8')
s=(root/'tmp/QaSceneSmokeProbe.cs').read_text(encoding='utf-8-sig')
s=s.replace('QaSceneSmokeProbe','QaBeginnerSmokeProbe').replace('--qa-scene-smoke','--qa-beginner-smoke')
s=s.replace('yield return Observe(Stages + "Stage_01.unity", true, 1f, 5);','yield return Observe(Stages + "Stage_01.unity", false, 3f, 5);')
(root/'tmp/QaBeginnerSmokeProbe.cs').write_text(s,encoding='utf-8')
print('Prepared Stage01 menu regression and campaign smoke probes.')
