﻿// Copyright (c) Mathias Thierbach
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PowerArgs;
using Serilog;

[assembly: InternalsVisibleTo("pbi-tools.tests")]
[assembly: InternalsVisibleTo("pbi-tools.netcore.tests")]

namespace PbiTools
{

    class Program
    {
#if NETFRAMEWORK
        [DllImport("kernel32.dll")]
        private static extern ErrorModes SetErrorMode(ErrorModes uMode);

        [Flags]
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private enum ErrorModes : uint
        {
            SYSTEM_DEFAULT = 0x0,
            SEM_FAILCRITICALERRORS = 0x0001,
            SEM_NOALIGNMENTFAULTEXCEPT = 0x0004,
            SEM_NOGPFAULTERRORBOX = 0x0002,
            SEM_NOOPENFILEERRORBOX = 0x8000
        }
#endif

        static Program()
        {
#if NETFRAMEWORK
            // Prevent the "This program has stopped working" messages.
            var prevMode = SetErrorMode(ErrorModes.SEM_NOGPFAULTERRORBOX);
            SetErrorMode(prevMode | ErrorModes.SEM_NOGPFAULTERRORBOX); // Set error mode w/o overwriting prev settings
#endif
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(AppSettings.LevelSwitch)
                .WriteTo.Console(
#if !DEBUG
                        outputTemplate: "{Message:lj}{NewLine}{Exception}"
#endif
                    )
                    .Filter.ByExcluding(_ => AppSettings.ShouldSuppressConsoleLogs)
                .CreateLogger();
            
            if (AppSettings.LevelSwitch.MinimumLevel < Serilog.Events.LogEventLevel.Information)
                Log.Information("Log level: {LogLevel}", AppSettings.LevelSwitch.MinimumLevel);
        }

        //internal static IConfigurationRoot Configuration { get; }
        internal static AppSettings AppSettings { get; } = new AppSettings();

        static int Main(string[] args)
        {
            // When invoked w/o args, print usage and exit immediately (do not trigger ArgException)
            if ((args ?? new string[0]).Length == 0)
            {
                ArgUsage.GenerateUsageFromTemplate<CmdLineActions>().WriteLine();
                return (int)ExitCode.NoArgsProvided;
            }

            var result = default(ExitCode);
            try
            {
                var action = Args.ParseAction<CmdLineActions>(args);
                
                action.Invoke(); // throws ArgException (DuplicateArg, MissingArg, UnexpectedArg, UnknownArg, ValidationArg)

                // in Debug compilation, propagates any exceptions thrown by executing action
                // in Release compilation, a user friendly error message is displayed, and exceptions thrown are available via the HandledException property

                /* This branch only applies in Release (StandardExceptionHandling enabled) mode, and only to __parser__ exceptions */
                if (action.HandledException != null)
                {
                    // Standard output has been generated by PowerArgs framework already
                    Console.WriteLine();
                    Log.Verbose(action.HandledException, "PowerArgs exception");
                }

                // TODO Define and handle specific exceptions to report back to user directly (No PBI install, etc...)

                result = action.HandledException == null ? ExitCode.Success : ExitCode.InvalidArgs;
            }
            catch (ArgException ex)
            {
                // In RELEASE mode, the PowerArgs error has already been emitted, and the usage docs have been printed to the console at this stage
                
                if (!Environment.UserInteractive)
                {
                    Log.Fatal(ex, "Bad user input.");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine(ex.Message);
                    Console.ResetColor();
#if DEBUG
                    ArgUsage.GenerateUsageFromTemplate(ex.Context.Definition).WriteLine();
#endif
                }
                result = ExitCode.InvalidArgs;
            }
            catch (PbiToolsCliException ex)
            {
                Log.Error(ex.Message);
                result = ex.ErrorCode;
            }
            catch (Exception ex) /* Any unhandled exception */
            {
                // TODO Explicitly log into crash file...
                // If CWD is not writable, put into user profile and show path...

                Log.Fatal(ex, "An unhandled exception occurred.");
                result = ExitCode.UnexpectedError;
            }

            // Prevent closing of window when debugging
            if (Debugger.IsAttached && Environment.UserInteractive)
            {
                Console.WriteLine();
                Console.Write("Press ENTER to exit...");
                Console.ReadLine();
            }

            // ExitCode:
            return (int)result;
        }
    }

    [System.Serializable]
    public class PbiToolsCliException : System.Exception
    {
        public ExitCode ErrorCode { get; }

        public PbiToolsCliException(ExitCode errorCode, string message) : base(message) {
            this.ErrorCode = errorCode;
        }

        protected PbiToolsCliException(
            System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    public enum ExitCode
    {
        UnexpectedError = -9,
        UnspecifiedError = -8,
        InvalidArgs = -2,
        NoArgsProvided = -1,
        Success = 0,
        FileNotFound = 1,
        DependenciesNotInstalled = 2,
        FileExists = 3,
    }

}
