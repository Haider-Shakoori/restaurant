using BusinessOS.Restaurant.LocalServer;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Tests;

public sealed class ReportingTests
{
    [Fact]
    public async Task Summary_uses_local_financial_inventory_and_closing_data()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bos-report-{Guid.NewGuid():N}.db");
        var factory = new LocalDatabaseFactory(path);
        await factory.EnsureCreatedAsync();

        await using (var db = factory.Create())
        {
            db.Bills.Add(new LocalBill { Id="bill-1", OrderId="order-1", BranchId="branch-1", CreatedByUserId=1, BillNumber="B-1", Status="paid", Subtotal=1200m, DiscountAmount=200m, Total=1000m, PaidAmount=1000m, BalanceDue=0m, IssuedAt=new DateTimeOffset(2026,10,6,10,0,0,TimeSpan.Zero) });
            db.Payments.Add(new LocalTenantPayment { Id="pay-1", BillId="bill-1", CashierSessionId="shift-1", ReceivedByUserId=1, Method="cash", Amount=1000m, Status="completed", ReceivedAt=new DateTimeOffset(2026,10,6,10,5,0,TimeSpan.Zero) });
            db.StockMovements.Add(new LocalStockMovement { Id="move-1", BranchId="branch-1", InventoryItemId="item-1", ActorUserId=1, MovementType="consumption", QuantityDelta=-2m, UnitCost=100m, SourceType="order", SourceId="order-1", IdempotencyKey="consume-1", OccurredAt=new DateTimeOffset(2026,10,6,10,0,0,TimeSpan.Zero) });
            db.InventoryValuations.Add(new LocalInventoryValuation { Id="val-1", BranchId="branch-1", InventoryItemId="item-1", Quantity=5m, Value=500m, AverageUnitCost=100m });
            db.DailyClosings.Add(new LocalDailyClosing { Id="close-1", BranchId="branch-1", BusinessDate=new DateOnly(2026,10,6), Status="finalized", CreatedByUserId=1 });
            db.DailyClosingSnapshots.Add(new LocalDailyClosingSnapshot { Id="snap-1", DailyClosingId="close-1", Version=1, FinalizedByUserId=1, NetSales=1000m, PaymentsTotal=1000m, CashVariance=-20m, FinalizedAt=DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var report = await new LocalReportingService(factory).SummaryAsync("branch-1", new DateOnly(2026,10,6), new DateOnly(2026,10,6));

        Assert.Equal(1, report.BillCount);
        Assert.Equal(1200m, report.GrossSales);
        Assert.Equal(200m, report.Discounts);
        Assert.Equal(1000m, report.NetSales);
        Assert.Equal(1000m, report.Payments);
        Assert.Equal(200m, report.CostOfGoods);
        Assert.Equal(800m, report.GrossProfit);
        Assert.Equal(500m, report.InventoryValue);
        Assert.Equal(-20m, report.CashVariance);

        File.Delete(path);
    }

    [Fact]
    public async Task Breakdowns_are_local_and_period_scoped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bos-report-{Guid.NewGuid():N}.db");
        var factory = new LocalDatabaseFactory(path);
        await factory.EnsureCreatedAsync();
        await using (var db = factory.Create())
        {
            db.Bills.Add(new LocalBill { Id="bill-1", OrderId="order-1", BranchId="branch-1", CreatedByUserId=1, BillNumber="B-1", Status="paid", Subtotal=600m, DiscountAmount=0m, Total=600m, PaidAmount=600m, BalanceDue=0m, IssuedAt=new DateTimeOffset(2026,10,6,12,0,0,TimeSpan.Zero) });
            db.BillLines.Add(new LocalBillLine { Id="line-1", BillId="bill-1", OrderItemId="oi-1", ItemName="Kabuli Pulao", Quantity=2, UnitPrice=300m, LineTotal=600m });
            db.Payments.Add(new LocalTenantPayment { Id="pay-1", BillId="bill-1", CashierSessionId="s-1", ReceivedByUserId=1, Method="cash", Amount=600m, Status="completed", ReceivedAt=new DateTimeOffset(2026,10,6,12,1,0,TimeSpan.Zero) });
            await db.SaveChangesAsync();
        }
        var service = new LocalReportingService(factory);
        var payments = await service.PaymentsAsync("branch-1", new DateOnly(2026,10,6), new DateOnly(2026,10,6));
        var items = await service.TopItemsAsync("branch-1", new DateOnly(2026,10,6), new DateOnly(2026,10,6));

        Assert.Single(payments);
        Assert.Equal(600m, payments[0].Amount);
        Assert.Single(items);
        Assert.Equal("Kabuli Pulao", items[0].ItemName);
        Assert.Equal(2, items[0].Quantity);
        File.Delete(path);
    }
}
