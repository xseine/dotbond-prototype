namespace BondPrototype.Models.DataSeeding;

public static class MovieDataSeed
{
    public static List<object> GetMovieData()
    {

        var duneDataUrl = File.ReadAllText(Path.Combine("Models", "DataSeeding", "dune.txt"));
        var noCountryDataUrl = File.ReadAllText(Path.Combine("Models", "DataSeeding", "no-country.txt"));
        var sicarioDataUrl = File.ReadAllText(Path.Combine("Models", "DataSeeding", "sicario.txt"));

        return new List<object>
        {
            new
            {
                Id = 1, Title = "Dune", Rating = (byte)8, ReleaseDate = new DateTime(2021, 10, 22), DirectedById = 1,
                Description = "A noble family becomes embroiled in a war for control over the galaxy's most valuable asset while its heir becomes troubled by visions of a dark future.",
                MoviePoster = duneDataUrl
            },
            new
            {
                Id = 2, Title = "No Country for Old Men", Rating = (byte)8, ReleaseDate = new DateTime(2007, 11, 21), DirectedById = 2,
                Description = "Violence and mayhem ensue after a hunter stumbles upon a drug deal gone wrong and more than two million dollars in cash near the Rio Grande.",
                MoviePoster = noCountryDataUrl
            },
            new
            {
                Id = 3, Title = "Sicario", Rating = (byte)7, ReleaseDate = new DateTime(2015, 10, 2), DirectedById = 1,
                Description = "An idealistic FBI agent is enlisted by a government task force to aid in the escalating war against drugs at the border area between the U.S. and Mexico.",
                MoviePoster = sicarioDataUrl
            }
        };
    }

    public static List<object> GetPersonData()
    {
        var benicioUrl = File.ReadAllText(Path.Combine("Models", "DataSeeding", "Actors", "benicio.txt"));
        var javierUrl = File.ReadAllText(Path.Combine("Models", "DataSeeding", "Actors", "javier.txt"));
        var joshUrl = File.ReadAllText(Path.Combine("Models", "DataSeeding", "Actors", "josh.txt"));
        var timotheeUrl = File.ReadAllText(Path.Combine("Models", "DataSeeding", "Actors", "timothee.txt"));
        var woodyUrl = File.ReadAllText(Path.Combine("Models", "DataSeeding", "Actors", "woody.txt"));

        var benicioBio = File.ReadAllText(Path.Combine("Models", "DataSeeding", "Actors", "Biographies", "benicio-bio.txt"));
        var javierBio = File.ReadAllText(Path.Combine("Models", "DataSeeding", "Actors", "Biographies", "javier-bio.txt"));
        var joshBio = File.ReadAllText(Path.Combine("Models", "DataSeeding", "Actors", "Biographies", "josh-bio.txt"));
        var timotheeBio = File.ReadAllText(Path.Combine("Models", "DataSeeding", "Actors", "Biographies", "timothee-bio.txt"));
        var woodyBio = File.ReadAllText(Path.Combine("Models", "DataSeeding", "Actors", "Biographies", "woody-bio.txt"));

        return new List<object>
        {
            new { Id = 1, Name = "Denis Villeneuve"},
            new { Id = 2, Name = "Ethan Coen" },
            new { Id = 3, Name = "Josh Brolin", Picture = joshUrl, Biography = joshBio },
            new { Id = 4, Name = "Javier Bardem", Picture = javierUrl, Biography = javierBio },
            new { Id = 5, Name = "Timothée Chalamet", Picture = timotheeUrl, Biography = timotheeBio },
            new { Id = 6, Name = "Woody Harrelson", Picture = woodyUrl, Biography = woodyBio },
            new { Id = 7, Name = "Benicio Del Toro", Picture = benicioUrl, Biography = benicioBio }
        };
    }

    public static List<object> GetMovieCastData()
    {
        return new List<object>
        {
            // Josh, Javier, Timothee in "Dune"
            new { ActedInId = 1, ActorsId = 3 },
            new { ActedInId = 1, ActorsId = 4 },
            new { ActedInId = 1, ActorsId = 5 },

            // Josh, Javier, Woody in "No Country for Old Men"
            new { ActedInId = 2, ActorsId = 3 },
            new { ActedInId = 2, ActorsId = 4 },
            new { ActedInId = 2, ActorsId = 6 },

            // Josh, Benicio in "Sicario"
            new { ActedInId = 3, ActorsId = 3 },
            new { ActedInId = 3, ActorsId = 7 }
        };
    }

    public static List<object> GetAwards()
    {
        return new List<object>
        {
            // Dune
            new {Id = 1, Type = AwardType.Oscar, Name = "Best Sound", Year = 2022, MovieId = 1},
            new { Id = 2, Type = AwardType.Oscar, Name = "Best Achievement in Visual Effects", Year = 2022, MovieId = 1 },
            new { Id = 3, Type = AwardType.Oscar, Name = "Best Achievement in Production Design", Year = 2022, MovieId = 1 },
            new { Id = 4, Type = AwardType.Bafta, Name = "Best Cinematography", Year = 2022, MovieId = 1 },
            new { Id = 5, Type = AwardType.Bafta, Name = "Original Score", Year = 2022, MovieId = 1 },
            new { Id = 6, Type = AwardType.Bafta, Name = "Best Production Design", Year = 2022, MovieId = 1 },
            
            // No Country for Old Men
            new { Id = 7, Type = AwardType.Oscar, Name = "Best Motion Picture of the Year", Year = 2008, MovieId = 2 },
            new { Id = 8, Type = AwardType.Oscar, Name = "Best Performance by an Actor in a Supporting Role", Year = 2008, MovieId = 2 },
            new { Id = 9, Type = AwardType.Oscar, Name = "Best Achievement in Directing", Year = 2008, MovieId = 2 },
            new { Id = 10, Type = AwardType.Oscar, Name = "Best Writing, Adapted Screenplay", Year = 2008, MovieId = 2 },
            new { Id = 11, Type = AwardType.Bafta, Name = "Best Supporting Actor", Year = 2008, MovieId = 2 },
            new { Id = 12, Type = AwardType.Bafta, Name = "Best Cinematography", Year = 2008, MovieId = 2 },
            new { Id = 13, Type = AwardType.Bafta, Name = "Best Director", Year = 2008, MovieId = 2 }
        };
    }
}