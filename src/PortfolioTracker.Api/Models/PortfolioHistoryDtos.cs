namespace PortfolioTracker.Api.Models;

public class PortfolioHistoryResponseDto
{
    public List<HistoryPointDto> Total { get; set; } = [];
    public Dictionary<string, List<HistoryPointDto>> Items { get; set; } = [];
    public List<HistoryPointDto> TotalReturn { get; set; } = [];
    public List<HistoryPointDto> TotalSimpleReturn { get; set; } = [];
    public Dictionary<string, List<HistoryPointDto>> ItemReturns { get; set; } = [];
}

public class HistoryPointDto
{
    public DateTime Date { get; set; }
    public decimal Value { get; set; }
}
