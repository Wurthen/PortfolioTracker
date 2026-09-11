using PortfolioTracker.Api.Models;

namespace PortfolioTracker.Api.Services;

public static class ReturnCalculators
{
    /// <summary>
    /// Computes the daily Time-Weighted Return (TWR) series for the whole portfolio.
    /// External cash flows (Buys/Sells) are subtracted from the daily market change so
    /// that contributions and withdrawals do not distort the performance curve.
    /// </summary>
    public static List<HistoryPointDto> ComputeTwrSeries(
        List<PortfolioHistoryPoint> totals,
        Dictionary<DateTime, decimal> flowsByDate)
    {
        var result = new List<HistoryPointDto>();
        if (totals.Count < 2)
        {
            if (totals.Count == 1)
            {
                result.Add(new HistoryPointDto { Date = totals[0].Date, Value = 0m });
            }
            return result;
        }

        var ordered = totals.OrderBy(p => p.Date).ToList();
        var orderedFlows = flowsByDate.OrderBy(kv => kv.Key).ToList();
        var cumulative = 1m;

        // First point is always 0% return.
        result.Add(new HistoryPointDto { Date = ordered[0].Date, Value = 0m });

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];

            // Flows are summed over (previous, current] so purchases landing on a
            // weekend/holiday without a history point still offset the change.
            var flow = SumFlowsBetween(orderedFlows, previous.Date, current.Date);

            var dailyReturn = previous.ValueEur != 0
                ? (current.ValueEur - previous.ValueEur - flow) / previous.ValueEur
                : 0m;

            cumulative *= 1 + dailyReturn;
            var twrPct = decimal.Round((cumulative - 1) * 100m, 2);
            result.Add(new HistoryPointDto { Date = current.Date, Value = twrPct });
        }

        return result;
    }

    /// <summary>
    /// Maps a transaction to its external cash flow: Buys and SafeBack are money in,
    /// Sells are money out. Transfers are internal and return zero.
    /// </summary>
    public static decimal ExternalFlowOf(PortfolioTransaction tx) => tx.Type switch
    {
        "Buy" => tx.AmountEur + tx.Commission,
        "SafeBack" => tx.AmountEur,
        "Sell" => -tx.AmountEur,
        _ => 0m
    };

    /// <summary>
    /// External flows grouped by day, ready for the TWR series. SafeBack is treated
    /// as a contribution (like a Buy), so its reinvested cash never shows as return.
    /// </summary>
    public static Dictionary<DateTime, decimal> BuildExternalFlowsByDate(IEnumerable<PortfolioTransaction> transactions)
        => transactions
            .Where(t => t.Type is "Buy" or "Sell" or "SafeBack")
            .GroupBy(t => t.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(ExternalFlowOf));

    /// <summary>Sum of external flows dated after <paramref name="afterDate"/> up to <paramref name="upToDate"/> (inclusive).</summary>
    internal static decimal SumFlowsBetween(IReadOnlyList<KeyValuePair<DateTime, decimal>> orderedFlows, DateTime afterDate, DateTime upToDate)
    {
        var sum = 0m;
        foreach (var flow in orderedFlows)
        {
            if (flow.Key.Date > afterDate.Date && flow.Key.Date <= upToDate.Date)
                sum += flow.Value;
        }
        return sum;
    }

    /// <summary>
    /// Computes the portfolio's daily simple return (%): (value - invested) / invested.
    /// Invested accumulates Buy cost (amount + commission), net transfers and SafeBack
    /// contributions (money the broker reinvests for the user, counted as if the user
    /// had deposited it). This is the same source of truth used since inception by the
    /// "Ganancia / Pérdida" KPI, so a full-history chart matches it.
    /// </summary>
    public static List<HistoryPointDto> ComputeSimpleReturnSeries(
        List<PortfolioHistoryPoint> totals,
        List<PortfolioTransaction> transactions)
    {
        var result = new List<HistoryPointDto>();
        if (totals.Count == 0)
            return result;

        var ordered = totals.OrderBy(p => p.Date).ToList();
        var investedTxs = transactions
            .Where(t => t.Type is "Buy" or "TransferIn" or "TransferOut" or "SafeBack")
            .OrderBy(t => t.Date)
            .ToList();

        var costBasis = 0m;
        var txIndex = 0;

        foreach (var point in ordered)
        {
            while (txIndex < investedTxs.Count && investedTxs[txIndex].Date.Date <= point.Date.Date)
            {
                costBasis += investedTxs[txIndex].Type switch
                {
                    "Buy" => investedTxs[txIndex].AmountEur + investedTxs[txIndex].Commission,
                    "SafeBack" => investedTxs[txIndex].AmountEur,
                    "TransferIn" => investedTxs[txIndex].AmountEur,
                    "TransferOut" => -investedTxs[txIndex].AmountEur,
                    _ => 0m
                };
                txIndex++;
            }

            var returnPct = costBasis > 0
                ? decimal.Round((point.ValueEur - costBasis) / costBasis * 100m, 2)
                : 0m;

            result.Add(new HistoryPointDto { Date = point.Date, Value = returnPct });
        }

        return result;
    }

    /// <summary>
    /// Computes the daily return (%) series for each position based on its market value
    /// and the cost basis accumulated from Buy/TransferIn/TransferOut/SafeBack transactions,
    /// using the same cost definition as ComputeSimpleReturnSeries (commission included).
    /// </summary>
    public static Dictionary<string, List<HistoryPointDto>> ComputeItemReturnSeries(
        List<PortfolioHistoryPoint> itemPoints,
        List<PortfolioTransaction> transactions)
    {
        var result = new Dictionary<string, List<HistoryPointDto>>();

        var txsByItem = transactions
            .Where(t => t.Type is "Buy" or "TransferIn" or "TransferOut" or "SafeBack")
            .GroupBy(t => t.ItemId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(t => t.Date).ToList());

        var pointsByItem = itemPoints
            .Where(p => p.ItemId != Guid.Empty)
            .GroupBy(p => p.ItemId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Date).ToList());

        foreach (var (itemId, points) in pointsByItem)
        {
            if (points.Count == 0)
                continue;

            txsByItem.TryGetValue(itemId, out var itemTxs);
            itemTxs ??= [];

            var series = new List<HistoryPointDto>();
            var costBasis = 0m;
            var txIndex = 0;

            foreach (var point in points)
            {
                // Accumulate all buys/transfers that happened on or before this point's date.
                while (txIndex < itemTxs.Count && itemTxs[txIndex].Date.Date <= point.Date.Date)
                {
                    costBasis += itemTxs[txIndex].Type switch
                    {
                        "Buy" => itemTxs[txIndex].AmountEur + itemTxs[txIndex].Commission,
                        "SafeBack" => itemTxs[txIndex].AmountEur,
                        "TransferIn" => itemTxs[txIndex].AmountEur,
                        "TransferOut" => -itemTxs[txIndex].AmountEur,
                        _ => 0m
                    };
                    txIndex++;
                }

                var returnPct = costBasis > 0
                    ? decimal.Round((point.ValueEur - costBasis) / costBasis * 100m, 2)
                    : 0m;

                series.Add(new HistoryPointDto { Date = point.Date, Value = returnPct });
            }

            result[itemId.ToString()] = series;
        }

        return result;
    }
}
