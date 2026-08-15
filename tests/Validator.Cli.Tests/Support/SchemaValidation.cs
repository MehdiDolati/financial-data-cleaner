using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Validator.Cli.Tests.Support
{
    public static class SchemaValidation
    {
        public static void AssertRequiredProperties(string json, params string[] requiredProperties)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            foreach (var property in requiredProperties)
            {
                if (!root.TryGetProperty(property, out _))
                {
                    throw new InvalidOperationException($"JSON payload is missing required property '{property}'.");
                }
            }
        }

        public static bool IsValidSubset(string json, IEnumerable<string> requiredProperties)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return requiredProperties.All(property => root.TryGetProperty(property, out _));
        }
    }
}
