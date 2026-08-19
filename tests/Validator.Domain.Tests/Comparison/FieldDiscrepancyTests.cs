using System;
using Validator.Domain.Comparison;
using Xunit;

namespace Validator.Domain.Tests.Comparison
{
    public class FieldDiscrepancyTests
    {
        private static readonly DateTimeOffset TestTimestamp = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Constructor_WithNonNegativeDifference_Succeeds()
        {
            var discrepancy = new FieldDiscrepancy(
                TestTimestamp,
                OhlcvField.Open,
                benchmarkValue: 0.63421m,
                candidateValue: 0.63458m,
                difference: 0.00037m,
                directionalDifference: 0.00037m,
                resolvedAbsoluteTolerance: 0.00010m,
                resolvedRelativeTolerance: 0.0001m,
                toleranceDecision: new ToleranceDecision.MaterialDifference());

            Assert.Equal(0.00037m, discrepancy.Difference);
            Assert.Equal(OhlcvField.Open, discrepancy.Field);
        }

        [Fact]
        public void Constructor_WithZeroDifference_Succeeds()
        {
            var discrepancy = new FieldDiscrepancy(
                TestTimestamp,
                OhlcvField.Close,
                benchmarkValue: 0.63502m,
                candidateValue: 0.63502m,
                difference: 0m,
                directionalDifference: 0m,
                resolvedAbsoluteTolerance: 0.00010m,
                resolvedRelativeTolerance: 0.0001m,
                toleranceDecision: new ToleranceDecision.AcceptedByAbsolute());

            Assert.Equal(0m, discrepancy.Difference);
        }

        [Fact]
        public void Constructor_WithNegativeDifference_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FieldDiscrepancy(
                TestTimestamp,
                OhlcvField.Open,
                benchmarkValue: 0.63421m,
                candidateValue: 0.63458m,
                difference: -0.001m,
                directionalDifference: 0.00037m,
                resolvedAbsoluteTolerance: 0.00010m,
                resolvedRelativeTolerance: 0.0001m,
                toleranceDecision: new ToleranceDecision.MaterialDifference()));
        }

        [Fact]
        public void DirectionalDifference_IsPreserved()
        {
            var discrepancy = new FieldDiscrepancy(
                TestTimestamp,
                OhlcvField.High,
                benchmarkValue: 0.63580m,
                candidateValue: 0.63540m,
                difference: 0.00040m,
                directionalDifference: -0.00040m,
                resolvedAbsoluteTolerance: 0.00010m,
                resolvedRelativeTolerance: 0.0001m,
                toleranceDecision: new ToleranceDecision.MaterialDifference());

            Assert.Equal(-0.00040m, discrepancy.DirectionalDifference);
        }

        [Fact]
        public void ToleranceDecision_Variants_AreDistinct()
        {
            var acceptedByAbsolute = new ToleranceDecision.AcceptedByAbsolute();
            var acceptedByRelative = new ToleranceDecision.AcceptedByRelative();
            var material = new ToleranceDecision.MaterialDifference();

            Assert.False(acceptedByAbsolute.Equals(acceptedByRelative));
            Assert.False(acceptedByRelative.Equals(material));
            Assert.False(acceptedByAbsolute.Equals(material));
        }

        [Fact]
        public void CandidateSourceLine_Optional()
        {
            var withoutLine = new FieldDiscrepancy(
                TestTimestamp, OhlcvField.Open, 0.63421m, 0.63458m,
                0.00037m, 0.00037m, 0.00010m, 0.0001m,
                new ToleranceDecision.MaterialDifference());

            var withLine = new FieldDiscrepancy(
                TestTimestamp, OhlcvField.Open, 0.63421m, 0.63458m,
                0.00037m, 0.00037m, 0.00010m, 0.0001m,
                new ToleranceDecision.MaterialDifference(),
                candidateSourceLine: 42);

            Assert.Null(withoutLine.CandidateSourceLine);
            Assert.Equal(42, withLine.CandidateSourceLine);
        }
    }
}
