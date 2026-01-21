using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AribEit
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override Guid Id => Guid.Parse("f8a4b2e1-7d3c-4a5b-9e8d-2c1a0b3f4e5d");
        public override string Name => "ARIB EIT Metadata Provider";

        public static Plugin? Instance { get; private set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "ARIB EIT Metadata",
                    EmbeddedResourcePath = string.Format("{0}.configPage.html", GetType().Namespace)
                }
            };
        }
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        public string ExternalCommandPath { get; set; } = string.Empty;
    }
}
