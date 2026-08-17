using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;

namespace Validator.Application.Reporting
{
    // Application-owned finding catalog. Headers are kept as constant-size
    // in-memory records; child location lines, evidence records, and
    // relationship edges are appended to temporary spools and canonicalized
    // (sorted by owning reference) at completion so a sequential merge join can
    // replay every finding's children in bounded memory. All temporary
    // artifacts are deleted on dispose, whether the run completed, failed, or
    // was cancelled.
    public sealed class FindingCatalog : IDetailedFindingSink, ICompletedFindingCatalog
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            Converters = { new JsonStringEnumConverter(), new TimeframeJsonConverter() }
        };

        private sealed class TimeframeJsonConverter : JsonConverter<Timeframe>
        {
            public override Timeframe Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options) =>
                Timeframe.Parse(reader.GetString() ?? string.Empty);

            public override void Write(
                Utf8JsonWriter writer,
                Timeframe value,
                JsonSerializerOptions options) =>
                writer.WriteStringValue(value.ToString());
        }

        private readonly object _syncRoot = new();
        private readonly Func<ISpoolWriter> _spoolWriterFactory;
        private readonly Func<string, ISpoolSeekableReader> _spoolReaderFactory;
        private readonly ISpoolCanonicalSorter? _canonicalSorter;
        private readonly ISpoolWriter _locationsSpool;
        private readonly ISpoolWriter _evidenceSpool;
        private readonly ISpoolWriter _relationshipsSpool;
        private readonly List<ISpoolWriter> _canonicalSpools = new();
        private readonly Dictionary<string, DetailedFindingHeader> _headers = new();
        private readonly Dictionary<string, HashSet<string>> _relationshipTargets = new();
        private readonly long[] _entryCounts = new long[6];
        private readonly long[] _contributionSums = new long[6];
        private List<DetailedFindingHeader>? _orderedHeaders;
        private Dictionary<string, (long Start, long End)>? _locationBlocks;
        private Dictionary<string, (long Start, long End)>? _evidenceBlocks;
        private Dictionary<string, (long Start, long End)>? _relationshipBlocks;
        private string? _canonicalLocationsPath;
        private string? _canonicalEvidencePath;
        private string? _canonicalRelationshipsPath;
        private bool _completed;
        private bool _disposed;

        public FindingCatalog(
            Func<ISpoolWriter> spoolWriterFactory,
            Func<string, ISpoolSeekableReader> spoolReaderFactory,
            ISpoolCanonicalSorter? canonicalSorter = null)
        {
            _spoolWriterFactory = spoolWriterFactory ?? throw new ArgumentNullException(nameof(spoolWriterFactory));
            _spoolReaderFactory = spoolReaderFactory ?? throw new ArgumentNullException(nameof(spoolReaderFactory));
            _canonicalSorter = canonicalSorter;
            _locationsSpool = spoolWriterFactory();
            _evidenceSpool = spoolWriterFactory();
            _relationshipsSpool = spoolWriterFactory();
        }

        public FindingCatalogStatistics Statistics
        {
            get
            {
                lock (_syncRoot)
                {
                    return new FindingCatalogStatistics(
                        StatisticsFor(FindingCategory.MissingCandle),
                        StatisticsFor(FindingCategory.DuplicateRecord),
                        StatisticsFor(FindingCategory.InvalidOhlc),
                        StatisticsFor(FindingCategory.ClosedMarketRecord),
                        StatisticsFor(FindingCategory.TimeGap),
                        StatisticsFor(FindingCategory.MalformedRow));
                }
            }
        }

        public ValueTask AppendFindingAsync(
            DetailedFindingHeader finding,
            CancellationToken cancellationToken = default)
        {
            if (finding is null)
            {
                throw new ArgumentNullException(nameof(finding));
            }

            lock (_syncRoot)
            {
                ThrowIfTerminal();

                if (!_headers.TryAdd(finding.Reference.Value, finding))
                {
                    throw new InvalidOperationException(
                        $"Finding reference '{finding.Reference.Value}' is already present; references must be unique.");
                }

                var index = CategoryIndex(finding.Category);
                _entryCounts[index]++;
                _contributionSums[index] += finding.CountContribution;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask AppendLocationLineAsync(
            FindingReference finding,
            long sourceLine,
            CancellationToken cancellationToken = default)
        {
            if (finding is null)
            {
                throw new ArgumentNullException(nameof(finding));
            }

            if (sourceLine <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceLine), "Source line must be positive.");
            }

            lock (_syncRoot)
            {
                ThrowIfTerminal();
                if (!_headers.ContainsKey(finding.Value))
                {
                    throw new InvalidOperationException(
                        $"Location lines cannot reference unknown finding '{finding.Value}'.");
                }

                return _locationsSpool.AppendLineAsync($"{finding.Value}|{sourceLine}", cancellationToken);
            }
        }

        public ValueTask AppendEvidenceAsync(
            FindingEvidenceRecord evidence,
            CancellationToken cancellationToken = default)
        {
            if (evidence is null)
            {
                throw new ArgumentNullException(nameof(evidence));
            }

            lock (_syncRoot)
            {
                ThrowIfTerminal();
                var finding = FindingOf(evidence);
                if (!_headers.ContainsKey(finding.Value))
                {
                    throw new InvalidOperationException(
                        $"Evidence cannot reference unknown finding '{finding.Value}'.");
                }

                var json = JsonSerializer.Serialize(evidence, evidence.GetType(), SerializerOptions);
                return _evidenceSpool.AppendLineAsync($"{finding.Value}|{evidence.Kind}|{json}", cancellationToken);
            }
        }

        public ValueTask AppendRelationshipPairAsync(
            FindingRelationship forward,
            FindingRelationship reverse,
            CancellationToken cancellationToken = default)
        {
            if (forward is null)
            {
                throw new ArgumentNullException(nameof(forward));
            }

            if (reverse is null)
            {
                throw new ArgumentNullException(nameof(reverse));
            }

            if (!(forward.Kind == RelationshipKind.PartOfGap && reverse.Kind == RelationshipKind.ContainsMissingCandle))
            {
                throw new InvalidOperationException(
                    "Relationship pairs must contain exactly one PartOfGap and one ContainsMissingCandle edge.");
            }

            lock (_syncRoot)
            {
                ThrowIfTerminal();

                // The forward edge (PartOfGap) is owned by the candle finding;
                // the reverse edge (ContainsMissingCandle) is owned by the gap
                // finding. Each owner is the other edge's target. Both edges
                // are persisted together and each owner replays its own edge.
                var forwardOwner = reverse.TargetReference.Value;
                var reverseOwner = forward.TargetReference.Value;
                TrackTarget(forwardOwner, forward.TargetReference.Value);
                TrackTarget(reverseOwner, reverse.TargetReference.Value);

                return AppendPairLinesAsync(
                    $"{forwardOwner}|{forward.Kind}|{forward.TargetReference.Value}",
                    $"{reverseOwner}|{reverse.Kind}|{reverse.TargetReference.Value}",
                    cancellationToken);
            }
        }

        public async ValueTask<CompletedFindingCatalogResult> CompleteAsync(
            CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                if (_completed)
                {
                    return new CompletedFindingCatalogResult.Succeeded(this);
                }
            }

            await _locationsSpool.CompleteAsync(cancellationToken);
            await _evidenceSpool.CompleteAsync(cancellationToken);
            await _relationshipsSpool.CompleteAsync(cancellationToken);

            var relationships = await CanonicalizeAsync(_relationshipsSpool, cancellationToken);
            var locations = await CanonicalizeAsync(_locationsSpool, cancellationToken);
            var evidence = await CanonicalizeAsync(_evidenceSpool, cancellationToken);

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(FindingCatalog));
                }

                foreach (var (finding, targets) in _relationshipTargets)
                {
                    foreach (var target in targets)
                    {
                        if (!_headers.ContainsKey(target))
                        {
                            var diagnostic = new FatalDiagnostic(
                                "VALIDATION_INCOMPLETE",
                                $"Finding '{finding}' references unknown finding '{target}'.",
                                "Reproduce the validation run and ensure every relationship target is produced.");
                            return new CompletedFindingCatalogResult.Failed(diagnostic);
                        }
                    }
                }

                _orderedHeaders = _headers.Values.OrderBy(header => header.Reference.Value, StringComparer.Ordinal).ToList();
                _relationshipBlocks = relationships.Index;
                _locationBlocks = locations.Index;
                _evidenceBlocks = evidence.Index;
                _canonicalLocationsPath = locations.Spool.Path;
                _canonicalEvidencePath = evidence.Spool.Path;
                _canonicalRelationshipsPath = relationships.Spool.Path;
                _canonicalSpools.Add(relationships.Spool);
                _canonicalSpools.Add(locations.Spool);
                _canonicalSpools.Add(evidence.Spool);
                _completed = true;
                return new CompletedFindingCatalogResult.Succeeded(this);
            }
        }

        public async IAsyncEnumerable<IDetailedFindingCursor> ReadCanonicalAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            List<DetailedFindingHeader> ordered;
            Dictionary<string, (long Start, long End)> locationBlocks;
            Dictionary<string, (long Start, long End)> evidenceBlocks;
            Dictionary<string, (long Start, long End)> relationshipBlocks;
            string locationsPath;
            string evidencePath;
            string relationshipsPath;

            lock (_syncRoot)
            {
                if (!_completed)
                {
                    throw new InvalidOperationException("The catalog must be completed before it can be read.");
                }

                ordered = _orderedHeaders!;
                locationBlocks = _locationBlocks!;
                evidenceBlocks = _evidenceBlocks!;
                relationshipBlocks = _relationshipBlocks!;
                locationsPath = _canonicalLocationsPath!;
                evidencePath = _canonicalEvidencePath!;
                relationshipsPath = _canonicalRelationshipsPath!;
            }

            foreach (var header in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new FindingCursor(
                    header,
                    _spoolReaderFactory,
                    locationsPath,
                    locationBlocks.TryGetValue(header.Reference.Value, out var locationBlock) ? locationBlock : null,
                    evidencePath,
                    evidenceBlocks.TryGetValue(header.Reference.Value, out var evidenceBlock) ? evidenceBlock : null,
                    relationshipsPath,
                    relationshipBlocks.TryGetValue(header.Reference.Value, out var relationshipBlock) ? relationshipBlock : null);
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            await _locationsSpool.DisposeAsync();
            await _evidenceSpool.DisposeAsync();
            await _relationshipsSpool.DisposeAsync();
            foreach (var spool in _canonicalSpools)
            {
                await spool.DisposeAsync();
            }

            _canonicalSpools.Clear();
        }

        private async ValueTask AppendPairLinesAsync(string first, string second, CancellationToken cancellationToken)
        {
            await _relationshipsSpool.AppendLineAsync(first, cancellationToken);
            await _relationshipsSpool.AppendLineAsync(second, cancellationToken);
        }

        private static FindingReference FindingOf(FindingEvidenceRecord evidence) => evidence switch
        {
            FindingEvidenceRecord.MissingCandle record => record.Finding,
            FindingEvidenceRecord.TimeGapHeader record => record.Finding,
            FindingEvidenceRecord.TimeGapMissingReference record => record.Finding,
            FindingEvidenceRecord.DuplicateHeader record => record.Finding,
            FindingEvidenceRecord.DuplicateDifferingField record => record.Finding,
            FindingEvidenceRecord.DuplicateRow record => record.Finding,
            FindingEvidenceRecord.InvalidOhlcValues record => record.Finding,
            FindingEvidenceRecord.InvalidOhlcViolation record => record.Finding,
            FindingEvidenceRecord.ClosedMarket record => record.Finding,
            FindingEvidenceRecord.MalformedHeader record => record.Finding,
            FindingEvidenceRecord.MalformedFieldErrorRecord record => record.Finding,
            FindingEvidenceRecord.MalformedSkippedCheck record => record.Finding,
            _ => throw new ArgumentOutOfRangeException(nameof(evidence))
        };

        private void TrackTarget(string finding, string target)
        {
            if (!_relationshipTargets.TryGetValue(finding, out var targets))
            {
                targets = new HashSet<string>();
                _relationshipTargets.Add(finding, targets);
            }

            targets.Add(target);
        }

        private async Task<(ISpoolWriter Spool, Dictionary<string, (long Start, long End)> Index)>
            CanonicalizeAsync(ISpoolWriter appendSpool, CancellationToken cancellationToken)
        {
            var reader = _spoolReaderFactory(appendSpool.Path);
            ISpoolReplayableRun? replayRun = null;
            IAsyncEnumerable<string> sorted;
            if (_canonicalSorter is null)
            {
                sorted = SortInMemoryAsync(reader, cancellationToken);
            }
            else
            {
                replayRun = await _canonicalSorter.PrepareAsync(reader, cancellationToken);
                sorted = replayRun.ReplayAsync(cancellationToken);
            }

            try
            {
                var spool = _spoolWriterFactory();
                var index = new Dictionary<string, (long Start, long End)>();
                string? currentReference = null;
                var blockStart = 0L;

                await foreach (var line in sorted)
                {
                    var reference = RefOf(line);
                    if (reference != currentReference)
                    {
                        if (currentReference is not null)
                        {
                            index[currentReference] = (blockStart, spool.BytesWritten);
                        }

                        currentReference = reference;
                        blockStart = spool.BytesWritten;
                    }

                    await spool.AppendLineAsync(line, cancellationToken);
                }

                if (currentReference is not null)
                {
                    index[currentReference] = (blockStart, spool.BytesWritten);
                }

                await spool.CompleteAsync(cancellationToken);
                return (spool, index);
            }
            finally
            {
                if (replayRun is not null)
                {
                    await replayRun.DisposeAsync();
                }
            }
        }

        private async IAsyncEnumerable<string> SortInMemoryAsync(
            ISpoolReader source,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var lines = new List<string>();
            await foreach (var line in source.ReadLinesAsync(cancellationToken))
            {
                lines.Add(line);
            }

            foreach (var line in lines.OrderBy(RefOf, StringComparer.Ordinal))
            {
                yield return line;
            }
        }

        private static string RefOf(string line)
        {
            var separator = line.IndexOf('|');
            return separator >= 0 ? line.Substring(0, separator) : line;
        }

        private CategoryStatistics StatisticsFor(FindingCategory category)
        {
            var index = CategoryIndex(category);
            return new CategoryStatistics(_entryCounts[index], _contributionSums[index]);
        }

        private static int CategoryIndex(FindingCategory category) => category switch
        {
            FindingCategory.MissingCandle => 0,
            FindingCategory.DuplicateRecord => 1,
            FindingCategory.InvalidOhlc => 2,
            FindingCategory.ClosedMarketRecord => 3,
            FindingCategory.TimeGap => 4,
            FindingCategory.MalformedRow => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };

        private void ThrowIfTerminal()
        {
            if (_completed)
            {
                throw new InvalidOperationException("A completed catalog cannot accept more findings.");
            }

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FindingCatalog));
            }
        }

        private sealed class FindingCursor : IDetailedFindingCursor
        {
            private readonly DetailedFindingHeader _header;
            private readonly Func<string, ISpoolSeekableReader> _readerFactory;
            private readonly string _locationsPath;
            private readonly (long Start, long End)? _locationBlock;
            private readonly string _evidencePath;
            private readonly (long Start, long End)? _evidenceBlock;
            private readonly string _relationshipsPath;
            private readonly (long Start, long End)? _relationshipBlock;

            public FindingCursor(
                DetailedFindingHeader header,
                Func<string, ISpoolSeekableReader> readerFactory,
                string locationsPath,
                (long Start, long End)? locationBlock,
                string evidencePath,
                (long Start, long End)? evidenceBlock,
                string relationshipsPath,
                (long Start, long End)? relationshipBlock)
            {
                _header = header;
                _readerFactory = readerFactory;
                _locationsPath = locationsPath;
                _locationBlock = locationBlock;
                _evidencePath = evidencePath;
                _evidenceBlock = evidenceBlock;
                _relationshipsPath = relationshipsPath;
                _relationshipBlock = relationshipBlock;
            }

            public DetailedFindingHeader Header => _header;

            public IAsyncEnumerable<long> ReadSourceLinesAsync(CancellationToken cancellationToken = default) =>
                EnumerateSourceLinesAsync(cancellationToken);

            public IAsyncEnumerable<FindingRelationship> ReadRelationshipsAsync(CancellationToken cancellationToken = default) =>
                EnumerateRelationshipsAsync(cancellationToken);

            public IAsyncEnumerable<FindingEvidenceRecord> ReadEvidenceAsync(CancellationToken cancellationToken = default) =>
                EnumerateEvidenceAsync(cancellationToken);

            private async IAsyncEnumerable<long> EnumerateSourceLinesAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                if (_locationBlock is null)
                {
                    yield break;
                }

                await foreach (var line in _readerFactory(_locationsPath).ReadRangeAsync(_locationBlock.Value.Start, _locationBlock.Value.End, cancellationToken))
                {
                    var separator = line.IndexOf('|');
                    yield return long.Parse(line.Substring(separator + 1));
                }
            }

            private async IAsyncEnumerable<FindingRelationship> EnumerateRelationshipsAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                if (_relationshipBlock is null)
                {
                    yield break;
                }

                await foreach (var line in _readerFactory(_relationshipsPath).ReadRangeAsync(_relationshipBlock.Value.Start, _relationshipBlock.Value.End, cancellationToken))
                {
                    var parts = line.Split('|', 3);
                    yield return new FindingRelationship(parts[1], new FindingReference(parts[2]));
                }
            }

            private async IAsyncEnumerable<FindingEvidenceRecord> EnumerateEvidenceAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                if (_evidenceBlock is null)
                {
                    yield break;
                }

                await foreach (var line in _readerFactory(_evidencePath).ReadRangeAsync(_evidenceBlock.Value.Start, _evidenceBlock.Value.End, cancellationToken))
                {
                    var parts = line.Split('|', 3);
                    var type = EvidenceTypeRegistry[parts[1]];
                    var record = (FindingEvidenceRecord)JsonSerializer.Deserialize(parts[2], type, SerializerOptions)!;
                    yield return record;
                }
            }
        }

        private static readonly Dictionary<string, Type> EvidenceTypeRegistry = new()
        {
            ["MissingCandle"] = typeof(FindingEvidenceRecord.MissingCandle),
            ["TimeGap"] = typeof(FindingEvidenceRecord.TimeGapHeader),
            ["TimeGapMissingReference"] = typeof(FindingEvidenceRecord.TimeGapMissingReference),
            ["DuplicateRecord"] = typeof(FindingEvidenceRecord.DuplicateHeader),
            ["DuplicateDifferingField"] = typeof(FindingEvidenceRecord.DuplicateDifferingField),
            ["DuplicateRow"] = typeof(FindingEvidenceRecord.DuplicateRow),
            ["InvalidOhlc"] = typeof(FindingEvidenceRecord.InvalidOhlcValues),
            ["InvalidOhlcViolation"] = typeof(FindingEvidenceRecord.InvalidOhlcViolation),
            ["ClosedMarketRecord"] = typeof(FindingEvidenceRecord.ClosedMarket),
            ["MalformedRow"] = typeof(FindingEvidenceRecord.MalformedHeader),
            ["MalformedFieldError"] = typeof(FindingEvidenceRecord.MalformedFieldErrorRecord),
            ["MalformedSkippedCheck"] = typeof(FindingEvidenceRecord.MalformedSkippedCheck)
        };
    }
}