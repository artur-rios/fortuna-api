using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Two tests that each start by checking a variable is absent and then set it: whichever xUnit runs second fails
///     unless <see cref="RestoreEnvironmentAttribute" /> took out what the first one left. The rest check the restore
///     itself.
/// </summary>
public sealed class RestoreEnvironmentAttributeTests
{
    private const string Probe = "FORTUNA_TEST_RESTORE_PROBE";

    [UnitFact]
    public void GivenAnEarlierTestSetAVariable_WhenTheFirstOfTwoRuns_ThenItDoesNotSeeIt() => SetProbeFromNothing();

    [UnitFact]
    public void GivenAnEarlierTestSetAVariable_WhenTheSecondOfTwoRuns_ThenItDoesNotSeeIt() => SetProbeFromNothing();

    [UnitFact]
    public void GivenChangedAndAddedVariables_WhenRestored_ThenTheBaselineValuesAreBackAndTheAddedOnesGone()
    {
        const string changed = "FORTUNA_TEST_RESTORE_CHANGED";
        const string added = "FORTUNA_TEST_RESTORE_ADDED";
        Environment.SetEnvironmentVariable(changed, "before");
        var baseline = RestoreEnvironmentAttribute.Snapshot();
        Environment.SetEnvironmentVariable(changed, "after");
        Environment.SetEnvironmentVariable(added, "new");

        RestoreEnvironmentAttribute.Restore(baseline);

        Assert.Equal("before", Environment.GetEnvironmentVariable(changed));
        Assert.Null(Environment.GetEnvironmentVariable(added));
        Environment.SetEnvironmentVariable(changed, null);
    }

    [UnitFact]
    public void GivenAVariableOutsideTheApplicationPrefixes_WhenSnapshotTaken_ThenItIsNotIncluded()
    {
        const string other = "OTHER_TEST_RESTORE_VARIABLE";
        Environment.SetEnvironmentVariable(other, "kept");

        var snapshot = RestoreEnvironmentAttribute.Snapshot();

        Assert.False(snapshot.ContainsKey(other));
        Environment.SetEnvironmentVariable(other, null);
    }

    private static void SetProbeFromNothing()
    {
        var before = Environment.GetEnvironmentVariable(Probe);
        Environment.SetEnvironmentVariable(Probe, "set");

        Assert.Null(before);
    }
}
