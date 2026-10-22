using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.NumberCover.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    // Legacy serialized name; this is the master switch for all current online sources.
    public bool EnableJableSource { get; set; } = true;

    public bool OnlyFillMissing { get; set; } = false;

    public int MaxRequestsPerMinute { get; set; } = 12;

    public int TimeoutSeconds { get; set; } = 10;
}
