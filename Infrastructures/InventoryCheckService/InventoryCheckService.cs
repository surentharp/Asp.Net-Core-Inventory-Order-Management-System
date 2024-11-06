using Indotalent.Applications.InventoryTransactions;
using Indotalent.Applications.Products;
using Indotalent.Applications.PurchaseOrderItems;
using Indotalent.Applications.PurchaseOrders;
using Indotalent.Data;
using Indotalent.Models.Entities;
using Indotalent.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Threading;

namespace Indotalent.Infrastructures.InventoryCheckService
{

    public class InventoryCheckService : IInventoryCheckService, IHostedService, IDisposable
    {
        private readonly IServiceProvider _services;
        private Timer _timer;

        public InventoryCheckService(IServiceProvider services)
        {
            _services = services;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(CheckInventoryAndCreatePurchaseOrders, null, TimeSpan.Zero, TimeSpan.FromHours(24));
            return Task.CompletedTask;
        }

        private async void CheckInventoryAndCreatePurchaseOrders(object state)
        {
            using (var scope = _services.CreateScope())
            {
                var inventoryTransactionService = scope.ServiceProvider.GetRequiredService<InventoryTransactionService>();
                var lowStockItems = inventoryTransactionService.GetAll()
                    .Include(x => x.Product)
                    .Where(x => x.Product.Physical && x.Stock <= x.Product.Threshold)
                    .GroupBy(x => new { x.ProductId, x.Product.Threshold })
                    .Select(group => new { group.Key.ProductId, group.Key.Threshold, Stock = group.Sum(x => x.Stock) })
                    .Where(x => x.Stock <= x.Threshold)
                    .ToList();

                if (lowStockItems.Any())
                {
                    var purchaseOrderService = scope.ServiceProvider.GetRequiredService<PurchaseOrderService>();
                    var productService = scope.ServiceProvider.GetRequiredService<ProductService>();

                    foreach (var item in lowStockItems)
                    {
                        var product = await productService.GetByIdAsync(item.ProductId);

                        // Retrieve the last purchase order and order item for the product
                        var (lastOrder, lastOrderItem) = purchaseOrderService.GetLastOrderDetailsForProduct(product.Id);

                        // Create a new purchase order using the last order's vendor and tax details
                        var purchaseOrder = new PurchaseOrder
                        {
                            Number = "PO-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                            OrderDate = DateTime.Now,
                            VendorId = lastOrder?.VendorId ?? 1, // Use last order's vendor or default
                            OrderStatus = PurchaseOrderStatus.Draft,
                            TaxId = lastOrder?.TaxId ?? 1, // Use last order's tax or default tax
                        };
                        await purchaseOrderService.AddAsync(purchaseOrder);

                        var purchaseOrderItem = new PurchaseOrderItem
                        {
                            PurchaseOrderId = purchaseOrder.Id,
                            ProductId = product.Id,
                            Summary = product.Name,
                            UnitPrice = lastOrderItem?.UnitPrice ?? product.UnitPrice, // Use last price or product's default
                            Quantity = lastOrderItem?.Quantity ?? 20, // Use last quantity or default
                        };

                        purchaseOrderItem.RecalculateTotal();
                        await scope.ServiceProvider.GetRequiredService<PurchaseOrderItemService>().AddAsync(purchaseOrderItem);
                    }
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }

}
