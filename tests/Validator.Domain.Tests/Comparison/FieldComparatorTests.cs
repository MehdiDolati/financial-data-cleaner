using Validator.Domain.Comparison;
using Xunit;

namespace Validator.Domain.Tests.Comparison
{
    public class FieldComparatorTests
    {
        [Fact]
        public void Compare_IdenticalValues_ReturnsAcceptedByAbsolute()
        {
            var result = FieldComparator.Compare(0.63421m, 0.63421m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.AcceptedByAbsolute>(result);
        }

        [Fact]
        public void Compare_WithinAbsoluteTolerance_ReturnsAcceptedByAbsolute()
        {
            // Difference = 0.00005, absolute tolerance = 0.00010
            var result = FieldComparator.Compare(0.63421m, 0.63416m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.AcceptedByAbsolute>(result);
        }

        [Fact]
        public void Compare_ExceedsAbsoluteButWithinRelative_ReturnsAcceptedByRelative()
        {
            // Difference = 0.00015, absolute tolerance = 0.00010
            // Relative threshold = 0.0001 * 0.63421 = 0.000063421
            // So this should actually be MaterialDifference, not AcceptedByRelative
            // Let me use a larger relative tolerance to test AcceptedByRelative
            var result = FieldComparator.Compare(1.00000m, 1.00020m, 0.00010m, 0.0005m);
            // Difference = 0.00020, absolute = 0.00010 (exceeds), relative threshold = 0.0005 * 1.0 = 0.0005 (within)
            Assert.IsType<ToleranceDecision.AcceptedByRelative>(result);
        }

        [Fact]
        public void Compare_ExceedsBothTolerances_ReturnsMaterialDifference()
        {
            // Difference = 0.00037, absolute tolerance = 0.00010, relative = 0.0001
            // Relative threshold = 0.0001 * 0.63421 = 0.000063421
            var result = FieldComparator.Compare(0.63421m, 0.63458m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.MaterialDifference>(result);
        }

        [Fact]
        public void Compare_ZeroBenchmarkValue_OnlyAbsoluteToleranceApplies()
        {
            // Zero benchmark value: relative tolerance is unstable, only absolute applies
            var result = FieldComparator.Compare(0m, 0.00005m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.AcceptedByAbsolute>(result);
        }

        [Fact]
        public void Compare_ZeroBenchmarkValue_ExceedsAbsolute_IsMaterial()
        {
            var result = FieldComparator.Compare(0m, 0.00020m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.MaterialDifference>(result);
        }

        [Fact]
        public void Compare_LargeDifference_IsMaterial()
        {
            var result = FieldComparator.Compare(0.63421m, 0.70000m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.MaterialDifference>(result);
        }

        [Fact]
        public void CreateDiscrepancy_NegativeDirectionalDifference()
        {
            // Candidate is lower than benchmark -> directional difference should be negative
            var ts = new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero);
            var discrepancy = FieldComparator.CreateDiscrepancy(
                ts, OhlcvField.Close, 0.65100m, 0.65062m, 0.00010m, 0.0001m);
            Assert.Equal(-0.00038m, discrepancy.DirectionalDifference);
            Assert.Equal(0.00038m, discrepancy.Difference);
            Assert.IsType<ToleranceDecision.MaterialDifference>(discrepancy.ToleranceDecision);
        }

        [Fact]
        public void Compare_VolumeLargeValues_WorksCorrectly()
        {
            // Volume: 125000 vs 118200, difference = 6800
            // Absolute tolerance = 0, relative = 0.05 (5%)
            // Relative threshold = 0.05 * 125000 = 6250
            // 6800 > 6250, so material
            var result = FieldComparator.Compare(125000m, 118200m, 0m, 0.05m);
            Assert.IsType<ToleranceDecision.MaterialDifference>(result);
        }

        [Fact]
        public void Compare_VolumeWithinRelativeTolerance_IsAccepted()
        {
            // Volume: 125000 vs 120000, difference = 5000
            // Relative threshold = 0.05 * 125000 = 6250
            // 5000 <= 6250, so accepted by relative
            var result = FieldComparator.Compare(125000m, 120000m, 0m, 0.05m);
            Assert.IsType<ToleranceDecision.AcceptedByRelative>(result);
        }

        [Fact]
        public void Compare_BrokerDifferenceWithinAbsolute_IsAccepted()
        {
            // Typical broker difference: 0.00005 on a 5-digit quote
            var result = FieldComparator.Compare(0.63421m, 0.63426m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.AcceptedByAbsolute>(result);
        }

        [Fact]
        public void Compare_NegativeAbsoluteTolerance_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FieldComparator.Compare(1m, 1m, -0.001m, 0.0001m));
        }

        [Fact]
        public void Compare_NegativeRelativeTolerance_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FieldComparator.Compare(1m, 1m, 0.0001m, -0.001m));
        }

        [Fact]
        public void CreateDiscrepancy_ReturnsCorrectFields()
        {
            var ts = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
            var discrepancy = FieldComparator.CreateDiscrepancy(
                ts, OhlcvField.Open, 0.63421m, 0.63458m, 0.00010m, 0.0001m);

            Assert.Equal(ts, discrepancy.TimestampUtc);
            Assert.Equal(OhlcvField.Open, discrepancy.Field);
            Assert.Equal(0.63421m, discrepancy.BenchmarkValue);
            Assert.Equal(0.63458m, discrepancy.CandidateValue);
            Assert.Equal(0.00037m, discrepancy.Difference);
            Assert.Equal(0.00037m, discrepancy.DirectionalDifference);
            Assert.Equal(0.00010m, discrepancy.ResolvedAbsoluteTolerance);
            Assert.Equal(0.0001m, discrepancy.ResolvedRelativeTolerance);
            Assert.IsType<ToleranceDecision.MaterialDifference>(discrepancy.ToleranceDecision);
        }

        [Fact]
        public void Compare_NegativeBenchmark_ExceedsAbsolute_WithinRelative_AcceptsByRelative()
        {
            // Negative benchmark: -1.0, candidate: -1.00020
            // Absolute difference = 0.00020, absolute tolerance = 0.00010 (exceeds)
            // Relative threshold = 0.0005 * 1.0 = 0.0005 (within)
            var result = FieldComparator.Compare(-1.0m, -1.00020m, 0.00010m, 0.0005m);
            Assert.IsType<ToleranceDecision.AcceptedByRelative>(result);
        }

        [Fact]
        public void Compare_NegativeBenchmark_ExceedsBothTolerances_IsMaterial()
        {
            // Negative benchmark: -1.0, candidate: -1.00100
            // Absolute difference = 0.00100, absolute tolerance = 0.00010 (exceeds)
            // Relative threshold = 0.0001 * 1.0 = 0.00010 (exceeds)
            var result = FieldComparator.Compare(-1.0m, -1.00100m, 0.00010m, 0.0001m);
            Assert.IsType<ToleranceDecision.MaterialDifference>(result);
        }
    }
}
