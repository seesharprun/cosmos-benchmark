using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Configuration;

public class Program
{
    public static async Task Main(string[] args)
    {
        IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile($"appsettings.json");

        IConfigurationRoot configuration = builder.Build();

        ConnectionPolicy policy = new ConnectionPolicy();
        CosmosSettings settings = new CosmosSettings(); 
        List<CollectionSettings> collectionSettings = new List<CollectionSettings>();

        configuration.GetSection(nameof(ConnectionPolicy)).Bind(policy);
        configuration.GetSection(nameof(CosmosSettings)).Bind(settings);
        configuration.GetSection(nameof(CollectionSettings)).Bind(collectionSettings);

        await Console.Out.WriteLineAsync("DocumentDBBenchmark starting...");
        try
        {
            using (DocumentClient client = new DocumentClient(settings.EndpointUri, settings.PrimaryKey, policy))
            {
                foreach (CollectionSettings collectionSetting in collectionSettings)
                {
                    Benchmark benchmark = new Benchmark();
                    await benchmark.StartBenchmarkAsync(client, policy, settings, collectionSetting);
                }
            }
        }
        catch (Exception e)
        {
            await Console.Out.WriteLineAsync($"Samples failed with exception:\t{e}");
        }
        finally
        {
            await Console.Out.WriteLineAsync("DocumentDBBenchmark completed successfully.");
            await Console.Out.WriteLineAsync("Press ENTER key to exit...");
            await Console.In.ReadLineAsync();
        }
    }
}