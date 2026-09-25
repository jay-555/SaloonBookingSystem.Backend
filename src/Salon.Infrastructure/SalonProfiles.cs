using Microsoft.EntityFrameworkCore;
using Salon.Application;

namespace Salon.Infrastructure;

public sealed class SalonProfiles(SalonDbContext database) : ISalonProfiles
{
    public Task<AccountProfile?> Account(Guid userId, CancellationToken cancellationToken) =>
        database.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new AccountProfile(x.Id, x.UserName!, x.Role, x.SalonId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<SalonProfile?> Read(Guid userId, CancellationToken cancellationToken)
    {
        var account = await Account(userId, cancellationToken);
        if (account?.SalonId is not Guid salonId) return null;
        var salon = await database.Salons.AsNoTracking().SingleAsync(x => x.Id == salonId, cancellationToken);
        var days = await database.WorkingDays.AsNoTracking().Where(x => x.SalonId == salonId).OrderBy(x => x.Day).ToArrayAsync(cancellationToken);
        return new(salon.Id, salon.Name, salon.TimeZoneId,
            days.Select(x => new DayInput(x.Day, Format(x.OpensAt), Format(x.ClosesAt))).ToArray());
    }

    public async Task<SalonProfile> Save(Guid userId, SalonInput input, bool create, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Serialize setup for the account. No tenant identity comes from the request.
        var user = await database.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        // Identity may already have tracked this user while validating the cookie.
        // Reload under the row lock so concurrent setup cannot use stale membership.
        if (user is not null) await database.Entry(user).ReloadAsync(cancellationToken);
        if (user is null || user.Role != SalonRoles.OwnerAdmin) throw new UnauthorizedAccessException();
        if (create != (user.SalonId is null)) throw new SetupConflictException();
        var salonId = user.SalonId ?? Guid.NewGuid();
        var days = input.Validate(salonId);
        if (create)
        {
            database.Salons.Add(new Domain.Entities.Salon(salonId, input.Name, input.TimeZoneId));
            user.SalonId = salonId;
            user.ConcurrencyStamp = Guid.NewGuid().ToString();
        }
        else
        {
            // Also serialize updates from multiple explicitly provisioned owners.
            var salon = await database.Salons.FromSqlInterpolated($"SELECT * FROM \"Salons\" WHERE \"Id\" = {salonId} FOR UPDATE").SingleAsync(cancellationToken);
            salon.UpdateProfile(input.Name, input.TimeZoneId);
            await database.WorkingDays.Where(x => x.SalonId == salonId).ExecuteDeleteAsync(cancellationToken);
        }
        database.WorkingDays.AddRange(days);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await Read(userId, cancellationToken))!;
    }

    private static string? Format(int? minutes) => minutes is int value ? $"{value / 60:00}:{value % 60:00}" : null;
}
