using System.Text.Json;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed class LocalOperationsControlService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalDatabaseFactory _databaseFactory;

    public LocalOperationsControlService(LocalDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public async Task<object> StartShiftAsync(
        string branchId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureShiftRole(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var branch = await db.Branches.SingleOrDefaultAsync(
            value => value.Id == branchId && value.IsActive,
            cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "The selected branch is inactive or unavailable.");

        var existing = await db.WaiterShifts.SingleOrDefaultAsync(
            value => value.UserId == actor.UserId && value.Status == "open",
            cancellationToken);

        if (existing is not null)
        {
            throw new LocalSyncConflictException("shift_conflict", "This staff member already has an open shift.");
        }

        var shift = new LocalWaiterShift
        {
            Id = Guid.CreateVersion7().ToString("N"),
            BranchId = branch.Id,
            UserId = actor.UserId,
            UserPublicId = actor.UserPublicId,
            UserName = actor.UserName,
            Role = actor.UserRole,
            Status = "open",
            StartedAt = DateTimeOffset.UtcNow,
            BreakMinutes = 0,
        };

        db.WaiterShifts.Add(shift);
        LocalCloudOutboxWriter.Enqueue(
            db,
            actor,
            "shift.upsert",
            "waiter_shift",
            shift.Id,
            new
            {
                branch_id = shift.BranchId,
                user_public_id = shift.UserPublicId,
                user_name = shift.UserName,
                role = shift.Role,
                status = shift.Status,
                started_at = shift.StartedAt,
                ended_at = shift.EndedAt,
                break_minutes = shift.BreakMinutes,
                closing_note = shift.ClosingNote,
            });
        AddAudit(
            db,
            actor,
            "shift",
            "shift.opened",
            branch.Id,
            "waiter_shift",
            shift.Id,
            new { shift_id = shift.Id, branch_id = branch.Id, role = actor.UserRole });

        await db.SaveChangesAsync(cancellationToken);
        return ShiftSnapshot(shift);
    }

    public async Task<object> EndShiftAsync(
        string shiftId,
        int breakMinutes,
        string? note,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        if (breakMinutes < 0 || breakMinutes > 24 * 60)
        {
            throw new LocalSyncConflictException("invalid_payload", "Break minutes are invalid.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var shift = await db.WaiterShifts.SingleOrDefaultAsync(
            value => value.Id == shiftId,
            cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Shift does not exist.");

        if (shift.UserId != actor.UserId && actor.UserRole is not ("owner" or "admin" or "manager"))
        {
            throw new LocalSyncConflictException("forbidden", "Only the shift owner or management can end this shift.", "rejected");
        }

        if (shift.Status == "closed")
        {
            return ShiftSnapshot(shift);
        }

        shift.Status = "closed";
        shift.EndedAt = DateTimeOffset.UtcNow;
        shift.BreakMinutes = breakMinutes;
        shift.ClosingNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        LocalCloudOutboxWriter.Enqueue(
            db,
            actor,
            "shift.upsert",
            "waiter_shift",
            shift.Id,
            new
            {
                branch_id = shift.BranchId,
                user_public_id = shift.UserPublicId,
                user_name = shift.UserName,
                role = shift.Role,
                status = shift.Status,
                started_at = shift.StartedAt,
                ended_at = shift.EndedAt,
                break_minutes = shift.BreakMinutes,
                closing_note = shift.ClosingNote,
            });
        AddAudit(
            db,
            actor,
            "shift",
            "shift.closed",
            shift.BranchId,
            "waiter_shift",
            shift.Id,
            new
            {
                shift_id = shift.Id,
                staff_user_id = shift.UserId,
                break_minutes = breakMinutes,
                worked_minutes = WorkedMinutes(shift),
                note = shift.ClosingNote,
            });

        await db.SaveChangesAsync(cancellationToken);
        return ShiftSnapshot(shift);
    }

    public async Task<object[]> ActiveShiftsAsync(
        string? branchId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureManagement(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var query = db.WaiterShifts.Where(value => value.Status == "open").AsNoTracking();

        if (!string.IsNullOrWhiteSpace(branchId))
        {
            query = query.Where(value => value.BranchId == branchId);
        }

        var rows = await query.ToArrayAsync(cancellationToken);

        return rows
            .OrderBy(value => value.StartedAt)
            .Select(ShiftSnapshot)
            .ToArray();
    }

    public async Task<object> FinalizeDailyClosingAsync(
        string branchId,
        DateOnly businessDate,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureClosingRole(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var branch = await db.Branches.SingleOrDefaultAsync(
            value => value.Id == branchId && value.IsActive,
            cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "The selected branch is inactive or unavailable.");

        var openSessions = (await db.CashierSessions
            .Where(value => value.BranchId == branch.Id && value.Status == "open")
            .AsNoTracking()
            .ToArrayAsync(cancellationToken))
            .Any(value => Date(value.OpenedAt) <= businessDate);

        if (openSessions)
        {
            throw new LocalSyncConflictException(
                "closing_blocked",
                "Close all cashier sessions for this business date before finalizing.");
        }

        var openBills = (await db.Bills
            .Where(value => value.BranchId == branch.Id && value.Status == "open")
            .AsNoTracking()
            .ToArrayAsync(cancellationToken))
            .Any(value => Date(value.IssuedAt) <= businessDate);

        if (openBills)
        {
            throw new LocalSyncConflictException(
                "closing_blocked",
                "Settle all open bills for this business date before finalizing.");
        }

        var openShifts = (await db.WaiterShifts
            .Where(value => value.BranchId == branch.Id && value.Status == "open")
            .AsNoTracking()
            .ToArrayAsync(cancellationToken))
            .Any(value => Date(value.StartedAt) <= businessDate);

        if (openShifts)
        {
            throw new LocalSyncConflictException(
                "closing_blocked",
                "End all staff shifts for this business date before finalizing.");
        }

        var closing = await db.DailyClosings.SingleOrDefaultAsync(
            value => value.BranchId == branch.Id && value.BusinessDate == businessDate,
            cancellationToken);

        if (closing is null)
        {
            closing = new LocalDailyClosing
            {
                Id = Guid.CreateVersion7().ToString("N"),
                BranchId = branch.Id,
                BusinessDate = businessDate,
                Status = "open",
                CreatedByUserId = actor.UserId,
            };
            db.DailyClosings.Add(closing);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (closing.Status == "finalized")
        {
            return await ClosingSnapshotAsync(db, closing, cancellationToken);
        }

        var bills = (await db.Bills
            .Where(value => value.BranchId == branch.Id && value.Status == "paid")
            .AsNoTracking()
            .ToArrayAsync(cancellationToken))
            .Where(value => value.PaidAt.HasValue && Date(value.PaidAt.Value) == businessDate)
            .ToArray();

        var billIds = bills.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        var payments = (await db.Payments
            .Where(value => value.Status == "posted")
            .AsNoTracking()
            .ToArrayAsync(cancellationToken))
            .Where(value => billIds.Contains(value.BillId) && Date(value.ReceivedAt) == businessDate)
            .ToArray();

        var sessions = (await db.CashierSessions
            .Where(value => value.BranchId == branch.Id && value.Status == "closed")
            .AsNoTracking()
            .ToArrayAsync(cancellationToken))
            .Where(value => value.ClosedAt.HasValue && Date(value.ClosedAt.Value) == businessDate)
            .ToArray();

        var version = (await db.DailyClosingSnapshots
            .Where(value => value.DailyClosingId == closing.Id)
            .Select(value => (int?)value.Version)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var now = DateTimeOffset.UtcNow;
        var snapshot = new LocalDailyClosingSnapshot
        {
            Id = Guid.CreateVersion7().ToString("N"),
            DailyClosingId = closing.Id,
            Version = version,
            FinalizedByUserId = actor.UserId,
            BillCount = bills.Length,
            PaymentCount = payments.Length,
            CashierSessionCount = sessions.Length,
            GrossSales = Money(bills.Sum(value => value.Subtotal)),
            Discounts = Money(bills.Sum(value => value.DiscountAmount)),
            NetSales = Money(bills.Sum(value => value.Total)),
            PaymentsTotal = Money(payments.Sum(value => value.Amount)),
            CashPayments = SumPayments(payments, "cash"),
            CardPayments = SumPayments(payments, "card"),
            BankPayments = SumPayments(payments, "bank"),
            MobileMoneyPayments = SumPayments(payments, "mobile_money"),
            OtherPayments = SumPayments(payments, "other"),
            ExpectedCash = Money(sessions.Sum(value => value.ExpectedCash ?? 0m)),
            DeclaredCash = Money(sessions.Sum(value => value.DeclaredCash ?? 0m)),
            CashVariance = Money(sessions.Sum(value => value.CashVariance ?? 0m)),
            FinalizedAt = now,
        };

        db.DailyClosingSnapshots.Add(snapshot);
        closing.Status = "finalized";
        closing.FinalizedAt = now;
        closing.ReopenedAt = null;

        LocalCloudOutboxWriter.Enqueue(
            db,
            actor,
            "daily_closing.finalize",
            "daily_closing",
            closing.Id,
            new
            {
                branch_id = branch.Id,
                business_date = businessDate.ToString("yyyy-MM-dd"),
            });
        AddAudit(
            db,
            actor,
            "daily_closing",
            "daily_closing.finalized",
            branch.Id,
            "daily_closing",
            closing.Id,
            new
            {
                closing_id = closing.Id,
                business_date = businessDate.ToString("yyyy-MM-dd"),
                version,
                snapshot.NetSales,
                snapshot.PaymentsTotal,
                snapshot.CashVariance,
            });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ClosingSnapshotAsync(db, closing, cancellationToken);
    }

    public async Task<object> ReopenDailyClosingAsync(
        string closingId,
        string reason,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureManagement(actor);

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
        {
            throw new LocalSyncConflictException("invalid_payload", "A meaningful reopen reason is required.");
        }

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var closing = await db.DailyClosings.SingleOrDefaultAsync(
            value => value.Id == closingId,
            cancellationToken)
            ?? throw new LocalSyncConflictException("dependency_missing", "Daily closing does not exist.");

        if (closing.Status != "finalized")
        {
            throw new LocalSyncConflictException("closing_state_conflict", "Only a finalized daily closing can be reopened.");
        }

        closing.Status = "reopened";
        closing.ReopenedAt = DateTimeOffset.UtcNow;

        LocalCloudOutboxWriter.Enqueue(
            db,
            actor,
            "daily_closing.reopen",
            "daily_closing",
            closing.Id,
            new
            {
                reason = reason.Trim(),
                business_date = closing.BusinessDate.ToString("yyyy-MM-dd"),
                branch_id = closing.BranchId,
            });
        AddAudit(
            db,
            actor,
            "daily_closing",
            "daily_closing.reopened",
            closing.BranchId,
            "daily_closing",
            closing.Id,
            new
            {
                closing_id = closing.Id,
                business_date = closing.BusinessDate.ToString("yyyy-MM-dd"),
                reason = reason.Trim(),
            });

        await db.SaveChangesAsync(cancellationToken);
        return await ClosingSnapshotAsync(db, closing, cancellationToken);
    }

    public async Task<object[]> ListClosingsAsync(
        string? branchId,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureClosingRole(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var query = db.DailyClosings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(branchId))
        {
            query = query.Where(value => value.BranchId == branchId);
        }

        var closings = await query.ToArrayAsync(cancellationToken);
        var result = new List<object>();

        foreach (var closing in closings
                     .OrderByDescending(value => value.BusinessDate)
                     .ThenByDescending(value => value.FinalizedAt)
                     .Take(100))
        {
            result.Add(await ClosingSnapshotAsync(db, closing, cancellationToken));
        }

        return result.ToArray();
    }

    public async Task<object[]> AuditAsync(
        long cursor,
        int limit,
        string? category,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureManagement(actor);
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();

        var safeLimit = Math.Clamp(limit, 1, 250);
        var query = db.AuditEvents
            .Where(value => value.Sequence > Math.Max(0, cursor))
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(value => value.Category == category);
        }

        var rows = await query
            .OrderBy(value => value.Sequence)
            .Take(safeLimit)
            .ToArrayAsync(cancellationToken);

        return rows.Select(value => new
        {
            cursor = value.Sequence,
            event_id = value.EventId,
            category = value.Category,
            event_type = value.EventType,
            actor = new
            {
                user_id = value.ActorUserId,
                name = value.ActorName,
                role = value.ActorRole,
            },
            branch_id = value.BranchId,
            entity_type = value.EntityType,
            entity_id = value.EntityId,
            payload = value.PayloadJson is null
                ? (JsonElement?)null
                : JsonSerializer.Deserialize<JsonElement>(value.PayloadJson, JsonOptions),
            occurred_at = value.OccurredAtUtc,
        }).ToArray<object>();
    }

    public static void AddAudit(
        RestaurantDbContext db,
        LocalTerminalPrincipal actor,
        string category,
        string eventType,
        string? branchId,
        string? entityType,
        string? entityId,
        object? payload = null)
    {
        db.AuditEvents.Add(new LocalAuditEvent
        {
            EventId = Guid.CreateVersion7().ToString("N"),
            Category = category,
            EventType = eventType,
            ActorUserId = actor.UserId,
            ActorName = actor.UserName,
            ActorRole = actor.UserRole,
            BranchId = branchId,
            EntityType = entityType,
            EntityId = entityId,
            PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload, JsonOptions),
            OccurredAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private static async Task<object> ClosingSnapshotAsync(
        RestaurantDbContext db,
        LocalDailyClosing closing,
        CancellationToken cancellationToken)
    {
        var snapshots = await db.DailyClosingSnapshots
            .Where(value => value.DailyClosingId == closing.Id)
            .OrderBy(value => value.Version)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var events = await db.AuditEvents
            .Where(value => value.EntityType == "daily_closing" && value.EntityId == closing.Id)
            .OrderBy(value => value.Sequence)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return new
        {
            id = closing.Id,
            branch_id = closing.BranchId,
            business_date = closing.BusinessDate.ToString("yyyy-MM-dd"),
            status = closing.Status,
            created_by_user_id = closing.CreatedByUserId,
            finalized_at = closing.FinalizedAt,
            reopened_at = closing.ReopenedAt,
            snapshots = snapshots.Select(value => new
            {
                id = value.Id,
                version = value.Version,
                finalized_by_user_id = value.FinalizedByUserId,
                bill_count = value.BillCount,
                payment_count = value.PaymentCount,
                cashier_session_count = value.CashierSessionCount,
                gross_sales = value.GrossSales.ToString("0.00"),
                discounts = value.Discounts.ToString("0.00"),
                net_sales = value.NetSales.ToString("0.00"),
                payments_total = value.PaymentsTotal.ToString("0.00"),
                cash_payments = value.CashPayments.ToString("0.00"),
                card_payments = value.CardPayments.ToString("0.00"),
                bank_payments = value.BankPayments.ToString("0.00"),
                mobile_money_payments = value.MobileMoneyPayments.ToString("0.00"),
                other_payments = value.OtherPayments.ToString("0.00"),
                expected_cash = value.ExpectedCash.ToString("0.00"),
                declared_cash = value.DeclaredCash.ToString("0.00"),
                cash_variance = value.CashVariance.ToString("0.00"),
                finalized_at = value.FinalizedAt,
            }).ToArray(),
            events = events.Select(value => new
            {
                event_id = value.EventId,
                event_type = value.EventType,
                actor_user_id = value.ActorUserId,
                actor_name = value.ActorName,
                actor_role = value.ActorRole,
                payload = value.PayloadJson is null
                    ? (JsonElement?)null
                    : JsonSerializer.Deserialize<JsonElement>(value.PayloadJson, JsonOptions),
                occurred_at = value.OccurredAtUtc,
            }).ToArray(),
        };
    }

    private static object ShiftSnapshot(LocalWaiterShift shift) => new
    {
        id = shift.Id,
        branch_id = shift.BranchId,
        user_id = shift.UserId,
        user_public_id = shift.UserPublicId,
        user_name = shift.UserName,
        role = shift.Role,
        status = shift.Status,
        started_at = shift.StartedAt,
        ended_at = shift.EndedAt,
        break_minutes = shift.BreakMinutes,
        worked_minutes = WorkedMinutes(shift),
        closing_note = shift.ClosingNote,
    };

    private static int WorkedMinutes(LocalWaiterShift shift)
    {
        var end = shift.EndedAt ?? DateTimeOffset.UtcNow;
        var gross = Math.Max(0, (int)Math.Floor((end - shift.StartedAt).TotalMinutes));
        return Math.Max(0, gross - shift.BreakMinutes);
    }

    private static DateOnly Date(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.UtcDateTime);

    private static decimal SumPayments(IEnumerable<LocalTenantPayment> payments, string method) =>
        Money(payments.Where(value => value.Method == method).Sum(value => value.Amount));

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static void EnsureShiftRole(LocalTerminalPrincipal actor)
    {
        if (actor.UserRole is not ("owner" or "admin" or "manager" or "waiter" or "cashier" or "kitchen"))
        {
            throw new LocalSyncConflictException("forbidden", "This user cannot open a staff shift.", "rejected");
        }
    }

    private static void EnsureClosingRole(LocalTerminalPrincipal actor)
    {
        if (actor.UserRole is not ("owner" or "admin" or "manager" or "cashier"))
        {
            throw new LocalSyncConflictException("forbidden", "This user cannot finalize daily closing.", "rejected");
        }
    }

    private static void EnsureManagement(LocalTerminalPrincipal actor)
    {
        if (actor.UserRole is not ("owner" or "admin" or "manager"))
        {
            throw new LocalSyncConflictException("forbidden", "Management permission is required.", "rejected");
        }
    }
}
