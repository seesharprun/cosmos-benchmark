using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class Benchmark
{
    private int pendingTaskCount;
    private long documentsInserted;
    private ConcurrentDictionary<int, double> requestUnitsConsumed = new ConcurrentDictionary<int, double>();

    private static List<Guid> potentialDeviceIds = Enumerable.Range(0, 200).Select(d => Guid.NewGuid()).ToList<Guid>();
    private static List<Guid> potentialLocationIds = Enumerable.Range(0, 10).Select(d => Guid.NewGuid()).ToList<Guid>();

    private Bogus.Faker<DeviceRecording> recordingGenerator = new Bogus.Faker<DeviceRecording>().Rules(
        (faker, recording) =>
        {
            recording.Version = 1.0d;
            recording.DeviceId = faker.PickRandom(potentialDeviceIds);
            recording.LocationId = faker.PickRandom(potentialLocationIds);
            recording.DeviceLocationComposite = $"{recording.DeviceId}_{recording.LocationId}";
            DateTime submitTime = DateTime.Now;
            recording.SubmitDay = submitTime.ToString("yyyy-MM-dd");
            recording.SubmitHour = submitTime.ToString("yyyy-MM-dd-HH");
            recording.SubmitMinute = submitTime.ToString("yyyy-MM-dd-HH-mm");
            recording.SubmitSecond = submitTime.ToString("yyyy-MM-dd-HH-mm-ss");
            recording.TemperatureCelsius = faker.Random.Double(20, 25);
            recording.Humidity = faker.Random.Double(0.1d, 0.25d);
        }
    );

    public async Task StartBenchmarkAsync(DocumentClient client, ConnectionPolicy policy, CosmosSettings settings, CollectionSettings collectionSetting)
    {
        await client.OpenAsync();

        Database database = await EnsureDatabaseResourceAsync(client, settings.Database);
        await Console.Out.WriteLineAsync($"Database Validated:\t{database.SelfLink}");

        DocumentCollection collection = await EnsureCollectionResourceAsync(client, database, collectionSetting);
        await Console.Out.WriteLineAsync($"Collection Validated:\t{collection.SelfLink}");

        int taskCount = settings.DegreeOfParallelism;
        if (settings.DegreeOfParallelism == -1)
        {
            // set TaskCount = 1 for each 1k RUs, minimum 1, maximum 250
            taskCount = Math.Max(collectionSetting.Throughput / 100, 1);
            taskCount = Math.Min(taskCount, 250);
        }

        await Console.Out.WriteLineAsync("Summary:");
        await Console.Out.WriteLineAsync("--------------------------------------------------------------------- ");
        await Console.Out.WriteLineAsync($"Endpoint:\t\t{settings.EndpointUri}");
        await Console.Out.WriteLineAsync($"Database\t\t{settings.Database}");
        await Console.Out.WriteLineAsync($"Collection\t\t{collectionSetting.Id}");
        await Console.Out.WriteLineAsync($"Partition Key:\t\t{String.Join(", ", collectionSetting.PartitionKeys)}");
        await Console.Out.WriteLineAsync($"Throughput:\t\t{collectionSetting.Throughput} Request Units per Second (RU/s)");
        await Console.Out.WriteLineAsync($"Insert Operation:\t{taskCount} Tasks Inserting {settings.NumberOfDocumentsToInsert} Documents Total");
        await Console.Out.WriteLineAsync("--------------------------------------------------------------------- ");
        await Console.Out.WriteLineAsync();

        await BenchmarkCollectionAsync(client, collection, settings.NumberOfDocumentsToInsert, taskCount);
    }

    public async Task BenchmarkCollectionAsync(DocumentClient client, DocumentCollection collection, int numberOfDocumentsToInsert, int taskCount)
    {           
        int minThreadPoolSize = 100;
        ThreadPool.SetMinThreads(minThreadPoolSize, minThreadPoolSize);

        await Console.Out.WriteLineAsync($"Starting Inserts with {taskCount} tasks");
        
        pendingTaskCount = taskCount;
        List<Task> tasks = new List<Task>();
        tasks.Add(
            this.LogOutputStatsAsync()
        );

        for (int i = 0; i < taskCount; i++)
        {
            tasks.Add(
                InsertDocumentAsync(i, client, collection, numberOfDocumentsToInsert / taskCount)
            );
        }

        await Task.WhenAll(tasks);
    }

    public async Task InsertDocumentAsync(int taskId, DocumentClient client, DocumentCollection collection, int numberOfDocumentsToInsert)
    {            
        requestUnitsConsumed[taskId] = 0;
        string partitionKeyProperty = collection.PartitionKey.Paths[0].Replace("/", "");

        for (int i = 0; i < numberOfDocumentsToInsert; i++)
        {
            DeviceRecording document = recordingGenerator.Generate();
            try
            {
                ResourceResponse<Document> response = await client.CreateDocumentAsync(collection.SelfLink, document);

                string partition = response.SessionToken.Split(':')[0];
                requestUnitsConsumed[taskId] += response.RequestCharge;
                Interlocked.Increment(ref this.documentsInserted);
            }
            catch (Exception e)
            {
                if (e is DocumentClientException)
                {
                    DocumentClientException de = (DocumentClientException)e;
                    if (de.StatusCode != HttpStatusCode.Forbidden)
                    {
                        Trace.TraceError("Failed to write {0}. Exception was {1}", JsonConvert.SerializeObject(document), e);
                    }
                    else
                    {
                        Interlocked.Increment(ref this.documentsInserted);
                    }
                }
            }
        }

        Interlocked.Decrement(ref this.pendingTaskCount);
    }

    public async Task LogOutputStatsAsync()
    {
        long lastCount = 0;
        double lastRequestUnits = 0;
        double lastSeconds = 0;
        double requestUnits = 0;
        double ruPerSecond = 0;
        double ruPerMonth = 0;

        Stopwatch watch = new Stopwatch();
        watch.Start();

        while (this.pendingTaskCount > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            double seconds = watch.Elapsed.TotalSeconds;

            requestUnits = 0;
            foreach (int taskId in requestUnitsConsumed.Keys)
            {
                requestUnits += requestUnitsConsumed[taskId];
            }

            long currentCount = this.documentsInserted;
            ruPerSecond = (requestUnits / seconds);
            ruPerMonth = ruPerSecond * 86400 * 30;

            await Console.Out.WriteLineAsync($"Inserted {currentCount} docs @ {Math.Round(this.documentsInserted / seconds)} writes/s, {Math.Round(ruPerSecond)} RU/s ({Math.Round(ruPerMonth / (1000 * 1000 * 1000))}B max monthly 1KB reads)");

            lastCount = documentsInserted;
            lastSeconds = seconds;
            lastRequestUnits = requestUnits;
        }

        double totalSeconds = watch.Elapsed.TotalSeconds;
        ruPerSecond = (requestUnits / totalSeconds);
        ruPerMonth = ruPerSecond * 86400 * 30;

        await Console.Out.WriteLineAsync();
        await Console.Out.WriteLineAsync("Summary:");
        await Console.Out.WriteLineAsync("--------------------------------------------------------------------- ");
        await Console.Out.WriteLineAsync($"Total Time Elapsed:\t{watch.Elapsed}");
        await Console.Out.WriteLineAsync($"Inserted {lastCount} docs @ {Math.Round(this.documentsInserted / watch.Elapsed.TotalSeconds)} writes/s, {Math.Round(ruPerSecond)} RU/s ({Math.Round(ruPerMonth / (1000 * 1000 * 1000))}B max monthly 1KB reads)");
        await Console.Out.WriteLineAsync("--------------------------------------------------------------------- ");
        await Console.Out.WriteLineAsync();
        await Console.Out.WriteLineAsync();
        await Console.Out.WriteLineAsync();
    }

    public async Task<Database> EnsureDatabaseResourceAsync(DocumentClient client, string databaseId)
    {
        Database database = new Database { Id = databaseId };
        database = await client.CreateDatabaseIfNotExistsAsync(database);

        return database;
    }

    public async Task<DocumentCollection> EnsureCollectionResourceAsync(DocumentClient client, Database database, CollectionSettings collectionSetting)
    {
        DocumentCollection collection = new DocumentCollection
        {
            Id = collectionSetting.Id,
            PartitionKey = new PartitionKeyDefinition
            {
                Paths = new Collection<string>(collectionSetting.PartitionKeys)
            } 
        };
        RequestOptions options = new RequestOptions
        {
            OfferThroughput = collectionSetting.Throughput
        };
        collection = await client.CreateDocumentCollectionIfNotExistsAsync(database.SelfLink, collection, options);

        return collection;
    }
}