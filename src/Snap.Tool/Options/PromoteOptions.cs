﻿using System.Diagnostics.CodeAnalysis;
using CommandLine;

namespace Snap.Tool.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("promote", HelpText = "Pushes a nupkg to the default release channel")]
    internal class PromoteOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Snap app name", Required = true)]
        public string App { get; set; }
        [Option("all", HelpText = "Promotes app in all remaining channels.", Required = true)]
        public bool All { get; set; }
    }
}
