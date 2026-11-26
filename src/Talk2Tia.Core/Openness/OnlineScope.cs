using System;
using ModelContextProtocol;
using Siemens.Engineering.Online;

namespace Talk2Tia.Core.Openness;

/// <summary>
/// Runs an operation that needs an online connection to the PLC. When the PLC is offline and <c>autoOnline</c> is set,
/// it goes online with the configured connection, runs the operation and goes back offline.
/// </summary>
internal static class OnlineScope
{
    public static T Run<T>(SoftwareRef plc, bool autoOnline, string operation, Func<T> action)
    {
        var online = plc.DeviceItem.GetService<OnlineProvider>()
                     ?? throw new McpException($"{plc.Name} does not support online access.");
        if (online.State == OnlineState.Online)
        {
            return action();
        }

        if (!autoOnline)
        {
            throw new McpException(
                $"{operation} needs an online connection and {plc.Name} is {online.State}. Call online_go_online first, " +
                "or pass autoOnline=true.");
        }

        if (!online.Configuration.IsConfigured)
        {
            throw new McpException(
                $"{operation} needs an online connection, but the connection of {plc.Name} is not configured. " +
                "Use online_connection_options and online_configure, then retry.");
        }

        var state = online.GoOnline();
        if (state != OnlineState.Online)
        {
            throw new McpException($"{operation}: could not go online with {plc.Name} (state {state}). Check the connection.");
        }

        try
        {
            return action();
        }
        finally
        {
            online.GoOffline();
        }
    }
}
