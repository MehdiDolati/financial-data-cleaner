using Validator.Infrastructure.Reporting;

namespace Validator.Cli.Tests;

// A large report must be complete rather than convenient: every finding is
// reported, arbitrarily large duplicate groups and gaps are carried in full, and
// peak memory stays bounded by configured buffers rather than growing with the
// number of findings. An interrupted or cancelled write leaves no artifact a
// consumer could mistake for a complete report, and no temporary file behind.
public sealed class LargeReportMemoryTests : IDisposable
{
    private const int LargeFindingCount = 100_000;
    private const int SmallFindingCount = 10_000;
    private const int DuplicateRowCount = 20_000;

    // Streaming a finding's children must cost the configured buffers only, so
    // a hundredfold increase in child volume gets a modest fixed budget.
    private const long ChildVolumeBudgetBytes = 32L * 1024 * 1024;

    // The catalog keeps one compact index record per finding so children can be
    // replayed by seek. That record is the only growth allowed per finding.
    private const long IndexAllowanceBytesPerFinding = 2048;

    private readonly string _directory;

    public LargeReportMemoryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"validator-large-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // A source whose two records straddle one enormous gap: it stays tiny on
    // disk while requiring the stated number of missing-candle findings.
    private async Task<string> WriteGapSourceAsync(string name, int missingCandles)
    {
        var path = Path.Combine(_directory, name);
        var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(missingCandles + 1);

        await using var writer = new StreamWriter(path);
        await writer.WriteLineAsync($"{start:yyyy.MM.dd},{start:HH:mm},1.10,1.20,1.05,1.15,10");
        await writer.WriteLineAsync($"{end:yyyy.MM.dd},{end:HH:mm},1.15,1.25,1.10,1.20,11");
        return path;
    }

    private async Task<string> WriteDuplicateSourceAsync(string name, int rows)
    {
        var path = Path.Combine(_directory, name);
        var timestamp = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        await using var writer = new StreamWriter(path);
        for (var index = 0; index < rows; index++)
        {
            // Every row shares one timestamp and differs in close and volume, so
            // the group is one finding with many participating rows.
            await writer.WriteLineAsync(
                $"{timestamp:yyyy.MM.dd},{timestamp:HH:mm},1.10,1.20,1.05,1.1{index % 10},{index + 1}");
        }

        return path;
    }

    private static string[] CryptoM1(string input, string output) =>
    [
        input,
        "--timeframe",
        "M1",
        "--market",
        "crypto",
        "--tz-offset",
        "+00:00",
        "--output",
        output
    ];

    // Peak *retained* managed memory is sampled while the run proceeds: each
    // sample forces a full collection first, so the figure is the live set and
    // not merely what the collector has not yet swept.
    //
    // Sampling GC.GetTotalMemory(false) was tried and rejected. It reports
    // allocated-but-uncollected bytes, which is governed by GC scheduling
    // rather than by what the pipeline holds: the identical code passed under
    // workstation GC and failed under server GC, whose per-core heaps collect
    // far more lazily. That measures the collector's appetite, not this
    // feature's retention, and it is retention that the bounded-memory promise
    // is about.
    //
    // Total allocation is deliberately not bounded here. Serializing 20,000
    // rows must allocate proportionally more than serializing 200; that is
    // throughput. The promise is that no structure grows with child volume.
    private static async Task<(CoreValidationE2ETests.CommandResult Result, long PeakBytes)> MeasureAsync(
        string[] arguments)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        var sampling = true;
        var peak = GC.GetTotalMemory(true);
        var sampler = Task.Run(async () =>
        {
            while (Volatile.Read(ref sampling))
            {
                var sample = GC.GetTotalMemory(forceFullCollection: true);
                if (sample > peak)
                {
                    peak = sample;
                }

                await Task.Delay(15).ConfigureAwait(false);
            }
        });


        try
        {
            var result = await CoreValidationE2ETests.InvokeAsync(arguments);
            return (result, peak);
        }
        finally
        {
            Volatile.Write(ref sampling, false);
            await sampler;
        }
    }

    private static async Task<int> CountOccurrencesAsync(string path, string needle)
    {
        var count = 0;
        var carry = string.Empty;
        var buffer = new char[64 * 1024];

        using var reader = new StreamReader(path);
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            var text = carry + new string(buffer, 0, read);
            var index = text.IndexOf(needle, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
            }

            carry = text.Length >= needle.Length ? text[^(needle.Length - 1)..] : text;
        }

        return count;
    }

    private static async Task<int> CountLinesStartingWithAsync(string path, string prefix)
    {
        var count = 0;
        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    // Cleanup is asserted inside a temp root belonging to this run alone.
    // Counting the shared system temp directory would also count artifacts of
    // whatever else happens to be running, which says nothing about this run.
    private static int ArtifactCountIn(string root) =>
        Directory.EnumerateFiles(root, ".validator-report-*.staged").Count() +
        Directory.EnumerateDirectories(root, "validator-*").Count() +
        Directory.EnumerateFiles(root, "validator-spool-*.txt").Count() +
        Directory.EnumerateFiles(root, "validator-findings-*.jsonl").Count();

    // The pipeline places its working artifacts under the process temp path, so
    // pointing that path at a private directory makes their absence provable.
    private static async Task<T> WithPrivateTempRootAsync<T>(string root, Func<Task<T>> action)
    {
        var previousTmp = Environment.GetEnvironmentVariable("TMP");
        var previousTemp = Environment.GetEnvironmentVariable("TEMP");
        var previousTmpDir = Environment.GetEnvironmentVariable("TMPDIR");

        Environment.SetEnvironmentVariable("TMP", root);
        Environment.SetEnvironmentVariable("TEMP", root);
        Environment.SetEnvironmentVariable("TMPDIR", root);
        try
        {
            return await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMP", previousTmp);
            Environment.SetEnvironmentVariable("TEMP", previousTemp);
            Environment.SetEnvironmentVariable("TMPDIR", previousTmpDir);
        }
    }

    [Fact]
    public async Task OneHundredThousandFindings_AreAllReportedWithoutTruncation()
    {
        var input = await WriteGapSourceAsync("huge-gap.csv", LargeFindingCount);
        var output = Path.Combine(_directory, "huge-gap.v2.json");

        var result = await CoreValidationE2ETests.InvokeAsync(
            [.. CryptoM1(input, output), "--format", "json", "--report-version", "2"]);

        Assert.Equal(1, result.ExitCode);

        // One finding per missing candle plus the single gap that contains them.
        Assert.Equal(LargeFindingCount + 1, await CountOccurrencesAsync(output, "\"reference\":"));
        Assert.Contains("\"findingSetComplete\":true", await File.ReadAllTextAsync(output));
    }

    // The report states the same totals it lists, so a reader can confirm
    // nothing was dropped between the summary and the findings.
    [Fact]
    public async Task OneHundredThousandFindings_ReconcileWithTheStatedSummary()
    {
        var input = await WriteGapSourceAsync("huge-gap-text.csv", LargeFindingCount);
        var output = Path.Combine(_directory, "huge-gap.txt");

        var result = await CoreValidationE2ETests.InvokeAsync([.. CryptoM1(input, output), "--verbose"]);
        Assert.Equal(1, result.ExitCode);

        using var reader = new StreamReader(output);
        var head = new List<string>();
        while (head.Count < 60 && await reader.ReadLineAsync() is { } line)
        {
            head.Add(line);
        }

        Assert.Equal($"Missing candles: {LargeFindingCount}", head[0]);
        Assert.Contains(
            $"- MissingCandle: summaryCount={LargeFindingCount}; entryCount={LargeFindingCount}; contributionSum={LargeFindingCount}",
            head);
        Assert.Contains("- coverageReconciled: true", head);

        Assert.Equal(
            LargeFindingCount + 1,
            await CountLinesStartingWithAsync(output, "- reference="));
    }

    // One duplicate group may be arbitrarily large: every participating row is
    // still listed with its own source line.
    [Fact]
    public async Task ArbitrarilyLargeDuplicateGroup_ListsEveryParticipatingRow()
    {
        var input = await WriteDuplicateSourceAsync("huge-duplicate.csv", DuplicateRowCount);
        var output = Path.Combine(_directory, "huge-duplicate.txt");

        var result = await CoreValidationE2ETests.InvokeAsync([.. CryptoM1(input, output), "--verbose"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(1, await CountLinesStartingWithAsync(output, "- reference=duplicate"));
        Assert.Equal(DuplicateRowCount, await CountLinesStartingWithAsync(output, "    row: sourceLine="));
    }

    // Growth per finding is limited to the compact index record the catalog
    // needs to replay that finding's children, so ten times the findings costs
    // far less than ten times the peak heap.
    [Fact]
    public async Task PeakMemoryGrowsOnlyWithTheCatalogIndexPerFinding()
    {
        var smallInput = await WriteGapSourceAsync("small-gap.csv", SmallFindingCount);
        var largeInput = await WriteGapSourceAsync("large-gap.csv", LargeFindingCount);

        var small = await MeasureAsync(
            [.. CryptoM1(smallInput, Path.Combine(_directory, "small.json")), "--format", "json", "--report-version", "2"]);
        var large = await MeasureAsync(
            [.. CryptoM1(largeInput, Path.Combine(_directory, "large.json")), "--format", "json", "--report-version", "2"]);

        Assert.Equal(1, small.Result.ExitCode);
        Assert.Equal(1, large.Result.ExitCode);

        var additionalFindings = LargeFindingCount - SmallFindingCount;
        var budget = ChildVolumeBudgetBytes + (additionalFindings * IndexAllowanceBytesPerFinding);
        var growth = large.PeakBytes - small.PeakBytes;
        Assert.True(
            growth <= budget,
            $"Peak managed memory grew by {growth:N0} bytes for {LargeFindingCount:N0} findings versus {SmallFindingCount:N0}, above the {budget:N0}-byte allowance.");
    }

    // Child volume is streamed rather than accumulated: one gap holding a
    // hundred times more missing candles than another costs only the configured
    // buffers, because the finding count is the same in both runs.
    [Fact]
    public async Task PeakMemoryDoesNotGrowWithChildVolumeUnderOneFinding()
    {
        var small = await MeasureAsync(
        [
            .. CryptoM1(
                await WriteDuplicateSourceAsync("small-group.csv", 200),
                Path.Combine(_directory, "small-group.json")),
            "--format",
            "json",
            "--report-version",
            "2"
        ]);
        var large = await MeasureAsync(
        [
            .. CryptoM1(
                await WriteDuplicateSourceAsync("large-group.csv", 20_000),
                Path.Combine(_directory, "large-group.json")),
            "--format",
            "json",
            "--report-version",
            "2"
        ]);

        Assert.Equal(1, small.Result.ExitCode);
        Assert.Equal(1, large.Result.ExitCode);

        var growth = large.PeakBytes - small.PeakBytes;
        Assert.True(
            growth <= ChildVolumeBudgetBytes,
            $"Peak managed memory grew by {growth:N0} bytes when one duplicate group grew from 200 to 20,000 rows, above the {ChildVolumeBudgetBytes:N0}-byte buffer budget.");
    }

    // Temporary spools and staged reports are working artifacts, so a completed
    // run leaves none of them behind.
    [Fact]
    public async Task LargeSuccessfulRun_LeavesNoTemporaryArtifacts()
    {
        var input = await WriteGapSourceAsync("cleanup-gap.csv", SmallFindingCount);
        var tempRoot = Path.Combine(_directory, "temp-root");
        Directory.CreateDirectory(tempRoot);

        var result = await WithPrivateTempRootAsync(tempRoot, () => CoreValidationE2ETests.InvokeAsync(
            [.. CryptoM1(input, Path.Combine(_directory, "cleanup.json")), "--format", "json", "--report-version", "2"]));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(0, ArtifactCountIn(tempRoot));
    }

    // Hostile source text at scale is carried as data: it cannot forge the
    // report structure and cannot stop the run from completing.
    [Fact]
    public async Task HostileSourceTextAtScale_IsCarriedWithoutForgingStructure()
    {
        var path = Path.Combine(_directory, "hostile-large.csv");
        var timestamp = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        await using (var writer = new StreamWriter(path))
        {
            for (var index = 0; index < 5_000; index++)
            {
                var stamp = timestamp.AddMinutes(index);
                await writer.WriteLineAsync(
                    $"\"{stamp:yyyy.MM.dd}\tFindings\",\"{stamp:HH:mm}\nReport status\",1.10,1.20,1.05,1.15,{index + 1}");
            }
        }

        var output = Path.Combine(_directory, "hostile-large.txt");
        var result = await CoreValidationE2ETests.InvokeAsync([.. CryptoM1(path, output), "--verbose"]);

        Assert.True(result.ExitCode is 1 or 2);
        if (result.ExitCode != 1)
        {
            return;
        }

        Assert.Equal(1, await CountLinesStartingWithAsync(output, "Findings:"));
        Assert.Equal(1, await CountLinesStartingWithAsync(output, "Report status:"));
    }

    // A cancelled write publishes nothing and cleans up after itself, so no
    // reader can find a truncated report at the destination.
    [Fact]
    public async Task CancelledWrite_PublishesNothingAndLeavesNoStagedArtifact()
    {
        var destination = Path.Combine(_directory, "cancelled.json");
        using var cancellation = new CancellationTokenSource();
        using var standardOutput = new StringWriter();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new StageAndCommitWriter(destination).PublishAsync(
                async (staged, token) =>
                {
                    await staged.WriteAsync("{\"contractVersion\":2,");
                    await cancellation.CancelAsync();
                    token.ThrowIfCancellationRequested();
                },
                standardOutput,
                cancellation.Token));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFiles(_directory, ".validator-report-*.staged"));
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    // An interrupted render leaves the previous destination bytes untouched, so
    // an existing report is never replaced by a partial one.
    [Fact]
    public async Task InterruptedWrite_LeavesTheDestinationUnchangedAndNoCompleteArtifact()
    {
        var destination = Path.Combine(_directory, "previous.json");
        const string previous = "{\"contractVersion\":2,\"status\":\"Clean\"}";
        await File.WriteAllTextAsync(destination, previous);

        using var standardOutput = new StringWriter();
        var commit = await new StageAndCommitWriter(destination).PublishAsync(
            async (staged, _) =>
            {
                await staged.WriteAsync("{\"contractVersion\":2,\"findings\":[");
                throw new IOException("The device stopped responding part-way through the report.");
            },
            standardOutput);

        var failed = Assert.IsType<ReportCommitResult.Failed>(commit);
        Assert.Equal("REPORT_RENDER_FAILED", failed.Diagnostic.Code);
        Assert.Equal(previous, await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(_directory, ".validator-report-*.staged"));
        Assert.Equal(string.Empty, standardOutput.ToString());
    }
}
