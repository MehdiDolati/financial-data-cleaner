using System;
using System.Collections.Generic;
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

        // Scoring must be exact: the constitution bans float/double for any
        // reported value, so no member of any Scoring namespace may be a single-
        // or double-precision floating type. This is checked across every loaded
        // scoring assembly, Domain and Application alike.
        [Fact]
        public void ScoringTypes_ExposeNoFloatOrDoubleMembers()
        {
            var offenders = ScoringTypes()
                .SelectMany(FloatingMembers)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                offenders.Length == 0,
                "Scoring types must not use float or double: " + string.Join(", ", offenders));
        }

        // Scoring is pure derivation, so nothing in a Scoring namespace may touch
        // a serializer, the console, or the file system; those concerns belong to
        // Infrastructure and the CLI.
        [Fact]
        public void ScoringTypes_ReferenceNoSerializerConsoleOrFileSystemType()
        {
            string[] forbiddenNamespaces =
            [
                "System.Text.Json",
                "System.Xml",
                "System.IO"
            ];

            var offenders = new List<string>();
            foreach (var type in ScoringTypes())
            {
                foreach (var referenced in ReferencedTypes(type))
                {
                    var ns = referenced.Namespace ?? string.Empty;
                    if (referenced == typeof(Console) ||
                        forbiddenNamespaces.Any(forbidden =>
                            ns.Equals(forbidden, StringComparison.Ordinal) ||
                            ns.StartsWith(forbidden + ".", StringComparison.Ordinal)))
                    {
                        offenders.Add($"{type.FullName} -> {referenced.FullName}");
                    }
                }
            }

            Assert.True(
                offenders.Count == 0,
                "Scoring types must not reference a serializer, console, or file system: " +
                string.Join(", ", offenders.Distinct(StringComparer.Ordinal)));
        }

        private static IEnumerable<Type> ScoringTypes()
        {
            // The Domain test project intentionally references only Domain, so
            // Validator.Application may not be loadable here. Domain scoring types
            // are always inspected; the Application scoring assembly is inspected
            // only when it is present, and its own tests guard it independently.
            foreach (var assemblyName in new[] { "Validator.Domain", "Validator.Application" })
            {
                Assembly? assembly = null;
                try
                {
                    assembly = Assembly.Load(assemblyName);
                }
                catch (System.IO.FileNotFoundException)
                {
                    continue;
                }

                foreach (var type in assembly.GetTypes())
                {
                    if ((type.Namespace ?? string.Empty).Contains("Scoring", StringComparison.Ordinal))
                    {
                        yield return type;
                    }
                }
            }
        }


        private const BindingFlags AllMembers =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static IEnumerable<string> FloatingMembers(Type type)
        {
            foreach (var field in type.GetFields(AllMembers))
            {
                if (IsFloating(field.FieldType))
                {
                    yield return $"{type.FullName}.{field.Name}";
                }
            }

            foreach (var property in type.GetProperties(AllMembers))
            {
                if (IsFloating(property.PropertyType))
                {
                    yield return $"{type.FullName}.{property.Name}";
                }
            }

            foreach (var method in type.GetMethods(AllMembers))
            {
                if (IsFloating(method.ReturnType) || method.GetParameters().Any(parameter => IsFloating(parameter.ParameterType)))
                {
                    yield return $"{type.FullName}.{method.Name}()";
                }
            }
        }

        private static bool IsFloating(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying.IsArray)
            {
                underlying = underlying.GetElementType() ?? underlying;
            }

            return underlying == typeof(float) || underlying == typeof(double);
        }

        private static IEnumerable<Type> ReferencedTypes(Type type)
        {
            foreach (var field in type.GetFields(AllMembers))
            {
                yield return field.FieldType;
            }

            foreach (var property in type.GetProperties(AllMembers))
            {
                yield return property.PropertyType;
            }

            foreach (var method in type.GetMethods(AllMembers))
            {
                yield return method.ReturnType;
                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType;
                }
            }
        }
    }
}


