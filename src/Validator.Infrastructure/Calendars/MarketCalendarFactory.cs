using Validator.Application.Abstractions;
using Validator.Domain.Calendars;

namespace Validator.Infrastructure.Calendars;

public sealed class MarketCalendarFactory : IMarketCalendarFactory
{
    private readonly CalendarJsonLoader _loader;

    public MarketCalendarFactory(CalendarJsonLoader? loader = null)
    {
        _loader = loader ?? new CalendarJsonLoader();
    }

    public Validator.Application.Abstractions.IMarketCalendar Create(LocalCalendarRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Profile switch
        {
            MarketProfile.Forex when request.CalendarPath is null => new ForexCalendar(),
            MarketProfile.Crypto when request.CalendarPath is null => new CryptoCalendar(),
            MarketProfile.Equities when request.CalendarPath is null => new EquitiesCalendar(),
            MarketProfile.Equities => _loader.Load(request.CalendarPath!, MarketProfile.Equities),
            MarketProfile.Custom when !string.IsNullOrWhiteSpace(request.CalendarPath) =>
                _loader.Load(request.CalendarPath, MarketProfile.Custom),
            MarketProfile.Custom => throw new ArgumentException(
                "--calendar <path> is required when --market custom is selected."),
            _ => throw new ArgumentException(
                $"--calendar cannot be used with the {request.Profile.ToString().ToLowerInvariant()} market profile.")
        };
    }
}
