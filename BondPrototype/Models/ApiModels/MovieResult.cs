namespace BondPrototype.Models;

public class MovieResult
{
    public int Id { get; set; }
    public string Title { get; set; }
    public byte Rating { get; set; }
    public DateTime ReleaseDate { get; set; }
    public string MoviePoster { get; set; }
    public string Description { get; set; }

    public record struct Director(int Id, string Name, DateTime? DateOfBirth);

    public Director DirectedBy { get; set; }

    public record struct Actor(int Id, string Name, DateTime? DateOfBirth);

    public List<Actor> Actors { get; set; }

    public record struct Award(AwardType Type, string Name);

    public List<Award> Awards { get; set; }
}