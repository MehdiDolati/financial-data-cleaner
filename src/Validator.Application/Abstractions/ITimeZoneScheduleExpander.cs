using System.Threading.Tasks;

namespace Validator.Application.Abstractions
{
    public interface ITimeZoneScheduleExpander
    {
        Task<object> ExpandAsync(object calendarDefinition); // placeholder for NodaTime-backed expansion
    }
}