namespace EliteDangerous.Eddn;

/// <summary>
/// EDDN schema references and the set of journal events the network accepts on the journal schema.
/// Sending an event outside <see cref="JournalWhitelist"/> gets the whole message rejected, so the
/// transformer drops them before upload.
/// </summary>
public static class EddnSchemas
{
    private const string Base = "https://eddn.edcd.io/schemas";

    public const string Journal = Base + "/journal/1";
    public const string Commodity = Base + "/commodity/3";
    public const string Outfitting = Base + "/outfitting/2";
    public const string Shipyard = Base + "/shipyard/2";
    public const string FcMaterialsJournal = Base + "/fcmaterials_journal/1";
    public const string NavRoute = Base + "/navroute/1";

    /// <summary>Journal events permitted on the <see cref="Journal"/> schema.</summary>
    public static readonly IReadOnlySet<string> JournalWhitelist = new HashSet<string>(StringComparer.Ordinal)
    {
        "Docked", "FSDJump", "CarrierJump", "Location", "Scan", "SAASignalsFound",
        "FSSDiscoveryScan", "FSSSignalDiscovered", "FSSAllBodiesFound", "FSSBodySignals",
        "CodexEntry", "ApproachSettlement", "NavBeaconScan", "ScanBaryCentre",
    };
}
