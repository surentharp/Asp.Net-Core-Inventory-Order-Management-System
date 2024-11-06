using Microsoft.AspNetCore.Mvc.Rendering;

namespace Indotalent.Infrastructures.InventoryCheckService
{
    public interface IInventoryCheckService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }
}
