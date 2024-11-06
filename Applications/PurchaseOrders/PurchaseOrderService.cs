using Indotalent.Data;
using Indotalent.Infrastructures.Repositories;
using Indotalent.Models.Contracts;
using Indotalent.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Indotalent.Applications.PurchaseOrders
{
    public class PurchaseOrderService : Repository<PurchaseOrder>
    {
        public PurchaseOrderService(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor,
            IAuditColumnTransformer auditColumnTransformer) :
                base(
                    context,
                    httpContextAccessor,
                    auditColumnTransformer)
        {
        }


        public async Task RecalculateParentAsync(int? masterId)
        {

            var master = await _context.Set<PurchaseOrder>()
                .Include(x => x.Tax)
                .Where(x => x.Id == masterId && x.IsNotDeleted == true)
                .FirstOrDefaultAsync();

            var childs = await _context.Set<PurchaseOrderItem>()
                .Where(x => x.PurchaseOrderId == masterId && x.IsNotDeleted == true)
                .ToListAsync();

            if (master != null)
            {
                master.BeforeTaxAmount = 0;
                foreach (var item in childs)
                {
                    master.BeforeTaxAmount += item.Total;
                }
                if (master.Tax != null)
                {
                    master.TaxAmount = (master.Tax.Percentage / 100.0) * master.BeforeTaxAmount;
                }
                master.AfterTaxAmount = master.BeforeTaxAmount + master.TaxAmount;
                _context.Set<PurchaseOrder>().Update(master);
                await _context.SaveChangesAsync();
            }
        }



        public override async Task UpdateAsync(PurchaseOrder? entity)
        {
            if (entity != null)
            {
                if (entity is IHasAudit auditEntity && !string.IsNullOrEmpty(_userId))
                {
                    auditEntity.UpdatedByUserId = _userId;
                }
                if (entity is IHasAudit auditedEntity)
                {
                    auditedEntity.UpdatedAtUtc = DateTime.Now;
                }

                _context.Set<PurchaseOrder>().Update(entity);
                await _context.SaveChangesAsync();

                await RecalculateParentAsync(entity.Id);
            }
            else
            {
                throw new Exception("Unable to process, entity is null");
            }
        }

        public PurchaseOrderItem? GetLastPurchaseOrderItemForProduct(int productId)
        {
            return _context.PurchaseOrderItem
                .Where(p => p.ProductId == productId)
                .OrderByDescending(p => p.Id) // Assumes higher ID means newer
                .FirstOrDefault();
        }

        // New method to get the last purchase order item and its order details for a product
        public (PurchaseOrder? LastOrder, PurchaseOrderItem? LastOrderItem) GetLastOrderDetailsForProduct(int productId)
        {
            var lastOrderItem = _context.PurchaseOrderItem
                .Include(poi => poi.PurchaseOrder) // Include related PurchaseOrder to get VendorId and TaxId
                .Where(poi => poi.ProductId == productId)
                .OrderByDescending(poi => poi.Id) // Assumes higher ID indicates more recent
                .FirstOrDefault();

            return (lastOrderItem?.PurchaseOrder, lastOrderItem);
        }

    }
}
