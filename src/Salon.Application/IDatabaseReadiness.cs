namespace Salon.Application;

public interface IDatabaseReadiness
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
