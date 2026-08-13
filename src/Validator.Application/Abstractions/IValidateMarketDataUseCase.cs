using System.Threading.Tasks;

namespace Validator.Application.Abstractions
{
    public interface IValidateMarketDataUseCase
    {
        Task<int> ExecuteAsync(object request); // returns exit code: 0,1,2
    }
}