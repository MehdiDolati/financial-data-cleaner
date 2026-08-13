using System;
using System.Collections.Generic;
using System.Linq;

namespace Validator.Domain.Calendars
{
    public sealed class MarketCalendarDefinition
    {
        public IReadOnlyList<WeeklySession> Sessions { get; }

        public MarketCalendarDefinition(IEnumerable<WeeklySession> sessions)
        {
            var list = sessions?.OrderBy(s => s.Day).ThenBy(s => s.Open).ToArray() ?? Array.Empty<WeeklySession>();

            // Ensure strict ordering and non-overlap for sessions on same day
            for (int i = 0; i < list.Length; i++)
            {
                for (int j = i + 1; j < list.Length; j++)
                {
                    if (list[i].Day == list[j].Day && list[i].Overlaps(list[j]))
                        throw new ArgumentException("Sessions must not overlap on the same day.");
                }
            }

            Sessions = Array.AsReadOnly(list);
        }
    }
}