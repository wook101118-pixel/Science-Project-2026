// Clone-only targeted preservation for opt-in gameplay probes.
using System;
using System.Collections.Generic;
using System.IO;
using sung_wook;
using UnityEngine;

public sealed class QaProgressPreferences
{
    [Serializable] private sealed class Entry { public string key; public bool existed, isFloat; public int integer; public float number; }
    [Serializable] private sealed class Snapshot { public bool restored, verified; public List<Entry> entries = new List<Entry>(); }
    private Snapshot snapshot;
    private string path;
    public static QaProgressPreferences Capture(string path)
    {
        var guard = new QaProgressPreferences { path = path, snapshot = new Snapshot() };
        guard.Add(ElectricStageProgression.UnlockKey, false);
        for (int stage = 1; stage <= 5; stage++)
        {
            string mastery = "ElectricBounce.Mastery.Stage" + stage;
            guard.Add(mastery + ".Flags", false); guard.Add(mastery + ".FewestRetries", false); guard.Add(mastery + ".BestVoltage", true);
            guard.Add("ElectricBounce.Plan.Stage" + stage + ".Flags", false);
        }
        guard.Add("ElectricBounce.Plan.Stage3.FixedCharges.Flags", false);
        guard.Save(); return guard;
    }
    private void Add(string key, bool isFloat)
    {
        snapshot.entries.Add(new Entry { key = key, isFloat = isFloat, existed = PlayerPrefs.HasKey(key), integer = isFloat ? 0 : PlayerPrefs.GetInt(key, 0), number = isFloat ? PlayerPrefs.GetFloat(key, 0f) : 0f });
    }
    public void Restore()
    {
        if (snapshot.restored) return;
        foreach (Entry entry in snapshot.entries)
        {
            if (!entry.existed) PlayerPrefs.DeleteKey(entry.key);
            else if (entry.isFloat) PlayerPrefs.SetFloat(entry.key, entry.number);
            else PlayerPrefs.SetInt(entry.key, entry.integer);
        }
        PlayerPrefs.Save(); snapshot.restored = true; snapshot.verified = true;
        foreach (Entry entry in snapshot.entries)
            snapshot.verified &= PlayerPrefs.HasKey(entry.key) == entry.existed && (!entry.existed || (entry.isFloat ? PlayerPrefs.GetFloat(entry.key) == entry.number : PlayerPrefs.GetInt(entry.key) == entry.integer));
        Save(); if (!snapshot.verified) throw new InvalidOperationException("Targeted progress restoration did not match the snapshot.");
    }
    private void Save() { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(snapshot, true)); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RecoverIfRequested()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-restore-progress") < 0) return;
        string path = null;
        foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith("--qa-progress-backup=", StringComparison.Ordinal)) path = arg.Substring("--qa-progress-backup=".Length);
        if (path == null || !Path.IsPathRooted(path)) throw new InvalidOperationException("Absolute QA progress backup path required.");
        var guard = new QaProgressPreferences { path = path, snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(path)) };
        guard.Restore(); Application.Quit(0);
    }
}
