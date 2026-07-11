using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>Material stocks, split into the three engineering categories.</summary>
public sealed partial class MaterialsCardViewModel : CardViewModel
{
    public MaterialsCardViewModel(DashboardContext context) : base(context, "materials", "MATERIALS", 452) { }

    [ObservableProperty] private string _rawMaterials = "0";
    [ObservableProperty] private string _manufacturedMaterials = "0";
    [ObservableProperty] private string _encodedMaterials = "0";
    [ObservableProperty] private string _materialsSummary = "—";

    public override void Update(CommanderState s)
    {
        var m = s.Materials;
        RawMaterials = m.Raw.Values.Sum().ToString("N0");
        ManufacturedMaterials = m.Manufactured.Values.Sum().ToString("N0");
        EncodedMaterials = m.Encoded.Values.Sum().ToString("N0");
        MaterialsSummary = $"{m.TotalCount:N0} total";
    }
}
