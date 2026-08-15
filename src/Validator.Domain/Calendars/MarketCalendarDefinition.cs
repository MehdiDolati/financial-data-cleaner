using System;
using System.Collections.Generic;
using System.Linq;

namespace Validator.Domain.Calendars
{
    public sealed class MarketCalendarDefinition
    {
        public MarketProfile Profile { get; }
        public int Version { get; }
        public string Name { get; }
        public string TimeZoneId { get; }
        public IReadOnlyList<WeeklySession> Sessions { get; }

        public MarketCalendarDefinition(IEnumerable<WeeklySession> sessions)
            : this(MarketProfile.Custom, 1, "Custom", "UTC", sessions)
        {
        }

        public MarketCalendarDefinition(
            MarketProfile profile,
            int version,
            string name,
            string timeZoneId,
            IEnumerable<WeeklySession> sessions)
        {
            if (version != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version), "Only market calendar version 1 is supported.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Calendar name must not be empty.", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                throw new ArgumentException("Calendar time zone must not be empty.", nameof(timeZoneId));
            }

            var list = sessions?
                .OrderBy(session => session.OpenDay)
                .ThenBy(session => session.OpenTime)
                .ToArray() ?? Array.Empty<WeeklySession>();

            for (int i = 0; i < list.Length; i++)
            {
                for (int j = i + 1; j < list.Length; j++)
                {
                    if (list[i].Overlaps(list[j]))
                    {
                        throw new ArgumentException("Sessions must not overlap on the same day.");
                    }
                }
            }

            Profile = profile;
            Version = version;
            Name = name;
            TimeZoneId = timeZoneId;
            Sessions = Array.AsReadOnly(list);
        }
    }
}