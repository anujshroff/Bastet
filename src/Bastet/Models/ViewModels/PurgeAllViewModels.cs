namespace Bastet.Models.ViewModels;

/// <summary>
/// Confirmation model for purging the subnet archive. <see cref="MaxId"/> is posted back so the
/// purge destroys exactly the records the operator was shown a count of, and not whatever happens
/// to exist by the time they submit.
/// </summary>
public class PurgeAllDeletedSubnetsViewModel
{
    public int Count { get; set; }

    /// <summary>
    /// Highest archived record ID at the moment this page was rendered, and the upper bound of the
    /// purge. Rows archived after this point necessarily have a higher ID - production is SQL Server
    /// only, the column is IDENTITY, and DELETE (unlike TRUNCATE) never reseeds it.
    /// </summary>
    public int MaxId { get; set; }
}

/// <summary>Confirmation model for purging the host IP archive. <see cref="MaxId"/> as above.</summary>
public class PurgeAllDeletedHostIpsViewModel
{
    public int Count { get; set; }

    public int MaxId { get; set; }
}
