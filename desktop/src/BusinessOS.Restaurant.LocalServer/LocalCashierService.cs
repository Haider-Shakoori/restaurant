using System.Text;
using System.Text.Json;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalBillSplitPart(string Label, decimal Amount);

public sealed record LocalPaymentRequest(
    string CashierSessionId,
    decimal Amount,
    string Method,
    string? ClientPaymentId = null,
    string? Reference = null,
    string? BillSplitId = null);

public sealed class LocalCashierService
{
    private static readonly string[] PaymentMethods = ["cash", "card", "bank", "mobile_money", "other"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalDatabaseFactory _databaseFactory;

    public LocalCashierService(LocalDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public async Task<object> OpenSessionAsync(
        string branchId,
        decimal openingCash,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureCashierRole(actor);

        if (openingCash < 0)
        {
            throw new LocalSyncConflictException("invalid_payload", "Opening cash cannot be negative.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var branch = await db.Branches.SingleOrDefaultAsync(
            value => value.Id == branchId && value.IsActive,
            cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "The selected branch is inactive or unavailable.");

        var existing = await db.CashierSessions.SingleOrDefaultAsync(
            value => value.CashierUserId == actor.UserId && value.Status == "open",
            cancellationToken);

        if (existing is not null)
        {
            throw new LocalSyncConflictException("cashier_session_conflict", "This cashier already has an open session.");
        }

        var session = new LocalCashierSession
        {
            Id = Guid.CreateVersion7().ToString("N"),
            BranchId = branch.Id,
            CashierUserId = actor.UserId,
            CashierName = actor.UserName,
            Status = "open",
            OpeningCash = Money(openingCash),
            OpenedAt = DateTimeOffset.UtcNow,
        };

        db.CashierSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return SessionSnapshot(session);
    }

    public async Task<object> CloseSessionAsync(
        string sessionId,
        decimal declaredCash,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureCashierRole(actor);

        if (declaredCash < 0)
        {
            throw new LocalSyncConflictException("invalid_payload", "Declared cash cannot be negative.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var session = await db.CashierSessions.SingleOrDefaultAsync(
            value => value.Id == sessionId,
            cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Cashier session does not exist.");

        AuthorizeSession(session, actor);

        if (session.Status == "closed")
        {
            return SessionSnapshot(session);
        }

        var cashCollected = await db.Payments
            .Where(value =>
                value.CashierSessionId == session.Id &&
                value.Status == "posted" &&
                value.Method == "cash")
            .SumAsync(value => value.Amount, cancellationToken);

        var expected = Money(session.OpeningCash + cashCollected);
        var declared = Money(declaredCash);

        session.Status = "closed";
        session.ExpectedCash = expected;
        session.DeclaredCash = declared;
        session.CashVariance = Money(declared - expected);
        session.ClosedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SessionSnapshot(session);
    }

    public async Task<object> ServeOrderAsync(
        string orderId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureOrderOperationRole(actor);

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var order = await db.Orders.SingleOrDefaultAsync(value => value.Id == orderId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Order does not exist.");

        if (order.Status == "served" || order.Status == "billed" || order.Status == "closed")
        {
            return await OrderSnapshotAsync(db, order, cancellationToken);
        }

        if (order.Status != "ready")
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "The order can only be served after every kitchen ticket is ready.");
        }

        var now = DateTimeOffset.UtcNow;
        order.Status = "served";
        order.ServedAt = now;
        order.UpdatedAtUtc = now;

        await db.OrderItems
            .Where(value => value.OrderId == order.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.Status, "served")
                    .SetProperty(value => value.UpdatedAtUtc, now),
                cancellationToken);

        var tickets = await db.KitchenTickets
            .Where(value => value.OrderId == order.Id)
            .ToArrayAsync(cancellationToken);

        foreach (var ticket in tickets.Where(value => value.Status == "ready"))
        {
            ticket.Status = "completed";
            ticket.CompletedAt = now;
            ticket.UpdatedAtUtc = now;

            await db.KitchenTicketItems
                .Where(value => value.KitchenTicketId == ticket.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(value => value.Status, "completed"),
                    cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        var orderSnapshot = await OrderSnapshotAsync(db, order, cancellationToken);
        AddChange(db, "order", order.Id, order.WaiterId, orderSnapshot);

        foreach (var ticket in tickets)
        {
            AddChange(
                db,
                "kitchen_ticket",
                ticket.Id,
                null,
                await LocalKitchenService.TicketSnapshotAsync(db, ticket, cancellationToken));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return orderSnapshot;
    }

    public async Task<object> CreateBillAsync(
        string orderId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureCashierRole(actor);

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var order = await db.Orders.SingleOrDefaultAsync(value => value.Id == orderId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Order does not exist.");

        var existing = await db.Bills.SingleOrDefaultAsync(value => value.OrderId == order.Id, cancellationToken);

        if (existing is not null)
        {
            return await BillSnapshotAsync(db, existing, cancellationToken);
        }

        if (order.Status != "served")
        {
            throw new LocalSyncConflictException("order_state_conflict", "A bill can only be issued after the order has been served.");
        }

        var table = await db.DiningTables.SingleAsync(value => value.Id == order.DiningTableId, cancellationToken);
        var area = await db.DiningAreas.SingleAsync(value => value.Id == table.DiningAreaId, cancellationToken);
        var lines = await db.OrderItems.Where(value => value.OrderId == order.Id).ToArrayAsync(cancellationToken);
        var subtotal = Money(lines.Sum(value => value.LineTotal));
        var now = DateTimeOffset.UtcNow;

        var bill = new LocalBill
        {
            Id = Guid.CreateVersion7().ToString("N"),
            OrderId = order.Id,
            BranchId = area.BranchId,
            CreatedByUserId = actor.UserId,
            BillNumber = $"BILL-{Guid.CreateVersion7():N}".ToUpperInvariant(),
            Status = "open",
            Subtotal = subtotal,
            DiscountAmount = 0m,
            Total = subtotal,
            PaidAmount = 0m,
            BalanceDue = subtotal,
            IssuedAt = now,
        };

        db.Bills.Add(bill);

        foreach (var line in lines)
        {
            db.BillLines.Add(new LocalBillLine
            {
                Id = Guid.CreateVersion7().ToString("N"),
                BillId = bill.Id,
                OrderItemId = line.Id,
                ItemName = line.ItemName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal,
            });
        }

        order.Status = "billed";
        order.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);

        var billSnapshot = await BillSnapshotAsync(db, bill, cancellationToken);
        AddChange(db, "bill", bill.Id, null, billSnapshot);
        AddChange(db, "order", order.Id, order.WaiterId, await OrderSnapshotAsync(db, order, cancellationToken));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return billSnapshot;
    }

    public async Task<object> ApplyDiscountAsync(
        string billId,
        string type,
        decimal value,
        string? reason,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureManagerOrCashier(actor);

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var bill = await db.Bills.SingleOrDefaultAsync(value => value.Id == billId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Bill does not exist.");

        if (bill.Status != "open")
        {
            throw new LocalSyncConflictException("bill_state_conflict", "Discounts can only be changed on an open bill.");
        }

        if (await db.Payments.AnyAsync(value => value.BillId == bill.Id && value.Status == "posted", cancellationToken))
        {
            throw new LocalSyncConflictException("bill_state_conflict", "Discount cannot be changed after a payment has been posted.");
        }

        decimal discount;

        if (type == "fixed")
        {
            if (value < 0 || value > bill.Subtotal)
            {
                throw new LocalSyncConflictException("invalid_payload", "Fixed discount must be between zero and the bill subtotal.");
            }

            discount = Money(value);
        }
        else if (type == "percent")
        {
            if (value < 0 || value > 100)
            {
                throw new LocalSyncConflictException("invalid_payload", "Percentage discount must be between 0 and 100.");
            }

            discount = Money(bill.Subtotal * value / 100m);
        }
        else
        {
            throw new LocalSyncConflictException("invalid_payload", "Discount type must be fixed or percent.");
        }

        bill.DiscountType = type;
        bill.DiscountValue = value;
        bill.DiscountAmount = discount;
        bill.DiscountReason = reason;
        bill.Total = Money(bill.Subtotal - discount);
        bill.BalanceDue = bill.Total;

        await db.SaveChangesAsync(cancellationToken);

        if (bill.Total == 0m)
        {
            await SettleBillAsync(db, bill, actor, cancellationToken);
        }

        var snapshot = await BillSnapshotAsync(db, bill, cancellationToken);
        AddChange(db, "bill", bill.Id, null, snapshot);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    public async Task<object> CreateSplitsAsync(
        string billId,
        IReadOnlyList<LocalBillSplitPart> parts,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureCashierRole(actor);

        if (parts.Count < 2 || parts.Count > 20)
        {
            throw new LocalSyncConflictException("invalid_payload", "A split bill requires between 2 and 20 parts.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var bill = await db.Bills.SingleOrDefaultAsync(value => value.Id == billId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Bill does not exist.");

        if (bill.Status != "open" || bill.PaidAmount != 0m)
        {
            throw new LocalSyncConflictException("bill_state_conflict", "The bill can only be split before payments are posted.");
        }

        if (parts.Any(value => value.Amount <= 0))
        {
            throw new LocalSyncConflictException("invalid_payload", "Every split amount must be positive.");
        }

        if (Money(parts.Sum(value => value.Amount)) != Money(bill.Total))
        {
            throw new LocalSyncConflictException("invalid_payload", "Split amounts must add up exactly to the bill total.");
        }

        db.BillSplits.RemoveRange(db.BillSplits.Where(value => value.BillId == bill.Id));

        var splitNumber = 1;
        foreach (var part in parts)
        {
            var amount = Money(part.Amount);
            db.BillSplits.Add(new LocalBillSplit
            {
                Id = Guid.CreateVersion7().ToString("N"),
                BillId = bill.Id,
                SplitNumber = splitNumber++,
                Label = string.IsNullOrWhiteSpace(part.Label) ? $"Split {splitNumber - 1}" : part.Label.Trim(),
                Amount = amount,
                PaidAmount = 0m,
                BalanceDue = amount,
                Status = "open",
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return await BillSnapshotAsync(db, bill, cancellationToken);
    }

    public async Task<object> AddPaymentAsync(
        string billId,
        LocalPaymentRequest request,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureCashierRole(actor);

        if (!PaymentMethods.Contains(request.Method, StringComparer.Ordinal))
        {
            throw new LocalSyncConflictException("invalid_payload", "Unsupported payment method.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.ClientPaymentId))
        {
            var existing = await db.Payments.SingleOrDefaultAsync(
                value => value.ClientPaymentId == request.ClientPaymentId,
                cancellationToken);

            if (existing is not null)
            {
                if (existing.BillId != billId)
                {
                    throw new LocalSyncConflictException("payment_conflict", "This client payment ID belongs to another bill.");
                }

                return PaymentSnapshot(existing);
            }
        }

        var bill = await db.Bills.SingleOrDefaultAsync(value => value.Id == billId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Bill does not exist.");
        var session = await db.CashierSessions.SingleOrDefaultAsync(
            value => value.Id == request.CashierSessionId,
            cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Cashier session does not exist.");

        AuthorizeSession(session, actor);

        if (bill.Status != "open")
        {
            throw new LocalSyncConflictException("bill_state_conflict", "This bill is not open for payment.");
        }

        if (session.Status != "open")
        {
            throw new LocalSyncConflictException("cashier_session_conflict", "The cashier session is closed.");
        }

        if (session.BranchId != bill.BranchId)
        {
            throw new LocalSyncConflictException("cashier_session_conflict", "The cashier session belongs to another branch.");
        }

        var amount = Money(request.Amount);
        if (amount <= 0 || amount > bill.BalanceDue)
        {
            throw new LocalSyncConflictException("invalid_payload", "Payment must be positive and cannot exceed the remaining balance.");
        }

        LocalBillSplit? split = null;
        if (!string.IsNullOrWhiteSpace(request.BillSplitId))
        {
            split = await db.BillSplits.SingleOrDefaultAsync(
                value => value.Id == request.BillSplitId && value.BillId == bill.Id,
                cancellationToken)
                ?? throw new LocalSyncConflictException("dependency_missing", "Bill split does not exist.");

            if (amount > split.BalanceDue)
            {
                throw new LocalSyncConflictException("invalid_payload", "Payment cannot exceed the selected split balance.");
            }
        }

        var payment = new LocalTenantPayment
        {
            Id = Guid.CreateVersion7().ToString("N"),
            BillId = bill.Id,
            BillSplitId = split?.Id,
            CashierSessionId = session.Id,
            ReceivedByUserId = actor.UserId,
            ClientPaymentId = string.IsNullOrWhiteSpace(request.ClientPaymentId) ? null : request.ClientPaymentId.Trim(),
            Method = request.Method,
            Amount = amount,
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            Status = "posted",
            ReceivedAt = DateTimeOffset.UtcNow,
        };

        db.Payments.Add(payment);
        bill.PaidAmount = Money(bill.PaidAmount + amount);
        bill.BalanceDue = Money(bill.Total - bill.PaidAmount);

        if (split is not null)
        {
            split.PaidAmount = Money(split.PaidAmount + amount);
            split.BalanceDue = Money(split.Amount - split.PaidAmount);
            split.Status = split.BalanceDue == 0m ? "paid" : "open";
        }

        await db.SaveChangesAsync(cancellationToken);

        if (bill.BalanceDue == 0m)
        {
            await SettleBillAsync(db, bill, actor, cancellationToken);
        }

        AddChange(db, "bill", bill.Id, null, await BillSnapshotAsync(db, bill, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PaymentSnapshot(payment);
    }

    public async Task<object> TransferOrderAsync(
        string orderId,
        string targetTableId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureManagerOrCashier(actor);

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var order = await db.Orders.SingleOrDefaultAsync(value => value.Id == orderId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Order does not exist.");

        if (order.Status is "billed" or "closed")
        {
            throw new LocalSyncConflictException("order_state_conflict", "Billed or closed orders cannot be transferred.");
        }

        var sourceTable = await db.DiningTables.SingleAsync(value => value.Id == order.DiningTableId, cancellationToken);
        var targetTable = await db.DiningTables.SingleOrDefaultAsync(value => value.Id == targetTableId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Target table does not exist.");

        if (!targetTable.IsActive || targetTable.Status != "available")
        {
            throw new LocalSyncConflictException("table_busy", "The target table is not available.");
        }

        order.DiningTableId = targetTable.Id;
        order.UpdatedAtUtc = DateTimeOffset.UtcNow;
        sourceTable.Status = "available";
        targetTable.Status = "occupied";

        await db.SaveChangesAsync(cancellationToken);

        AddChange(db, "order", order.Id, order.WaiterId, await OrderSnapshotAsync(db, order, cancellationToken));
        AddChange(db, "dining_table", sourceTable.Id, null, TableSnapshot(db, sourceTable));
        AddChange(db, "dining_table", targetTable.Id, null, TableSnapshot(db, targetTable));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await OrderSnapshotAsync(db, order, cancellationToken);
    }

    public async Task<object> MergeDraftOrdersAsync(
        string targetOrderId,
        string sourceOrderId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureManagerOrCashier(actor);

        if (targetOrderId == sourceOrderId)
        {
            throw new LocalSyncConflictException("invalid_payload", "An order cannot be merged into itself.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var target = await db.Orders.SingleOrDefaultAsync(value => value.Id == targetOrderId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Target order does not exist.");
        var source = await db.Orders.SingleOrDefaultAsync(value => value.Id == sourceOrderId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Source order does not exist.");

        if (target.Status != "draft" || source.Status != "draft")
        {
            throw new LocalSyncConflictException(
                "order_state_conflict",
                "Orders can only be merged while both are still draft, before kitchen routing.");
        }

        var sourceTable = await db.DiningTables.SingleAsync(value => value.Id == source.DiningTableId, cancellationToken);
        var items = await db.OrderItems.Where(value => value.OrderId == source.Id).ToArrayAsync(cancellationToken);

        foreach (var item in items)
        {
            item.OrderId = target.Id;
            item.ClientLineId = $"{source.ClientOrderId}-{item.ClientLineId}";
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        target.GuestCount += source.GuestCount;
        target.Subtotal = Money(target.Subtotal + source.Subtotal);
        target.Total = Money(target.Total + source.Total);
        target.UpdatedAtUtc = DateTimeOffset.UtcNow;

        source.Status = "closed";
        source.ClosedAt = DateTimeOffset.UtcNow;
        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        sourceTable.Status = "available";

        await db.SaveChangesAsync(cancellationToken);

        AddChange(db, "order", target.Id, target.WaiterId, await OrderSnapshotAsync(db, target, cancellationToken));
        AddChange(db, "order", source.Id, source.WaiterId, await OrderSnapshotAsync(db, source, cancellationToken));
        AddChange(db, "dining_table", sourceTable.Id, null, TableSnapshot(db, sourceTable));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await OrderSnapshotAsync(db, target, cancellationToken);
    }

    public async Task ConfigureReceiptPrinterAsync(
        string printerName,
        int copies,
        bool enabled,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureManager(actor);

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var setting = await db.ReceiptPrinterSettings.FindAsync([1], cancellationToken);

        if (setting is null)
        {
            setting = new LocalReceiptPrinterSetting
            {
                Id = 1,
                PrinterName = printerName.Trim(),
            };
            db.ReceiptPrinterSettings.Add(setting);
        }

        setting.PrinterName = printerName.Trim();
        setting.Copies = Math.Clamp(copies, 1, 5);
        setting.IsEnabled = enabled;
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task QueueReceiptAsync(
        string billId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureCashierRole(actor);

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var bill = await db.Bills.SingleOrDefaultAsync(value => value.Id == billId, cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Bill does not exist.");
        var setting = await db.ReceiptPrinterSettings.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == 1 && value.IsEnabled,
            cancellationToken);

        if (setting is null)
        {
            throw new LocalSyncConflictException("printer_not_configured", "A receipt printer is not configured.");
        }

        await AddReceiptJobAsync(db, bill, setting, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<object[]> OpenBillsAsync(CancellationToken cancellationToken)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var bills = await db.Bills
            .Where(value => value.Status == "open")
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var result = new List<object>(bills.Length);
        foreach (var bill in bills.OrderBy(value => value.IssuedAt))
        {
            result.Add(await BillSnapshotAsync(db, bill, cancellationToken));
        }

        return result.ToArray();
    }

    private async Task SettleBillAsync(
        RestaurantDbContext db,
        LocalBill bill,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        bill.Status = "paid";
        bill.PaidAt = DateTimeOffset.UtcNow;
        bill.BalanceDue = 0m;

        var order = await db.Orders.SingleAsync(value => value.Id == bill.OrderId, cancellationToken);
        var table = await db.DiningTables.SingleAsync(value => value.Id == order.DiningTableId, cancellationToken);

        order.Status = "closed";
        order.ClosedAt = DateTimeOffset.UtcNow;
        order.UpdatedAtUtc = DateTimeOffset.UtcNow;
        table.Status = "available";

        var splits = await db.BillSplits.Where(value => value.BillId == bill.Id).ToArrayAsync(cancellationToken);
        if (splits.Length > 0 && splits.Any(value => value.BalanceDue != 0m))
        {
            throw new LocalSyncConflictException("bill_state_conflict", "All split balances must be paid before closing the bill.");
        }

        await db.SaveChangesAsync(cancellationToken);

        var setting = await db.ReceiptPrinterSettings.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == 1 && value.IsEnabled,
            cancellationToken);
        if (setting is not null)
        {
            await AddReceiptJobAsync(db, bill, setting, cancellationToken);
        }

        AddChange(db, "bill", bill.Id, null, await BillSnapshotAsync(db, bill, cancellationToken));
        AddChange(db, "order", order.Id, order.WaiterId, await OrderSnapshotAsync(db, order, cancellationToken));
        AddChange(db, "dining_table", table.Id, null, TableSnapshot(db, table));
    }

    private static async Task AddReceiptJobAsync(
        RestaurantDbContext db,
        LocalBill bill,
        LocalReceiptPrinterSetting setting,
        CancellationToken cancellationToken)
    {
        db.ReceiptPrintJobs.Add(new LocalReceiptPrintJob
        {
            Id = Guid.CreateVersion7().ToString("N"),
            BillId = bill.Id,
            PrinterName = setting.PrinterName,
            DocumentName = bill.BillNumber,
            PayloadText = await BuildReceiptTextAsync(db, bill, cancellationToken),
            Copies = Math.Clamp(setting.Copies, 1, 5),
            Status = "pending",
            Attempts = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private static async Task<string> BuildReceiptTextAsync(
        RestaurantDbContext db,
        LocalBill bill,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().SingleAsync(value => value.Id == bill.OrderId, cancellationToken);
        var table = await db.DiningTables.AsNoTracking().SingleAsync(value => value.Id == order.DiningTableId, cancellationToken);
        var lines = await db.BillLines.Where(value => value.BillId == bill.Id).AsNoTracking().ToArrayAsync(cancellationToken);
        var payments = await db.Payments.Where(value => value.BillId == bill.Id && value.Status == "posted").AsNoTracking().ToArrayAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine("BUSINESSOS RESTAURANT");
        builder.AppendLine($"RECEIPT: {bill.BillNumber}");
        builder.AppendLine($"TABLE: {table.Name} ({table.Code})");
        builder.AppendLine($"WAITER: {order.WaiterName}");
        builder.AppendLine($"DATE: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine("--------------------------------");

        foreach (var line in lines)
        {
            builder.AppendLine($"{line.Quantity} x {line.ItemName}");
            builder.AppendLine($"  {line.LineTotal:0.00} AFN");
        }

        builder.AppendLine("--------------------------------");
        builder.AppendLine($"SUBTOTAL: {bill.Subtotal:0.00} AFN");
        if (bill.DiscountAmount > 0)
        {
            builder.AppendLine($"DISCOUNT: -{bill.DiscountAmount:0.00} AFN");
        }
        builder.AppendLine($"TOTAL: {bill.Total:0.00} AFN");

        foreach (var payment in payments)
        {
            builder.AppendLine($"{payment.Method.ToUpperInvariant()}: {payment.Amount:0.00} AFN");
        }

        builder.AppendLine($"PAID: {bill.PaidAmount:0.00} AFN");
        builder.AppendLine($"BALANCE: {bill.BalanceDue:0.00} AFN");
        builder.AppendLine("--------------------------------");
        builder.AppendLine("Thank you");

        return builder.ToString();
    }

    private static object SessionSnapshot(LocalCashierSession session) => new
    {
        id = session.Id,
        branch_id = session.BranchId,
        cashier_user_id = session.CashierUserId,
        cashier_name = session.CashierName,
        status = session.Status,
        opening_cash = session.OpeningCash.ToString("0.00"),
        expected_cash = session.ExpectedCash?.ToString("0.00"),
        declared_cash = session.DeclaredCash?.ToString("0.00"),
        cash_variance = session.CashVariance?.ToString("0.00"),
        opened_at = session.OpenedAt,
        closed_at = session.ClosedAt,
    };

    private static object PaymentSnapshot(LocalTenantPayment payment) => new
    {
        id = payment.Id,
        bill_id = payment.BillId,
        bill_split_id = payment.BillSplitId,
        cashier_session_id = payment.CashierSessionId,
        client_payment_id = payment.ClientPaymentId,
        method = payment.Method,
        amount = payment.Amount.ToString("0.00"),
        reference = payment.Reference,
        status = payment.Status,
        received_at = payment.ReceivedAt,
    };

    private static async Task<object> BillSnapshotAsync(
        RestaurantDbContext db,
        LocalBill bill,
        CancellationToken cancellationToken)
    {
        var lines = await db.BillLines.Where(value => value.BillId == bill.Id).AsNoTracking().ToArrayAsync(cancellationToken);
        var payments = await db.Payments.Where(value => value.BillId == bill.Id).AsNoTracking().ToArrayAsync(cancellationToken);
        var splits = await db.BillSplits.Where(value => value.BillId == bill.Id).OrderBy(value => value.SplitNumber).AsNoTracking().ToArrayAsync(cancellationToken);

        return new
        {
            id = bill.Id,
            order_id = bill.OrderId,
            branch_id = bill.BranchId,
            bill_number = bill.BillNumber,
            status = bill.Status,
            subtotal = bill.Subtotal.ToString("0.00"),
            discount_type = bill.DiscountType,
            discount_value = bill.DiscountValue?.ToString("0.####"),
            discount_amount = bill.DiscountAmount.ToString("0.00"),
            discount_reason = bill.DiscountReason,
            total = bill.Total.ToString("0.00"),
            paid_amount = bill.PaidAmount.ToString("0.00"),
            balance_due = bill.BalanceDue.ToString("0.00"),
            issued_at = bill.IssuedAt,
            paid_at = bill.PaidAt,
            lines = lines.Select(value => new
            {
                id = value.Id,
                order_item_id = value.OrderItemId,
                item_name = value.ItemName,
                quantity = value.Quantity,
                unit_price = value.UnitPrice.ToString("0.00"),
                line_total = value.LineTotal.ToString("0.00"),
            }).ToArray(),
            payments = payments.Select(PaymentSnapshot).ToArray(),
            splits = splits.Select(value => new
            {
                id = value.Id,
                split_number = value.SplitNumber,
                label = value.Label,
                amount = value.Amount.ToString("0.00"),
                paid_amount = value.PaidAmount.ToString("0.00"),
                balance_due = value.BalanceDue.ToString("0.00"),
                status = value.Status,
            }).ToArray(),
        };
    }

    private static async Task<object> OrderSnapshotAsync(
        RestaurantDbContext db,
        LocalOrder order,
        CancellationToken cancellationToken)
    {
        var table = await db.DiningTables.AsNoTracking().SingleAsync(value => value.Id == order.DiningTableId, cancellationToken);
        var items = await db.OrderItems.Where(value => value.OrderId == order.Id).AsNoTracking().ToArrayAsync(cancellationToken);

        return new
        {
            id = order.Id,
            client_order_id = order.ClientOrderId,
            status = order.Status,
            guest_count = order.GuestCount,
            subtotal = order.Subtotal.ToString("0.00"),
            total = order.Total.ToString("0.00"),
            served_at = order.ServedAt,
            closed_at = order.ClosedAt,
            table = new
            {
                id = table.Id,
                code = table.Code,
                name = table.Name,
                status = table.Status,
            },
            items = items.Select(value => new
            {
                id = value.Id,
                client_line_id = value.ClientLineId,
                item_name = value.ItemName,
                quantity = value.Quantity,
                line_total = value.LineTotal.ToString("0.00"),
                status = value.Status,
            }).ToArray(),
        };
    }

    private static object TableSnapshot(RestaurantDbContext db, LocalDiningTable table)
    {
        var area = db.DiningAreas.AsNoTracking().Single(value => value.Id == table.DiningAreaId);
        var branch = db.Branches.AsNoTracking().Single(value => value.Id == area.BranchId);

        return new
        {
            id = table.Id,
            code = table.Code,
            name = table.Name,
            capacity = table.Capacity,
            status = table.Status,
            is_active = table.IsActive,
            area = new { id = area.Id, name = area.Name },
            branch = new { id = branch.Id, name = branch.Name },
        };
    }

    private static void AddChange(
        RestaurantDbContext db,
        string entityType,
        string entityId,
        long? ownerUserId,
        object data)
    {
        db.Changes.Add(new LocalChange
        {
            EntityType = entityType,
            EntityId = entityId,
            Operation = "upsert",
            OwnerUserId = ownerUserId,
            DataJson = JsonSerializer.Serialize(data, JsonOptions),
            OccurredAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private static void AuthorizeSession(LocalCashierSession session, LocalTerminalPrincipal actor)
    {
        if (session.CashierUserId == actor.UserId)
        {
            return;
        }

        if (actor.UserRole is "owner" or "admin" or "manager")
        {
            return;
        }

        throw new LocalSyncConflictException("forbidden", "This cashier session belongs to another user.", "rejected");
    }

    private static void EnsureCashierRole(LocalTerminalPrincipal actor)
    {
        if (actor.UserRole is not ("owner" or "admin" or "manager" or "cashier"))
        {
            throw new LocalSyncConflictException("forbidden", "This user cannot operate the cashier POS.", "rejected");
        }
    }

    private static void EnsureManagerOrCashier(LocalTerminalPrincipal actor) => EnsureCashierRole(actor);

    private static void EnsureManager(LocalTerminalPrincipal actor)
    {
        if (actor.UserRole is not ("owner" or "admin" or "manager"))
        {
            throw new LocalSyncConflictException("forbidden", "Only management can configure receipt printing.", "rejected");
        }
    }

    private static void EnsureOrderOperationRole(LocalTerminalPrincipal actor)
    {
        if (actor.UserRole is not ("owner" or "admin" or "manager" or "cashier" or "waiter"))
        {
            throw new LocalSyncConflictException("forbidden", "This user cannot serve restaurant orders.", "rejected");
        }
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
