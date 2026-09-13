using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Abstractions.Tests;

/// <summary>
/// A trivial plugin implementation that compiles against the SDK contract assembly alone.
/// Its existence (and this project referencing nothing but
/// <c>EDNexus.Plugins.Abstractions</c>) is the acceptance-criteria proof that the SDK has no
/// dependency on EDNexus.Core.
/// </summary>
internal sealed class SamplePlugin : IEDNexusPlugin
{
    public bool Initialized { get; private set; }
    public bool ShutDown { get; private set; }
    public IPluginContext? LastContext { get; private set; }

    public void Initialize(IPluginContext context)
    {
        LastContext = context;
        Initialized = true;

        context.Events.Subscribe("FSDJump", e => context.Log.Info($"Jumped, historical={e.IsHistorical}"));
        context.Events.SubscribeAny(_ => { });
        context.Storage.SetString("last-run", context.State.LastUpdated.ToString("O"));
    }

    public void Shutdown() => ShutDown = true;
}
