using Validator.Domain.Calendars;

namespace Validator.Application.Abstractions
{
    public interface IMarketCalendarFactory
    {
        IMarketCalendar Create(LocalCalendarRequest request);
    }

    public sealed record LocalCalendarRequest(string PathOrProfile);
}