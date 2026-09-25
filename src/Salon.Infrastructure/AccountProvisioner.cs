using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Salon.Application;

namespace Salon.Infrastructure;

public sealed class AccountProvisioner(SalonDbContext database, UserManager<SalonUser> users)
{
    public async Task<IdentityResult> Provision(string login, string role, Guid? salonId, string password)
    {
        if (!SalonRoles.IsValid(role) || (role != SalonRoles.OwnerAdmin && salonId is null))
            return Failure("Supply a valid role and salon membership; only OwnerAdmin may be unassigned.");
        if (salonId is Guid id && !await database.Salons.AnyAsync(x => x.Id == id))
            return Failure("Salon does not exist.");
        if (string.IsNullOrWhiteSpace(login)) return Failure("Login is required.");
        login = login.Trim();
        if (await users.FindByNameAsync(login) is not null) return Failure("Account already exists; no changes made.");
        return await users.CreateAsync(new SalonUser
        {
            Id = Guid.NewGuid(),
            UserName = login,
            Role = role,
            SalonId = salonId,
            LockoutEnabled = true
        }, password);
    }

    private static IdentityResult Failure(string message) => IdentityResult.Failed(new IdentityError { Description = message });
}
