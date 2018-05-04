using System.Collections.Generic;

public class CollectionSettings
{
    public string Id { get; set; }    

    public List<string> PartitionKeys { get; set; }

    public int Throughput { get; set; }
}