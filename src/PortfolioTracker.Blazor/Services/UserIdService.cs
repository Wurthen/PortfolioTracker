namespace PortfolioTracker.Blazor.Services;

public class UserIdService
{
    private static readonly Guid FixedUserId = Guid.Parse("12345678-1234-1234-1234-123456789012");

    public Guid UserId
    {
        get => FixedUserId;
        set { }
    }
}
