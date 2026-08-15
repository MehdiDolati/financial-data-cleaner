using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Validator.Domain.Tests.Architecture
{
    public class DependencyRulesTests
    {
        [Fact]
        public void DomainAssembly_Has_No_NonBcl_ProjectReferences()
        {
            // Load the domain assembly by name
            var asm = Assembly.Load("Validator.Domain")!; // non-null asserted for test runtime
            var referenced = asm.GetReferencedAssemblies()?.Select(a => a.Name ?? string.Empty).ToArray() ?? Array.Empty<string>();

            // Allowlist common BCL and Microsoft assemblies (conservative)
            string[] allowedPrefixes = new[] { "System", "Microsoft", "netstandard", "net5", "mscorlib", "Microsoft.Extensions", "NUnit", "xunit", "FluentAssertions" };

            var forbidden = referenced.Where(r => !allowedPrefixes.Any(p => r.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToArray();

            Assert.True(forbidden.Length == 0, "Domain assembly must not reference non-BCL packages directly: " + string.Join(", ", forbidden));
        }
    }
}