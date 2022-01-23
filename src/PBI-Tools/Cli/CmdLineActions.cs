﻿// Copyright (c) Mathias Thierbach
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using PowerArgs;
using Serilog;

namespace PbiTools.Cli
{
    using Configuration;
    using Utils;

#if !DEBUG
    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]  // PowerArgs will print the user friendly error message as well as the auto-generated usage documentation for the program.
#endif
    [ArgDescription(AssemblyVersionInformation.AssemblyProduct + " (" + AppSettings.Edition + "), " + AssemblyVersionInformation.AssemblyInformationalVersion)]
    [ArgProductVersion(AssemblyVersionInformation.AssemblyVersion)]
    [ArgProductName(AssemblyVersionInformation.AssemblyProduct)]
    [ApplyDefinitionTransforms]
    public partial class CmdLineActions
    {

        private static readonly ILogger Log = Serilog.Log.ForContext<CmdLineActions>();

        private readonly IDependenciesResolver _dependenciesResolver = DependenciesResolver.Default;
        private readonly AppSettings _appSettings;
        private readonly Stopwatch _stopWatch = Stopwatch.StartNew();

        public CmdLineActions() : this(Program.AppSettings)
        {
        }

        public CmdLineActions(AppSettings appSettings)
        {
            _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        }


        [HelpHook, ArgShortcut("-?"), ArgDescription("Shows this help")]
        public bool Help { get; set; }

    }


    public enum ExternalToolAction
    {
        List = 1,
        Install = 2,
        Uninstall = 3,
        ExtractCurrentProject = 4,
        LaunchInteractive = 5
    }
}