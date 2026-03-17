namespace StockAnalysis.Api.Models.Strategy;

public class Strategy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<StrategyRule> Rules { get; set; } = [];
}
