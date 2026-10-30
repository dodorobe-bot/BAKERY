using FluentAssertions;
using Seruni22Bakery.Domain.Entities;
using Xunit;

namespace BakeryDesktop.Domain.Tests;

public class DomainEntityTests
{
    [Fact]
    public void User_PasswordHashing_AndVerification_ShouldSucceed()
    {
        var password = "secretPassword123";
        var hash = User.HashPassword(password);

        var user = new User
        {
            Username = "baker",
            PasswordHash = hash,
            Role = UserRole.Pegawai,
            IsActive = true
        };

        user.VerifyPassword(password).Should().BeTrue();
        user.VerifyPassword("wrongPassword").Should().BeFalse();
    }

    [Fact]
    public void User_LockoutCheck_ShouldReflectExpiryTime()
    {
        var user = new User
        {
            Username = "test",
            LockedUntil = DateTime.UtcNow.AddMinutes(10)
        };

        user.IsLocked().Should().BeTrue();

        user.LockedUntil = DateTime.UtcNow.AddMinutes(-5);
        user.IsLocked().Should().BeFalse();
    }

    [Fact]
    public void SalesOrder_PaymentCalculations_ShouldBeAccurate()
    {
        var order = new SalesOrder
        {
            TotalAmount = 150000,
            DownPayment = 50000,
            Status = OrderStatus.Processing,
            Payments = new List<SalesPayment>
            {
                new() { Amount = 50000, Method = "Transfer" },
                new() { Amount = 30000, Method = "Cash" }
            }
        };

        order.TotalPaid().Should().Be(130000);
        order.RemainingAmount().Should().Be(20000);
        order.IsFullyPaid().Should().BeFalse();

        order.Payments.Add(new SalesPayment { Amount = 20000, Method = "Cash" });
        order.TotalPaid().Should().Be(150000);
        order.RemainingAmount().Should().Be(0);
        order.IsFullyPaid().Should().BeTrue();
    }
}
