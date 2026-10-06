using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed class LocalExpenseService(LocalDatabaseFactory databaseFactory)
{
    public async Task<object> RecordAsync(string branchId, string category, string description, decimal amount,
        string paymentMethod, DateOnly expenseDate, string? reference, LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureRole(actor);
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(description) || amount <= 0)
            throw new LocalSyncConflictException("invalid_payload", "Category, description and a positive amount are required.");
        if (paymentMethod is not ("cash" or "card" or "bank" or "mobile_money" or "other"))
            throw new LocalSyncConflictException("invalid_payload", "Unsupported restaurant expense payment method.");

        await databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = databaseFactory.Create();
        if (!await db.Branches.AnyAsync(x => x.Id == branchId && x.IsActive, cancellationToken))
            throw new LocalSyncConflictException("dependency_missing", "Branch is missing or inactive.");

        var expense = new LocalExpense {
            Id = Guid.CreateVersion7().ToString("N"), BranchId = branchId, RecordedByUserId = actor.UserId,
            Category = category.Trim(), Description = description.Trim(),
            Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero), Currency = "AFN",
            PaymentMethod = paymentMethod, Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            ExpenseDate = expenseDate, RecordedAtUtc = DateTimeOffset.UtcNow
        };
        db.Expenses.Add(expense);
        LocalCloudOutboxWriter.Enqueue(db, actor, "expense.record", "expense", expense.Id, new {
            branch_id = branchId, category = expense.Category, description = expense.Description,
            amount = expense.Amount, currency = expense.Currency, payment_method = expense.PaymentMethod,
            reference = expense.Reference, expense_date = expense.ExpenseDate
        });
        LocalOperationsControlService.AddAudit(db, actor, "expenses", "expense.recorded", branchId, "expense", expense.Id,
            new { expense_id = expense.Id, expense.Category, expense.Amount, expense.PaymentMethod, expense.ExpenseDate });
        await db.SaveChangesAsync(cancellationToken);
        return Snapshot(expense);
    }

    public async Task<object[]> ListAsync(string? branchId, DateOnly? from, DateOnly? to, LocalTerminalPrincipal actor,
        CancellationToken cancellationToken)
    {
        EnsureRole(actor);
        await databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = databaseFactory.Create();
        var q = db.Expenses.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(branchId)) q = q.Where(x => x.BranchId == branchId);
        if (from.HasValue) q = q.Where(x => x.ExpenseDate >= from.Value);
        if (to.HasValue) q = q.Where(x => x.ExpenseDate <= to.Value);
        return (await q.OrderByDescending(x => x.ExpenseDate).ThenByDescending(x => x.RecordedAtUtc).Take(500).ToArrayAsync(cancellationToken))
            .Select(Snapshot).ToArray();
    }

    private static object Snapshot(LocalExpense x) => new { id=x.Id, branch_id=x.BranchId, category=x.Category,
        description=x.Description, amount=x.Amount.ToString("0.00"), currency=x.Currency, payment_method=x.PaymentMethod,
        reference=x.Reference, expense_date=x.ExpenseDate, recorded_by_user_id=x.RecordedByUserId, recorded_at=x.RecordedAtUtc };

    private static void EnsureRole(LocalTerminalPrincipal actor)
    {
        if (actor.UserRole is not ("owner" or "admin" or "manager" or "cashier"))
            throw new LocalSyncConflictException("forbidden", "This user cannot record restaurant expenses.", "rejected");
    }
}
