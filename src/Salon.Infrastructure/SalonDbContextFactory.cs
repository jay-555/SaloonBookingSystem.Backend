using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Salon.Infrastructure;

public sealed class SalonDbContextFactory : IDesignTimeDbContextFactory<SalonDbContext>
{
    public SalonDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Salon");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("Set ConnectionStrings__Salon before running EF commands.");
        }
        var options = new DbContextOptionsBuilder<SalonDbContext>().UseNpgsql(connection).Options;
        return new SalonDbContext(options);
    }
}
