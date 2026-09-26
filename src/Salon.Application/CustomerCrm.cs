namespace Salon.Application;

public sealed record CustomerSearchItem(Guid Id, string Name, string Phone, string? Email);

public sealed record CustomerVisitSummary(
    Guid BookingId,
    string ServiceName,
    string StartsAtLocal,
    DateOnly Date);

public sealed record CustomerServiceSummary(Guid ServiceId, string ServiceName, int Count);

public sealed record CustomerHistoryItem(
    Guid BookingId,
    Guid ServiceId,
    string ServiceName,
    Guid EmployeeId,
    string EmployeeName,
    string StartsAtLocal,
    string EndsAtLocal,
    DateOnly Date,
    string Status);

public sealed record CustomerProfile(
    Guid Id,
    string Name,
    string Phone,
    string? Email,
    int VisitCount,
    decimal TotalSpend,
    CustomerVisitSummary? LastVisit,
    CustomerHistoryItem? Upcoming,
    IReadOnlyList<CustomerServiceSummary> ServiceSummary,
    IReadOnlyList<CustomerHistoryItem> History);

public interface ICustomerCrm
{
    Task<IReadOnlyList<CustomerSearchItem>> Search(Guid userId, string? query, CancellationToken cancellationToken);
    Task<CustomerProfile> Get(Guid userId, Guid customerId, CancellationToken cancellationToken);
}
