using System.Collections.Generic;
using System.Threading.Tasks;

namespace DefikarteBackend.Interfaces
{
    public interface ICacheRepository<T>
    {
        Task<IList<T>> GetAsync();

        Task<T?> GetByIdAsync(string id);

        Task<bool> TryUpdateCacheAsync(IList<T> values);
    }
}