using System.Globalization;
using System.Text;
using System.Text.Json;
using Validator.Application.Abstractions;
using Validator.Domain.Calendars;

namespace Validator.Infrastructure.Calendars;

public sealed class CalendarJsonLoader
{
    private static readonly HashSet<string> RootProperties =
        ["version", "name", "timeZone", "sessions"];

    private static readonly HashSet<string> SessionProperties =
        ["openDay", "openTime", "closeDay", "closeTime"];

    private readonly NodaTimeScheduleExpander _expander;

    public CalendarJsonLoader(NodaTimeScheduleExpander? expander = null)
    {
        _expander = expander ?? new NodaTimeScheduleExpander();
    }

    public Validator.Application.Abstractions.IMarketCalendar Load(
        string jsonPath,
        MarketProfile profile = MarketProfile.Custom)
    {
        if (profile is not (MarketProfile.Custom or MarketProfile.Equities))
        {
            throw new ArgumentException(
                "Calendar files are supported only for custom and equities profiles.",
                nameof(profile));
        }

        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException("Calendar JSON not found.", jsonPath);
        }

        JsonDocument document;
        try
        {
            using var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            document = JsonDocument.Parse(reader.ReadToEnd(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw new InvalidDataException("Calendar JSON is malformed or is not valid UTF-8.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            RequireObject(root, "Calendar JSON root");
            RejectAdditionalProperties(root, RootProperties, "calendar");

            var version = RequireInteger(root, "version");
            if (version != 1)
            {
                throw new InvalidDataException(
                    $"Unsupported calendar version '{version}'. Only version 1 is supported.");
            }

            var name = RequireString(root, "name");
            if (name.Length is < 1 or > 100)
            {
                throw new InvalidDataException("Calendar name must contain between 1 and 100 characters.");
            }

            var timeZoneId = RequireString(root, "timeZone");
            var sessionsElement = RequireProperty(root, "sessions");
            if (sessionsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Calendar property 'sessions' must be an array.");
            }

            var sessionCount = sessionsElement.GetArrayLength();
            if (sessionCount is < 1 or > 14)
            {
                throw new InvalidDataException("Calendar sessions must contain between 1 and 14 entries.");
            }

            var sessions = sessionsElement.EnumerateArray().Select(ParseSession).ToArray();
            MarketCalendarDefinition definition;
            try
            {
                definition = new MarketCalendarDefinition(
                    profile,
                    version,
                    name,
                    timeZoneId,
                    sessions);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Calendar sessions overlap or are invalid: {exception.Message}", exception);
            }

            _expander.ValidateDefinition(definition.TimeZoneId, definition.Sessions);
            return new WeeklyMarketCalendar(definition, _expander);
        }
    }

    private static WeeklySession ParseSession(JsonElement element)
    {
        RequireObject(element, "Calendar session");
        RejectAdditionalProperties(element, SessionProperties, "calendar session");

        var openDay = ParseDay(RequireString(element, "openDay"), "openDay");
        var openTime = ParseTime(RequireString(element, "openTime"), "openTime");
        var closeDay = ParseDay(RequireString(element, "closeDay"), "closeDay");
        var closeTime = ParseTime(RequireString(element, "closeTime"), "closeTime");

        try
        {
            return new WeeklySession(openDay, openTime, closeDay, closeTime);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Calendar session is invalid: {exception.Message}", exception);
        }
    }

    private static DayOfWeek ParseDay(string value, string propertyName)
    {
        if (!Enum.TryParse<DayOfWeek>(value, ignoreCase: false, out var day) ||
            !Enum.IsDefined(day))
        {
            throw new InvalidDataException(
                $"Calendar property '{propertyName}' has invalid day '{value}'.");
        }

        return day;
    }

    private static TimeSpan ParseTime(string value, string propertyName)
    {
        var formats = value.Count(character => character == ':') == 1
            ? new[] { @"hh\:mm" }
            : new[] { @"hh\:mm\:ss" };

        if (!TimeSpan.TryParseExact(value, formats, CultureInfo.InvariantCulture, out var time) ||
            time < TimeSpan.Zero ||
            time >= TimeSpan.FromDays(1))
        {
            throw new InvalidDataException(
                $"Calendar property '{propertyName}' has invalid local time '{value}'.");
        }

        return time;
    }

    private static int RequireInteger(JsonElement element, string propertyName)
    {
        var property = RequireProperty(element, propertyName);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            throw new InvalidDataException($"Calendar property '{propertyName}' must be an integer.");
        }

        return value;
    }

    private static string RequireString(JsonElement element, string propertyName)
    {
        var property = RequireProperty(element, propertyName);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Calendar property '{propertyName}' must be a string.");
        }

        return property.GetString()!;
    }

    private static JsonElement RequireProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            throw new InvalidDataException($"Calendar JSON is missing required property '{propertyName}'.");
        }

        return property;
    }

    private static void RequireObject(JsonElement element, string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{description} must be a JSON object.");
        }
    }

    private static void RejectAdditionalProperties(
        JsonElement element,
        IReadOnlySet<string> allowed,
        string description)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new InvalidDataException(
                    $"Unknown {description} property '{property.Name}' is not allowed.");
            }
        }
    }
}
