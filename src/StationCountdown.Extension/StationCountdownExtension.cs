// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// PowerToys の src/modules/cmdpal/ExtensionTemplate（TemplateCmdPalExtension.cs）をもとにしている。

using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace StationCountdown.Extension;

[Guid("F092A33B-F431-4615-B529-256A0983390B")]
public sealed partial class StationCountdownExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;

    private readonly StationCountdownCommandsProvider _provider = new();

    public StationCountdownExtension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => _provider,
            _ => null,
        };
    }

    public void Dispose()
    {
        _provider.Shutdown();
        _extensionDisposedEvent.Set();
    }
}
