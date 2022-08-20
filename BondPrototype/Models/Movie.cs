using System.ComponentModel.DataAnnotations;

namespace BondPrototype.Models;
// aaaa
public class Movie
{
    [Key] public int Id { get; set; }
    public string Title { get; set; }
    public byte Rating { get; set; }
    public DateTime ReleaseDate { get; set; }
    public string MoviePoster { get; set; }
    public string Description { get; set; }
    
    public Person DirectedBy { get; set; }
    public List<Person> Actors { get; set; }
    public List<Award> Awards { get; set; }
}