// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// PowerToys の src/modules/cmdpal/ExtensionTemplate（Program.cs）をもとにしている。

using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer.CsWinRT;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            Log.Initialize();
            Log.Info("extension process started: " + Environment.ProcessId);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("unhandled exception", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error("unobserved task exception", e.Exception);
                e.SetObserved();
            };

            global::Shmuelie.WinRTServer.ComServer server = new();

            using ManualResetEvent extensionDisposedEvent = new(false);

            // We are instantiating an extension instance once above, and returning it every time the callback in RegisterExtension below is called.
            // This makes sure that only one instance of the extension is alive, which is returned every time the host asks for the IExtension object.
            StationCountdownExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<StationCountdownExtension, IExtension>(() => extensionInstance);
            server.Start();

            // This will make the main thread wait until the event is signalled by the extension class.
            // Since we have single instance of the extension object, we exit as soon as it is disposed.
            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
            Log.Info("extension process exiting");
        }
        else
        {
            Console.WriteLine("Not being launched as a Extension... exiting.");
        }
    }
}
