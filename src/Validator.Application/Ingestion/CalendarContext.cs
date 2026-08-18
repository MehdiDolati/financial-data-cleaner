using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Domain.Calendars;

namespace Validator.Application.Ingestion
{
    // Resolved market calendar identity and weekly session definitions.
    public sealed record CalendarContext
    {
        private static readonly string[] KnownProfiles = ["forex", "equities", "crypto", "custom"];

        public string Profile { get; }
        public string Name { get; }
        public string? TimeZone { get; }
        public string? DefinitionSha256 { get; }
        public IReadOnlyList<WeeklySession> Sessions { get; }

        public CalendarContext(
            string profile,
            string name,
            IReadOnlyList<WeeklySession>? sessions = null,
            string? timeZone = null,
            string? definitionSha256 = null)
        {
            if (!KnownProfiles.Contains(profile))
            {
                throw new ArgumentException("Profile must be forex, equities, crypto, or custom.", nameof(profile));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Calendar name must be a non-empty value.", nameof(name));
            }

            if (definitionSha256 is not null &&
                (definitionSha256.Length != 64 || definitionSha256.Any(character => !SourceIdentity.IsLowerHex(character))))
            {
                throw new ArgumentException("Definition SHA-256 must be exactly 64 lower-case hexadecimal characters.", nameof(definitionSha256));
            }

            Profile = profile;
            Name = name;
            TimeZone = timeZone;
            DefinitionSha256 = definitionSha256;
            Sessions = sessions ?? Array.Empty<WeeklySession>();
        }
    }
}