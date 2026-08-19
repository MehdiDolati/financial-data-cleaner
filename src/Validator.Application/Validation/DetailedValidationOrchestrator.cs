using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Candles;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;


namespace Validator.Application.Validation
{
    // Drives one detailed validation run end to end: prepare the source,
    // resolve the timeframe, execute the six established checks into a finding
    // catalog, complete and reconcile the catalog, and produce either a
    // successful detailed report or a fatal diagnostic.
    public sealed class DetailedValidationOrchestrator : IDetailedValidationUseCase
    {
        private readonly Func<IDetailedFindingSink> _catalogFactory;

        public DetailedValidationOrchestrator(Func<IDetailedFindingSink> catalogFactory)
        {
            _catalogFactory = catalogFactory ?? throw new ArgumentNullException(nameof(catalogFactory));
        }

        public async ValueTask<DetailedValidationOutcome> ExecuteAsync(
            DetailedValidationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var prepared = await request.CandleSource
                .PrepareAsync(request.CsvOptions, cancellationToken)
                .ConfigureAwait(false);
            if (prepared is PreparedCandleDataResult.Failed failed)
            {
                return new DetailedValidationOutcome.Failed(failed.Diagnostic);
            }

            var succeeded = (PreparedCandleDataResult.Succeeded)prepared;
            var candles = await MaterializeAsync(succeeded.Data.ReplayAsync(), cancellationToken).ConfigureAwait(false);
            var malformedRows = (request.CandleSource as IMalformedRowSource)?.MalformedRows
                ?? Array.Empty<MalformedRow>();

            Timeframe timeframe;
            try
            {
                timeframe = ResolveTimeframe(request, candles);
            }
            catch (FormatException exception)
            {
                return new DetailedValidationOutcome.Failed(new FatalDiagnostic(
                    "INVALID_ARGUMENT",
                    "The timeframe override is not a valid M<n>, H<n>, or D<n> code.",
                    exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return new DetailedValidationOutcome.Failed(new FatalDiagnostic(
                    "AMBIGUOUS_TIMEFRAME",
                    "A unique timeframe could not be inferred from the open-market timestamps.",
                    exception.Message));
            }

            IDetailedFindingSink? catalog = null;
            try
            {
                catalog = _catalogFactory();

                var (checks, summary, expectedCandles) = await RunChecksAsync(
                    candles,
                    malformedRows,
                    timeframe,
                    request.MarketCalendar,
                    catalog,
                    cancellationToken).ConfigureAwait(false);


                var completion = await catalog.CompleteAsync(cancellationToken).ConfigureAwait(false);
                if (completion is CompletedFindingCatalogResult.Failed completionFailed)
                {
                    return new DetailedValidationOutcome.Failed(completionFailed.Diagnostic);
                }

                var completed = (CompletedFindingCatalogResult.Succeeded)completion;
                catalog = null;

                var reconciliation = ReportReconciliation.Create(
                    summary,
                    succeeded.Coverage,
                    completed.Catalog.Statistics);
                var fatal = ReconciliationValidator.Validate(
                    checks,
                    summary,
                    succeeded.Coverage,
                    completed.Catalog.Statistics);
                if (fatal is not null)
                {
                    await completed.Catalog.DisposeAsync().ConfigureAwait(false);
                    return new DetailedValidationOutcome.Failed(fatal);
                }

                // Scoring is a pure derivation over the reconciled run. It is
                // attempted only when requested and only after reconciliation has
                // passed, so a fatal run never carries a score. An impossible
                // defect rate is an internal inconsistency and fails the run as a
                // reconciliation failure rather than being clamped.
                DatasetScoreReport? score = null;
                if (request.Options.Score is { } scoreRequest)
                {
                    try
                    {
                        var populations = MetricPopulations.FromScanCoverage(succeeded.Coverage, expectedCandles);
                        score = ScoreSectionBuilder.Build(summary, populations, checks, scoreRequest.Weighting);
                    }
                    catch (ImpossibleDefectRateException exception)
                    {
                        await completed.Catalog.DisposeAsync().ConfigureAwait(false);
                        return new DetailedValidationOutcome.Failed(new FatalDiagnostic(
                            "REPORT_RECONCILIATION_FAILED",
                            "A metric's defect count exceeds its population, implying an impossible rate.",
                            exception.Message,
                            checks: checks));
                    }
                }

                var context = CreateContext(request, timeframe, succeeded);
                var report = new DetailedValidationReport(
                    succeeded.Source,
                    context,
                    succeeded.Coverage,
                    checks,
                    summary,
                    reconciliation,
                    completed.Catalog)
                {
                    Score = score
                };
                return new DetailedValidationOutcome.Succeeded(report);

            }
            finally
            {
                if (catalog is not null)
                {
                    await catalog.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static async ValueTask<List<PriceCandle>> MaterializeAsync(
            IAsyncEnumerable<PriceCandle> replay,
            CancellationToken cancellationToken)
        {
            var candles = new List<PriceCandle>();
            await foreach (var candle in replay.WithCancellation(cancellationToken))
            {
                candles.Add(candle);
            }

            return candles;
        }

        private static Timeframe ResolveTimeframe(
            DetailedValidationRequest request,
            IReadOnlyList<PriceCandle> candles)
        {
            var overrideTimeframe = request.Options.GetParsedTimeframe();
            if (overrideTimeframe is not null)
            {
                return overrideTimeframe;
            }

            var openCandles = candles.Where(candle => request.MarketCalendar.IsOpen(candle.Timestamp));
            return TimeframeDetector.Detect(openCandles) ??
                throw new InvalidOperationException(
                    "Unable to infer a unique timeframe from the open-market timestamps.");
        }

        private static ValidationContextSnapshot CreateContext(
            DetailedValidationRequest request,
            Timeframe timeframe,
            PreparedCandleDataResult.Succeeded succeeded)
        {
            var profile = request.MarketCalendar.Profile;
            var calendar = new CalendarContext(
                profile.ToString().ToLowerInvariant(),
                profile.ToString());
            var delimiter = succeeded.Csv.Delimiter switch
            {
                ',' => "comma",
                ';' => "semicolon",
                '\t' => "tab",
                var other => throw new ArgumentException(
                    $"Unsupported resolved delimiter '{other}'.",
                    nameof(succeeded))
            };

            return new ValidationContextSnapshot(
                timeframe.ToString(),
                calendar,
                succeeded.Csv.Timestamp,
                delimiter,
                succeeded.Csv.HasHeader,
                succeeded.Csv.DateRange);
        }

        private static async ValueTask<(CheckExecution[] Checks, DetailedSummary Summary, long? ExpectedCandles)> RunChecksAsync(
            IReadOnlyList<PriceCandle> candles,
            IReadOnlyList<MalformedRow> malformedRows,
            Timeframe timeframe,
            IMarketCalendar calendar,
            IDetailedFindingSink sink,
            CancellationToken cancellationToken)
        {

            var ordered = candles
                .OrderBy(candle => candle.Timestamp)
                .ThenBy(candle => candle.SourceLine)
                .ToArray();

            var openTimestamps = ordered
                .Where(candle => calendar.IsOpen(candle.Timestamp))
                .Select(candle => candle.Timestamp)
                .Distinct()
                .OrderBy(timestamp => timestamp)
                .ToArray();

            var occupied = new HashSet<DateTimeOffset>(ordered.Select(candle => candle.Timestamp));
            foreach (var row in malformedRows)
            {
                if (row.ParsedTimestampUtc.HasValue)
                {
                    occupied.Add(row.ParsedTimestampUtc.Value);
                }
            }

            var counters = new long[6];
            var allocator = new ReferenceAllocator();

            var sequenceReason = "Fewer than two open-market timestamps bound an expected sequence.";
            CheckExecution missingExecution;
            CheckExecution gapsExecution;
            // Expected open-market candles are counted only when the sequence
            // checks actually run; otherwise the population is unknown and must
            // stay null so the time-based metrics can report NotApplicable.
            long? expectedCandles = null;
            if (openTimestamps.Length < 2)
            {
                missingExecution = new CheckExecution(CheckName.MissingCandles, CheckStatus.NotApplicable, sequenceReason);
                gapsExecution = new CheckExecution(CheckName.TimeGaps, CheckStatus.NotApplicable, sequenceReason);
            }
            else
            {
                missingExecution = new CheckExecution(CheckName.MissingCandles, CheckStatus.Completed);
                gapsExecution = new CheckExecution(CheckName.TimeGaps, CheckStatus.Completed);
                expectedCandles = await RunSequenceChecksAsync(
                    openTimestamps,
                    occupied,
                    timeframe,
                    calendar,
                    counters,
                    allocator,
                    sink,
                    cancellationToken).ConfigureAwait(false);
            }


            await RunDuplicateCheckAsync(ordered, counters, allocator, sink, cancellationToken).ConfigureAwait(false);
            await RunInvalidOhlcCheckAsync(ordered, counters, allocator, sink, cancellationToken).ConfigureAwait(false);
            await RunClosedMarketCheckAsync(ordered, calendar, counters, allocator, sink, cancellationToken).ConfigureAwait(false);
            await RunMalformedRowsCheckAsync(malformedRows, counters, allocator, sink, cancellationToken).ConfigureAwait(false);

            var checks = new CheckExecution[]
            {
                missingExecution,
                new(CheckName.DuplicateRecords, CheckStatus.Completed),
                new(CheckName.InvalidOhlc, CheckStatus.Completed),
                new(CheckName.ClosedMarketRecords, CheckStatus.Completed),
                gapsExecution,
                new(CheckName.MalformedRows, CheckStatus.Completed)
            };

            var summary = new DetailedSummary(
                counters[(int)FindingCategory.MissingCandle],
                counters[(int)FindingCategory.DuplicateRecord],
                counters[(int)FindingCategory.InvalidOhlc],
                counters[(int)FindingCategory.ClosedMarketRecord],
                counters[(int)FindingCategory.TimeGap],
                counters[(int)FindingCategory.MalformedRow]);

            return (checks, summary, expectedCandles);
        }

        // Runs the missing-candle and time-gap checks over one expected sequence
        // and returns the number of expected open-market slots it visited. That
        // count is the shared population for both time-based metrics and, being
        // produced by the same walk that reported the missing candles, cannot
        // disagree with them.
        private static async ValueTask<long> RunSequenceChecksAsync(
            DateTimeOffset[] openTimestamps,
            HashSet<DateTimeOffset> occupied,
            Timeframe timeframe,
            IMarketCalendar calendar,
            long[] counters,
            ReferenceAllocator allocator,
            IDetailedFindingSink sink,
            CancellationToken cancellationToken)
        {
            var first = openTimestamps[0];
            var last = openTimestamps[^1];
            var previousObserved = (DateTimeOffset?)null;
            var gapStart = (DateTimeOffset?)null;
            var gapPreviousObserved = (DateTimeOffset?)null;
            var gapCandles = new List<FindingReference>();
            var expectedOpenCandles = 0L;


            async ValueTask CloseGapAsync()
            {
                if (gapStart is null)
                {
                    return;
                }

                var lastMissing = gapStart.Value + (gapCandles.Count - 1) * timeframe.Duration;
                var gapReference = allocator.Allocate(FindingReferenceFactory.TimeGap(gapStart.Value, lastMissing));
                var nextObserved = NextObservedAfter(openTimestamps, lastMissing);

                for (var index = 0; index < gapCandles.Count; index++)
                {
                    var timestamp = gapStart.Value + index * timeframe.Duration;
                    var candleReference = gapCandles[index];
                    counters[(int)FindingCategory.MissingCandle]++;
                    await sink.AppendFindingAsync(new DetailedFindingHeader(
                        candleReference,
                        FindingCategory.MissingCandle,
                        "Missing candle",
                        "An expected candle is absent from the dataset.",
                        1,
                        new FindingLocation(null, timestamp),
                        EvidenceKind.MissingCandle,
                        "Verify the source feed for the expected timestamp."), cancellationToken).ConfigureAwait(false);
                    await sink.AppendEvidenceAsync(new FindingEvidenceRecord.MissingCandle(
                        candleReference,
                        new MissingCandleEvidence(timestamp, timeframe, gapReference, gapPreviousObserved, nextObserved)), cancellationToken).ConfigureAwait(false);
                    await sink.AppendRelationshipPairAsync(
                        new FindingRelationship(RelationshipKind.PartOfGap, gapReference),
                        new FindingRelationship(RelationshipKind.ContainsMissingCandle, candleReference),
                        cancellationToken).ConfigureAwait(false);
                }

                var elapsedSeconds = (long)(lastMissing - gapStart.Value + timeframe.Duration).TotalSeconds;
                counters[(int)FindingCategory.TimeGap]++;
                await sink.AppendFindingAsync(new DetailedFindingHeader(
                    gapReference,
                    FindingCategory.TimeGap,
                    "Time gap",
                    "A contiguous run of expected candles is absent.",
                    1,
                    new FindingLocation(null, gapStart.Value),
                    EvidenceKind.TimeGap,
                    "Investigate data discontinuities around the gap."), cancellationToken).ConfigureAwait(false);
                await sink.AppendEvidenceAsync(new FindingEvidenceRecord.TimeGapHeader(
                    gapReference,
                    new TimeGapEvidence(
                        gapStart.Value,
                        lastMissing,
                        timeframe,
                        gapCandles.Count,
                        elapsedSeconds,
                        gapPreviousObserved,
                        nextObserved)), cancellationToken).ConfigureAwait(false);

                for (var index = 0; index < gapCandles.Count; index++)
                {
                    await sink.AppendEvidenceAsync(
                        new FindingEvidenceRecord.TimeGapMissingReference(gapReference, gapCandles[index], index),
                        cancellationToken).ConfigureAwait(false);
                }

                gapCandles.Clear();
                gapStart = null;
            }

            for (var expected = first; expected <= last; expected += timeframe.Duration)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!calendar.IsOpen(expected))
                {
                    await CloseGapAsync().ConfigureAwait(false);
                    continue;
                }

                // Every open-market slot in the evaluated range is one expected
                // candle, whether or not the source actually contains it. This is
                // the denominator both time-based metrics are scored against.
                expectedOpenCandles++;

                if (occupied.Contains(expected))
                {
                    await CloseGapAsync().ConfigureAwait(false);
                    previousObserved = expected;
                    continue;
                }

                if (gapStart is null)
                {
                    gapStart = expected;
                    gapPreviousObserved = previousObserved;
                }

                gapCandles.Add(FindingReferenceFactory.MissingCandle(expected));
            }

            await CloseGapAsync().ConfigureAwait(false);
            return expectedOpenCandles;
        }


        private static async ValueTask RunDuplicateCheckAsync(
            PriceCandle[] ordered,
            long[] counters,
            ReferenceAllocator allocator,
            IDetailedFindingSink sink,
            CancellationToken cancellationToken)
        {
            foreach (var group in ordered.GroupBy(candle => candle.Timestamp).OrderBy(group => group.Key))
            {
                var rows = group.OrderBy(candle => candle.SourceLine).ToArray();
                var count = rows.Length - 1;
                if (count == 0)
                {
                    continue;
                }

                var first = rows[0];
                var exact = rows.Skip(1).All(row =>
                    row.Open == first.Open &&
                    row.High == first.High &&
                    row.Low == first.Low &&
                    row.Close == first.Close &&
                    row.Volume == first.Volume);

                var differing = exact
                    ? Array.Empty<string>()
                    : rows.Skip(1)
                        .SelectMany(row => DifferingFields(first, row))
                        .Distinct()
                        .OrderBy(field => field, StringComparer.Ordinal)
                        .ToArray();

                var reference = allocator.Allocate(FindingReferenceFactory.DuplicateRecord(group.Key, rows[0].SourceLine));
                counters[(int)FindingCategory.DuplicateRecord] += count;
                await sink.AppendFindingAsync(new DetailedFindingHeader(
                    reference,
                    FindingCategory.DuplicateRecord,
                    exact ? "Exact duplicate record" : "Conflicting duplicate records",
                    "Multiple rows share the same timestamp.",
                    count,
                    new FindingLocation(rows.Select(row => row.SourceLine).ToArray(), group.Key),
                    EvidenceKind.DuplicateRecord,
                    "Keep one canonical row and remove the rest."), cancellationToken).ConfigureAwait(false);
                await sink.AppendEvidenceAsync(new FindingEvidenceRecord.DuplicateHeader(
                    reference,
                    new DuplicateRecordEvidence(
                        group.Key,
                        exact ? DuplicateClassification.Exact : DuplicateClassification.Conflicting,
                        differing)), cancellationToken).ConfigureAwait(false);

                var childOrder = 0L;
                foreach (var field in differing)
                {
                    await sink.AppendEvidenceAsync(
                        new FindingEvidenceRecord.DuplicateDifferingField(reference, field, childOrder++),
                        cancellationToken).ConfigureAwait(false);
                }

                foreach (var row in rows)
                {
                    await sink.AppendEvidenceAsync(new FindingEvidenceRecord.DuplicateRow(
                        reference,
                        new DuplicateRowEvidence(row.SourceLine, null, row.Open, row.High, row.Low, row.Close, row.Volume),
                        childOrder++), cancellationToken).ConfigureAwait(false);
                }

                foreach (var row in rows)
                {
                    await sink.AppendLocationLineAsync(reference, row.SourceLine, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static async ValueTask RunInvalidOhlcCheckAsync(
            PriceCandle[] ordered,
            long[] counters,
            ReferenceAllocator allocator,
            IDetailedFindingSink sink,
            CancellationToken cancellationToken)
        {
            foreach (var candle in ordered)
            {
                var violations = new List<OhlcViolationCode>();
                if (candle.High < candle.Open) violations.Add(OhlcViolationCode.HIGH_BELOW_OPEN);
                if (candle.High < candle.Close) violations.Add(OhlcViolationCode.HIGH_BELOW_CLOSE);
                if (candle.High < candle.Low) violations.Add(OhlcViolationCode.HIGH_BELOW_LOW);
                if (candle.Low > candle.Open) violations.Add(OhlcViolationCode.LOW_ABOVE_OPEN);
                if (candle.Low > candle.Close) violations.Add(OhlcViolationCode.LOW_ABOVE_CLOSE);
                if (candle.Open <= 0m) violations.Add(OhlcViolationCode.NON_POSITIVE_OPEN);
                if (candle.High <= 0m) violations.Add(OhlcViolationCode.NON_POSITIVE_HIGH);
                if (candle.Low <= 0m) violations.Add(OhlcViolationCode.NON_POSITIVE_LOW);
                if (candle.Close <= 0m) violations.Add(OhlcViolationCode.NON_POSITIVE_CLOSE);
                if (candle.Volume < 0m) violations.Add(OhlcViolationCode.NEGATIVE_VOLUME);
                if (violations.Count == 0)
                {
                    continue;
                }

                var reference = allocator.Allocate(FindingReferenceFactory.PhysicalRecord(FindingCategory.InvalidOhlc, candle.SourceLine));
                counters[(int)FindingCategory.InvalidOhlc]++;
                await sink.AppendFindingAsync(new DetailedFindingHeader(
                    reference,
                    FindingCategory.InvalidOhlc,
                    "Invalid OHLC values",
                    "The row violates established OHLC/volume invariants.",
                    1,
                    new FindingLocation([candle.SourceLine], candle.Timestamp),
                    EvidenceKind.InvalidOhlc,
                    "Correct or remove the row."), cancellationToken).ConfigureAwait(false);
                await sink.AppendEvidenceAsync(new FindingEvidenceRecord.InvalidOhlcValues(
                    reference,
                    new OhlcValues(candle.Open, candle.High, candle.Low, candle.Close, candle.Volume)), cancellationToken).ConfigureAwait(false);

                for (var index = 0; index < violations.Count; index++)
                {
                    await sink.AppendEvidenceAsync(
                        new FindingEvidenceRecord.InvalidOhlcViolation(reference, violations[index], index),
                        cancellationToken).ConfigureAwait(false);
                }

                await sink.AppendLocationLineAsync(reference, candle.SourceLine, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async ValueTask RunClosedMarketCheckAsync(
            PriceCandle[] ordered,
            IMarketCalendar calendar,
            long[] counters,
            ReferenceAllocator allocator,
            IDetailedFindingSink sink,
            CancellationToken cancellationToken)
        {
            foreach (var candle in ordered)
            {
                if (calendar.IsOpen(candle.Timestamp))
                {
                    continue;
                }

                var reference = allocator.Allocate(FindingReferenceFactory.PhysicalRecord(FindingCategory.ClosedMarketRecord, candle.SourceLine));
                counters[(int)FindingCategory.ClosedMarketRecord]++;
                await sink.AppendFindingAsync(new DetailedFindingHeader(
                    reference,
                    FindingCategory.ClosedMarketRecord,
                    "Closed-market record",
                    "The row's timestamp falls outside the market calendar's open sessions.",
                    1,
                    new FindingLocation([candle.SourceLine], candle.Timestamp),
                    EvidenceKind.ClosedMarketRecord,
                    "Remove the row or verify the calendar."), cancellationToken).ConfigureAwait(false);
                await sink.AppendEvidenceAsync(new FindingEvidenceRecord.ClosedMarket(
                    reference,
                    new ClosedMarketRecordEvidence(
                        calendar.Profile.ToString().ToLowerInvariant(),
                        calendar.Profile.ToString(),
                        "RecurringClosedRule")), cancellationToken).ConfigureAwait(false);
                await sink.AppendLocationLineAsync(reference, candle.SourceLine, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async ValueTask RunMalformedRowsCheckAsync(
            IReadOnlyList<MalformedRow> malformedRows,
            long[] counters,
            ReferenceAllocator allocator,
            IDetailedFindingSink sink,
            CancellationToken cancellationToken)
        {
            var skippedChecks = new[]
            {
                CheckName.DuplicateRecords,
                CheckName.InvalidOhlc,
                CheckName.ClosedMarketRecords
            };

            for (var index = 0; index < malformedRows.Count; index++)
            {
                var row = malformedRows[index];
                var reference = allocator.Allocate(FindingReferenceFactory.PhysicalRecord(FindingCategory.MalformedRow, row.LineNumber));
                counters[(int)FindingCategory.MalformedRow]++;
                await sink.AppendFindingAsync(new DetailedFindingHeader(
                    reference,
                    FindingCategory.MalformedRow,
                    "Malformed row",
                    "The row could not be parsed as a valid record.",
                    1,
                    new FindingLocation([row.LineNumber], row.ParsedTimestampUtc),
                    EvidenceKind.MalformedRow,
                    "Fix the row or remove it from the source."), cancellationToken).ConfigureAwait(false);
                await sink.AppendEvidenceAsync(new FindingEvidenceRecord.MalformedHeader(
                    reference,
                    new MalformedRowEvidence(
                        row.LineNumber,
                        row.ParsedTimestampUtc,
                        null,
                        row.ParsedTimestampUtc.HasValue)), cancellationToken).ConfigureAwait(false);
                await sink.AppendEvidenceAsync(new FindingEvidenceRecord.MalformedFieldErrorRecord(
                    reference,
                    new MalformedFieldError("row", row.RawText, MalformedReasonCode.INVALID_VALUE, row.Reason),
                    0), cancellationToken).ConfigureAwait(false);

                for (var skippedIndex = 0; skippedIndex < skippedChecks.Length; skippedIndex++)
                {
                    await sink.AppendEvidenceAsync(new FindingEvidenceRecord.MalformedSkippedCheck(
                        reference,
                        skippedChecks[skippedIndex],
                        skippedIndex + 1), cancellationToken).ConfigureAwait(false);
                }

                await sink.AppendLocationLineAsync(reference, row.LineNumber, cancellationToken).ConfigureAwait(false);
            }
        }

        private static DateTimeOffset? NextObservedAfter(DateTimeOffset[] openTimestamps, DateTimeOffset timestamp)
        {
            var index = Array.BinarySearch(openTimestamps, timestamp + TimeSpan.FromTicks(1));
            if (index >= 0)
            {
                return openTimestamps[index];
            }

            var insertionPoint = ~index;
            return insertionPoint < openTimestamps.Length
                ? openTimestamps[insertionPoint]
                : null;
        }

        private static IEnumerable<string> DifferingFields(PriceCandle left, PriceCandle right)
        {
            if (left.Open != right.Open) yield return "Open";
            if (left.High != right.High) yield return "High";
            if (left.Low != right.Low) yield return "Low";
            if (left.Close != right.Close) yield return "Close";
            if (left.Volume != right.Volume) yield return "Volume";
        }

        private sealed class ReferenceAllocator
        {
            private readonly Dictionary<string, int> _used = new(StringComparer.Ordinal);

            public FindingReference Allocate(FindingReference baseReference)
            {
                if (!_used.TryGetValue(baseReference.Value, out var seen))
                {
                    _used.Add(baseReference.Value, 1);
                    return baseReference;
                }

                var ordinal = seen + 1;
                _used[baseReference.Value] = ordinal;
                return new FindingReference($"{baseReference.Value}:{ordinal}");
            }
        }
    }
}
