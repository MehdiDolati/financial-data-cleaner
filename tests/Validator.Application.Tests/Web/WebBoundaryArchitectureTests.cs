using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Validator.Application.Web;

namespace Validator.Application.Tests.Web;

// Mechanically enforced boundary purity (FR-021, FR-022, FR-025, FR-030):
// the Validator.Application assembly must reference no HTTP/server, session/
// identity, or view/markup framework, and the web boundary types must accept
// no filesystem paths, console, environment, or ambient-culture types —
// streams are permitted, paths are not. This is the checkable form of
// Principle II asserted by test.
public class WebBoundaryArchitectureTests
{
    private static readonly Assembly ApplicationAssembly = typeof(WebRunId).Assembly;

    // Transport, server, session, identity, authorization, and view frameworks
    // that must never be referenced by the Application assembly.
    private static readonly string[] ProhibitedAssemblyPrefixes =
    [
        "Microsoft.AspNetCore",
        "System.Web",
        "System.Net.Http",
        "System.Security.Claims",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.DependencyInjection",
        "Blazor",
        "MudBlazor",
        "Razor"
    ];

    // Types that must never cross the web boundary as inputs or members.
    // System.IO.Stream is deliberately absent: streams are permitted, paths
    // are not (contracts/web-integration-contract.md).
    private static readonly string[] ProhibitedParameterTypes =
    [
        "System.Console",
        "System.Environment",
        "System.Globalization.CultureInfo",
        "System.TimeZoneInfo",
        "System.IO.File",
        "System.IO.Directory",
        "System.IO.Path",
        "System.IO.FileStream",
        "System.IO.StreamWriter",
        "System.IO.StreamReader",
        "System.Random"
    ];

    // Ambient APIs that must not appear inside the boundary's source.
    private static readonly string[] ProhibitedSourceTokens =
    [
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTimeOffset.Now",
        "DateTimeOffset.UtcNow",
        "Console.",
        "Environment.",
        "CultureInfo.CurrentCulture",
        "TimeZoneInfo.Local",
        "new Random",
        "Guid.NewGuid"
    ];

    private static Type[] BoundaryTypes =>
    [
        .. ApplicationAssembly.GetExportedTypes().Where(t =>
            t.Namespace == "Validator.Application.Web" ||
            t.Name is "IWebRunStore" or "IUploadedDatasetStore" or "IWebRunQueue" or "UploadedDataset" or "WebRunTransitionData")
    ];

    [Fact]
    public void Application_references_no_web_or_transport_framework_assemblies()
    {
        var referenced = ApplicationAssembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        var offenders = referenced.Where(name =>
                ProhibitedAssemblyPrefixes.Any(prefix =>
                    name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        offenders.Should().BeEmpty(
            "Validator.Application must stay transport-neutral; found prohibited references: {0}",
            string.Join(", ", offenders));
    }

    [Fact]
    public void Boundary_types_exist()
    {
        BoundaryTypes.Select(t => t.Name).Should().Contain(
        [
            "WebRunId",
            "WebRunStatus",
            "WebRunRecord",
            "WebRunRequest",
            "WebResultView",
            "IValidationWebService",
            "WebRunOptionsValidator"
        ]);
    }

    [Fact]
    public void Boundary_public_surface_exposes_no_prohibited_types()
    {
        var offenders = new List<string>();
        foreach (var type in BoundaryTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                CollectProhibitedParameters(method.GetParameters(), type, method, offenders);
            }

            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                CollectProhibitedParameters(constructor.GetParameters(), type, constructor, offenders);
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var payload = Unwrap(property.PropertyType);
                var fullName = payload?.FullName ?? property.PropertyType.FullName;
                if (fullName is not null && IsProhibited(fullName))
                {
                    offenders.Add($"{type.Name}.{property.Name}: {fullName}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "the web boundary must not expose prohibited types; found: {0}",
            string.Join("; ", offenders));
    }

    [Fact]
    public void Boundary_types_carry_no_markup_or_pre_rendered_text()
    {
        foreach (var type in BoundaryTypes)
        {
            var publicPropertyTypes = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.PropertyType.FullName ?? property.PropertyType.Name)
                .ToArray();

            var offenders = publicPropertyTypes.Where(name =>
                    name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("MarkupString", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("HtmlString", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            offenders.Should().BeEmpty(
                "the view must carry typed data only; {0} exposes {1}",
                type.Name, string.Join(", ", offenders));
        }
    }

    [Fact]
    public void Boundary_source_uses_no_ambient_or_console_apis()
    {
        var sourceDirectory = LocateBoundarySourceDirectory();
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            foreach (var token in ProhibitedSourceTokens.Where(source.Contains))
            {
                offenders.Add($"{Path.GetFileName(file)}: {token}");
            }
        }

        offenders.Should().BeEmpty(
            "ambient time, console, environment, culture, and randomness must not appear in the boundary; found: {0}",
            string.Join("; ", offenders));
    }

    private static string LocateBoundarySourceDirectory()
    {
        // Test assemblies run from <repo>/tests/Validator.Application.Tests/bin/<cfg>/<tfm>;
        // walk up until the repository root (marked by the solution file) is found.
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "FinancialDataCleaner.slnx")))
        {
            current = current.Parent;
        }

        current.Should().NotBeNull("the repository root must be reachable from the test output directory");

        var boundary = Path.Combine(current!.FullName, "src", "Validator.Application", "Web");
        Directory.Exists(boundary).Should().BeTrue("the web boundary source directory must exist");
        return boundary;
    }

    private static void CollectProhibitedParameters(
        ParameterInfo[] parameters,
        Type type,
        MemberInfo member,
        List<string> offenders)
    {
        foreach (var parameter in parameters)
        {
            var payloadType = Unwrap(parameter.ParameterType);
            if (payloadType is null)
            {
                continue;
            }

            var fullName = payloadType.FullName ?? payloadType.Name;
            if (IsProhibited(fullName))
            {
                offenders.Add($"{type.Name}.{member.Name}({parameter.Name}: {fullName})");
            }
        }
    }

    private static bool IsProhibited(string fullName) =>
        ProhibitedParameterTypes.Contains(fullName) ||
        ProhibitedAssemblyPrefixes.Any(prefix =>
            fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static Type? Unwrap(Type type)
    {
        if (type.IsByRef)
        {
            return Unwrap(type.GetElementType()!);
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Task<>) ||
                definition == typeof(ValueTask<>) ||
                definition == typeof(Nullable<>) ||
                definition == typeof(List<>) ||
                definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IEnumerable<>) ||
                definition == typeof(IAsyncEnumerable<>))
            {
                return Unwrap(type.GetGenericArguments()[0]);
            }
        }

        return type;
    }
}