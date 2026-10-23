using System;
using sung_wook;
using UnityEngine;

internal static class Stage3ComparisonCoreUnit
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static ElectricForceSample Sample(float q, float source, Vector2? position = null)
        => ElectricForceSample.PointCharge(position ?? new Vector2(0f, 2f), Vector2.zero,
            q, source, 16f, 0.5f, 80f, true, 3f, true);

    private static void Main()
    {
        var record = new ElectricComparisonRecord();
        var baseline = Sample(1f, 0.5f);
        var changed = Sample(1f, 1f);
        record.RecordBaseline(baseline);
        record.RecordResult(baseline, true);
        Check(!record.HasQualifiedComparison, "unchanged conditions do not qualify");
        record.RecordResult(changed, true);
        Check(record.HasQualifiedComparison && record.HasSingleVariableComparison,
            "Q-only launch qualifies");
        Check(Math.Abs(changed.ForceMagnitude / baseline.ForceMagnitude - 2f) < 0.0001f,
            "Q doubling yields twice the model force at fixed distance");
        record.RecordResult(Sample(1f, 1f, new Vector2(0f, 2.5f)), false);
        Check(record.HasQualifiedComparison && record.QualifiedResultSample.Position == changed.Position,
            "later flight preserves successful comparison evidence");
        record.RecordBaseline(baseline);
        Check(!record.HasQualifiedComparison && !record.HasResult, "explicit new baseline clears previous comparison");
        record.RecordResult(Sample(1f, 1f, new Vector2(0f, 2.5f)), true);
        Check(!record.HasQualifiedComparison, "different launch position does not qualify");
        record.RecordBaseline(Sample(0.5f, 1f));
        record.RecordResult(Sample(1f, 1f), true);
        Check(record.HasQualifiedComparison && record.HasSingleVariableComparison,
            "advanced q-only alternative remains valid");
        record.Reset();
        Check(!record.HasBaseline && !record.HasResult && !record.HasQualifiedComparison,
            "new experiment removes all comparison records");
        Console.WriteLine("RESULT " + passed + " checks passed");
    }
}
