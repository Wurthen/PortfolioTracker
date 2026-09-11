namespace PortfolioTracker.Api.Services;

public static class PerformanceCalculators
{
    /// <summary>
    /// Annualized money-weighted return (%) via bisection.
    /// Cashflows: negative = money outlaid (purchases), positive = withdrawals/current value.
    /// Returns null when the flow structure has no sign change (no meaningful IRR).
    /// </summary>
    public static decimal? Xirr(IReadOnlyList<(DateTime Date, decimal Amount)> cashflows)
    {
        if (cashflows.Count < 2)
            return null;

        var t0 = cashflows.Min(c => c.Date);
        double Npv(double rate)
        {
            double sum = 0;
            foreach (var cf in cashflows)
            {
                var years = (cf.Date - t0).TotalDays / 365.25;
                sum += (double)cf.Amount / Math.Pow(1 + rate, years);
            }
            return sum;
        }

        double low = -0.9999, high = 10.0;
        var fLow = Npv(low);
        var fHigh = Npv(high);
        if (fLow * fHigh > 0)
            return null;

        for (var i = 0; i < 200; i++)
        {
            var mid = (low + high) / 2;
            var fMid = Npv(mid);
            if (Math.Abs(fMid) < 1e-9)
                break;
            if (fLow * fMid < 0)
            {
                high = mid;
                fHigh = fMid;
            }
            else
            {
                low = mid;
                fLow = fMid;
            }
        }

        return (decimal)((low + high) / 2 * 100);
    }
}
