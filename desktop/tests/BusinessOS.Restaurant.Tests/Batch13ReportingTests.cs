using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class Batch13ReportingTests
{
    [Fact]
    public async Task Accounting_and_sales_trend_are_derived_from_local_authoritative_records()
    {
        var root=Path.Combine(Path.GetTempPath(), $"bos-b13-{Guid.NewGuid():N}");
        var factory=new LocalDatabaseFactory(root); await factory.EnsureCreatedAsync();
        await using(var db=factory.Create())
        {
            db.Bills.Add(new LocalBill { Id="b1", OrderId="o1", BranchId="branch-1", CreatedByUserId=1, BillNumber="B1", Status="paid", Subtotal=1100m, DiscountAmount=100m, Total=1000m, PaidAmount=800m, BalanceDue=200m, IssuedAt=new DateTimeOffset(2026,10,6,8,0,0,TimeSpan.Zero) });
            db.Payments.Add(new LocalTenantPayment { Id="p1", BillId="b1", CashierSessionId="s1", ReceivedByUserId=1, Method="cash", Amount=800m, Status="completed", ReceivedAt=new DateTimeOffset(2026,10,6,8,5,0,TimeSpan.Zero) });
            db.StockMovements.Add(new LocalStockMovement { Id="m1", BranchId="branch-1", InventoryItemId="i1", ActorUserId=1, MovementType="consumption", QuantityDelta=-2m, UnitCost=125m, SourceType="order", SourceId="o1", IdempotencyKey="c1", OccurredAt=new DateTimeOffset(2026,10,6,8,0,0,TimeSpan.Zero) });
            await db.SaveChangesAsync();
        }
        var service=new LocalReportingService(factory);
        var summary=await service.SummaryAsync("branch-1",new DateOnly(2026,10,6),new DateOnly(2026,10,6));
        var trend=await service.SalesTrendAsync("branch-1",new DateOnly(2026,10,6),new DateOnly(2026,10,6));
        var accounting=await service.AccountingSummaryAsync("branch-1",new DateOnly(2026,10,6),new DateOnly(2026,10,6));
        Assert.Equal(200m,summary.Outstanding); Assert.Equal(250m,summary.CostOfGoods); Assert.Equal(750m,summary.GrossProfit); Assert.Equal(75m,summary.GrossMarginPercent);
        Assert.Single(trend); Assert.Equal(750m,trend[0].GrossProfit);
        Assert.Contains(accounting,x=>x.Code=="4000" && x.Credit==1100m);
        Assert.Contains(accounting,x=>x.Code=="4050" && x.Debit==100m);
        Assert.Contains(accounting,x=>x.Code=="1100" && x.Debit==200m);
        Directory.Delete(root,true);
    }

    [Fact]
    public async Task Inventory_report_flags_reorder_items_and_uses_weighted_value()
    {
        var root=Path.Combine(Path.GetTempPath(), $"bos-b13-{Guid.NewGuid():N}");
        var factory=new LocalDatabaseFactory(root); await factory.EnsureCreatedAsync();
        await using(var db=factory.Create())
        {
            db.InventoryItems.Add(new LocalInventoryItem { Id="i1", Sku="RICE", Name="Rice", BaseUnit="kg", PurchaseToBaseFactor=1m, ReorderLevel=10m, IsActive=true });
            db.InventoryValuations.Add(new LocalInventoryValuation { Id="v1", BranchId="branch-1", InventoryItemId="i1", Quantity=5m, AverageUnitCost=90m, Value=450m });
            await db.SaveChangesAsync();
        }
        var rows=await new LocalReportingService(factory).InventoryAsync("branch-1");
        Assert.Single(rows); Assert.True(rows[0].IsLowStock); Assert.Equal(450m,rows[0].Value); Assert.Equal(90m,rows[0].AverageUnitCost);
        Directory.Delete(root,true);
    }

    [Fact]
    public void Report_period_rejects_reverse_and_excessive_ranges()
    {
        Assert.Throws<ArgumentException>(()=>LocalReportingService.ValidatePeriod(new DateOnly(2026,10,7),new DateOnly(2026,10,6)));
        Assert.Throws<ArgumentException>(()=>LocalReportingService.ValidatePeriod(new DateOnly(2025,1,1),new DateOnly(2026,10,6)));
    }
}
