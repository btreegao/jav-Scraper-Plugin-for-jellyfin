using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.NumberCover.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool EnableJableSource { get; set; } = false;

    public bool OnlyFillMissing { get; set; } = true;

    public int MaxRequestsPerMinute { get; set; } = 12;

    public int TimeoutSeconds { get; set; } = 10;
}
