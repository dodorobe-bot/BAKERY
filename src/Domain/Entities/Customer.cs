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
        // 1 point per Rp 10.000 (10000 * 100 minor units)
        LoyaltyPoints += (int)(orderTotalMinorUnits / 1_000_000);
    }
}
