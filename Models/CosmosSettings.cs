using System;

public class CosmosSettings
{
    public Uri EndpointUri { get; set; }

    public string PrimaryKey { get; set; }

    public string Database { get; set; }

    public int DegreeOfParallelism { get; set; }

    public int NumberOfDocumentsToInsert { get; set; }
}