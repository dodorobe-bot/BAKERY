namespace Seruni22Bakery.Domain.Entities;

public enum AccountType { Asset, Liability, Equity, Revenue, Expense }
public enum JournalStatus { Draft, Posted, Reversed }

public class Account
{
    public int Id { get; set; }
    public int? LegacyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public int? ParentId { get; set; }
    public bool IsSystem { get; set; } = false;
    public bool IsActive { get; set; } = true;

    // Normal balance: Asset+Expense = Debit, Liability+Equity+Revenue = Credit
    public bool NormalDebit() => Type is AccountType.Asset or AccountType.Expense;
}

public class JournalEntry
{
    public int Id { get; set; }
    public string? Description { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? SourceDocumentType { get; set; } // SalesOrder, Expense, Manual
    public int? SourceDocumentId { get; set; }
    public JournalStatus Status { get; set; } = JournalStatus.Posted;
    public int? ReversalOfId { get; set; }
    public DateTime? PostedAt { get; set; }
    public int? PostedByUserId { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<JournalLine> Lines { get; set; } = new();

    public bool IsBalanced()
    {
        var debit = Lines.Sum(l => l.DebitAmount);
        var credit = Lines.Sum(l => l.CreditAmount);
        return debit == credit && debit > 0;
    }
}

public class JournalLine
{
    public int Id { get; set; }
    public int JournalEntryId { get; set; }
    public int AccountId { get; set; }
    public long DebitAmount { get; set; } = 0;    // minor units
    public long CreditAmount { get; set; } = 0;   // minor units
    public string? Notes { get; set; }
    public Account? Account { get; set; }
}

/// <summary>
/// Saldo akun pada arah normalnya: aset/beban = debit - kredit,
/// pasif/modal/pendapatan = kredit - debit. Jadi pendapatan dan beban selalu positif saat wajar.
/// </summary>
public record AccountBalance(int AccountId, string Code, string Name, AccountType Type, long Balance);
