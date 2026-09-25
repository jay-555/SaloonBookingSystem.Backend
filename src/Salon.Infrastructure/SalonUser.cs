using Microsoft.AspNetCore.Identity;

namespace Salon.Infrastructure;

public sealed class SalonUser : IdentityUser<Guid>
{
    public string Role { get; set; } = "OwnerAdmin";
    public Guid? SalonId { get; set; }
}
