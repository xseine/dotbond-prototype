namespace BondPrototype.Models;

public class PersonResult
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string Picture { get; set; }
    public string Biography { get; set; }

    public record struct DirectedOrActedInMovie(int Id, string Title, byte Rating, DateTime ReleaseDate);

    public List<DirectedOrActedInMovie> Directed { get; set; }
    public List<DirectedOrActedInMovie> ActedIn { get; set; }
}