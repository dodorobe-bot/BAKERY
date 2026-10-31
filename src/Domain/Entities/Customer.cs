namespace Seruni22Bakery.Domain.Entities;

public class Customer
{
    public int Id { get; set; }
    public int? LegacyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public int LoyaltyPoints { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public void AddLoyaltyPoints(long orderTotalMinorUnits)
    {
        LoyaltyPoints += LoyaltyPointsFor(orderTotalMinorUnits);
    }

    /// <summary>1 poin per Rp 10.000 (10000 * 100 unit minor).</summary>
    public static int LoyaltyPointsFor(long orderTotalMinorUnits) => (int)(orderTotalMinorUnits / 1_000_000);
}
