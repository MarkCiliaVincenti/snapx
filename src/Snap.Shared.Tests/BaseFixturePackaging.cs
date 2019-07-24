using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Packaging;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Shared.Tests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    internal class SnapReleaseBuilder : IDisposable, IEnumerable<string>
    {
        readonly Dictionary<string, IDisposable> _nuspec;

        public SnapAppsReleases SnapAppsReleases { get; }
        public ICoreRunLib CoreRunLib { get; }
        public ISnapFilesystem SnapFilesystem { get; }
        public SnapApp SnapApp { get; }
        public ISnapCryptoProvider SnapCryptoProvider { get; }
        public ISnapEmbeddedResources SnapEmbeddedResources { get; }
        public ISnapPack SnapPack { get; }
        public DisposableDirectory BaseDirectory { get; }
        public string SnapAppBaseDirectory { get; }
        public string SnapAppInstallDirectory { get; }
        public string SnapAppPackagesDirectory { get; }
        public string NugetPackagingDirectory { get; }
        public string CoreRunExe => SnapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(SnapApp);

        public SnapReleaseBuilder([NotNull] DisposableDirectory disposableDirectory, SnapAppsReleases snapAppsReleases, [NotNull] SnapApp snapApp, [NotNull] SnapReleaseBuilderContext builderContext)
        {
            if (builderContext == null) throw new ArgumentNullException(nameof(builderContext));

            _nuspec = new Dictionary<string, IDisposable>();

            SnapFilesystem = builderContext.SnapFilesystem ?? throw new ArgumentNullException(nameof(builderContext.SnapFilesystem));
            SnapAppsReleases = snapAppsReleases ?? throw new ArgumentNullException(nameof(snapAppsReleases));
            SnapApp = snapApp ?? throw new ArgumentNullException(nameof(snapApp));
            CoreRunLib = builderContext.CoreRunLib ?? throw new ArgumentNullException(nameof(builderContext.CoreRunLib));
            SnapCryptoProvider = builderContext.SnapCryptoProvider ?? throw new ArgumentNullException(nameof(builderContext.SnapCryptoProvider));
            SnapEmbeddedResources = builderContext.SnapEmbeddedResources ?? throw new ArgumentNullException(nameof(builderContext.SnapEmbeddedResources));
            SnapPack = builderContext.SnapPack ?? throw new ArgumentNullException(nameof(builderContext.SnapPack));

            BaseDirectory = disposableDirectory ?? throw new ArgumentNullException(nameof(disposableDirectory));
            NugetPackagingDirectory = SnapFilesystem.PathCombine(BaseDirectory.WorkingDirectory, "nuget", $"app-{snapApp.Version}");
            SnapAppBaseDirectory = SnapFilesystem.PathCombine(BaseDirectory.WorkingDirectory, snapApp.Id);
            SnapAppInstallDirectory = SnapFilesystem.PathCombine(SnapAppBaseDirectory, $"app-{snapApp.Version}");
            SnapAppPackagesDirectory = SnapFilesystem.PathCombine(SnapAppBaseDirectory, "packages");
            
            SnapFilesystem.DirectoryCreateIfNotExists(NugetPackagingDirectory);
            SnapFilesystem.DirectoryCreateIfNotExists(SnapAppPackagesDirectory);
            SnapFilesystem.DirectoryCreateIfNotExists(SnapAppInstallDirectory);            
        }

        public SnapReleaseBuilder AddNuspecItem([NotNull] string relativePath, [NotNull] AssemblyDefinition assemblyDefinition)
        {
            if (relativePath == null) throw new ArgumentNullException(nameof(relativePath));
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            var targetPath = SnapFilesystem.PathCombine(relativePath, assemblyDefinition.BuildRelativeFilename());

            _nuspec.Add(targetPath, assemblyDefinition);
            return this;
        }

        public SnapReleaseBuilder AddNuspecItem([NotNull] string targetPath, [NotNull] MemoryStream memoryStream)
        {
            if (targetPath == null) throw new ArgumentNullException(nameof(targetPath));
            if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));
            
            _nuspec.Add(targetPath, memoryStream);
            return this;
        }
        
        public SnapReleaseBuilder AddNuspecItem([NotNull] SnapReleaseBuilder releaseBuilder, int index)
        {
            if (releaseBuilder == null) throw new ArgumentNullException(nameof(releaseBuilder));
            var kv = releaseBuilder._nuspec.ElementAt(index);
            _nuspec.Add(kv.Key, kv.Value);
            return this;
        }

        public SnapReleaseBuilder AddNuspecItem([NotNull] AssemblyDefinition assemblyDefinition)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            _nuspec.Add(assemblyDefinition.BuildRelativeFilename(), assemblyDefinition);
            return this;
        }
        
        public SnapReleaseBuilder AddSnapDll()
        {
            var assemblyDefinition = AssemblyDefinition.ReadAssembly(typeof(SnapPack).Assembly.Location);
            _nuspec.Add(assemblyDefinition.BuildRelativeFilename(), assemblyDefinition);
            return this;
        }
        
        public SnapReleaseBuilder AddDelayLoadedNuspecItem([NotNull] string targetPath)
        {
            if (targetPath == null) throw new ArgumentNullException(nameof(targetPath));
            _nuspec.Add(targetPath, null);
            return this;
        }

        public Dictionary<string, IDisposable> GetNuspecItems()
        {
            return _nuspec;
        }

        public async Task<string> WritePackageAsync([NotNull] MemoryStream memoryStream, [NotNull] SnapRelease snapRelease, CancellationToken cancellationToken = default)
        {
            if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            
            var task = !snapRelease.IsDelta ? 
                 WriteFullPackageAsync(memoryStream, snapRelease, cancellationToken) :
                 WriteDeltaPackageAsync(memoryStream, snapRelease, cancellationToken);
                 
            return await task;
        }

        async Task<string> WriteDeltaPackageAsync([NotNull] MemoryStream memoryStream, [NotNull] SnapRelease snapRelease, CancellationToken cancellationToken = default)
        {
            if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            memoryStream.Seek(0, SeekOrigin.Begin);
            var path = SnapFilesystem.PathCombine(SnapAppPackagesDirectory, snapRelease.BuildNugetDeltaFilename());
            await SnapFilesystem.FileWriteAsync(memoryStream, path, cancellationToken);
            return path;
        }
        
        async Task<string> WriteFullPackageAsync([NotNull] MemoryStream memoryStream, [NotNull] SnapRelease snapRelease, CancellationToken cancellationToken = default)
        {
            if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            memoryStream.Seek(0, SeekOrigin.Begin);
            var path = SnapFilesystem.PathCombine(SnapAppPackagesDirectory, snapRelease.BuildNugetFullFilename());
            await SnapFilesystem.FileWriteAsync(memoryStream, path, cancellationToken);
            return path;
        }

        internal void AssertSnapAppIsGenesis([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            Assert.True(snapApp.IsGenesis);
            Assert.True(snapApp.IsFull);
            Assert.False(snapApp.IsDelta);
        }

        internal void AssertSnapAppIsFull([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            Assert.False(snapApp.IsGenesis);
            Assert.True(snapApp.IsFull);
            Assert.False(snapApp.IsDelta);
        }

        internal void AssertSnapAppIsDelta([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            Assert.False(snapApp.IsGenesis);
            Assert.False(snapApp.IsFull);
            Assert.True(snapApp.IsDelta);
        }

        internal void AssertSnapReleaseIsGenesis([NotNull] SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            Assert.True(snapRelease.IsGenesis);
            Assert.True(snapRelease.IsFull);
            Assert.False(snapRelease.IsDelta);
            Assert.Equal(snapRelease.BuildNugetFullFilename(), snapRelease.Filename);
            Assert.Equal(snapRelease.BuildNugetFullUpstreamId(), snapRelease.UpstreamId);
            Assert.NotEmpty(snapRelease.Files);
            Assert.Empty(snapRelease.New);
            Assert.Empty(snapRelease.Modified);
            Assert.Empty(snapRelease.Unmodified);
            Assert.Empty(snapRelease.Deleted);
        }

        internal void AssertSnapReleaseIsFull([NotNull] SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            Assert.False(snapRelease.IsGenesis);
            Assert.True(snapRelease.IsFull);
            Assert.False(snapRelease.IsDelta);
            Assert.Equal(snapRelease.BuildNugetFullFilename(), snapRelease.Filename);
            Assert.Equal(snapRelease.BuildNugetFullUpstreamId(), snapRelease.UpstreamId);
            Assert.NotEmpty(snapRelease.Files);
            Assert.NotNull(snapRelease.New);
            Assert.NotNull(snapRelease.Modified);
            Assert.NotNull(snapRelease.Unmodified);
            Assert.NotNull(snapRelease.Deleted);
        }
        
        internal void AssertSnapReleaseIsDelta([NotNull] SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            Assert.False(snapRelease.IsGenesis);
            Assert.False(snapRelease.IsFull);
            Assert.True(snapRelease.IsDelta);
            Assert.Equal(snapRelease.BuildNugetDeltaFilename(), snapRelease.Filename);
            Assert.Equal(snapRelease.BuildNugetDeltaUpstreamId(), snapRelease.UpstreamId);
            Assert.NotEmpty(snapRelease.Files);
            Assert.NotNull(snapRelease.New);
            Assert.NotNull(snapRelease.Modified);
            Assert.NotNull(snapRelease.Unmodified);
            Assert.NotNull(snapRelease.Deleted);
        }

        internal void AssertSnapReleaseFiles([NotNull] SnapRelease snapRelease, [NotNull] params string[] targetPaths)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
            if (targetPaths.Length == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(targetPaths));
            Assert.Equal(targetPaths.Length, snapRelease.Files.Count);

            foreach (var targetPath in targetPaths)
            {
                var checksum = snapRelease.Files.SingleOrDefault(x => x.NuspecTargetPath == targetPath);
                Assert.NotNull(checksum);
            }            
        }

        internal void AssertDeltaChangeset(SnapRelease snapRelease, string[] newNuspecTargetPaths = null, string[] deletedNuspecTargetPaths = null,
             string[] modifiedNuspecTargetPaths = null, string[] unmodifiedNuspecTargetPaths = null)
        {
            void AssertDeltaChangesetSnapReleaseImpl(string[] expectedTargetPaths, List<SnapReleaseChecksum> actualChecksums)
            {
                if (actualChecksums == null) throw new ArgumentNullException(nameof(actualChecksums));
                if (expectedTargetPaths == null) throw new ArgumentNullException(nameof(expectedTargetPaths));
                Assert.Equal(expectedTargetPaths.Length, actualChecksums.Count);

                for (var index = 0; index < actualChecksums.Count; index++)
                {
                    var expectedTargetPath = expectedTargetPaths[index];
                    var actualChecksum = actualChecksums[index];
                    Assert.Equal(expectedTargetPath, actualChecksum.NuspecTargetPath);
                }
            }
            
            void AssertDeltaChangesetNuspecTargetPathImpl(string[] expectedTargetPaths, List<string> actualTargetPaths)
            {
                if (actualTargetPaths == null) throw new ArgumentNullException(nameof(actualTargetPaths));
                if (expectedTargetPaths == null) throw new ArgumentNullException(nameof(expectedTargetPaths));
                Assert.Equal(expectedTargetPaths.Length, actualTargetPaths.Count);

                for (var index = 0; index < actualTargetPaths.Count; index++)
                {
                    var expectedTargetPath = expectedTargetPaths[index];
                    var actualChecksumNuspecTargetPath = actualTargetPaths[index];
                    Assert.Equal(expectedTargetPath, actualChecksumNuspecTargetPath);
                }
            }

            AssertDeltaChangesetSnapReleaseImpl(newNuspecTargetPaths ?? new string[] {}, snapRelease.New);
            AssertDeltaChangesetNuspecTargetPathImpl(deletedNuspecTargetPaths ?? new string[] {}, snapRelease.Deleted);
            AssertDeltaChangesetSnapReleaseImpl(modifiedNuspecTargetPaths ?? new string[] {}, snapRelease.Modified);
            AssertDeltaChangesetNuspecTargetPathImpl(unmodifiedNuspecTargetPaths ?? new string[] {}, snapRelease.Unmodified);            
        }

        internal void AssertChannels([NotNull] SnapApp snapApp, [NotNull] params string[] channels)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (channels == null) throw new ArgumentNullException(nameof(channels));
            Assert.Equal(snapApp.Channels.Count, channels.Length);

            for (var index = 0; index < snapApp.Channels.Count; index++)
            {
                var expectedChannelName = snapApp.Channels[index].Name;
                var actualChannelName = channels[index];
                Assert.Equal(expectedChannelName, actualChannelName);
            }
        } 
        
        internal void AssertChannels([NotNull] SnapRelease snapRelease, [NotNull] params string[] channels)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (channels == null) throw new ArgumentNullException(nameof(channels));
            Assert.Equal(channels.Length, snapRelease.Channels.Count);

            for (var index = 0; index < snapRelease.Channels.Count; index++)
            {
                var expectedChannelName = snapRelease.Channels[index];
                var actualChannelName = channels[index];
                Assert.Equal(expectedChannelName, actualChannelName);
            }
        } 

        internal void AssertChecksums([NotNull] SnapApp snapApp, [NotNull] SnapRelease snapRelease, [NotNull] List<string> extractedFiles)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (extractedFiles == null) throw new ArgumentNullException(nameof(extractedFiles));

            var nupkgAbsoluteFilename = SnapFilesystem.PathCombine(SnapAppPackagesDirectory, snapRelease.Filename);
            Assert.True(SnapFilesystem.FileExists(nupkgAbsoluteFilename));

            using (var packageArchiveReader = new PackageArchiveReader(nupkgAbsoluteFilename))
            {
                Assert.Equal(snapApp.ReleaseNotes, snapRelease.ReleaseNotes);

                foreach (var releaseChecksum in snapRelease.Files)
                {
                    var (_, _, checksumFileAbsolutePath) = NormalizePath(snapRelease, releaseChecksum.NuspecTargetPath);
                    Assert.NotNull(checksumFileAbsolutePath);

                    if (snapRelease.IsDelta)
                    {
                        var deletedChecksum = snapRelease.Deleted.SingleOrDefault(x => x == releaseChecksum.NuspecTargetPath);
                        if (deletedChecksum != null)
                        {
                            Assert.False(SnapFilesystem.FileExists(checksumFileAbsolutePath));
                            continue;
                        }

                        var unmodifiedChecksum = snapRelease.Unmodified.SingleOrDefault(x => x == releaseChecksum.NuspecTargetPath);
                        if (unmodifiedChecksum != null)
                        {
                            Assert.False(SnapFilesystem.FileExists(checksumFileAbsolutePath));
                            continue;
                        }               
                    }

                    Assert.True(SnapFilesystem.FileExists(checksumFileAbsolutePath));

                    using (var fileStream = SnapFilesystem.FileRead(checksumFileAbsolutePath))
                    {
                        var diskSha512Checksum = SnapCryptoProvider.Sha512(fileStream);
                        var diskFilesize = SnapFilesystem.FileStat(checksumFileAbsolutePath).Length;

                        SnapReleaseChecksum targetChecksum;
                        bool useFullChecksum;
                        if (snapRelease.IsFull)
                        {
                            targetChecksum = releaseChecksum;
                            useFullChecksum = true;
                            goto checksum;
                        }

                        if (SnapPack.NeverGenerateBsDiffsTheseAssemblies.Any(x => x == releaseChecksum.NuspecTargetPath))
                        {
                            targetChecksum = releaseChecksum;
                            useFullChecksum = true;
                            goto checksum;
                        }

                        targetChecksum = snapRelease.New.SingleOrDefault(x => x.NuspecTargetPath == releaseChecksum.NuspecTargetPath);
                        if (targetChecksum != null)
                        {
                            useFullChecksum = true;
                        }
                        else
                        {
                            targetChecksum = snapRelease.Modified.SingleOrDefault(x => x.NuspecTargetPath == releaseChecksum.NuspecTargetPath);
                            useFullChecksum = false;
                        }

                        checksum:
                        Assert.NotNull(targetChecksum);
   
                        var expectedChecksum = useFullChecksum ? targetChecksum.FullSha512Checksum : targetChecksum.DeltaSha512Checksum;
                        var expectedFilesize = useFullChecksum ? targetChecksum.FullFilesize : targetChecksum.DeltaFilesize;

                        Assert.NotNull(expectedChecksum);
                        if (expectedChecksum == SnapConstants.Sha512EmptyFileChecksum)
                        {
                            Assert.Equal(0, expectedFilesize);
                        }
                        else
                        {
                            Assert.True(expectedFilesize > 0);
                        }

                        Assert.Equal(expectedChecksum, diskSha512Checksum);
                        Assert.Equal(expectedFilesize, diskFilesize);
                    }
                }

                var snapReleaseChecksum = SnapCryptoProvider.Sha512(snapRelease, packageArchiveReader, SnapPack);
                var snapReleaseFilesize = SnapFilesystem.FileStat(nupkgAbsoluteFilename)?.Length;
                
                var expectedReleaseChecksum = snapRelease.IsFull ? snapRelease.FullSha512Checksum : snapRelease.DeltaSha512Checksum;
                var expectedReleaseFilesize = snapRelease.IsFull ? snapRelease.FullFilesize : snapRelease.DeltaFilesize;

                Assert.Equal(expectedReleaseChecksum, snapReleaseChecksum);
                Assert.Equal(expectedReleaseFilesize, snapReleaseFilesize);
            }
        }

        public void Dispose()
        {
            foreach (var kv in _nuspec)
            {
                kv.Value?.Dispose();
            }

            _nuspec.Clear();
        }

        public IEnumerator<string> GetEnumerator()
        {
            foreach (var kv in _nuspec)
            {
                yield return kv.Key;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedTupleComponentInReturnValue")]
        (string targetPath, string nuspecPath, string diskAbsoluteFilename) NormalizePath([NotNull] SnapRelease snapRelease, [NotNull] string relativePath)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (relativePath == null) throw new ArgumentNullException(nameof(relativePath));

            var snapAppInstallDirectory = SnapFilesystem.PathCombine(SnapAppBaseDirectory, $"app-{snapRelease.Version}");

            string diskAbsoluteFilename;
            string targetPath;
            if (relativePath.StartsWith(SnapConstants.NuspecAssetsTargetPath))
            {
                relativePath = relativePath.Substring(SnapConstants.NuspecAssetsTargetPath.Length + 1);
                targetPath = SnapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, relativePath);
                var isCoreRunExe = relativePath.EndsWith(CoreRunExe);
                diskAbsoluteFilename = SnapFilesystem.PathCombine(isCoreRunExe ? SnapAppBaseDirectory : snapAppInstallDirectory, relativePath);
            }
            else if (relativePath.StartsWith(SnapConstants.NuspecRootTargetPath))
            {
                relativePath = relativePath.Substring(SnapConstants.NuspecRootTargetPath.Length + 1);
                targetPath = SnapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, relativePath);
                diskAbsoluteFilename = SnapFilesystem.PathCombine(snapAppInstallDirectory, relativePath);
            }
            else
            {
                throw new Exception($"Unexpected file: {relativePath}");
            }

            return (targetPath, relativePath, diskAbsoluteFilename);
        }

    }

    internal class SnapReleaseBuilderContext
    {
        public ICoreRunLib CoreRunLib { get; }
        public ISnapFilesystem SnapFilesystem { get; }
        public ISnapCryptoProvider SnapCryptoProvider { get; }
        public ISnapEmbeddedResources SnapEmbeddedResources { get; }
        public ISnapPack SnapPack { get; }

        public SnapReleaseBuilderContext([NotNull] ICoreRunLib coreRunLib, [NotNull] ISnapFilesystem snapFilesystem,
            [NotNull] ISnapCryptoProvider snapCryptoProvider, [NotNull] ISnapEmbeddedResources snapEmbeddedResources,
            [NotNull] ISnapPack snapPack)
        {
            CoreRunLib = coreRunLib ?? throw new ArgumentNullException(nameof(coreRunLib));
            SnapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
            SnapCryptoProvider = snapCryptoProvider ?? throw new ArgumentNullException(nameof(snapCryptoProvider));
            SnapEmbeddedResources = snapEmbeddedResources ?? throw new ArgumentNullException(nameof(snapEmbeddedResources));
            SnapPack = snapPack ?? throw new ArgumentNullException(nameof(snapPack));
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    internal class BuildPackageContext : IDisposable
    {
        public MemoryStream FullPackageMemoryStream { get; set; }
        public SnapApp FullPackageSnapApp { get; set; }
        public SnapRelease FullPackageSnapRelease { get; set; }
        public MemoryStream DeltaPackageMemoryStream { get; set; }
        public SnapApp DeltaPackageSnapApp { get; set; }
        public SnapRelease DeltaPackageSnapRelease { get; set; }
        public string FullPackageAbsolutePath { get; set; }
        public string DeltaPackageAbsolutePath { get; set; }

        public void Dispose()
        {
            FullPackageMemoryStream?.Dispose();
            DeltaPackageMemoryStream?.Dispose();
        }
    }

    public class BaseFixturePackaging : BaseFixture
    {
        internal SnapReleaseBuilder WithSnapReleaseBuilder(DisposableDirectory disposableDirectory, [NotNull] SnapAppsReleases snapAppsReleases, [NotNull] SnapApp snapApp, [NotNull] SnapReleaseBuilderContext builderContext)
        {
            if (snapAppsReleases == null) throw new ArgumentNullException(nameof(snapAppsReleases));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (builderContext == null) throw new ArgumentNullException(nameof(builderContext));
            return new SnapReleaseBuilder(disposableDirectory, snapAppsReleases, snapApp, builderContext);
        }
    
        internal async Task<BuildPackageContext> BuildPackageAsync([NotNull] SnapReleaseBuilder releaseBuilder, string releaseNotes = "My Release Notes?", CancellationToken cancellationToken = default)
        {
            if (releaseBuilder == null) throw new ArgumentNullException(nameof(releaseBuilder));

            var (coreRunMemoryStream, _, _) =
                releaseBuilder.SnapEmbeddedResources.GetCoreRunForSnapApp(releaseBuilder.SnapApp, releaseBuilder.SnapFilesystem, releaseBuilder.CoreRunLib);
            coreRunMemoryStream.Dispose();

            var nuspecContent = $@"<?xml version=""1.0""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
    <metadata>
        <title>Random Title</title>
        <authors>Peter Rekdal Sunde</authors>
        <releaseNotes>{releaseNotes}</releaseNotes>
    </metadata>
</package>";

            var nuspecFilename = releaseBuilder.SnapFilesystem.PathCombine(releaseBuilder.NugetPackagingDirectory, "test.nuspec");

            var nuspecBaseDirectory = releaseBuilder.SnapFilesystem.PathCombine(releaseBuilder.NugetPackagingDirectory, "content");
            releaseBuilder.SnapFilesystem.DirectoryCreate(nuspecBaseDirectory);

            var snapPackDetails = new SnapPackageDetails
            {
                NuspecFilename = nuspecFilename,
                NuspecBaseDirectory = nuspecBaseDirectory,
                PackagesDirectory = releaseBuilder.SnapAppPackagesDirectory,
                SnapApp = releaseBuilder.SnapApp,
                SnapAppsReleases = releaseBuilder.SnapAppsReleases
            };

            foreach (var kv in releaseBuilder.GetNuspecItems())
            {
                var targetPath = kv.Key;
                var value = kv.Value;
                var destinationFilename = releaseBuilder.SnapFilesystem.PathCombine(snapPackDetails.NuspecBaseDirectory, targetPath);
                var destinationDirectory = releaseBuilder.SnapFilesystem.PathGetDirectoryName(destinationFilename);
                releaseBuilder.SnapFilesystem.DirectoryCreateIfNotExists(destinationDirectory);
                
                switch (value)
                {
                    case AssemblyDefinition assemblyDefinition:
                        assemblyDefinition.Write(destinationFilename);
                        break;
                    case MemoryStream memoryStream:
                        await releaseBuilder.SnapFilesystem.FileWriteAsync(memoryStream, destinationFilename, cancellationToken);
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        break;
                    default:
                        throw new NotSupportedException($"{targetPath}: {value?.GetType().FullName}");
                }                
            }

            await releaseBuilder.SnapFilesystem.FileWriteUtf8StringAsync(nuspecContent, snapPackDetails.NuspecFilename, cancellationToken);

            var (fullPackageMemoryStream, fullSnapApp, fullPackageSnapRelease, deltaPackageMemoryStream, deltaSnapApp, deltaSnapRelease) = await releaseBuilder.SnapPack
                .BuildPackageAsync(snapPackDetails, releaseBuilder.CoreRunLib, cancellationToken);

            var fullPackageAbsolutePath = await releaseBuilder.WritePackageAsync(fullPackageMemoryStream, fullPackageSnapRelease, cancellationToken);

            string deltaPackageAbsolutePath = null;
            if (deltaSnapApp != null 
                && deltaPackageMemoryStream != null 
                && deltaSnapRelease != null)
            {
                deltaPackageAbsolutePath = await releaseBuilder.WritePackageAsync(deltaPackageMemoryStream, deltaSnapRelease, cancellationToken);                
            }

            return new BuildPackageContext
            {
                FullPackageMemoryStream = fullPackageMemoryStream,
                FullPackageSnapApp = fullSnapApp,
                FullPackageSnapRelease = fullPackageSnapRelease,
                FullPackageAbsolutePath = fullPackageAbsolutePath,
                DeltaPackageMemoryStream = deltaPackageMemoryStream,
                DeltaPackageSnapApp = deltaSnapApp,
                DeltaPackageSnapRelease = deltaSnapRelease,
                DeltaPackageAbsolutePath = deltaPackageAbsolutePath
            };
        }
    }
}
