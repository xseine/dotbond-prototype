namespace BondPrototype.Models;

public class AwardResult
{
    public int Id { get; set; }
    public AwardType Type { get; set; }
    public string Name { get; set; }
    public int Year { get; set; }
    public int MovieId { get; set; }
    public string MovieName { get; set; }
}