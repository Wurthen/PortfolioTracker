using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace PortfolioTracker.Api.Models;

public class PortfolioHistoryPoint
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    // Guid.Empty represents the portfolio total; otherwise the PortfolioItem id.
    // No FK so history survives item deletion.
    public Guid ItemId { get; set; }

    public DateTime Date { get; set; }

    [Precision(18, 2)]
    public decimal ValueEur { get; set; }
}
