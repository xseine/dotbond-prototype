using System.ComponentModel.DataAnnotations;

namespace BondPrototype.Models;

// asda
public class Award
{
    [Key] public int Id { get; set; }
    public AwardType Type { get; set; }
    public string Name { get; set; }
    public int Year { get; set; }
    public int MovieId { get; set; }

    public Movie AwardedMovie { get; set; }
}

public enum AwardType
{
    Oscar,
    Bafta
}