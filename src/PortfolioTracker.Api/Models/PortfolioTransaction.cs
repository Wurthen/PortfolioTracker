using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace PortfolioTracker.Api.Models;

public class PortfolioTransaction
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    // No FK so history survives item deletion.
    public Guid ItemId { get; set; }

    // Buy | Sell | TransferIn | TransferOut
    [MaxLength(20)]
    public required string Type { get; set; }

    public DateTime Date { get; set; }

    [Range(0.00000001, double.MaxValue)]
    [Precision(18, 8)]
    public decimal Shares { get; set; }

    [Precision(18, 2)]
    public decimal AmountEur { get; set; }

    [Precision(18, 2)]
    public decimal Commission { get; set; }

    // For transfers: the paired transaction on the other fund.
    public Guid? LinkedTransactionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
