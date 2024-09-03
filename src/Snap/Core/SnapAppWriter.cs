using System;
using System.IO;
using System.Text;
using JetBrains.Annotations;
using MessagePack;
using Snap.Core.Models;
using Snap.Core.Yaml.Emitters;
using Snap.Core.Yaml.TypeConverters;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;
using YamlDotNet.Serialization.NamingConventions;

namespace Snap.Core;

internal interface ISnapAppWriter
{
    MemoryStream BuildSnapApp(SnapApp snapsApp);
    string ToSnapAppYamlString(SnapApp snapApp);
    string ToSnapAppsYamlString(SnapApps snapApps);
    byte[] ToSnapAppsReleases(SnapAppsReleases snapAppsApps);
}

internal sealed class SnapAppWriter : ISnapAppWriter
{      
    static readonly ISerializer YamlSerializerSnapApp = Build(new SerializerBuilder()
        .WithEventEmitter(eventEmitter => new AbstractClassTagEventEmitter(eventEmitter,
            SnapAppReader.AbstractClassTypeMappingsSnapApp)
        )
    );

    static readonly ISerializer YamlSerializerSnapApps = Build(new SerializerBuilder()
        .WithEventEmitter(eventEmitter => new AbstractClassTagEventEmitter(eventEmitter,
            SnapAppReader.AbstractClassTypeMappingsSnapApps)
        )
    );

    static ISerializer Build(SerializerBuilder serializerBuilder)
    {
        return serializerBuilder
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new OsPlatformYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .WithTypeConverter(new DateTimeConverter(DateTimeKind.Utc))     
            .DisableAliases()
            .Build();
    }

    public MemoryStream BuildSnapApp([NotNull] SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

        snapApp = new SnapApp(snapApp);

        foreach (var channel in snapApp.Channels)
        {
            if (channel.PushFeed == null)
            {
                throw new Exception($"{nameof(channel.PushFeed)} cannot be null. Channel: {channel.Name}. Application id: {snapApp.Id}");
            }

            if (channel.UpdateFeed == null)
            {
                throw new Exception($"{nameof(channel.UpdateFeed)} cannot be null. Channel: {channel.Name}. Application id: {snapApp.Id}");
            }

            channel.PushFeed.ApiKey = null;
            channel.PushFeed.Username = null;
            channel.PushFeed.Password = null;

            if (channel.UpdateFeed.Source == null)
            {
                throw new Exception(
                    $"Update feed {nameof(channel.UpdateFeed.Source)} cannot be null. Channel: {channel.Name}. Application id: {snapApp.Id}");
            }
            
            if (channel.UpdateFeed is SnapNugetFeed updateFeed)
            {
                updateFeed.ApiKey = null;
                
                // Prevent publishing nuget.org credentials.
                if (updateFeed.Source.Host.IndexOf("nuget.org", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    updateFeed.Username = null;
                    updateFeed.Password = null;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(updateFeed.Username))
                    {
                        updateFeed.Username = null;
                    } 
                    
                    if (string.IsNullOrWhiteSpace(updateFeed.Password))
                    {
                        updateFeed.Password = null;
                    }
                }
            }
        }
                                                         
        var yaml = ToSnapAppYamlString(snapApp);
        var memoryStream = new MemoryStream();
        memoryStream.Write(Encoding.UTF8.GetBytes(yaml));
        memoryStream.Seek(0, SeekOrigin.Begin);
        return memoryStream;
    }

    public string ToSnapAppYamlString([NotNull] SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return YamlSerializerSnapApp.Serialize(snapApp);
    }

    public string ToSnapAppsYamlString([NotNull] SnapApps snapApps)
    {
        if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));
        return YamlSerializerSnapApps.Serialize(snapApps);
    }

    public byte[] ToSnapAppsReleases([NotNull] SnapAppsReleases snapAppsApps)
    {
        if (snapAppsApps == null) throw new ArgumentNullException(nameof(snapAppsApps));
        return MessagePackSerializer.Serialize(snapAppsApps, MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray));
    }
}
