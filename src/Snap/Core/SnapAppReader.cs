using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using MessagePack;
using Snap.Core.Models;
using Snap.Core.Yaml.NodeTypeResolvers;
using Snap.Core.Yaml.TypeConverters;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.NodeTypeResolvers;

namespace Snap.Core;

internal interface ISnapAppReader
{
    SnapApps BuildSnapAppsFromYamlString(string yamlString);
    SnapApp BuildSnapAppFromStream(Stream stream);
    SnapApp BuildSnapAppFromYamlString(string yamlString);
    ValueTask<SnapAppsReleases> BuildSnapAppsReleasesFromStreamAsync(Stream stream);
}

internal sealed class SnapAppReader : ISnapAppReader
{
    internal static readonly Dictionary<string, Type> AbstractClassTypeMappingsSnapApp = new()
    {
        { "nuget", typeof(SnapNugetFeed) }, 
        { "http", typeof(SnapHttpFeed)}
    };

    internal static readonly Dictionary<string, Type> AbstractClassTypeMappingsSnapApps = new()
    {
        { "nuget", typeof(SnapsNugetFeed) },
        { "http", typeof(SnapsHttpFeed)}
    };

    static readonly IDeserializer DeserializerSnapApp = Build(new DeserializerBuilder()
        .WithNodeTypeResolver(new AbstractClassTypeResolver(AbstractClassTypeMappingsSnapApp),
            selector => selector.After<TagNodeTypeResolver>()
        )
    );

    static readonly IDeserializer DeserializerSnapApps = Build(new DeserializerBuilder()
        .WithNodeTypeResolver(new AbstractClassTypeResolver(AbstractClassTypeMappingsSnapApps),
            selector => selector.After<TagNodeTypeResolver>()
        )
    );
 
    static IDeserializer Build(DeserializerBuilder builder)
    {
        return builder
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .WithTypeConverter(new OsPlatformYamlTypeConverter())
            .WithTypeConverter(new DateTimeConverter(DateTimeKind.Utc))     
            .Build();
    }

    public SnapApp BuildSnapAppFromStream([NotNull] Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var textReader = new StreamReader(stream, Encoding.UTF8);
        return BuildSnapAppFromYamlString(textReader.ReadToEnd());
    }

    public SnapApp BuildSnapAppFromYamlString(string yamlString)
    {
        if (string.IsNullOrWhiteSpace(yamlString)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(yamlString));
        return DeserializerSnapApp.Deserialize<SnapApp>(yamlString);
    }

    public ValueTask<SnapAppsReleases> BuildSnapAppsReleasesFromStreamAsync([NotNull] Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        return MessagePackSerializer.DeserializeAsync<SnapAppsReleases>(stream, MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray));
    }

    public SnapApps BuildSnapAppsFromYamlString([NotNull] string yamlString)
    {
        if (string.IsNullOrWhiteSpace(yamlString)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(yamlString));
        return DeserializerSnapApps.Deserialize<SnapApps>(yamlString);
    }
}
