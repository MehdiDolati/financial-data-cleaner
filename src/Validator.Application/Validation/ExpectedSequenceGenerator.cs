using System;
using System.Collections.Generic;
using Validator.Domain.Candles;

namespace Validator.Application.Validation
{
    public sealed class ExpectedSequenceGenerator
    {
        public IEnumerable<DateTimeOffset> Generate(DateTimeOffset startUtc, DateTimeOffset endUtc, TimeSpan interval)
        {
            var current = startUtc;
            while (current <= endUtc)
            {
                yield return current;
                current = current.Add(interval);
            }
        }
    }
}