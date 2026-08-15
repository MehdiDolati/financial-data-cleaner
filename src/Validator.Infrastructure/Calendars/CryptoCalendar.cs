using Validator.Domain.Calendars;

namespace Validator.Infrastructure.Calendars;

public sealed class CryptoCalendar : Validator.Application.Abstractions.IMarketCalendar
{
    public MarketProfile Profile => MarketProfile.Crypto;

    public bool IsOpen(DateTimeOffset timestamp) => true;
}