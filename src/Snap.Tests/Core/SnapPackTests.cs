﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using Moq;
using NuGet.Packaging;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapPackTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapPack _snapPack;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapExtractor _snapExtractor;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly Mock<ICoreRunLib> _coreRunLibMock;
        readonly ISnapAppReader _snapAppReader;

        public SnapPackTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _coreRunLibMock = new Mock<ICoreRunLib>();
            ISnapCryptoProvider snapCryptoProvider = new SnapCryptoProvider();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapFilesystem = new SnapFilesystem();
            _snapPack = new SnapPack(_snapFilesystem,  new SnapAppReader(), new SnapAppWriter(), snapCryptoProvider, _snapEmbeddedResources);
            _snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, _snapEmbeddedResources);
            _snapAppReader = new SnapAppReader();
            _snapAppWriter = new SnapAppWriter();
        }

        [Fact]
        public void TestNuspecTargetFrameworkMoniker()
        {
            Assert.Equal("Any", SnapConstants.NuspecTargetFrameworkMoniker);
        }

        [Fact]
        public void TestNuspecRootTargetPath()
        {
            Assert.Equal(_snapFilesystem.PathCombine("lib", "Any").ForwardSlashesSafe(), SnapConstants.NuspecRootTargetPath);
        }

        [Fact]
        public void TestSnapNuspecRootTargetPath()
        {
            Assert.Equal(_snapFilesystem.PathCombine("lib", "Any", "a97d941bdd70471289d7330903d8b5b3").ForwardSlashesSafe(), SnapConstants.SnapNuspecTargetPath);
        }

        [Fact]
        public void TestSnapUniqueTargetPathFolderName()
        {
            Assert.Equal("a97d941bdd70471289d7330903d8b5b3", SnapConstants.SnapUniqueTargetPathFolderName);
        }
        
        [Fact]
        public void TestAlwaysRemoveTheseAssemblies()
        {
            var assemblies = new List<string>
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, SnapConstants.SnapDllFilename),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, SnapConstants.SnapAppDllFilename)
            }.Select(x => x.ForwardSlashesSafe()).ToList();

            Assert.Equal(assemblies, _snapPack.AlwaysRemoveTheseAssemblies);
        }

        [Fact]
        public void TestNeverGenerateBsDiffsTheseAssemblies()
        {
            var assemblies = new List<string>
            {
                _snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, SnapConstants.SnapAppDllFilename)
            }.Select(x => x.ForwardSlashesSafe()).ToList();

            Assert.Equal(assemblies, _snapPack.NeverGenerateBsDiffsTheseAssemblies);
        }
        
        [Fact]
        public async Task TestBuildDeltaSummaryAsync_Existing_File_Is_Not_Modified()
        {
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();                        
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition }
            };            
                        
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(previousNupkgSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = previousNupkgMainExecutableAssemblyDefinition;
            var currentNupkgTestDllAssemblyDefinition = previousNupkgTestDllAssemblyDefinition;
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), currentNupkgTestDllAssemblyDefinition }
            };            

            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(currentNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                using (var deltaSummary =
                    await _snapPack.BuildDeltaSummaryAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename, CancellationToken.None))
                {
                    // New
                    Assert.Single(deltaSummary.New);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, 
                        SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), deltaSummary.New[0].TargetPath);
                    
                    // Modified
                    Assert.Empty(deltaSummary.Modified);
                    
                    // Unmodified
                    Assert.Equal(4, deltaSummary.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaSummary.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, 
                        SnapConstants.SnapDllFilename).ForwardSlashesSafe(), deltaSummary.Unmodified[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, 
                        currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Unmodified[2].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, 
                        currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Unmodified[3].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaSummary.Deleted);
                }
            }            
        }
        
        [Fact]
        public async Task TestBuildDeltaSummaryAsync_Existing_File_Is_Updated()
        {
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();                        
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition }
            };            
            
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(previousNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = previousNupkgMainExecutableAssemblyDefinition;
            var currentNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), currentNupkgTestDllAssemblyDefinition }
            };            
                        
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(currentNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                using (var deltaSummary =
                    await _snapPack.BuildDeltaSummaryAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename, CancellationToken.None))
                {
                    // New
                    Assert.Single(deltaSummary.New);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath,
                        SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), deltaSummary.New[0].TargetPath);
                    
                    // Modified
                    Assert.Single(deltaSummary.Modified);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                        currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Modified[0].TargetPath);
                    
                    // Unmodified
                    Assert.Equal(3, deltaSummary.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaSummary.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath,
                        SnapConstants.SnapDllFilename).ForwardSlashesSafe(), deltaSummary.Unmodified[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                        currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Unmodified[2].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaSummary.Deleted);                    
                }
            }            
        }
        
        [Fact]
        public async Task TestBuildDeltaSummaryAsync_Existing_File_Is_Deleted_And_New_File_Is_Added()
        {
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgAssemblyDefinition.BuildRelativeFilename(), previousNupkgAssemblyDefinition }
            };            
                        
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(previousNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = previousNupkgMainExecutableAssemblyDefinition;
            var currentNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary("test2");
            var currentNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test3");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgAssemblyDefinition1.BuildRelativeFilename(), currentNupkgAssemblyDefinition1 },
                { currentNupkgAssemblyDefinition2.BuildRelativeFilename(), currentNupkgAssemblyDefinition2 }
            };            
                        
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(currentNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                using (var deltaSummary =
                    await _snapPack.BuildDeltaSummaryAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename, CancellationToken.None))
                {
                    // New
                    Assert.Equal(3, deltaSummary.New.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, 
                        SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), deltaSummary.New[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, 
                        currentNupkgAssemblyDefinition1.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.New[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, 
                        currentNupkgAssemblyDefinition2.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.New[2].TargetPath);

                    // Modified
                    Assert.Empty(deltaSummary.Modified);
                    
                    // Unmodified
                    Assert.Equal(3, deltaSummary.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaSummary.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath,
                        SnapConstants.SnapDllFilename).ForwardSlashesSafe(), deltaSummary.Unmodified[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                        currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Unmodified[2].TargetPath);

                    // Deleted
                    Assert.Single(deltaSummary.Deleted);                    
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                        previousNupkgAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Deleted[0].TargetPath);
                }
            }            
        }
        
        [Fact]
        public async Task TestBuildDeltaSummaryAsync_New_File_Is_Added()
        {
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();                        
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition }
            };            
             
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(previousNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = previousNupkgMainExecutableAssemblyDefinition;
            var currentNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test2");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition },
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), currentNupkgTestDllAssemblyDefinition }
            };            
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(currentNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                using (var deltaSummary =
                    await _snapPack.BuildDeltaSummaryAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename, CancellationToken.None))
                {
                    // New
                    Assert.Equal(2, deltaSummary.New.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, 
                        SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), deltaSummary.New[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, 
                        currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.New[1].TargetPath);

                    // Modified
                    Assert.Empty(deltaSummary.Modified);
                    
                    // Unmodified
                    Assert.Equal(4, deltaSummary.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaSummary.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, 
                        SnapConstants.SnapDllFilename).ForwardSlashesSafe(), deltaSummary.Unmodified[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, 
                        currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Unmodified[2].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                        previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Unmodified[3].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaSummary.Deleted);    
                }
            }            
        }
        
        [Fact]
        public async Task TestBuildDeltaSummaryAsync_New_File_Is_Added_With_Same_Name_As_Previous_But_Resides_In_Sub_Directory()
        {
             var previousNupkgSnapApp = _baseFixture.BuildSnapApp();                        
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition }
            };            
             
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(previousNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = previousNupkgMainExecutableAssemblyDefinition;
            var currentNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test2");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition },
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { _snapFilesystem.PathCombine("zubdirectory", currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()), currentNupkgTestDllAssemblyDefinition },
                { _snapFilesystem.PathCombine("zubdirectory", "zubdirectory2", currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()), currentNupkgTestDllAssemblyDefinition }
            };            
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(currentNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                using (var deltaSummary =
                    await _snapPack.BuildDeltaSummaryAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename, CancellationToken.None))
                {
                    // New
                    Assert.Equal(3, deltaSummary.New.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, 
                        SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), deltaSummary.New[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "zubdirectory",
                        currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.New[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "zubdirectory", "zubdirectory2",
                        currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.New[2].TargetPath);

                    // Modified
                    Assert.Empty(deltaSummary.Modified);
                    
                    // Unmodified
                    Assert.Equal(4, deltaSummary.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaSummary.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, 
                        SnapConstants.SnapDllFilename).ForwardSlashesSafe(), deltaSummary.Unmodified[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, 
                        currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Unmodified[2].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                        previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Unmodified[3].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaSummary.Deleted);    
                }
            }            
        }

        [Fact]
        public async Task TestCountNonNugetFilesAsync()
        {
            var snapApp = _baseFixture.BuildSnapApp();

            var testExeAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);            
            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { testExeAssemblyDefinition.BuildRelativeFilename(), testExeAssemblyDefinition },
                { testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition },
                { $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition }
            };
            
            var (nupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(snapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

            using (testDllAssemblyDefinition)
            using (nupkgMemoryStream)
            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
            {                
                Assert.Equal(8, await _snapPack.CountNonNugetFilesAsync(asyncPackageCoreReader, CancellationToken.None));
            }
        }

        [Fact]
        public async Task TestBuildFullPackageAsync_Includes_Checksum_Manifest()
        {
            var snapApp = _baseFixture.BuildSnapApp();

            var testExeAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);            
            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { testExeAssemblyDefinition.BuildRelativeFilename(), testExeAssemblyDefinition },
                { testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition },
                { $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition }
            };
            
            var (nupkgMemoryStream, packageDetails) = await _baseFixture
                .BuildInMemoryFullPackageAsync(snapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

            using (testDllAssemblyDefinition)
            using (nupkgMemoryStream)
            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var appDirName = $"app-{packageDetails.App.Version}";
                var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                await _snapExtractor.ExtractAsync(asyncPackageCoreReader, appDir, true);

                var checksumFilename = _snapFilesystem.PathCombine(appDir, SnapConstants.ChecksumManifestFilename);
                Assert.True(_snapFilesystem.FileExists(checksumFilename));

                var checksums =
                    _snapPack.ParseChecksumManifest(
                        await _snapFilesystem.FileReadAllTextAsync(checksumFilename)).ToList();

                var expectedLayout = new List<string>
                    {
                        _snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(snapApp)),
                        _snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, SnapConstants.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, SnapConstants.SnapDllFilename),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, testExeAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, testDllAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", testDllAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "subdirectory2", testDllAssemblyDefinition.BuildRelativeFilename())
                    }
                    .Select(x => x.ForwardSlashesSafe())
                    .OrderBy(x => x)
                    .ToList();
                
                Assert.True(expectedLayout.Count == checksums.Count);

                for (var i = 0; i < expectedLayout.Count; i++)
                {
                    var checksum = checksums[i];
                    var expectedNuspecEffectivePath = expectedLayout[i];
                    
                    Assert.StartsWith(SnapConstants.NuspecRootTargetPath, checksum.TargetPath);
                    Assert.Equal(expectedNuspecEffectivePath, checksum.TargetPath);
                    Assert.Equal(128, checksum.Sha512Checksum.Length);                                        
                }
            }
        }

        [Fact]
        public async Task TestBuildFullPackageAsync()
        {
            var snapApp = _baseFixture.BuildSnapApp();
            var mainAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);
            var dllDefinition1 = _baseFixture.BuildEmptyLibrary("test");
            var dllDefinition2 = _baseFixture.BuildEmptyLibrary("test");

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { mainAssemblyDefinition.BuildRelativeFilename(), mainAssemblyDefinition },
                { $"subdirectory/{dllDefinition1.BuildRelativeFilename()}", dllDefinition1 },
                { $"subdirectory/subdirectory2/{dllDefinition2.BuildRelativeFilename()}", dllDefinition2 }
            };
            
            var (nupkgMemoryStream, packageDetails) = await _baseFixture
                .BuildInMemoryFullPackageAsync(snapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

            using (mainAssemblyDefinition)
            using (nupkgMemoryStream)
            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var appDirName = $"app-{packageDetails.App.Version}";
                var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                var extractedFiles = await _snapExtractor.ExtractAsync(asyncPackageCoreReader, appDir);
                
                var extractedDiskLayout = _snapFilesystem
                    .DirectoryGetAllFilesRecursively(rootDir.WorkingDirectory)
                    .OrderBy(x => x)
                    .ToList();

                var expectedLayout = new List<string>
                {
                    _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App)),
                    _snapFilesystem.PathCombine(appDir, SnapConstants.SnapAppDllFilename),
                    _snapFilesystem.PathCombine(appDir, SnapConstants.SnapDllFilename),
                    _snapFilesystem.PathCombine(appDir, mainAssemblyDefinition.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", dllDefinition1.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", "subdirectory2", dllDefinition2.BuildRelativeFilename())
                }
                    .OrderBy(x => x)
                    .ToList();

                Assert.Equal(expectedLayout.Count, extractedFiles.Count);
                Assert.Equal(expectedLayout.Count, extractedDiskLayout.Count);

                expectedLayout.ForEach(x =>
                {
                    var stat = _snapFilesystem.FileStat(x);
                    Assert.NotNull(stat);
                    Assert.True(stat.Length > 0, x);
                });

                for (var i = 0; i < expectedLayout.Count; i++)
                {
                    Assert.Equal(expectedLayout[i], extractedDiskLayout[i]);
                }
            }
        }
        
        [Fact]
        public async Task TestBuildFullPackageAsync_Filenames_Without_Extension()
        {
            var snapApp = _baseFixture.BuildSnapApp();
            var mainAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);
            var file1AssemblyDefinition = _baseFixture.BuildEmptyLibrary("test", true);
            var file2AssemblyDefinition = _baseFixture.BuildEmptyLibrary("test", true);
            var file3AssemblyDefinition = _baseFixture.BuildEmptyLibrary("test", true);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var nuspecLayout = new Dictionary<string, AssemblyDefinition>
                {
                    { mainAssemblyDefinition.BuildRelativeFilename(), mainAssemblyDefinition },
                    { "file1", file1AssemblyDefinition },
                    { _snapFilesystem.PathCombine("subdirectory", "file1"), file2AssemblyDefinition },
                    { _snapFilesystem.PathCombine("subdirectory", "file2"), file3AssemblyDefinition }
                };

                var subdirectory = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "subdirectory");
                _snapFilesystem.DirectoryCreate(subdirectory);
                                
                var (nupkgMemoryStream, packageDetails) = await _baseFixture
                    .BuildInMemoryFullPackageAsync(snapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);
                    
                using (mainAssemblyDefinition)
                using (nupkgMemoryStream)
                using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))               
                {
                    var appDirName = $"app-{packageDetails.App.Version}";
                    var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);
                    
                    var extractedFiles = await _snapExtractor.ExtractAsync(asyncPackageCoreReader, appDir);
                    
                    var extractedDiskLayout = _snapFilesystem
                        .DirectoryGetAllFilesRecursively(rootDir.WorkingDirectory)
                        .OrderBy(x => x)
                        .ToList();

                    var expectedLayout = new List<string>
                    {
                        _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App)),
                        _snapFilesystem.PathCombine(appDir, SnapConstants.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(appDir, SnapConstants.SnapDllFilename),
                        _snapFilesystem.PathCombine(appDir, mainAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(appDir, "file1"),
                        _snapFilesystem.PathCombine(appDir, "subdirectory", "file1"),
                        _snapFilesystem.PathCombine(appDir, "subdirectory", "file2")
                    }
                        .OrderBy(x => x)
                        .ToList();
    
                    Assert.Equal(expectedLayout.Count, extractedFiles.Count);
                    Assert.Equal(expectedLayout.Count, extractedDiskLayout.Count);
    
                    expectedLayout.ForEach(x =>
                    {
                        _snapFilesystem.FileExists(x);
                        var stat = _snapFilesystem.FileStat(x);
                        Assert.True(stat.Length > 0);
                    });
    
                    for (var i = 0; i < expectedLayout.Count; i++)
                    {
                        Assert.Equal(expectedLayout[i], extractedDiskLayout[i]);
                    }
                }
            }
            
        }
        
        [Fact]
        public async Task TestBuildDeltaPackageAsync()
        {
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary("test1"); 
            var previousNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test2");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // Modified in current
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgAssemblyDefinition1.BuildRelativeFilename(), previousNupkgAssemblyDefinition1 },
                // Deleted in current
                { previousNupkgAssemblyDefinition2.BuildRelativeFilename(), previousNupkgAssemblyDefinition2 }
            };            
                        
            var (previousNupkgMemoryStream, currentPackageDetails) = await _baseFixture
                .BuildInMemoryFullPackageAsync(previousNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(currentPackageDetails.App, true);
            var currentNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary(previousNupkgAssemblyDefinition1.Name.Name, true);
            var currentNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test3");
            var currentNupkgAssemblyDefinition3 = _baseFixture.BuildEmptyLibrary("test4");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // Modified
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgAssemblyDefinition1.BuildRelativeFilename(), currentNupkgAssemblyDefinition1 },
                // New
                { currentNupkgAssemblyDefinition2.BuildRelativeFilename(), currentNupkgAssemblyDefinition2 },
                { currentNupkgAssemblyDefinition3.BuildRelativeFilename(), currentNupkgAssemblyDefinition3 }
            };            
            
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(currentNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

            MemoryStream deltaNupkgStream;
            
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                var tuple = await _snapPack.BuildDeltaPackageAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename);
                deltaNupkgStream = tuple.memoryStream;
                Assert.NotNull(deltaNupkgStream);                
            }

            using (var asyncPackageCoreReader = new PackageArchiveReader(deltaNupkgStream))
            {
                var snapAppDelta = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader);
                
                Assert.Contains("delta", snapAppDelta.BuildNugetLocalFilename());
                Assert.Equal(currentNupkgSnapApp.Version, snapAppDelta.Version);

                Assert.True(snapAppDelta.Delta);
                Assert.NotNull(snapAppDelta.DeltaSummary);
                Assert.Equal(snapAppDelta.DeltaSummary.FullNupkgFilename, previousNupkgSnapApp.BuildNugetLocalFilename());
                Assert.Equal(128, snapAppDelta.DeltaSummary.FullNupkgSha512Checksum.Length);

                var expectedLayout = new List<string>
                {
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, currentNupkgAssemblyDefinition1.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, currentNupkgAssemblyDefinition2.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, currentNupkgAssemblyDefinition3.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, SnapConstants.ChecksumManifestFilename),
                    _snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, SnapConstants.SnapAppDllFilename)
                }.Select(x => x.ForwardSlashesSafe()).OrderBy(x => x).ToList();
                
                var actualLayout = (await _snapPack.GetFilesAsync(asyncPackageCoreReader, CancellationToken.None))
                    .Where(x => x.StartsWith(SnapConstants.NuspecRootTargetPath))
                    .OrderBy(x => x)
                    .ToList();

                Assert.Equal(expectedLayout.Count, actualLayout.Count);

                for (var i = 0; i < expectedLayout.Count; i++)
                {
                    Assert.Equal(expectedLayout[i], actualLayout[i]);
                }

                var deltaSummary = snapAppDelta.DeltaSummary;
                Assert.NotNull(deltaSummary);
                                
                // New
                Assert.Equal(3, deltaSummary.New.Count);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath, 
                    SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), deltaSummary.New[0]);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, 
                    currentNupkgAssemblyDefinition2.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.New[1]);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, 
                    currentNupkgAssemblyDefinition3.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.New[2]);

                // Modified
                Assert.Equal(2, deltaSummary.Modified.Count);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, 
                    currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Modified[0]);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, 
                    currentNupkgAssemblyDefinition1.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Modified[1]);
                
                // Unmodified
                Assert.Equal(2, deltaSummary.Unmodified.Count);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath,
                    _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaSummary.Unmodified[0]);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.SnapNuspecTargetPath,
                    SnapConstants.SnapDllFilename).ForwardSlashesSafe(), deltaSummary.Unmodified[1]);

                // Deleted
                Assert.Single(deltaSummary.Deleted);                    
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    previousNupkgAssemblyDefinition2.BuildRelativeFilename()).ForwardSlashesSafe(), deltaSummary.Deleted[0]);
            }
        }

        [Fact]
        public async Task TestReassambleFullPackageAsync()
        {
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            var progressSource = new Mock<ISnapProgressSource>();
            progressSource.Setup(x => x.Raise(It.IsAny<int>()));
                
            // 1. Previous

            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary("test1"); 
            var previousNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test2");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // Modified in current
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgAssemblyDefinition1.BuildRelativeFilename(), previousNupkgAssemblyDefinition1 },
                // Deleted in current
                { previousNupkgAssemblyDefinition2.BuildRelativeFilename(), previousNupkgAssemblyDefinition2 }
            };               
                        
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(previousNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(currentNupkgSnapApp, true);
            var currentNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary(previousNupkgAssemblyDefinition1.Name.Name, true);
            var currentNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test3");
            var currentNupkgAssemblyDefinition3 = _baseFixture.BuildEmptyLibrary("test4");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // Modified
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgAssemblyDefinition1.BuildRelativeFilename(), currentNupkgAssemblyDefinition1 },
                // New
                { currentNupkgAssemblyDefinition2.BuildRelativeFilename(), currentNupkgAssemblyDefinition2 },
                { currentNupkgAssemblyDefinition3.BuildRelativeFilename(), currentNupkgAssemblyDefinition3 }
            };     
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(currentNupkgSnapApp, _coreRunLibMock.Object,  _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                var (deltaNupkgStream, snapAppDelta) = await _snapPack.BuildDeltaPackageAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename);
                Assert.NotNull(deltaNupkgStream);

                var deltaNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, snapAppDelta.BuildNugetLocalFilename());
                await _snapFilesystem.FileWriteAsync(deltaNupkgStream, deltaNupkgAbsoluteFilename, CancellationToken.None);

                var (reassembledNupkgStream, snapAppReassembled) = await _snapPack.ReassambleFullPackageAsync(deltaNupkgAbsoluteFilename, 
                    previousNupkgAbsoluteFilename, progressSource.Object);
                Assert.NotNull(reassembledNupkgStream);
                Assert.NotNull(snapAppReassembled);

                Assert.Equal(currentNupkgSnapApp.BuildNugetLocalFilename(), snapAppReassembled.BuildNugetLocalFilename());
                
                progressSource.Verify(x => x.Raise(It.Is<int>( v => v == 100)), Times.Once);
                
                using (var reassembledAsyncCoreReader = new PackageArchiveReader(reassembledNupkgStream))
                using (var extractDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
                {                
                    var appDirName = $"app-{snapAppReassembled.Version}";
                    var appDir = _snapFilesystem.PathCombine(extractDir.WorkingDirectory, appDirName);
                    
                    var extractedFiles = await _snapExtractor.ExtractAsync(reassembledAsyncCoreReader, appDir);
                
                    var extractedDiskLayout = _snapFilesystem
                        .DirectoryGetAllFilesRecursively(extractDir.WorkingDirectory)
                        .OrderBy(x => x)
                        .ToList();

                    var expectedLayout = new List<string>
                    {
                        _snapFilesystem.PathCombine(extractDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)),
                        _snapFilesystem.PathCombine(appDir, currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(appDir, currentNupkgAssemblyDefinition1.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(appDir, currentNupkgAssemblyDefinition2.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(appDir, currentNupkgAssemblyDefinition3.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(appDir, SnapConstants.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(appDir, SnapConstants.SnapDllFilename)
                    }.OrderBy(x => x).ToList();
                    
                    Assert.Equal(expectedLayout.Count, extractedFiles.Count);
                    Assert.Equal(expectedLayout.Count, extractedDiskLayout.Count);

                    expectedLayout.ForEach(x =>
                    {
                        var stat = _snapFilesystem.FileStat(x);
                        Assert.NotNull(stat);
                        Assert.True(stat.Length > 0, x);
                    });

                    var currentFullNupkgChecksums = (await _snapPack.GetChecksumManifestAsync(new PackageArchiveReader(currentNupkgMemoryStream), CancellationToken.None)).ToList();
                    var reassembledFullNupkgChecksums = (await _snapPack.GetChecksumManifestAsync(new PackageArchiveReader(reassembledNupkgStream), CancellationToken.None)).ToList();

                    Assert.Equal(currentFullNupkgChecksums.Count, reassembledFullNupkgChecksums.Count);
                    
                    for (var i = 0; i < expectedLayout.Count; i++)
                    {
                        Assert.Equal(expectedLayout[i], extractedDiskLayout[i]);
                    }
                    
                }
            
            }
        }

        [Fact]
        public async Task TestGetSnapAppAsync()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();

            var testDll = _baseFixture.BuildEmptyLibrary("test");
            var mainExe = _baseFixture.BuildSnapAwareEmptyExecutable(snapAppBefore);
            
            var (nupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(snapAppBefore, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources, new Dictionary<string, AssemblyDefinition>
                {
                    { mainExe.BuildRelativeFilename(), mainExe },
                    { testDll.BuildRelativeFilename(), testDll }
                });

            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
            {
                var snapAppAfter = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader);
                Assert.NotNull(snapAppAfter);
            }
        }

        [Fact]
        public async Task TestBuildReleasesPackage()
        {
            var snapApp1 = _baseFixture.BuildSnapApp();
            snapApp1.Version = new SemanticVersion(1, 0, 0);
            var snapApp2 = _baseFixture.BuildSnapApp();
            snapApp2.Version = new SemanticVersion(2, 0, 0);

            var releases = new SnapReleases();
            releases.Apps.Add(new SnapRelease(snapApp1, snapApp1.GetCurrentChannelOrThrow(), 10, 1));
            releases.Apps.Add(new SnapRelease(snapApp2, snapApp2.GetCurrentChannelOrThrow(), 20, 2));

            using (var releasesStream = _snapPack.BuildReleasesPackage(releases))
            {
                Assert.NotNull(releasesStream);
                Assert.Equal(0, releasesStream.Position);

                using (var packageArchiveReader = new PackageArchiveReader(releasesStream))
                {
                    var snapReleases = await _snapExtractor.ExtractReleasesAsync(packageArchiveReader, _snapAppReader);
                    Assert.NotNull(snapReleases);
                    Assert.Equal(2, snapReleases.Apps.Count);
                    Assert.Equal(snapApp2.Version, snapReleases.Apps[0].Version);
                    Assert.Equal(snapApp1.Version, snapReleases.Apps[1].Version);
                }
            }
        }
    }
}
