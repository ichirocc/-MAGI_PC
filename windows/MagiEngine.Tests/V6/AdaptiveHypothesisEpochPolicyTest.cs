using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5d (first piece): <see cref="AdaptiveHypothesisEpochPolicy"/> — a pure, self-contained
/// scheduling policy (no coroutines/MagiState/I-O) consumed by
/// <see cref="V6NativeOptimizer.RunAdaptivePortfolio"/>. Ported and tested ahead of the much
/// riskier coroutines-to-TPL conversion so its logic is independently verified first.
/// </summary>
public class AdaptiveHypothesisEpochPolicyTest
{
    [Fact]
    public void AssignmentFor_Slot0IsAlwaysBaselineRefineRegardlessOfReassignments()
    {
        foreach (var reassignments in new[] { 0, 1, 5, 100 })
        {
            var a = AdaptiveHypothesisEpochPolicy.AssignmentFor(0, reassignments);
            Assert.Equal(HypothesisEpochRole.BaselineRefine, a.Role);
            Assert.Equal(V6Algorithm.RsiPlus, a.Algorithm);
        }
        // Slot wraps every 8 (KotlinInterop.FloorMod) — index 8, 16, ... behave like index 0 too.
        Assert.Equal(HypothesisEpochRole.BaselineRefine, AdaptiveHypothesisEpochPolicy.AssignmentFor(8, 3).Role);
    }

    [Fact]
    public void AssignmentFor_Slot4StartsBaselineThenBecomesEliteRelink()
    {
        Assert.Equal(HypothesisEpochRole.BaselineRefine, AdaptiveHypothesisEpochPolicy.AssignmentFor(4, 0).Role);
        Assert.Equal(HypothesisEpochRole.EliteRelink, AdaptiveHypothesisEpochPolicy.AssignmentFor(4, 1).Role);
        Assert.Equal(HypothesisEpochRole.EliteRelink, AdaptiveHypothesisEpochPolicy.AssignmentFor(4, 50).Role);
    }

    [Theory]
    [InlineData(1, 0, HypothesisEpochRole.DayBlockAlns)]
    [InlineData(2, 0, HypothesisEpochRole.HardFamilyRsi)]
    [InlineData(3, 0, HypothesisEpochRole.HardDebtRsiPlus)]
    [InlineData(5, 0, HypothesisEpochRole.LargeDestroyAlns)]
    [InlineData(6, 0, HypothesisEpochRole.PersonalRsi)]
    [InlineData(7, 0, HypothesisEpochRole.MaxDistanceRsiPlus)]
    // Reassignments advance through the 6-role escape cycle (wraps at 6).
    [InlineData(1, 1, HypothesisEpochRole.HardFamilyRsi)]
    [InlineData(1, 6, HypothesisEpochRole.DayBlockAlns)]
    public void AssignmentFor_EscapeSlotsCycleThroughSixRoles(int index, int reassignments, HypothesisEpochRole expected)
    {
        Assert.Equal(expected, AdaptiveHypothesisEpochPolicy.AssignmentFor(index, reassignments).Role);
    }

    [Theory]
    [InlineData(HypothesisEpochRole.DayBlockAlns, V6Algorithm.Alns)]
    [InlineData(HypothesisEpochRole.LargeDestroyAlns, V6Algorithm.Alns)]
    [InlineData(HypothesisEpochRole.HardFamilyRsi, V6Algorithm.Rsi)]
    [InlineData(HypothesisEpochRole.PersonalRsi, V6Algorithm.Rsi)]
    [InlineData(HypothesisEpochRole.BaselineRefine, V6Algorithm.RsiPlus)]
    [InlineData(HypothesisEpochRole.EliteRelink, V6Algorithm.RsiPlus)]
    [InlineData(HypothesisEpochRole.HardDebtRsiPlus, V6Algorithm.RsiPlus)]
    [InlineData(HypothesisEpochRole.MaxDistanceRsiPlus, V6Algorithm.RsiPlus)]
    public void AlgorithmFor_MapsEveryRoleToItsAlgorithmFamily(HypothesisEpochRole role, V6Algorithm expected)
    {
        Assert.Equal(expected, AdaptiveHypothesisEpochPolicy.AlgorithmFor(role));
    }

    [Fact]
    public void CarriesImprovingQuantum_OnlyTrueWhenImprovedAndRoleUnchanged()
    {
        Assert.True(AdaptiveHypothesisEpochPolicy.CarriesImprovingQuantum(improvedThisEpoch: true, roleChanged: false));
        Assert.False(AdaptiveHypothesisEpochPolicy.CarriesImprovingQuantum(improvedThisEpoch: true, roleChanged: true));
        Assert.False(AdaptiveHypothesisEpochPolicy.CarriesImprovingQuantum(improvedThisEpoch: false, roleChanged: false));
        Assert.False(AdaptiveHypothesisEpochPolicy.CarriesImprovingQuantum(improvedThisEpoch: false, roleChanged: true));
    }

    [Fact]
    public void IntensityFor_GrowsByOneEveryTwoStagnantEpochsCappedAtThreeOnTopOfRoleBase()
    {
        // BaselineRefine base=0; growth = min(max(g,0)/2, 3).
        Assert.Equal(0, AdaptiveHypothesisEpochPolicy.IntensityFor(HypothesisEpochRole.BaselineRefine, 0));
        Assert.Equal(0, AdaptiveHypothesisEpochPolicy.IntensityFor(HypothesisEpochRole.BaselineRefine, 1)); // 1/2=0
        Assert.Equal(1, AdaptiveHypothesisEpochPolicy.IntensityFor(HypothesisEpochRole.BaselineRefine, 2)); // 2/2=1
        Assert.Equal(3, AdaptiveHypothesisEpochPolicy.IntensityFor(HypothesisEpochRole.BaselineRefine, 100)); // capped at 3
        // Negative growthBasis is not an expected caller input but must clamp to 0, not underflow.
        Assert.Equal(0, AdaptiveHypothesisEpochPolicy.IntensityFor(HypothesisEpochRole.BaselineRefine, -5));
        // MaxDistanceRsiPlus base=3, so at max growth intensity is 3+3=6.
        Assert.Equal(6, AdaptiveHypothesisEpochPolicy.IntensityFor(HypothesisEpochRole.MaxDistanceRsiPlus, 100));
    }

    [Fact]
    public void ShouldReassign_Slot0NeverReassignsEvenWhenStagnantOrColliding()
    {
        Assert.False(AdaptiveHypothesisEpochPolicy.ShouldReassign(0, improvedThisEpoch: false, stagnantEpochs: 99, nearestOtherDistance: 0));
    }

    [Fact]
    public void ShouldReassign_CollidingBasinAlwaysReassignsRegardlessOfImprovement()
    {
        // nearestOtherDistance <= DUPLICATE_DISTANCE_CELLS (2) forces reassignment even if it just improved.
        Assert.True(AdaptiveHypothesisEpochPolicy.ShouldReassign(1, improvedThisEpoch: true, stagnantEpochs: 0, nearestOtherDistance: 2));
        Assert.True(AdaptiveHypothesisEpochPolicy.ShouldReassign(1, improvedThisEpoch: true, stagnantEpochs: 0, nearestOtherDistance: 0));
    }

    [Fact]
    public void ShouldReassign_StagnantAndDiverseReassignsOnlyAfterOneStagnantEpoch()
    {
        Assert.False(AdaptiveHypothesisEpochPolicy.ShouldReassign(1, improvedThisEpoch: false, stagnantEpochs: 0, nearestOtherDistance: 50));
        Assert.True(AdaptiveHypothesisEpochPolicy.ShouldReassign(1, improvedThisEpoch: false, stagnantEpochs: 1, nearestOtherDistance: 50));
        Assert.False(AdaptiveHypothesisEpochPolicy.ShouldReassign(1, improvedThisEpoch: true, stagnantEpochs: 5, nearestOtherDistance: 50));
    }

    [Fact]
    public void NextStagnantEpochs_ResetsOnImprovementOtherwiseIncrements()
    {
        Assert.Equal(0, AdaptiveHypothesisEpochPolicy.NextStagnantEpochs(previous: 7, improvedThisEpoch: true));
        Assert.Equal(8, AdaptiveHypothesisEpochPolicy.NextStagnantEpochs(previous: 7, improvedThisEpoch: false));
    }

    [Fact]
    public void QuantumSeconds_ZeroRemainingReturnsZero()
    {
        var a = AdaptiveHypothesisEpochPolicy.AssignmentFor(1, 0);
        Assert.Equal(0, AdaptiveHypothesisEpochPolicy.QuantumSeconds(a, improvedPreviousEpoch: false, remainingSeconds: 0));
        Assert.Equal(0, AdaptiveHypothesisEpochPolicy.QuantumSeconds(a, improvedPreviousEpoch: true, remainingSeconds: -3));
    }

    [Fact]
    public void QuantumSeconds_NonRsiPlusUsesBaseOrImprovingQuantumClampedToRemaining()
    {
        var alns = AdaptiveHypothesisEpochPolicy.AssignmentFor(1, 0); // DayBlockAlns
        Assert.Equal(AdaptiveHypothesisEpochPolicy.BASE_QUANTUM_SEC,
            AdaptiveHypothesisEpochPolicy.QuantumSeconds(alns, improvedPreviousEpoch: false, remainingSeconds: 1000));
        Assert.Equal(AdaptiveHypothesisEpochPolicy.IMPROVING_QUANTUM_SEC,
            AdaptiveHypothesisEpochPolicy.QuantumSeconds(alns, improvedPreviousEpoch: true, remainingSeconds: 1000));
        // Clamped down to a small remaining budget, but never below 1.
        Assert.Equal(3, AdaptiveHypothesisEpochPolicy.QuantumSeconds(alns, improvedPreviousEpoch: false, remainingSeconds: 3));
        Assert.Equal(1, AdaptiveHypothesisEpochPolicy.QuantumSeconds(alns, improvedPreviousEpoch: false, remainingSeconds: 0 + 1 - 1 + 1));
    }

    [Fact]
    public void QuantumSeconds_RsiPlusUsesTheLargerDedicatedQuantum()
    {
        var rsiPlus = AdaptiveHypothesisEpochPolicy.AssignmentFor(4, 0); // BaselineRefine -> RsiPlus algorithm
        Assert.Equal(V6Algorithm.RsiPlus, rsiPlus.Algorithm);
        Assert.Equal(AdaptiveHypothesisEpochPolicy.RSI_PLUS_BASE_QUANTUM_SEC,
            AdaptiveHypothesisEpochPolicy.QuantumSeconds(rsiPlus, improvedPreviousEpoch: false, remainingSeconds: 1000));
        Assert.Equal(AdaptiveHypothesisEpochPolicy.RSI_PLUS_IMPROVING_QUANTUM_SEC,
            AdaptiveHypothesisEpochPolicy.QuantumSeconds(rsiPlus, improvedPreviousEpoch: true, remainingSeconds: 1000));
    }

    [Fact]
    public void EpochSeed_IsDeterministicAndVariesWithEveryInput()
    {
        var s0 = AdaptiveHypothesisEpochPolicy.EpochSeed(42L, 1, 2, 3);
        var s1 = AdaptiveHypothesisEpochPolicy.EpochSeed(42L, 1, 2, 3);
        Assert.Equal(s0, s1); // Determinism: same inputs, same output.

        Assert.NotEqual(s0, AdaptiveHypothesisEpochPolicy.EpochSeed(43L, 1, 2, 3));
        Assert.NotEqual(s0, AdaptiveHypothesisEpochPolicy.EpochSeed(42L, 2, 2, 3));
        Assert.NotEqual(s0, AdaptiveHypothesisEpochPolicy.EpochSeed(42L, 1, 3, 3));
        Assert.NotEqual(s0, AdaptiveHypothesisEpochPolicy.EpochSeed(42L, 1, 2, 4));
    }

    [Fact]
    public void InitialAssignmentFor_MatchesAssignmentForAtZeroReassignments()
    {
        for (var i = 0; i < 8; i++)
            Assert.Equal(AdaptiveHypothesisEpochPolicy.AssignmentFor(i, 0), AdaptiveHypothesisEpochPolicy.InitialAssignmentFor(i));
    }

    [Fact]
    public void RoleLabel_CombinesTheKotlinStyleUpperSnakeCaseNameWithIntensity()
    {
        var a = new HypothesisEpochAssignment(HypothesisEpochRole.HardDebtRsiPlus, V6Algorithm.RsiPlus, Intensity: 4);
        Assert.Equal("HARD_DEBT_RSI_PLUS/x4", AdaptiveHypothesisEpochPolicy.RoleLabel(a));
    }

    [Fact]
    public void RoleName_ReturnsTheOriginalKotlinUpperSnakeCaseNameForEveryRole()
    {
        var expected = new Dictionary<HypothesisEpochRole, string>
        {
            [HypothesisEpochRole.BaselineRefine] = "BASELINE_REFINE",
            [HypothesisEpochRole.EliteRelink] = "ELITE_RELINK",
            [HypothesisEpochRole.DayBlockAlns] = "DAY_BLOCK_ALNS",
            [HypothesisEpochRole.HardFamilyRsi] = "HARD_FAMILY_RSI",
            [HypothesisEpochRole.HardDebtRsiPlus] = "HARD_DEBT_RSI_PLUS",
            [HypothesisEpochRole.LargeDestroyAlns] = "LARGE_DESTROY_ALNS",
            [HypothesisEpochRole.PersonalRsi] = "PERSONAL_RSI",
            [HypothesisEpochRole.MaxDistanceRsiPlus] = "MAX_DISTANCE_RSI_PLUS",
        };
        foreach (var (role, name) in expected)
            Assert.Equal(name, AdaptiveHypothesisEpochPolicy.RoleName(role));
    }
}
