using System;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Validation;
using Validator.Infrastructure.Csv;
using Validator.Infrastructure.Reporting;

namespace Validator.Cli.Commands
{
    public static class ValidateCommand
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                Console.WriteLine("Usage: validator <input-file> [--timeframe M1|H1|D1]");
                return 0;
            }

            var inputPath = args[0];
            string? timeframe = null;
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--timeframe" && i + 1 < args.Length)
                {
                    timeframe = args[i + 1];
                    i++;
                }
            }

            var request = new ValidationRequest(inputPath, timeframe, ReportFormat.Text, null, false);
            var source = new CsvCandleSource(inputPath);
            var writer = new TextReportWriter();
            var useCase = new ValidateMarketDataUseCase(source, writer);
            return await useCase.ExecuteAsync(request);
        }
    }
}