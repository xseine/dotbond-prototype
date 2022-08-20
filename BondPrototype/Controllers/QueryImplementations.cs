//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using BondPrototype.Models;
using Microsoft.Extensions.Logging;
using BondPrototype.Controllers;

namespace GeneratedControllers;

public class QueryImplementations : ControllerBase
{
    Entities db;
	ILogger<MovieApiController> logger;

    public QueryImplementations(Entities db, ILogger<MovieApiController> logger)
    {
        this.db = db;
		this.logger = logger;
    }
    

    public virtual IQueryable<GetMovieListDetailsType> GetMovieListDetails()
    {
        var movieApi = new MovieApiController(db, logger);

        return movieApi.GetMovies()
            .Select(movie => new GetMovieListDetailsType {
                    Picture = movie.MoviePoster,
                    Name = movie.Title,
                    Year = movie.ReleaseDate.Year,
                    Rating = movie.Rating,
                    Description = movie.Description,
                    Director = movie.DirectedBy.Name,
                    Actors = movie.Actors.Select(actor => actor.Name),
                    Awards = movie.Awards.Select(award => new GetMovieListDetailsType.AnonymousSubType1 {Type = award.Type, Name = award.Name})
                });
    }


    public virtual IQueryable<GetShortProfilesOfActorsType> GetShortProfilesOfActors()
    {
        var movieApi = new MovieApiController(db, logger);

        return movieApi.GetActors()
            .Select(actor => new GetShortProfilesOfActorsType {
                    Id = actor.Id,
                    Picture = actor.Picture,
                    Name = actor.Name,
                    NumberOfMovies = actor.ActedIn.Count
                });
    }


    public virtual IQueryable<GetListOfActorNamesType> GetListOfActorNames()
    {
        return GetShortProfilesOfActors().Select(e => new GetListOfActorNamesType {Name = e.Name, Id = e.Id});
    }


    public virtual GetBiographyType GetBiography(decimal actorId)
    {
        var movieApi = new MovieApiController(db, logger);

        return movieApi.GetActors()
            .Where(actor => actor.Id == actorId)
            .Select(actor => new GetBiographyType {
                Biography = actor.Biography,
                Movies = actor.ActedIn.Select(movie => movie.Title)
            }).FirstOrDefault();
    }


    public virtual GetBiography2Type GetBiography2(decimal actorId)
    {
        var movieApi = new MovieApiController(db, logger);

        return movieApi.GetActors()
            .Where(actor => actor.Id == actorId)
            .Select(actor => new GetBiography2Type {
                Biography = actor.Biography,
                Movies = actor.ActedIn.Select(movie => movie.Title)
            }).FirstOrDefault();
    }


    public virtual GetShortProfileAndWorkStatsType GetShortProfileAndWorkStats(decimal actorId)
    {
        var movieApi = new MovieApiController(db, logger);

        return movieApi.GetActors()
            .Select(actor => new {
                Id = actor.Id,
                Average = actor.ActedIn.Select(e => e.Rating).Sum(e => e) / actor.ActedIn.Count,
                Colleagues = movieApi.GetActors()
                    .Where(potentialColleague => potentialColleague.ActedIn.Any(e => actor.ActedIn.Select(e => e.Id).Contains(e.Id)))
                    .Select(e => new GetShortProfileAndWorkStatsType.AnonymousSubType1 {Name = e.Name, Id = e.Id})
                    .Where(e => e.Id != actor.Id).ToList()
            })
            .Join(GetShortProfilesOfActors(), e => e.Id, e => e.Id,  (stats, profile) => new GetShortProfileAndWorkStatsType {
                Picture= profile.Picture,
                Name= profile.Name,
                NumberOfMovies= profile.NumberOfMovies,
                Id= stats.Id,
                Average= stats.Average,
                Colleagues= stats.Colleagues
            })
            .FirstOrDefault(e => e.Id == actorId);
    }


    public virtual IQueryable<PersonResult> MyCustomQuery()
    {
        var movieApi = new MovieApiController(db, logger);

        return movieApi.GetDirectors().Where(director => director.Directed.Count == 1);
    }


    public virtual IQueryable<PersonResult> AnotherCustomQuery()
    {
        return MyCustomQuery();
    }


    public virtual IEnumerable<MovieResult> AnotherOne(decimal year)
    {
        var movieApi = new MovieApiController(db, logger);

        return movieApi.GetMoviesFromAYear((Int32) year, "").Value?.Where(movie => movie.Awards.Count != 0 && movie.Awards.Count == 0);
    }

}

public class GetMovieListDetailsType
{
    public string Picture;
	public string Name;
	public int Year;
	public byte Rating;
	public string Description;
	public string Director;
	public IEnumerable<string> Actors;
	public IEnumerable<AnonymousSubType1> Awards;

	public class AnonymousSubType1
	{
	    public AwardType Type;
		public string Name;
	}
}

public class GetShortProfilesOfActorsType
{
    public int Id;
	public string Picture;
	public string Name;
	public int NumberOfMovies;
}

public class GetBiographyType
{
    public string Biography;
	public IEnumerable<string> Movies;
}

public class GetBiography2Type
{
    public string Biography;
	public IEnumerable<string> Movies;
}

public class GetListOfActorNamesType
{
    public string Name;
	public int Id;
}

public class GetShortProfileAndWorkStatsType
{
    public string Picture;
	public string Name;
	public int NumberOfMovies;
	public int Id;
	public int Average;
	public List<AnonymousSubType1> Colleagues;

	public class AnonymousSubType1
	{
	    public string Name;
		public int Id;
	}
}











