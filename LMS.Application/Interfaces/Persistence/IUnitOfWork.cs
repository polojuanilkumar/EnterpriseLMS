using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.Persistence
{
    public interface IUnitOfWork
    {
        Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default);
    }
}
