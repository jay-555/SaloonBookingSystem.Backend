using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Salon.Domain.Entities;
using SalonEntity = Salon.Domain.Entities.Salon;

namespace Salon.Infrastructure;

public sealed class SalonDbContext(DbContextOptions<SalonDbContext> options) : IdentityUserContext<SalonUser, Guid>(options)
{
    public DbSet<SalonEntity> Salons => Set<SalonEntity>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<WorkingDay> WorkingDays => Set<WorkingDay>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalonDbContext).Assembly);
        modelBuilder.Entity<SalonUser>(user =>
        {
            user.Property(x => x.Role).IsRequired();
            user.HasOne<SalonEntity>().WithMany().HasForeignKey(x => x.SalonId).OnDelete(DeleteBehavior.Restrict);
            user.ToTable("AspNetUsers", table => table.HasCheckConstraint("CK_Users_Membership",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Role\" IN ('OwnerAdmin','Manager','Employee') AND (\"Role\" = 'OwnerAdmin' OR \"SalonId\" IS NOT NULL)"));
        });
        modelBuilder.Entity<WorkingDay>(day =>
        {
            day.HasKey(x => new { x.SalonId, x.Day });
            day.HasOne<SalonEntity>().WithMany().HasForeignKey(x => x.SalonId).OnDelete(DeleteBehavior.Restrict);
            day.ToTable("WorkingDays", table => table.HasCheckConstraint("CK_WorkingDays_Interval",
                "\"Day\" BETWEEN 0 AND 6 AND ((\"OpensAt\" IS NULL AND \"ClosesAt\" IS NULL) OR (\"OpensAt\" IS NOT NULL AND \"ClosesAt\" IS NOT NULL AND \"OpensAt\" >= 0 AND \"ClosesAt\" <= 1439 AND \"OpensAt\" < \"ClosesAt\"))"));
        });
    }
}
