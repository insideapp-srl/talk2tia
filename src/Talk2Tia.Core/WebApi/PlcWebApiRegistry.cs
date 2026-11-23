using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol;
using Talk2Tia.Core.Configuration;

namespace Talk2Tia.Core.WebApi;

/// <summary>Web API clients for the PLCs declared in the configuration (one client per PLC, created on first use).</summary>
public sealed class PlcWebApiRegistry : IDisposable
{
    private readonly IReadOnlyDictionary<string, PlcEndpoint> _endpoints;
    private readonly Func<string, PlcEndpoint, PlcWebApiClient> _factory;
    private readonly ConcurrentDictionary<string, PlcWebApiClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public PlcWebApiRegistry(Talk2TiaConfig config, Func<string, PlcEndpoint, PlcWebApiClient>? factory = null)
    {
        _endpoints = new Dictionary<string, PlcEndpoint>(config.Plcs ?? new Dictionary<string, PlcEndpoint>(), StringComparer.OrdinalIgnoreCase);
        _factory = factory ?? ((name, endpoint) => new PlcWebApiClient(name, endpoint));
    }

    public IReadOnlyDictionary<string, PlcEndpoint> Endpoints => _endpoints;

    /// <summary>Returns the client for <paramref name="plc"/>, or for the only configured PLC when it is omitted.</summary>
    public PlcWebApiClient Get(string? plc)
    {
        if (_endpoints.Count == 0)
        {
            throw new McpException(
                "No PLC is configured for the Web API. Add it under \"plcs\" in the Talk2Tia configuration file " +
                "(address, username, passwordEnv) and restart the server. See docs/getting-started.md.");
        }

        string name;
        if (string.IsNullOrWhiteSpace(plc))
        {
            if (_endpoints.Count > 1)
            {
                throw new McpException($"Several PLCs are configured; pass 'plc': {string.Join(", ", _endpoints.Keys)}");
            }

            name = _endpoints.Keys.First();
        }
        else
        {
            name = _endpoints.Keys.FirstOrDefault(k => string.Equals(k, plc!.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? throw new McpException($"PLC '{plc}' is not configured. Configured: {string.Join(", ", _endpoints.Keys)}");
        }

        return _clients.GetOrAdd(name, n => _factory(n, _endpoints[n]));
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();
    }
}
