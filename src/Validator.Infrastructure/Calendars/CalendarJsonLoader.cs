using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Validator.Application.Abstractions;

namespace Validator.Infrastructure.Calendars
{
    public class CalendarJsonLoader
    {
        public Validator.Application.Abstractions.IMarketCalendar Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Calendar JSON not found", jsonPath);

            var txt = File.ReadAllText(jsonPath);
            var cfg = JsonSerializer.Deserialize<WeeklyCalendarConfig>(txt, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (cfg == null)
                throw new InvalidDataException("Calendar JSON could not be parsed");

            if (!string.Equals(cfg.Type, "weekly", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Unsupported calendar type: {cfg.Type}");

            var days = new List<DayOfWeek>();
            foreach (var d in cfg.OpenDays ?? Array.Empty<string>())
            {
                if (Enum.TryParse<DayOfWeek>(d, true, out var dow))
                    days.Add(dow);
                else
                    throw new InvalidDataException($"Invalid open day: {d}");
            }

            // For now only support UTC timezone specifier
            if (!string.Equals(cfg.Timezone ?? "UTC", "UTC", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Only UTC timezone is supported in this loader. Requested: {cfg.Timezone}");

            return new WeeklyMarketCalendar(days, cfg.OpenHour, cfg.CloseHour); // implements Validator.Application.Abstractions.IMarketCalendar
        }
    }

    public class WeeklyCalendarConfig
    {
        public string? Type { get; set; }
        public string[]? OpenDays { get; set; }
        public int OpenHour { get; set; }
        public int CloseHour { get; set; }
        public string? Timezone { get; set; }
    }
}
