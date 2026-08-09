using System.ComponentModel.DataAnnotations;

namespace PortfolioTracker.Api.Models;

public class PortfolioItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(20)]
    public required string Symbol { get; set; }

    [MaxLength(200)]
    public required string Name { get; set; }

    [MaxLength(50)]
    public required string Type { get; set; }

    [Range(0.0001, double.MaxValue)]
    public decimal Shares { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal PurchasePrice { get; set; }

    public DateTime? PurchaseDate { get; set; }

    public decimal Commission { get; set; }

    [MaxLength(50)]
    public string? AlternativeSymbol { get; set; }

    public bool UseAlternativeSymbol { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
