using Microsoft.EntityFrameworkCore;
using Salon.Domain.Entities;
using SalonEntity = Salon.Domain.Entities.Salon;

namespace Salon.Infrastructure;

public sealed class SalonDbContext(DbContextOptions<SalonDbContext> options) : DbContext(options)
{
    public DbSet<SalonEntity> Salons => Set<SalonEntity>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Service> Services => Set<Service>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalonDbContext).Assembly);
}
