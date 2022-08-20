using BondPrototype.Models.DataSeeding;
using Microsoft.EntityFrameworkCore;

namespace BondPrototype.Models;

public class Entities : DbContext
{
    public Entities(DbContextOptions<Entities> options) : base(options)
    {
        
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Movie>()
            .HasMany(movie => movie.Actors)
            .WithMany(person => person.ActedIn)
            .UsingEntity(j => j.ToTable("MovieCast").HasData(MovieDataSeed.GetMovieCastData()))
            .HasOne(movie => movie.DirectedBy)
            .WithMany(person => person.Directed);

        modelBuilder.Entity<Movie>()
            .HasData(MovieDataSeed.GetMovieData());
        
        modelBuilder.Entity<Person>()
            .HasData(MovieDataSeed.GetPersonData());

        modelBuilder.Entity<Award>()
            .HasData(MovieDataSeed.GetAwards());
    }
    
    /*========================== Database Tables ==========================*/

    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<Award> Awards => Set<Award>();

}