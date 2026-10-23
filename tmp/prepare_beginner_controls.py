from pathlib import Path
root=Path(__file__).resolve().parents[1]
s=(root/'tmp/QaControlProbe.cs').read_text(encoding='utf-8-sig')
s=s.replace('QaControlProbe','QaBeginnerControlProbe').replace('--qa-controls','--qa-beginner-controls')
old='private IEnumerator Seconds(float duration) { float end = Time.realtimeSinceStartup + duration; while (Time.realtimeSinceStartup < end) yield return null; }'
new='''private IEnumerator Seconds(float duration)
    {
        // Production sprite interpolation uses scaled deltaTime, so its fixture waits on the same clock.
        // A stalled render frame must not consume the whole fade assertion's wall-time allowance.
        float end = Time.time + duration, watchdog = Time.realtimeSinceStartup + 5f;
        while (Time.time < end) { Require(Time.realtimeSinceStartup < watchdog, "Scaled visual wait exceeded watchdog."); yield return null; }
    }'''
assert old in s
s=s.replace(old,new)
s=s.replace('float end = Time.realtimeSinceStartup + 0.55f;\n        while (Time.realtimeSinceStartup < end)',
'''float end = Time.time + 0.55f, visualWatchdog = Time.realtimeSinceStartup + 5f;
        while (Time.time < end)''')
s=s.replace('if (overlay.color.a > 0.01f && overlay.color.a < 0.99f) sawIntermediate = true;',
'''Require(Time.realtimeSinceStartup < visualWatchdog, "Negative transition exceeded watchdog.");
            if (overlay.color.a > 0.01f && overlay.color.a < 0.99f) sawIntermediate = true;''')
(root/'tmp/QaBeginnerControlProbe.cs').write_text(s,encoding='utf-8')
print('Prepared the same 63 control cases with sprite waits on the production scaled game clock.')
