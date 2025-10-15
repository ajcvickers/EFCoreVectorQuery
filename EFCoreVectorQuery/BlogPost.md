# MongoDB Vector Search with EF Core

The [8.3.2](https://www.nuget.org/packages/MongoDB.EntityFrameworkCore/8.3.2) and [9.0.2](https://www.nuget.org/packages/MongoDB.EntityFrameworkCore/9.0.2) releases of the [EF Core database provider for MongoDB](https://www.mongodb.com/docs/entity-framework/current/) introduce support for [Atlas Vector Search](https://www.mongodb.com/products/platform/atlas-vector-search) in LINQ queries. This enables vector similarity search integrated with MongoDB's powerful document mapping and distributed architecture. Vector searches are [used in AI](https://learn.mongodb.com/courses/rag-with-mongodb), but can also be used for [powerful similarity matching](https://www.mongodb.com/docs/atlas/atlas-vector-search/vector-search-stage/) in non-AI application.

In this post, we will create a simple console application that will demonstrate the capabilities of MongoDB vector similarity search with EF Core. The application uses a `Movie` entity type and contains embeddings for the movie's plot such that searches for similar movies can be made. 

## The application

We will start with a simple console application targeting .NET 9, and install the [latest version of the EF Core provider for MongoDB](https://www.nuget.org/packages/MongoDB.EntityFrameworkCore/).

> Tip: The [completed application can be found on GitHub](https://github.com/ajcvickers/EFCoreVectorQuery).

EF Core requires a [DbContext class](https://learn.microsoft.com/ef/core/dbcontext-configuration/) and entity types to be defined. For this post, we will use a simple model with a single entity type representing a movie:

```csharp
[BsonIgnoreExtraElements]
public class EmbeddedMovie
{
    public ObjectId Id { get; }
    public required string Title { get; set; }
    public required int Year { get; set; }
    public string? Plot { get; set; }
    public float[]? PlotEmbedding { get; set; }
}
```

The `MoviesDbDbContext` class includes a `DbSet<Movie>` property as the root for LINQ queries, and configuration of the mapping from C# names (e.g. "Plot") to the lower-case names (e.g. "plot") in the documents. In addition, embeddings are configured to use an efficient binary format with`HasBinaryVectorDataType(BinaryVectorDataType.Float32)`.

```csharp
public class MoviesDbContext : DbContext
{
    protected override void OnConfiguring(
        DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder
            .UseMongoDB(
                connectionString:
                "mongodb://localhost:50934/?directConnection=true",
                databaseName:
                "sample_mflix")
            .LogTo(Console.WriteLine, LogLevel.Information);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<EmbeddedMovie>(b =>
            {
                b.ToCollection("embedded_movies");

                b.Property(e => e.Id);
                b.Property(e => e.Title).HasElementName("title");
                b.Property(e => e.Plot).HasElementName("plot");
                b.Property(e => e.Year).HasElementName("year");

                b.Property(e => e.PlotEmbedding)
                    .HasElementName("plot_embedding_voyage_3_large")
                    .HasBinaryVectorDataType(BinaryVectorDataType.Float32);

                b.HasIndex(e => e.PlotEmbedding).IsVectorIndex(
                    VectorSimilarity.DotProduct,
                    dimensions: 2048,
                    options => options.HasQuantization(VectorQuantization.Scalar)
                        .AllowsFiltersOn(e => e.Title)
                        .AllowsFiltersOn(e => e.Year));
            });
    }

    public DbSet<EmbeddedMovie> Movies => Set<EmbeddedMovie>();
}
```

Optionally, the binary format can be configured directly on the property using `BinaryVectorAttribute`. For example:

```csharp
[BinaryVector(BinaryVectorDataType.Float32)]
public float[]? PlotEmbedding { get; set; }
```

> Tip: See [the EF Core documentation](https://learn.microsoft.com/ef/core/) for more information about `DbContext` and EF Core in general, and [the MongoDB provider documentation](https://www.mongodb.com/docs/entity-framework/current/) for more information about using EF Core with MongoDB.

## Using MongoDB Atlas local

We will test our code against MongoDB Atlas local running in Docker, which is fast, cheap, and convenient. Follow [the instructions for setup Atlas local](https://www.mongodb.com/docs/atlas/cli/current/atlas-cli-deploy-local/); it's very easy, even if you don't know Docker.  Alternately, you can [sign up for a free Atlas deployment](https://www.mongodb.com/products/platform), or use an existing Atlas deployment.

> Tip: If you already have an Atlas deployment with the "MFlix" sample data loaded, then you may need to refresh this data, since the dataset is updated periodically. Consider using [MongoDB Compas](https://www.mongodb.com/products/tools/compass) or [mongosh](https://www.mongodb.com/try/download/shell) to examine and/or delete existing data.

Get the connection string for Atlas local using the command `atlas deployments connect`, and copy it into the call to `UseMongoDB`. For example:

```shell
arthur.vickers@M-MG6X9WX2PC mongo-efcore-provider % atlas deployments connect                                                              
? Select a deployment local3537 (Local)
? How would you like to connect to local3537? connectionString
mongodb://localhost:50934/?directConnection=true
arthur.vickers@M-MG6X9WX2PC mongo-efcore-provider % 
```

### Import sample data

Realistic vector data is hard to create manually. Instead, we will import the ["MFlix" sample database](https://www.mongodb.com/docs/atlas/sample-data/sample-mflix/#std-label-mflix-embedded_movies), which contains an embedding for the plot of each movie. When running Atlas locally, the sample data can be downloaded using `curl`:

```shell
curl  https://atlas-education.s3.amazonaws.com/sampledata.archive -o sampledata.archive
```

And then copied into the database using `mongorestore`:

```shell
mongorestore --archive=sampledata.archive --port=<port-number>
```

Make sure to use the port number from your connection string.

## Why use a vector query?

The sample data contains "embeddings" for movie plots. Each of these embeddings encodes across many dimensions the various aspects of the plot. For example, a James Bond movie will have an embedding indicating it is a spy, action movie, with a character called James Bond, among many other aspects of the plot.

If we now create an embedding for a plot, or part of a plot, we can use a vector search to find the other movies with a similar plot, searching across this large number of possible dimensions. To demonstrate this, we will first use a normal MongoDB query to fetch the plot for the James Bond film _Octopussy_:

```csharp
using var db = new MoviesDbContext();

var octopussy = await db.Movies.SingleAsync(e => e.Title == "Octopussy");

Console.WriteLine($"Found '{octopussy.Title}' with plot '{octopussy.Plot}'");
```

Now we can use the embedding for Octopussy's plot to find movies that are similar. For example:

```csharp
var similarMovies = await db.Movies.VectorSearch(
        e => e.PlotEmbedding,
        octopussy.PlotEmbedding,
        limit: 10)
    .ToListAsync();
```

**But wait!** If you run this query now in our sample project without any addition setup, you will see the following error:

```text
Unhandled exception. System.InvalidOperationException: A vector query for 'EmbeddedMovie.PlotEmbedding' could not be executed
because the vector index for this query could not be found. Use 'HasIndex' on the EF model builder to specify the index,
or specify the index name in the call to 'VectorQuery' if indexes are being managed outside of EF Core.
   at MongoDB.EntityFrameworkCore.Query.Visitors.MongoEFToLinqTranslatingExpressionVisitor.<>c__DisplayClass8_2.<Visit>g__ThrowForBadOptions|5(String reason)
   at ...
```

This illustrates a critical point: **vector queries require a vector index before they will return results**.

## Creating a vector index

EF Core can help us create this index, but first we need to define it in the EF Core model. For example, add the following code to the configuration for `Movie` in the `OnModelCreating` method of `MoviesDbContext`:

```csharp
b.HasIndex(e => e.PlotEmbedding).IsVectorIndex(
    VectorSimilarity.DotProduct,
    dimensions: 2048,
    options => options.HasQuantization(VectorQuantization.Scalar)
        .AllowsFiltersOn(e => e.Title)
        .AllowsFiltersOn(e => e.year));
```

This defines an index with 2048 dimensions and some other configuration that we will discuss below. However, running the query after adding this code to the model still generates a warning:

```text
warn: 14/10/2025 11:23:16.057 MongoEventId.VectorSearchReturnedZeroResults[35014] (Microsoft.EntityFrameworkCore.Query) 
      The vector query against 'EmbeddedMovie.PlotEmbedding' using index 'PlotEmbeddingVectorIndex' returned zero results. This could be because either there is no vector index defined in the database for query property, or because vector data (embeddings) have recently been inserted and the index is still building. Consider disabling index creation in 'DbContext.Database.EnsureCreated' and performing initial ingestion of embeddings, before calling 'DbContext.Database.CreateMissingVectorIndexes' and 'DbContext.Database.WaitForVectorIndexes'.
```

This is because we have defined the vector index in the EF Core model, but it has not yet been created in the MongoDB database.

Creation of vector indexes is slow, and the index must be fully created before vector searches can be performed. This is normally done outside of the normal application flow at the time the embeddings are created. EF Core can help with this by creating vector indexes and waiting for them to be ready.

Calling `DbContext.Database.EnsureCreatedAsync` will create any missing vector indexes by default. Alternately, this can be done explicitly at any time using `DbContext.Database.CreateMissingVectorIndexes` and `DbContext.Database.WaitForVectorIndexes`. In our sample, we make these calls before running any queries to ensure that the vector index is created and ready before the query runs. For example:

```csharp
using var db = new MoviesDbContext();

await db.Database.CreateMissingVectorIndexesAsync();
await db.Database.WaitForVectorIndexesAsync();
```

## Running vector queries

Now let's re-run the vector query for movies with plots like _Octopussy_, and print out the results:

```csharp
var similarMovies = await db.Movies.VectorSearch(
        e => e.PlotEmbedding,
        octopussy.PlotEmbedding,
        limit: 10)
    .ToListAsync();

int i;
PrintMovieResults(similarMovies, "the plot of 'Octopussy'");

void PrintMovieResults(List<EmbeddedMovie> movies, string? embeddingDescription)
{
    Console.WriteLine($"Found {movies.Count} movies with plots similar to {embeddingDescription}:");
    i = 1;
    foreach (var movie in movies)
    {
        Console.WriteLine($"  {i++}: '{movie.Title}' with plot '{movie.Plot?.Substring(0, 20)}...'");
    }
}
```

Running this code results in the following output:

```text
Found 10 movies with plots similar to the plot of 'Octopussy':
  1: 'Octopussy' with plot 'A fake Fabergè egg a...'
  2: 'Never Say Never Again' with plot 'A SPECTRE agent has ...'
  3: 'The Spy Who Loved Me' with plot 'James Bond investiga...'
  4: 'From Russia with Love' with plot 'James Bond willingly...'
  5: 'Goldfinger' with plot 'Investigating a gold...'
  6: 'GoldenEye' with plot 'James Bond teams up ...'
  7: 'Diamonds Are Forever' with plot 'A diamond smuggling ...'
  8: 'Thunderball' with plot 'James Bond heads to ...'
  9: 'The World Is Not Enough' with plot 'James Bond uncovers ...'
  10: 'For Your Eyes Only' with plot 'Agent 007 is assigne...'
```

As we might expect, all the top ten most similar movies also feature James Bond!

> Tip: Only ten results were returned because we set that limit in the query. Limiting in the query this way is a best practice to help reduce the amount of work done by the vector search.

The plot embedding used in the query does not have to come from an existing movie in the database. Instead, an embedding can be created for any user-supplied fragment of a plot. This is achieved by calling an endpoint provided by the embedding model, passing in the text or other data to be embedded. The endpoint returns a list of floating point numbers that represent the embedding.

This process is described in [How to Create Vector Embeddings](https://www.mongodb.com/docs/atlas/atlas-vector-search/create-embeddings) for the "voyage-3-large" model we are using here. Some code adapted from the linked instructions is also [included in the GitHub repo for this post](https://github.com/ajcvickers/EFCoreVectorQuery). Since the sample data contains embeddings with 2048 dimensions, we need to instruct VoyageAI to do the same by specifying "the output_dimension" when calling the endpoint. For example:

```csharp
var requestBody = new
{
    input = texts,
    model = EmbeddingModelName,
    truncation = true,
    output_dimension = 2048
};
```

Assume we have obtained an embedding for "time travel".

> Tip: The "time travel" embedding is included in the [code on GitHub](https://github.com/ajcvickers/EFCoreVectorQuery), along with three other sample embeddings for you to use without creating your own.

We can now use this embedding to find movies about time travel:

```csharp
var timeTravelEmbedding = new[] {-0.034731735, ... , 040714234};

var timeTravelMovies = await db.Movies.VectorSearch(
        e => e.PlotEmbedding,
        timeTravelEmbedding,
        limit: 10)
    .ToListAsync();
```

The returned movies are all related to "time travel":

```text
Found 10 movies with plots similar to 'time travel':
  1: 'About Time' with plot 'At the age of 21, Ti...'
  2: 'Retroactive' with plot 'A psychiatrist makes...'
  3: 'Timecop' with plot 'An officer for a sec...'
  4: 'A.P.E.X.' with plot 'A time-travel experi...'
  5: 'Back to the Future Part II' with plot 'After visiting 2015,...'
  6: 'Thrill Seekers' with plot 'A reporter, learning...'
  7: 'Timerider: The Adventure of Lyle Swann' with plot 'Lyle, a motorcycle c...'
  8: 'The Time Machine' with plot 'Hoping to alter the ...'
  9: 'The Time Traveler's Wife' with plot 'A romantic drama abo...'
  10: 'Stargate: Continuum' with plot 'Ba'al travels back i...'
```

## Pre-filtering vector searches

Often there are other constraints on the returned results from a vector search. The search is more efficient if unwanted results are filtered out before looking for similarities. For example, imagine we only want to find time travel movies from the 1980s. We can add this filter to our vector query:

```csharp
var eightiesTimeTravelMovies = await db.Movies.VectorSearch(
        e => e.PlotEmbedding,
        preFilter: e => e.year >= 1980 && e.year < 1990,
        timeTravelEmbedding,
        limit: 10)
    .ToListAsync();
```

> Tip: For a field to be used in a vector search filter, it must be included in the vector index as a "filter" field. Refer back to the index definition above to see how "year" and "Title" are included for filtering.

Looking at the results from the 1980s only, it is clear that some of the movies at the bottom of the list are not very time travel like.

```text
Found 10 movies with plots similar to 'time travel' from the 1980s:
  1: 'Back to the Future Part II' with plot 'After visiting 2015,...'
  2: 'Timerider: The Adventure of Lyle Swann' with plot 'Lyle, a motorcycle c...'
  3: 'The Final Countdown' with plot 'A modern aircraft ca...'
  4: 'Time Bandits' with plot 'A young boy accident...'
  5: 'Peggy Sue Got Married' with plot 'Peggy Sue faints at ...'
  6: 'Tommy Tricker and the Stamp Traveller' with plot 'When the joker Tommy...'
  7: 'Mirror for a Hero' with plot 'Two not quite simila...'
  8: 'The Navigator: A Medieval Odyssey' with plot 'Men seeking relief f...'
  9: 'Warlock' with plot 'A warlock flees from...'
  10: 'The Karate Kid, Part II' with plot 'Daniel accompanies h...'
```

If we want to find out, relatively, how close each of the matches is, then we can project out the "score" for each result. Creating a projection also means we can avoid fetching the large embedding models in each document, which are not normally needed on the client. For example, let's project out the ID, title, plot, and score for each match:

```csharp
var projectedMoviesWithScore = await db.Movies.VectorSearch(
        e => e.PlotEmbedding,
        preFilter: e => e.year >= 1980 && e.year < 1990,
        timeTravelEmbedding,
        limit: 10)
    .Select(e => new
    {
        e.Id,
        e.Title,
        e.Plot,
        Score = EF.Property<float>(e, "__score")
    }).ToListAsync();
```

> Tip: This projection uses the [EF Property method](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.ef.property) to request the query metadata value "__score" even though there is no Score property in the entity itself. 

Let's look at the scores for "time travel" movies limited to the 1980s:

```text
Projecting details and score from 10 movies:
  1: (Score: 0.7521951) 'Back to the Future Part II' with plot 'After visiting 2015,...'
  2: (Score: 0.7504921) 'Timerider: The Adventure of Lyle Swann' with plot 'Lyle, a motorcycle c...'
  3: (Score: 0.74751997) 'The Final Countdown' with plot 'A modern aircraft ca...'
  4: (Score: 0.7193389) 'Time Bandits' with plot 'A young boy accident...'
  5: (Score: 0.7188964) 'Peggy Sue Got Married' with plot 'Peggy Sue faints at ...'
  6: (Score: 0.71291494) 'Tommy Tricker and the Stamp Traveller' with plot 'When the joker Tommy...'
  7: (Score: 0.7125094) 'Mirror for a Hero' with plot 'Two not quite simila...'
  8: (Score: 0.70869136) 'The Navigator: A Medieval Odyssey' with plot 'Men seeking relief f...'
  9: (Score: 0.70750093) 'Warlock' with plot 'A warlock flees from...'
  10: (Score: 0.7039094) 'The Karate Kid, Part II' with plot 'Daniel accompanies h...'
```

And compare to the scores for all "time travel" movies:

```text
Projecting details and score from 10 movies:
  1: (Score: 0.7706156) 'About Time' with plot 'At the age of 21, Ti...'
  2: (Score: 0.7603297) 'Retroactive' with plot 'A psychiatrist makes...'
  3: (Score: 0.7581115) 'Timecop' with plot 'An officer for a sec...'
  4: (Score: 0.75784063) 'A.P.E.X.' with plot 'A time-travel experi...'
  5: (Score: 0.7521951) 'Back to the Future Part II' with plot 'After visiting 2015,...'
  6: (Score: 0.751307) 'Thrill Seekers' with plot 'A reporter, learning...'
  7: (Score: 0.7504921) 'Timerider: The Adventure of Lyle Swann' with plot 'Lyle, a motorcycle c...'
  8: (Score: 0.7499349) 'The Time Machine' with plot 'Hoping to alter the ...'
  9: (Score: 0.74950624) 'The Time Traveler's Wife' with plot 'A romantic drama abo...'
  10: (Score: 0.7475202) 'Stargate: Continuum' with plot 'Ba'al travels back i...'
```

Notice how, relatively speaking, the scores for the filtered list drop off much faster than the scores for the full list. This aligns with the observation that the movies at the end of the filtered list don't have that much to do with time travel.

## Summary

Vector queries add powerful similarity searches for both AI and non-AI applications to the MongoDB repertoire. EF Core provides modelling for vector indexes, along with utilities for creating the vector indexes in MongoDB and waiting for them to be ready. EF Core exposes vector queries in LINQ, and vector queries can be pre-filtered using a LINQ predicate. Projections from a vector query can include the score for each result. 
