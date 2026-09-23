using Microsoft.EntityFrameworkCore;
using Salon.Application;

namespace Salon.Infrastructure;

public sealed class DatabaseReadiness(SalonDbContext database) : IDatabaseReadiness
{
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken) =>
        database.Database.CanConnectAsync(cancellationToken);
}
