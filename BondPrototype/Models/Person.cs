using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BondPrototype.Models;

public class Person
{
    
    [Key] public int Id { get; set; }
    public string Name { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string Picture { get; set; }
    public string Biography { get; set; }

    public List<Movie> Directed { get; set; }
    public List<Movie> ActedIn { get; set; }
}