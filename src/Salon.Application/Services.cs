using Salon.Domain.Entities;

namespace Salon.Application;

public sealed record ServiceInput(string Name, string Category, decimal Price, int DurationMinutes)
{
    public void Validate(Guid serviceId, Guid salonId) =>
        _ = new Service(serviceId, salonId, Name, Category, Price, DurationMinutes);
}

public sealed record ServiceSummary(Guid Id, string Name, string Category, decimal Price, int DurationMinutes);
public sealed record ServiceDetail(Guid Id, string Name, string Category, decimal Price, int DurationMinutes);

public interface IServiceCatalog
{
    Task<IReadOnlyList<ServiceSummary>> List(Guid userId, CancellationToken cancellationToken);
    Task<ServiceDetail?> Get(Guid userId, Guid serviceId, CancellationToken cancellationToken);
    Task<ServiceDetail> Create(Guid userId, ServiceInput input, CancellationToken cancellationToken);
    Task<ServiceDetail> Update(Guid userId, Guid serviceId, ServiceInput input, CancellationToken cancellationToken);
    Task Delete(Guid userId, Guid serviceId, CancellationToken cancellationToken);
}
