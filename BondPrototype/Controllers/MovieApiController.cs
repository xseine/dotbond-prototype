using BondPrototype.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BondPrototype.Controllers;


public class Savo : MovieResult
{
    
}

// aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
[ApiController]
[Route("[controller]/[action]")]
public class MovieApiController : ControllerBase
{
    private readonly Entities _db;

    private readonly ILogger<MovieApiController> _logger;

    public MovieApiController(Entities db, ILogger<MovieApiController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public IQueryable<MovieResult> GetMovies()
    {
        return _db.Movies.Include(e => e.DirectedBy).Include(e => e.DirectedBy).Select(movie => new Savo
        {
            Id = movie.Id,
            Title = movie.Title,
            Rating = movie.Rating,
            ReleaseDate = movie.ReleaseDate,
            Description = movie.Description,
            MoviePoster = movie.MoviePoster,
            DirectedBy = new MovieResult.Director(movie.DirectedBy.Id, movie.DirectedBy.Name, movie.DirectedBy.DateOfBirth),
            Actors = movie.Actors.Select(e => new MovieResult.Actor(e.Id, e.Name, e.DateOfBirth)).ToList(),
            Awards = movie.Awards.Select(e => new MovieResult.Award(e.Type, e.Name)).ToList()
        });
    }
    
    [HttpGet]
    public ActionResult<IQueryable<PersonResult>> GetActors()
    {
        // hejasa
        
        var a = _db.Persons.Where(person => person.ActedIn.Any()).Include(e => e.ActedIn).Select(actor => new PersonResult
        {
            Id = actor.Id,
            Name = actor.Name,
            DateOfBirth = actor.DateOfBirth,
            Picture = actor.Picture,
            Biography = actor.Biography,
            Directed = actor.Directed.Select(movie => new PersonResult.DirectedOrActedInMovie(movie.Id, movie.Title, movie.Rating, movie.ReleaseDate)).ToList(),
            ActedIn = actor.ActedIn.Select(movie => new PersonResult.DirectedOrActedInMovie(movie.Id, movie.Title, movie.Rating, movie.ReleaseDate)).ToList()
        });

        // return a;
        return new ActionResult<IQueryable<PersonResult>>(a);
    }
    
    [HttpGet]
    public IQueryable<PersonResult> GetDirectors()
    {
        return _db.Persons.Where(person => person.Directed.Any()).Include(e => e.Directed).Select(director => new PersonResult
        {
            Id = director.Id,
            Name = director.Name,
            DateOfBirth = director.DateOfBirth,
            Picture = director.Picture,
            Directed = director.Directed.Select(movie => new PersonResult.DirectedOrActedInMovie(movie.Id, movie.Title, movie.Rating, movie.ReleaseDate)).ToList(),
            ActedIn = director.ActedIn.Select(movie => new PersonResult.DirectedOrActedInMovie(movie.Id, movie.Title, movie.Rating, movie.ReleaseDate)).ToList()
        });
    }

    [HttpGet]
    public IQueryable<AwardResult> GetAwards()
    {
        return _db.Awards
            .Select(award => new AwardResult
            {
                Id = award.Id,
                Type = award.Type,
                Name = award.Name,
                Year = award.Year,
                MovieId = award.MovieId,
                MovieName = award.AwardedMovie.Title
            });
    }

    [HttpGet]
    public ActionResult<List<MovieResult>> GetMoviesFromAYear(int year, string _)
    {
        return GetMovies().Where(e => e.ReleaseDate.Year == year).ToList();
    }


    // [HttpGet]
    // public ActionResult<IEnumerable<object>> FaultyQuery()
    // {
    //     return _db.Movies
    //         .Include(e => e.DirectedBy)
    //         .Include(e => e.Actors)
    //         .GroupJoin(
    //             inner: _db.Persons
    //                 .Where(person => person.Directed
    //                     .AsQueryable()
    //                     .Any())
    //                 .Include(e => e.Directed),
    //             outerKeySelector: e => e.DirectedBy.Id,
    //             innerKeySelector: e => e.Id,
    //             resultSelector: (movie, directors) => new
    //             {
    //                 Id = movie.Id,
    //                 Title = movie.Title,
    //                 Rating = movie.Rating,
    //                 ReleaseDate = movie.ReleaseDate,
    //                 DirectedBy = movie.DirectedBy,
    //                 Actors = movie.Actors,
    //                 MovieTitle = movie.Title.ToLower(),
    //                 DirectorName = directors
    //                     .AsQueryable()
    //                     .Where(_ => true)
    //                     .Select(s => s.Name)
    //                     .First()
    //             }
    //         ).ToList();
    // }
    //
    //
    // [HttpGet]
    // public ActionResult<IEnumerable<object>> FaultyQueryMagical()
    // {
    //     return _db.Movies
    //         .Include(e => e.DirectedBy)
    //         .Include(e => e.Actors)
    //         .Select(e => new
    //         {
    //             movie = e,
    //             directors = _db.Persons.Where(person => person.Id == e.DirectedBy.Id).ToList()
    //         })
    //         .Select(e => new
    //             {
    //                 Id = e.movie.Id,
    //                 Title = e.movie.Title,
    //                 Rating = e.movie.Rating,
    //                 ReleaseDate = e.movie.ReleaseDate,
    //                 DirectedBy = e.movie.DirectedBy,
    //                 Actors = e.movie.Actors,
    //                 MovieTitle = e.movie.Title.ToLower(),
    //                 DirectorName = e.directors
    //                     .AsQueryable()
    //                     .Where(_ => true)
    //                     .Select(s => s.Name)
    //                     .First()
    //             }
    //         ).ToList();
    // }
    

}