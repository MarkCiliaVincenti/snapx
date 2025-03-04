using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using Snap.Core;
using Snap.Core.Models;
using Snap.NuGet;

namespace Snap.Extensions;

internal static class SnapExtensions
{
    static readonly Regex AppIdRegex = new(@"^\w+([._-]\w+)*$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
    static readonly Regex ChannelNameRegex = new(@"^[a-zA-Z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
    static readonly Regex NetAppRegex = new("^(netcoreapp|net)\\d{1}.\\d{1}$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
    static readonly Regex ExpansionRegex = new("((\\$[0-9A-Za-z\\\\_]*)\\$)", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled);

    public static (FileStream fileStream, string filename) GetStubExeStream([NotNull] this SnapApp snapApp, [NotNull] ISnapFilesystem snapFilesystem, [NotNull] string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(snapApp);
        ArgumentNullException.ThrowIfNull(snapFilesystem);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        var nativeDirectory = snapFilesystem.PathCombine(workingDirectory, "runtimes", snapApp.Target.Rid, "native");
        snapFilesystem.DirectoryExistsThrowIfNotExists(nativeDirectory);

        string stubExeRealFilename;
        if (snapApp.Target.Os == OSPlatform.Windows)
        {
            stubExeRealFilename = $"SnapxStub-{snapApp.Target.Rid}.exe";
        } else if (snapApp.Target.Os == OSPlatform.Linux)
        {
            stubExeRealFilename = $"SnapxStub-{snapApp.Target.Rid}.bin";
        }
        else
        {
            throw new PlatformNotSupportedException();
        }
        
        var fileStream = File.OpenRead(snapFilesystem.PathCombine(nativeDirectory, stubExeRealFilename));
        return (fileStream, snapApp.GetStubExeFilename());
    }
    
    public static FileStream GetWarpPackerExeStream([NotNull] this string hostRid, [NotNull] ISnapFilesystem snapFilesystem, [NotNull] string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(hostRid);
        ArgumentNullException.ThrowIfNull(snapFilesystem);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        var nativeDirectory = snapFilesystem.PathCombine(workingDirectory, "runtimes", hostRid, "native");
        snapFilesystem.DirectoryExistsThrowIfNotExists(nativeDirectory);

        var warpPackerExeFilename = hostRid.GetWarpPackerFilename();
        return File.OpenRead(snapFilesystem.PathCombine(nativeDirectory, warpPackerExeFilename));
    }
    
    public static FileStream GetInstallerZipStream([NotNull] this SnapApp snapApp, [NotNull] ISnapFilesystem snapFilesystem, [NotNull] string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(snapApp);
        ArgumentNullException.ThrowIfNull(snapFilesystem);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        var nativeDirectory = snapFilesystem.PathCombine(workingDirectory, "runtimes", snapApp.Target.Rid, "native");
        snapFilesystem.DirectoryExistsThrowIfNotExists(nativeDirectory);

        var installerZipFilename = $"SnapxInstaller-{snapApp.Target.Rid}.zip";
        return File.OpenRead(snapFilesystem.PathCombine(nativeDirectory, installerZipFilename));
    }
    
    public static (FileStream fileStream, string filename) GetLibPalStream([NotNull] this SnapApp snapApp, [NotNull] ISnapFilesystem snapFilesystem, [NotNull] string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(snapApp);
        ArgumentNullException.ThrowIfNull(snapFilesystem);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        var nativeDirectory = snapFilesystem.PathCombine(workingDirectory, "runtimes", snapApp.Target.Rid, "native");
        snapFilesystem.DirectoryExistsThrowIfNotExists(nativeDirectory);

        var libPalFilename = snapApp.GetLibPalFilename();
        var fileStream = File.OpenRead(snapFilesystem.PathCombine(nativeDirectory, libPalFilename));
        return (fileStream, libPalFilename);
    }
    
    public static (FileStream fileStream, string filename) GetLibBsdiffStream([NotNull] this SnapApp snapApp, [NotNull] ISnapFilesystem snapFilesystem, [NotNull] string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(snapApp);
        ArgumentNullException.ThrowIfNull(snapFilesystem);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        var nativeDirectory = snapFilesystem.PathCombine(workingDirectory, "runtimes", snapApp.Target.Rid, "native");
        snapFilesystem.DirectoryExistsThrowIfNotExists(nativeDirectory);

        var libBsdiffFilename = snapApp.GetLibBsdiffFilename();
        var fileStream = File.OpenRead(snapFilesystem.PathCombine(nativeDirectory, libBsdiffFilename));
        return (fileStream, libBsdiffFilename);
    }
    
    internal static string GetStubExeFilename(this SnapApp snapApp)
    {
        ArgumentNullException.ThrowIfNull(snapApp);
        
        var assemblyName = snapApp.MainExe ?? snapApp.Id;
        
        if (snapApp.Target.Os == OSPlatform.Windows)
        {
            return $"{assemblyName}.exe";
        }

        if (snapApp.Target.Os == OSPlatform.Linux)
        {
            return $"{assemblyName}";
        }

        throw new PlatformNotSupportedException();
    }

    internal static string GetWarpPackerFilename(this string rid)
    {
        switch (rid)
        {
            case "win-x86":
            case "win-x64":
                return $"SnapxWarpPacker-{rid}.exe";
            case "linux-x64":
            case "linux-arm64":
                return $"SnapxWarpPacker-{rid}.bin";
            default:
                throw new NotSupportedException($"Unsupported warp packer rid: {rid}.");
        }
    }
    
    internal static string GetLibPalFilename(this SnapApp snapApp)
    {
        ArgumentNullException.ThrowIfNull(snapApp);

        if (snapApp.Target.Os == OSPlatform.Windows)
        {
            return $"SnapxLibPal-{snapApp.Target.Rid}.dll";
        }

        if (snapApp.Target.Os == OSPlatform.Linux)
        {
            return $"SnapxLibPal-{snapApp.Target.Rid}.so";
        }

        throw new PlatformNotSupportedException();
    }
    
    internal static string GetLibBsdiffFilename(this SnapApp snapApp)
    {
        ArgumentNullException.ThrowIfNull(snapApp);

        if (snapApp.Target.Os == OSPlatform.Windows)
        {
            return $"SnapxLibBsdiff-{snapApp.Target.Rid}.dll";
        }

        if (snapApp.Target.Os == OSPlatform.Linux)
        {
            return $"SnapxLibBsdiff-{snapApp.Target.Rid}.so";
        }

        throw new PlatformNotSupportedException();
    }
    
    internal static string BuildRid(this OSPlatform osPlatform)
    {
#if SNAPX_ALLOW_RID_OVERRIDE
            var supportedRids = new List<string>
            {
                "win-x86",
                "win-x64",
                "linux-x64",
                "linux-arm64"
            };

            var ridOverride = Environment.GetEnvironmentVariable("SNAPX_RID_OVERRIDE")?.ToLowerInvariant();
            if (ridOverride != null)
            {
                if (!supportedRids.Contains(ridOverride))
                {
                    throw new PlatformNotSupportedException(ridOverride);
                }
                return ridOverride;
            }
#endif

        if (osPlatform == OSPlatform.Windows)
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "win-x86" : "win-x64";
        }

        if (osPlatform == OSPlatform.Linux)
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "linux-x64" : "linux-arm64";
        }

        throw new PlatformNotSupportedException();
    }

    internal static void SetRidUsingCurrentOsPlatformAndProcessArchitecture([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            snapApp.Target.Os = OSPlatform.Windows;
            snapApp.Target.Rid = snapApp.Target.Os.BuildRid();
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            snapApp.Target.Os = OSPlatform.Linux;
            snapApp.Target.Rid = snapApp.Target.Os.BuildRid();
            return;
        }

        throw new PlatformNotSupportedException();
    }

    internal static string ExpandProperties([NotNull] this string value, [NotNull] Dictionary<string, string> properties)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (properties == null) throw new ArgumentNullException(nameof(properties));
        if (properties.Count == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(properties));

        // ReSharper disable once RedundantEnumerableCastCall
        foreach (var match in ExpansionRegex.Matches(value).Cast<Match>())
        {
            var key = match.Value.Replace("$", string.Empty);

            if (!properties.ContainsKey(key))
            {
                throw new Exception($"Failed to expand key: {key}.");
            }

            value = value.Replace(match.Value, properties[key]);
        }

        return value;
    }

    internal static bool IsNetAppSafe(this string framework)
    {
        return framework != null && NetAppRegex.IsMatch(framework);
    }

    internal static bool IsNetFrameworkValidSafe(this string framework)
    {
        return framework.IsNetAppSafe();
    }

    internal static bool IsRuntimeIdentifierValidSafe(this string runtimeIdentifier)
    {
        return runtimeIdentifier != null && (
            runtimeIdentifier == "win-x86" 
            || runtimeIdentifier == "win-x64" 
            || runtimeIdentifier == "linux-x64"
            || runtimeIdentifier == "linux-arm64");
    }

    internal static SnapChannel GetDefaultChannelOrThrow([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        var channel = snapApp.Channels.FirstOrDefault();
        if (channel == null)
        {
            throw new Exception($"Default channel not found. Snap id: {snapApp.Id}");
        }
        return channel;
    }
        
    internal static SnapChannel GetCurrentChannelOrThrow([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        var channel = snapApp.Channels.SingleOrDefault(x => x.Current);
        if (channel == null)
        {
            throw new Exception($"Current channel not found. Snap id: {snapApp.Id}");
        }
        return channel;
    }
        
    internal static SnapChannel GetNextChannel([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

        var channelsCount = snapApp.Channels.Count;
            
        for (var index = 0; index < channelsCount; index++)
        {
            var currentSnapChannel = snapApp.Channels[index];
            var nextSnapChannel = index + 1 < channelsCount ? snapApp.Channels[index + 1] : null;
            if (currentSnapChannel.Current && nextSnapChannel != null)
            {
                return nextSnapChannel;
            }
        }

        return null;
    }
        
    internal static bool IsValidAppId([NotNull] this string value)
    {
        return AppIdRegex.IsMatch(value);
    }
        
    internal static bool IsValidChannelName([NotNull] this string value)
    {
        return ChannelNameRegex.IsMatch(value);
    }
        
    internal static bool IsValidAppId([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return snapApp.Id.IsValidAppId();
    }
        
    internal static bool IsValidChannelName([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        var channel = snapApp.GetCurrentChannelOrThrow();
        return channel.Name.IsValidChannelName();
    }
        
    internal static string BuildNugetUpstreamId([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return snapApp.IsFull ? snapApp.BuildNugetFullUpstreamId() : snapApp.BuildNugetDeltaUpstreamId();
    }
                
    internal static string BuildNugetFullUpstreamId([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return $"{snapApp.Id}_full_{snapApp.Target.Rid}_snapx".ToLowerInvariant();
    }
        
    internal static string BuildNugetDeltaUpstreamId([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return $"{snapApp.Id}_delta_{snapApp.Target.Rid}_snapx".ToLowerInvariant();
    }

    internal static string BuildNugetUpstreamId([NotNull] this SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        return snapRelease.IsFull ? snapRelease.BuildNugetFullUpstreamId() : snapRelease.BuildNugetDeltaUpstreamId();
    }

    internal static string BuildNugetFullUpstreamId([NotNull] this SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        return $"{snapRelease.Id}_full_{snapRelease.Target.Rid}_snapx".ToLowerInvariant();
    }
        
    internal static string BuildNugetDeltaUpstreamId([NotNull] this SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        return $"{snapRelease.Id}_delta_{snapRelease.Target.Rid}_snapx".ToLowerInvariant();
    }

    internal static string BuildNugetFilename([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return snapApp.IsFull ? snapApp.BuildNugetFullFilename() : snapApp.BuildNugetDeltaFilename();
    }
        
    internal static string BuildNugetFullFilename([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return $"{snapApp.Id}_full_{snapApp.Target.Rid}_snapx.{snapApp.Version.ToNuGetVersion()}.nupkg".ToLowerInvariant();
    }

    internal static string BuildNugetDeltaFilename([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return $"{snapApp.Id}_delta_{snapApp.Target.Rid}_snapx.{snapApp.Version.ToNuGetVersion()}.nupkg".ToLowerInvariant();
    }
        
    internal static string BuildNugetFilename([NotNull] this SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        return snapRelease.IsFull ? snapRelease.BuildNugetFullFilename() : snapRelease.BuildNugetDeltaFilename();
    }
        
    internal static string BuildNugetFullFilename([NotNull] this SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        return $"{snapRelease.Id}_full_{snapRelease.Target.Rid}_snapx.{snapRelease.Version.ToNuGetVersion()}.nupkg".ToLowerInvariant();
    }

    internal static string BuildNugetDeltaFilename([NotNull] this SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        return $"{snapRelease.Id}_delta_{snapRelease.Target.Rid}_snapx.{snapRelease.Version.ToNuGetVersion()}.nupkg".ToLowerInvariant();
    }

    internal static string BuildNugetReleasesUpstreamId([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return $"{snapApp.Id.ToLowerInvariant()}_snapx";
    }

    internal static PackageIdentity BuildNugetReleasesUpstreamPackageIdentityId([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return new PackageIdentity(snapApp.BuildNugetReleasesUpstreamId(), snapApp.Version.ToNuGetVersion());
    }

    internal static string BuildNugetReleasesUpstreamId([NotNull] this SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        return $"{snapRelease.Id.ToLowerInvariant()}_snapx";
    }
        
    internal static string BuildNugetReleasesFilename([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return $"{snapApp.BuildNugetReleasesUpstreamId()}.nupkg".ToLowerInvariant();
    }

    public static SnapApp AsFullSnapApp([NotNull] this SnapApp snapApp, bool isGenesis)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

        var fullSnapApp = new SnapApp(snapApp)
        {
            IsFull = true,
            IsGenesis = isGenesis
        };
            
        return fullSnapApp;
    }
        
    public static SnapApp AsDeltaSnapApp([NotNull] this SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

        var fullSnapApp = new SnapApp(snapApp)
        {
            IsFull = false,
            IsGenesis = false
        };

        return fullSnapApp;
    }

    public static SnapRelease AsFullRelease([NotNull] this SnapRelease snapRelease, bool isGenesis)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            
        var fullSnapRelease = new SnapRelease(snapRelease)
        {
            Filename = snapRelease.BuildNugetFullFilename(),
            UpstreamId = snapRelease.BuildNugetFullUpstreamId(),
            IsGenesis = isGenesis,
            IsFull = true,
            Gc = isGenesis && snapRelease.Gc
        };

        fullSnapRelease.Files.Clear();
        fullSnapRelease.New.Clear();
        fullSnapRelease.Modified.Clear();
        fullSnapRelease.Unmodified.Clear();
        fullSnapRelease.Deleted.Clear();
            
        fullSnapRelease.Files.AddRange(snapRelease.Files.Select(x => new SnapReleaseChecksum(x)));

        return fullSnapRelease;
    }

    public static SnapRelease AsDeltaRelease([NotNull] this SnapRelease snapRelease)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));

        var deltaSnapRelease = new SnapRelease(snapRelease).AsFullRelease(false);
        deltaSnapRelease.IsFull = false;
        deltaSnapRelease.Filename = deltaSnapRelease.BuildNugetDeltaFilename();
        deltaSnapRelease.UpstreamId = deltaSnapRelease.BuildNugetDeltaUpstreamId();
            
        return deltaSnapRelease; 
    }

    internal static PackageSource BuildPackageSource([NotNull] this SnapNugetFeed snapFeed, [NotNull] ISettings settings)
    {
        if (snapFeed == null) throw new ArgumentNullException(nameof(snapFeed));
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        var packageSource = new PackageSource(snapFeed.Source.ToString(), snapFeed.Name, true, true, false);

        var storePasswordInClearText = !settings.IsPasswordEncryptionSupported();
            
        if (snapFeed.Username != null && snapFeed.Password != null)
        {
            snapFeed.Password = storePasswordInClearText ? snapFeed.Password : EncryptionUtility.EncryptString(snapFeed.Password);

            // Comma-delimited list of authentication types the credential is valid for as stored in the config file.
            // If null or empty, all authentication types are valid. Example: 'basic,negotiate'
            string validAuthenticationTypesText = null;
                
            packageSource.Credentials = new PackageSourceCredential(packageSource.Name,
                // ReSharper disable once ExpressionIsAlwaysNull
                snapFeed.Username, snapFeed.Password, storePasswordInClearText, validAuthenticationTypesText);

            settings.AddOrUpdate(ConfigurationConstants.CredentialsSectionName, packageSource.Credentials.AsCredentialsItem());
        }

        if (snapFeed.ApiKey != null)
        {
            if (storePasswordInClearText)
            {
                settings.AddOrUpdate(ConfigurationConstants.ApiKeys, new AddItem(packageSource.Source, snapFeed.ApiKey));
            }
            else
            {
                SettingsUtility.SetEncryptedValueForAddItem(settings, ConfigurationConstants.ApiKeys, packageSource.Source, snapFeed.ApiKey);                    
            }
        }

        packageSource.ProtocolVersion = (int)snapFeed.ProtocolVersion;

        return packageSource;
    }

    internal static string GetDecryptedValue([NotNull] this PackageSource packageSource, [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] string sectionName)
    {
        if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
        if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
        if (sectionName == null) throw new ArgumentNullException(nameof(sectionName));

        var nuGetSupportsEncryption = nuGetPackageSources.IsPasswordEncryptionSupported();
            
        var decryptedValue = nuGetSupportsEncryption ? 
            SettingsUtility.GetDecryptedValueForAddItem(nuGetPackageSources.Settings, sectionName, packageSource.Source) : 
            SettingsUtility.GetValueForAddItem(nuGetPackageSources.Settings, sectionName, packageSource.Source);
            
        return string.IsNullOrWhiteSpace(decryptedValue) ? null : decryptedValue;
    }

    internal static SnapNugetFeed BuildSnapNugetFeed([NotNull] this PackageSource packageSource, [NotNull] INuGetPackageSources nuGetPackageSources)
    {
        if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
        if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));

        var apiKey = packageSource.GetDecryptedValue(nuGetPackageSources, ConfigurationConstants.ApiKeys);
            
        var snapFeed = new SnapNugetFeed
        {
            Name = packageSource.Name,
            Source = packageSource.SourceUri,
            ProtocolVersion = (NuGetProtocolVersion)packageSource.ProtocolVersion,
            Username = packageSource.Credentials?.Username,
            Password = packageSource.Credentials?.Password,
            ApiKey = apiKey
        };

        return snapFeed;
    }

    internal static IEnumerable<SnapApp> BuildSnapApps([NotNull] this SnapApps snapApps,
        [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] ISnapFilesystem snapFilesystem,
        bool requireUpdateFeed = true, bool requirePushFeed = true)
    {
        return snapApps.Apps.Select(snapsApp => snapApps.BuildSnapApp(snapsApp.Id, snapsApp.Target.Rid, 
            nuGetPackageSources, snapFilesystem, requireUpdateFeed, requirePushFeed));
    }

    internal static SnapApp BuildSnapApp([NotNull] this SnapApps snapApps, string id, [NotNull] string rid,
        [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] ISnapFilesystem snapFilesystem,
        bool requireUpdateFeed = true, bool requirePushFeed = true)
    {
        if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));
        if (rid == null) throw new ArgumentNullException(nameof(rid));
        if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
        if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));

        var snapApp = snapApps.Apps.SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (snapApp == null)
        {
            throw new Exception($"Unable to find snap with id: {id}");
        }

        if (!Guid.TryParse(snapApp.SuperVisorId, out var superVisorId))
        {
            throw new Exception("Unable to parse supervisor id. Please use a unique guid to identify your application.");
        }

        if (superVisorId == Guid.Empty)
        {
            throw new Exception("Supervisor id cannot be an empty guid.");
        }

        if (snapApp.MainExe != null && string.IsNullOrWhiteSpace(snapApp.MainExe))
        {
            throw new Exception($"{nameof(snapApp.MainExe)} property cannot be null or whitespace.");
        }

        if (snapApp.InstallDirectoryName != null && string.IsNullOrWhiteSpace(snapApp.InstallDirectoryName))
        {
            throw new Exception($"{nameof(snapApp.InstallDirectoryName)} property cannot be null or whitespace.");
        }

        snapApp.Target.Installers = snapApp.Target.Installers.Distinct().ToList();
        snapApp.Target.Rid = snapApp.Target.Rid?.ToLowerInvariant();

        if (!snapApp.Target.Rid.IsRuntimeIdentifierValidSafe())
        {
            throw new Exception($"Unsupported rid: {rid}. Snap id: {snapApp.Id}");
        }

        if (!snapApp.Target.Framework.IsNetFrameworkValidSafe())
        {
            throw new Exception($"Unknown .NET framework: {snapApp.Target.Framework}");
        }
            
        var snapAppTargetUniqueShortcuts = snapApp.Target.Shortcuts.Select(x => x).ToList();
        if (snapAppTargetUniqueShortcuts.Distinct().Count() != snapApp.Target.Shortcuts.Count)
        {
            throw new Exception($"Target shortcut locations must be unique: {string.Join(", ", snapAppTargetUniqueShortcuts)}. Snap id: {snapApp.Id}");
        }
            
        var snapAppTargetUniqueInstallers = snapApp.Target.Installers.Select(x => x).ToList();
        if (snapAppTargetUniqueInstallers.Distinct().Count() != snapApp.Target.Installers.Count)
        {
            throw new Exception($"Target installer types must be unique: {string.Join(", ", snapAppTargetUniqueInstallers)}. Snap id: {snapApp.Id}");
        }
            
        if (snapApp.Target.Icon != null)
        {
            snapApp.Target.Icon = snapFilesystem.PathGetFullPath(snapApp.Target.Icon);

            if (!snapFilesystem.FileExists(snapApp.Target.Icon))
            {                    
                throw new Exception($"Unable to find icon: {snapApp.Target.Icon}.");
            }
        }

        var snapAppUniqueChannels = snapApp.Channels.Distinct().ToList();
        if (snapAppUniqueChannels.Count != snapApp.Channels.Count)
        {
            throw new Exception($"Channel list must be unique: {string.Join(",", snapApp.Channels)}. Snap id: {snapApp.Id}");
        }

        var snapAppsDefaultChannel = snapApps.Channels.First();
        var snapAppDefaultChannel = snapApp.Channels.First();

        if (!string.Equals(snapAppsDefaultChannel.Name, snapAppDefaultChannel, StringComparison.Ordinal))
        {
            throw new Exception($"Default channel must be {snapAppsDefaultChannel.Name}. Snap id: {snapApp.Id}");
        }

        var snapAppAvailableChannels = snapApps.Channels.Where(rhs => snapApp.Channels.Any(lhs => lhs.Equals(rhs.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        if (!snapAppAvailableChannels.Any())
        {
            throw new Exception($"Could not find any global channels. Channel list: {string.Join(",", snapAppUniqueChannels)}. Snap id: {snapApp.Id}");
        }
                      
        var snapFeeds = new List<SnapFeed>();
        snapFeeds.AddRange(nuGetPackageSources.BuildSnapFeeds());
        snapFeeds.AddRange(snapApps.Channels.Select(x => x.UpdateFeed).OfType<SnapsHttpFeed>().DistinctBy(x => x.Source).Select(x => new SnapHttpFeed(x)));

        var snapNugetFeeds = snapFeeds.Where(x => x is SnapNugetFeed).Cast<SnapNugetFeed>().ToList();
            
        var snapHttpFeeds = snapFeeds.Where(x => x is SnapHttpFeed).Cast<SnapHttpFeed>().ToList();
        var snapAppChannels = new List<SnapChannel>();

        for (var i = 0; i < snapAppAvailableChannels.Count; i++)
        {
            var snapsChannel = snapAppAvailableChannels[i];
            var pushFeed = snapNugetFeeds.SingleOrDefault(x => x.Name == snapsChannel.PushFeed.Name);

            SnapFeed updateFeed = snapsChannel.UpdateFeed switch
            {
                SnapsNugetFeed snapsNugetFeed => snapNugetFeeds.SingleOrDefault(x => x.Name == snapsNugetFeed.Name),
                SnapsHttpFeed snapsHttpFeed => snapHttpFeeds.SingleOrDefault(x => x.Source == snapsHttpFeed.Source),
                _ => null
            };

            if (requirePushFeed && pushFeed == null)
            {
                throw new Exception($"Unable to resolve push feed: {snapsChannel.PushFeed.Name}. Channel: {snapsChannel.Name}. Application id: {snapApp.Id}");
            }

            if (!requirePushFeed && pushFeed == null)
            {
                pushFeed = new SnapNugetFeed();
            }

            // Todo: Use actual feed name instead of type when throwing exception.
            if (requireUpdateFeed && updateFeed == null)
            {
                throw new Exception($"Unable to resolve update feed type: {snapsChannel.UpdateFeed?.GetType().Name}. Channel: {snapsChannel.Name}. Application id: {snapApp.Id}");
            }

            if (!requireUpdateFeed && updateFeed == null)
            {
                updateFeed = new SnapNugetFeed();
            }

            var currentChannel = i == 0; // Default snap channel is always the first one defined. 
            snapAppChannels.Add(new SnapChannel(snapsChannel.Name, currentChannel, pushFeed, updateFeed));
        }

        var snapChannelNugetFeedNames = snapAppChannels.Select(x => x.PushFeed.Name).Distinct().ToList();
        if (snapChannelNugetFeedNames.Count > 1)
        {
            throw new Exception($"Multiple nuget push feeds is not supported: {string.Join(",", snapChannelNugetFeedNames)}. Application id: {snapApp.Id}");
        }

        if (snapApp.Target.PersistentAssets.Any(x => x.StartsWith("app-", StringComparison.OrdinalIgnoreCase)))
        {
            throw new Exception("Fatal error! A persistent asset starting with 'app-' was detected in manifest. This is a reserved keyword.");
        }

        return new SnapApp
        {
            Id = snapApp.Id,
            InstallDirectoryName = snapApp.InstallDirectoryName,
            MainExe = snapApp.MainExe,
            SuperVisorId = superVisorId.ToString(),
            Channels = snapAppChannels,
            Target = new SnapTarget(snapApp.Target),
            Authors = snapApp.Nuspec.Authors,
            Description = snapApp.Nuspec.Description,
            ReleaseNotes = snapApp.Nuspec.ReleaseNotes,
            RepositoryUrl = snapApp.Nuspec.RepositoryUrl,
            RepositoryType = snapApp.Nuspec.RepositoryType
        };
    }

    internal static INuGetPackageSources BuildNugetSources([NotNull] this SnapApp snapApp, [NotNull] string tempDirectory)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        if (tempDirectory == null) throw new ArgumentNullException(nameof(tempDirectory));
        var inMemorySettings = new NugetInMemorySettings(tempDirectory);

        var nugetFeeds = snapApp.Channels
            .SelectMany(x => new List<SnapNugetFeed> { x.PushFeed, x.UpdateFeed as SnapNugetFeed })
            .Where(x => x?.Name != null && x.Source != null)
            .DistinctBy(x => x.Name)
            .Select(x => x.BuildPackageSource(inMemorySettings))
            .ToList();

        return new NuGetPackageSources(inMemorySettings, nugetFeeds);
    }
    
    internal static INuGetPackageSources BuildNugetSources([NotNull] this SnapNugetFeed nugetFeed, [NotNull] string tempDirectory)
    {
        if (nugetFeed == null) throw new ArgumentNullException(nameof(nugetFeed));
        if (tempDirectory == null) throw new ArgumentNullException(nameof(tempDirectory));
        var inMemorySettings = new NugetInMemorySettings(tempDirectory);
        var packageSource = nugetFeed.BuildPackageSource(inMemorySettings);
        return new NuGetPackageSources(inMemorySettings, [packageSource]);
    }

    internal static INuGetPackageSources BuildNugetSources([NotNull] this SnapApps snapApps, INuGetPackageSources nuGetPackageSources)
    {
        var allPackageSources = snapApps.Channels
            .SelectMany(x =>
            {
                var packageSources = new List<PackageSource>();

                var pushFeed = nuGetPackageSources.Items.SingleOrDefault(packageSource => packageSource.Name == x.PushFeed.Name);
                if (pushFeed != null)
                {
                    packageSources.Add(pushFeed);
                }

                if (x.UpdateFeed is SnapsNugetFeed snapsNugetFeed)
                {
                    var updateFeed = nuGetPackageSources.Items.SingleOrDefault(packageSource => packageSource.Name == snapsNugetFeed.Name);
                    if (updateFeed != null)
                    {
                        packageSources.Add(updateFeed);
                    }
                }


                return packageSources;
            })
            .DistinctBy(x => x.Name)
            .ToList();

        return !allPackageSources.Any() ? NuGetPackageSources.Empty : new NuGetPackageSources(nuGetPackageSources.Settings, allPackageSources);
    }

    internal static List<SnapNugetFeed> BuildSnapFeeds([NotNull] this INuGetPackageSources nuGetPackageSources)
    {
        if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
        var snapFeeds = nuGetPackageSources.Items.Select(x => x.BuildSnapNugetFeed(nuGetPackageSources)).ToList();
        return snapFeeds;
    }

    internal static SnapApp GetSnapAppFromDirectory([NotNull] this string workingDirectory, [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader snapAppReader)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(filesystem);
        ArgumentNullException.ThrowIfNull(snapAppReader);

        var snapAppDll = filesystem.PathCombine(workingDirectory, SnapConstants.SnapAppYamlFilename);
        if (!filesystem.FileExists(snapAppDll))
        {
            throw new FileNotFoundException(snapAppDll);
        }
        
        var yaml = filesystem.FileReadAllText(snapAppDll);
        return snapAppReader.BuildSnapAppFromYamlString(yaml);
    }

    internal static void GetStubExeFullPath([NotNull] this SnapApp snapApp, [NotNull] ISnapFilesystem snapFilesystem, [NotNull] string baseDirectory, out string stubExeFullPath)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
        if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));

        var exeName = snapApp.MainExe ?? snapApp.Id;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            exeName += ".exe";
        }
            
        stubExeFullPath = snapFilesystem.PathCombine(baseDirectory, exeName);
    }
    
    internal static void GetStubExeFullPath([NotNull] this Assembly assembly, [NotNull] ISnapFilesystem snapFilesystem,
        [NotNull] ISnapAppReader snapAppReader, out string stubExeFullPath)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
        if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));

        var assemblyLocationDirectory = snapFilesystem.PathGetDirectoryName(assembly.Location);
        var snapApp = assemblyLocationDirectory.GetSnapAppFromDirectory(snapFilesystem, snapAppReader);
        var parentDirectory = snapFilesystem.DirectoryGetParent(assemblyLocationDirectory);

        snapApp.GetStubExeFullPath(snapFilesystem, parentDirectory, out stubExeFullPath);
    }

    internal static bool IsSupportedOsVersion(this OSPlatform oSPlatform)
    {
        return oSPlatform == OSPlatform.Linux || oSPlatform == OSPlatform.Windows;
    }
}
