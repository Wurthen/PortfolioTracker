namespace PortfolioTracker.Blazor.Services;

public class UserIdService
{
    private Guid _userId = Guid.Empty;
    private bool _initialized = false;

    public Guid UserId
    {
        get
        {
            if (_userId == Guid.Empty && !_initialized)
            {
                _userId = Guid.NewGuid();
            }
            return _userId;
        }
        set
        {
            _userId = value;
            _initialized = true;
        }
    }
}
