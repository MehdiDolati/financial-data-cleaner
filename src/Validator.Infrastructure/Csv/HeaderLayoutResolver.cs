using System;
using System.Collections.Generic;
using System.Linq;

namespace Validator.Infrastructure.Csv
{
    public static class HeaderLayoutResolver
    {
        public static Dictionary<string, int> Resolve(IEnumerable<string> headers, params string[] requiredColumns)
        {
            if (headers is null)
            {
                throw new ArgumentNullException(nameof(headers));
            }

            var index = headers
                .Select((header, idx) => new { Header = header?.Trim() ?? string.Empty, Index = idx })
                .Where(item => !string.IsNullOrWhiteSpace(item.Header))
                .ToDictionary(item => item.Header, item => item.Index, StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var required in requiredColumns)
            {
                if (string.IsNullOrWhiteSpace(required))
                {
                    continue;
                }

                if (!index.TryGetValue(required.Trim(), out var columnIndex))
                {
                    throw new InvalidOperationException($"Required header '{required}' was not found in the CSV input.");
                }

                result[required.Trim()] = columnIndex;
            }

            return result;
        }
    }
}
