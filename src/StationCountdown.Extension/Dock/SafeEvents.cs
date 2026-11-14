// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// PowerToys の src/modules/cmdpal/extensionsdk/Microsoft.CommandPalette.Extensions.Toolkit/EventHelpers.cs を移植した。
// 公開されている Toolkit 0.12.260812002 にはまだ含まれていないため。SDK を更新したら、Toolkit の EventHelpers に置き換える。

using Windows.Foundation;

namespace StationCountdown.Extension.Dock;

/// <summary>Helpers for delivering notifications independently to each subscriber.</summary>
internal static class SafeEvents
{
    private const int RpcDisconnectedHResult = unchecked((int)0x80010108); // RPC_E_DISCONNECTED
    private const int RpcServerUnavailableHResult = unchecked((int)0x800706BA); // HRESULT_FROM_WIN32(RPC_S_SERVER_UNAVAILABLE)

    /// <summary>Raises an event without allowing one subscriber's failure to block the others.</summary>
    public static void Raise<TEventArgs>(TypedEventHandler<object, TEventArgs>? handlers, object sender, TEventArgs args)
    {
        // A callback to a previous host must not prevent delivery to the current host.
        foreach (TypedEventHandler<object, TEventArgs> handler in Delegate.EnumerateInvocationList(handlers))
        {
            try
            {
                handler(sender, args);
            }
            catch (Exception ex) when (ex.HResult is RpcDisconnectedHResult or RpcServerUnavailableHResult)
            {
                // RPC_E_DISCONNECTED and RPC_S_SERVER_UNAVAILABLE identify dead recipients.
                // https://devblogs.microsoft.com/oldnewthing/20190521-00/?p=102505
            }
#pragma warning disable CA1031 // Preserve the existing exception policy, but continue with the other subscribers.
            catch
#pragma warning restore CA1031
            {
            }
        }
    }
}
