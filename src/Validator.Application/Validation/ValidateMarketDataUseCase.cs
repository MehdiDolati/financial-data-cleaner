using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;
using Validator.Domain.Candles;

namespace Validator.Application.Validation
{
    public sealed class ValidateMarketDataUseCase : IValidateMarketDataUseCase
    {
        private readonly ICandleSource _source;
        private readonly IReportWriter _reportWriter;

        public ValidateMarketDataUseCase(ICandleSource source, IReportWriter reportWriter)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _reportWriter = reportWriter ?? throw new ArgumentNullException(nameof(reportWriter));
        }

        public async Task<int> ExecuteAsync(object request)
        {
            var validationRequest = request as ValidationRequest ?? throw new ArgumentException("Request must be ValidationRequest", nameof(request));

            var candles = new List<PriceCandle>();
            await foreach (var candle in _source.ReadAllAsync())
            {
                candles.Add(candle);
            }

            if (candles.Count == 0)
            {
                var emptyReport = new ValidationReport(
                    new ValidationSummary(0, 0, 0),
                    new DateRange(DateTimeOffset.MinValue, DateTimeOffset.MinValue),
                    validationRequest.InputPath);

                await _reportWriter.WriteReportAsync(emptyReport);
                return emptyReport.IsClean ? 0 : 1;
            }

            var minTs = candles.Min(c => c.Timestamp);
            var maxTs = candles.Max(c => c.Timestamp);
            var summary = new ValidationSummary(
                TotalFindings: candles.Count(c => c.High < c.Low || c.High == c.Low || c.Volume < 0m || c.Close <= 0m),
                MalformedRows: 0,
                ValidRows: candles.Count);

            var report = new ValidationReport(summary, new DateRange(minTs, maxTs), validationRequest.InputPath);
            await _reportWriter.WriteReportAsync(report);
            return report.IsClean ? 0 : 1;
        }
    }
}