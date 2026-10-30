namespace Seruni22Bakery.Desktop.Contracts;

public record ApiResponse<T>(bool Success, T? Data, string? ErrorCode, string? ErrorMessage);

public record LoginDto(string Username, string Password);
public record AuthTokenDto(string Token, string Username, string FullName, string Role, int UserId);

public record ChangePasswordRequestDto(string CurrentPassword, string NewPassword);

public record PaymentDto(int OrderId, long Amount, string Method, string? Notes, string? IdempotencyKey);
public record UpdateOrderStatusDto(int OrderId, string Status, string? CancelReason);
public record RecordExpenseDto(string Category, string Description, long Amount);

public record OrderItemDto(int ProductTemplateId, string ProductName, int Quantity, long UnitPrice, long Subtotal);
public record CreateSalesOrderDto(
    int CustomerId,
    DateTime OrderDate,
    DateTime RequiredDate,
    List<OrderItemDto> Items,
    long DiscountAmount,
    string? Notes
);

public record DashboardDto(
    int PendingOrdersCount,
    int InProductionOrdersCount,
    int ReadyOrdersCount,
    int CompletedTodayCount,
    long TodayRevenue,
    long MonthRevenue,
    long TotalReceivables,
    int LowStockIngredientsCount
);
