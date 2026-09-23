using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salon.Domain.Entities;
using SalonEntity = Salon.Domain.Entities.Salon;

namespace Salon.Infrastructure.Configurations;

internal static class Constraints
{
    // Unicode White_Space characters recognized by .NET's required-text guard.
    private const string WhiteSpace = "U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'";
    internal const string NonEmptyId = "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid";
    internal static string RequiredText(string column) => $"btrim(\"{column}\", {WhiteSpace}) <> ''";

    internal static void OwnedBySalon<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property<Guid>("SalonId").IsRequired();
        builder.HasIndex("SalonId");
        builder.HasOne<SalonEntity>().WithMany().HasForeignKey("SalonId").OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalonConfiguration : IEntityTypeConfiguration<SalonEntity>
{
    public void Configure(EntityTypeBuilder<SalonEntity> builder)
    {
        builder.ToTable("Salons", table =>
        {
            table.HasCheckConstraint("CK_Salons_Id", Constraints.NonEmptyId);
            table.HasCheckConstraint("CK_Salons_Name", Constraints.RequiredText("Name"));
            table.HasCheckConstraint("CK_Salons_TimeZoneId", Constraints.RequiredText("TimeZoneId"));
        });
        builder.HasKey(salon => salon.Id);
        builder.Property(salon => salon.Id).ValueGeneratedNever();
        builder.Property(salon => salon.Name).IsRequired();
        builder.Property(salon => salon.TimeZoneId).IsRequired();
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees", table =>
        {
            table.HasCheckConstraint("CK_Employees_Id", Constraints.NonEmptyId);
            table.HasCheckConstraint("CK_Employees_Name", Constraints.RequiredText("Name"));
        });
        builder.HasKey(employee => employee.Id);
        builder.Property(employee => employee.Id).ValueGeneratedNever();
        builder.Property(employee => employee.Name).IsRequired();
        Constraints.OwnedBySalon(builder);
    }
}

internal sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("Seats", table =>
        {
            table.HasCheckConstraint("CK_Seats_Id", Constraints.NonEmptyId);
            table.HasCheckConstraint("CK_Seats_Name", Constraints.RequiredText("Name"));
            table.HasCheckConstraint("CK_Seats_Type", Constraints.RequiredText("Type"));
        });
        builder.HasKey(seat => seat.Id);
        builder.Property(seat => seat.Id).ValueGeneratedNever();
        builder.Property(seat => seat.Name).IsRequired();
        builder.Property(seat => seat.Type).IsRequired();
        Constraints.OwnedBySalon(builder);
    }
}

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("Services", table =>
        {
            table.HasCheckConstraint("CK_Services_Id", Constraints.NonEmptyId);
            table.HasCheckConstraint("CK_Services_Name", Constraints.RequiredText("Name"));
            table.HasCheckConstraint("CK_Services_Category", Constraints.RequiredText("Category"));
            table.HasCheckConstraint("CK_Services_DurationMinutes", "\"DurationMinutes\" > 0");
            // Unconstrained numeric avoids rounding before the CHECK is evaluated.
            // The normalized coefficient bound also excludes NaN/infinity and CLR decimal overflow.
            table.HasCheckConstraint("CK_Services_Price",
                "\"Price\" >= 0 AND \"Price\" < 'Infinity'::numeric AND \"Price\" = trunc(\"Price\", 2) AND " +
                "\"Price\" * power(10::numeric, scale(trim_scale(\"Price\"))) <= 79228162514264337593543950335");
        });
        builder.HasKey(service => service.Id);
        builder.Property(service => service.Id).ValueGeneratedNever();
        builder.Property(service => service.Name).IsRequired();
        builder.Property(service => service.Category).IsRequired();
        builder.Property(service => service.Price).HasColumnType("numeric").IsRequired();
        builder.Property(service => service.DurationMinutes).IsRequired();
        Constraints.OwnedBySalon(builder);
    }
}
