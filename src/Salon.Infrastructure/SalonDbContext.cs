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
    public DbSet<EmployeeSkill> EmployeeSkills => Set<EmployeeSkill>();
    public DbSet<EmployeeWorkingDay> EmployeeWorkingDays => Set<EmployeeWorkingDay>();
    public DbSet<EmployeeBreak> EmployeeBreaks => Set<EmployeeBreak>();
    public DbSet<SeatService> SeatServices => Set<SeatService>();
    public DbSet<ServiceResourceRequirement> ServiceResourceRequirements => Set<ServiceResourceRequirement>();

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
        modelBuilder.Entity<EmployeeSkill>(skill =>
        {
            skill.HasKey(x => new { x.EmployeeId, x.ServiceId });
            skill.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            skill.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            skill.ToTable("EmployeeSkills");
        });
        modelBuilder.Entity<EmployeeWorkingDay>(day =>
        {
            day.HasKey(x => new { x.EmployeeId, x.Day });
            day.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            day.ToTable("EmployeeWorkingDays", table => table.HasCheckConstraint("CK_EmployeeWorkingDays_Interval",
                "\"Day\" BETWEEN 0 AND 6 AND ((\"OpensAt\" IS NULL AND \"ClosesAt\" IS NULL) OR (\"OpensAt\" IS NOT NULL AND \"ClosesAt\" IS NOT NULL AND \"OpensAt\" >= 0 AND \"ClosesAt\" <= 1439 AND \"OpensAt\" < \"ClosesAt\"))"));
        });
        modelBuilder.Entity<EmployeeBreak>(item =>
        {
            item.HasKey(x => x.Id);
            item.Property(x => x.Id).ValueGeneratedNever();
            item.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            item.HasIndex(x => x.EmployeeId);
            item.ToTable("EmployeeBreaks", table => table.HasCheckConstraint("CK_EmployeeBreaks_Interval",
                "\"Day\" BETWEEN 0 AND 6 AND \"StartsAt\" >= 0 AND \"EndsAt\" <= 1439 AND \"StartsAt\" < \"EndsAt\""));
        });
        modelBuilder.Entity<SeatService>(link =>
        {
            link.HasKey(x => new { x.SeatId, x.ServiceId });
            link.HasOne<Seat>().WithMany().HasForeignKey(x => x.SeatId).OnDelete(DeleteBehavior.Cascade);
            link.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            link.ToTable("SeatServices");
        });
        modelBuilder.Entity<ServiceResourceRequirement>(requirement =>
        {
            requirement.HasKey(x => x.ServiceId);
            requirement.Property(x => x.SeatType).IsRequired();
            requirement.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            requirement.ToTable("ServiceResourceRequirements", table => table.HasCheckConstraint(
                "CK_ServiceResourceRequirements_Rules",
                "\"EmployeeCapacity\" = 1 AND btrim(\"SeatType\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000') <> '' AND \"BufferMinutes\" >= 0"));
        });
    }
}
