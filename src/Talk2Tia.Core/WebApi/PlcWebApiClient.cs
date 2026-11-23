using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Talk2Tia.Core.Configuration;

namespace Talk2Tia.Core.WebApi;

/// <summary>Error returned by the CPU Web API (JSON-RPC error object).</summary>
public sealed class WebApiException(int code, string message, string method)
    : Exception($"Web API {method} failed ({code}): {message}{Hint(code)}")
{
    public int Code { get; } = code;
    public string Method { get; } = method;

    public const int MethodNotFound = -32601;
    public const int PermissionDenied = 2;
    public const int NoResources = 4;
    public const int LoginFailed = 100;

    private static string Hint(int code) => code switch
    {
        MethodNotFound => " — the CPU firmware does not support this method.",
        PermissionDenied => " — the web server user lacks the required right (CPU properties > Web server > User management).",
        NoResources => " — the CPU has no free sessions; wait about 150 s.",
        LoginFailed => " — wrong web server user or password.",
        _ => string.Empty,
    };
}

/// <summary>A single JSON-RPC call.</summary>
public sealed record WebApiCall(string Method, JsonNode? Params = null);

/// <summary>
/// JSON-RPC 2.0 client for the Web API of SIMATIC CPUs (POST https://&lt;address&gt;/api/jsonrpc).
/// Logs in on demand with the configured user and renews the token when it expires.
/// </summary>
public sealed class PlcWebApiClient : IDisposable
{
    private const int TokenIdleSeconds = 100; // The CPU invalidates a token after about 120-150 s without requests.
    private readonly PlcEndpoint _endpoint;
    private readonly HttpClient _http;
    private readonly Func<string, string?> _env;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Uri _uri;
    private string? _token;
    private DateTime _lastUse;
    private int _nextId;

    public PlcWebApiClient(string name, PlcEndpoint endpoint, HttpMessageHandler? handler = null, Func<string, string?>? env = null)
    {
        Name = name;
        _endpoint = endpoint;
        _env = env ?? Environment.GetEnvironmentVariable;
        _uri = new Uri($"https://{endpoint.Address.Trim().TrimEnd('/')}/api/jsonrpc");
        _http = new HttpClient(handler ?? CreateHandler(endpoint)) { Timeout = TimeSpan.FromSeconds(endpoint.TimeoutSeconds) };
    }

    public string Name { get; }
    public string Address => _endpoint.Address;
    public string? Username => _endpoint.Username;

    /// <summary>Calls one method and returns its result.</summary>
    public async Task<JsonNode?> CallAsync(string method, JsonNode? parameters = null, CancellationToken cancellationToken = default) =>
        (await CallManyAsync(new[] { new WebApiCall(method, parameters) }, cancellationToken).ConfigureAwait(false))[0];

    /// <summary>Calls several methods in one bulk request. Throws on the first failing call.</summary>
    public async Task<IReadOnlyList<JsonNode?>> CallManyAsync(IReadOnlyList<WebApiCall> calls, CancellationToken cancellationToken = default)
    {
        var results = await TryCallManyAsync(calls, cancellationToken).ConfigureAwait(false);
        var failed = results.FirstOrDefault(r => r.Error is not null);
        if (failed.Error is not null)
        {
            throw failed.Error;
        }

        return results.Select(r => r.Result).ToList();
    }

    /// <summary>Calls several methods in one bulk request and returns each result or error.</summary>
    public async Task<IReadOnlyList<(JsonNode? Result, WebApiException? Error)>> TryCallManyAsync(
        IReadOnlyList<WebApiCall> calls, CancellationToken cancellationToken = default)
    {
        if (calls.Count == 0)
        {
            return Array.Empty<(JsonNode?, WebApiException?)>();
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var freshLogin = await EnsureLoginAsync(cancellationToken).ConfigureAwait(false);
            var results = await SendAsync(calls, _token, cancellationToken).ConfigureAwait(false);

            // An expired token is reported as "permission denied": log in again once, unless the token is brand new.
            if (_token is not null && !freshLogin && results.Any(r => r.Error?.Code == WebApiException.PermissionDenied))
            {
                _token = null;
                await EnsureLoginAsync(cancellationToken).ConfigureAwait(false);
                results = await SendAsync(calls, _token, cancellationToken).ConfigureAwait(false);
            }

            _lastUse = DateTime.UtcNow;
            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Logs in when needed. Returns true when a new token was obtained.</summary>
    private async Task<bool> EnsureLoginAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_endpoint.Username))
        {
            return false; // Anonymous access with the rights of the "Everybody" user.
        }

        if (_token is not null && (DateTime.UtcNow - _lastUse).TotalSeconds < TokenIdleSeconds)
        {
            return false;
        }

        var password = _env(_endpoint.PasswordEnv!);
        if (password is null)
        {
            throw new InvalidOperationException(
                $"PLC '{Name}': the environment variable '{_endpoint.PasswordEnv}' with the web server password is not set.");
        }

        var login = await SendAsync(
            new[] { new WebApiCall("Api.Login", new JsonObject { ["user"] = _endpoint.Username, ["password"] = password }) },
            null, cancellationToken).ConfigureAwait(false);
        if (login[0].Error is { } error)
        {
            throw error;
        }

        _token = login[0].Result?["token"]?.GetValue<string>()
                 ?? throw new InvalidOperationException($"PLC '{Name}': Api.Login returned no token.");
        _lastUse = DateTime.UtcNow;
        return true;
    }

    private async Task<List<(JsonNode? Result, WebApiException? Error)>> SendAsync(
        IReadOnlyList<WebApiCall> calls, string? token, CancellationToken cancellationToken)
    {
        var first = Interlocked.Add(ref _nextId, calls.Count) - calls.Count + 1;
        var body = new JsonArray();
        for (var i = 0; i < calls.Count; i++)
        {
            var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = first + i, ["method"] = calls[i].Method };
            if (calls[i].Params is not null)
            {
                request["params"] = calls[i].Params!.DeepClone();
            }

            body.Add(request);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, _uri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (token is not null)
        {
            message.Headers.Add("X-Auth-Token", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"PLC '{Name}' ({Address}) did not answer within {_endpoint.TimeoutSeconds} s.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Cannot reach the Web API of PLC '{Name}' ({Address}): {ex.GetBaseException().Message}. " +
                "Check that the web server is enabled on the CPU, the address is right and the certificate is trusted " +
                "(certificateThumbprint in the configuration).", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"PLC '{Name}' Web API returned HTTP {(int)response.StatusCode}: {Truncate(text)}");
            }

            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"PLC '{Name}' Web API returned invalid JSON: {Truncate(text)}", ex);
            }

            var responses = parsed as JsonArray ?? new JsonArray(parsed?.DeepClone());
            var byId = responses.OfType<JsonObject>()
                .Where(r => r["id"] is JsonValue)
                .GroupBy(r => r["id"]!.GetValue<int>())
                .ToDictionary(g => g.Key, g => g.First());

            var results = new List<(JsonNode?, WebApiException?)>(calls.Count);
            for (var i = 0; i < calls.Count; i++)
            {
                if (!byId.TryGetValue(first + i, out var item))
                {
                    results.Add((null, new WebApiException(-32603, "no response for this request", calls[i].Method)));
                }
                else if (item["error"] is JsonObject error)
                {
                    var code = error["code"] is JsonValue c && c.TryGetValue<int>(out var n) ? n : -32603;
                    results.Add((null, new WebApiException(code, error["message"]?.ToString() ?? "unknown error", calls[i].Method)));
                }
                else
                {
                    results.Add((item["result"]?.DeepClone(), null));
                }
            }

            return results;
        }
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text.Substring(0, 300) + "…";

    private static HttpMessageHandler CreateHandler(PlcEndpoint endpoint) => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            ValidateCertificate(endpoint, certificate, errors),
    };

    internal static bool ValidateCertificate(PlcEndpoint endpoint, X509Certificate2? certificate, SslPolicyErrors errors)
    {
        if (endpoint.AllowUntrustedCertificate)
        {
            return true;
        }

        if (endpoint.CertificateThumbprint is { } expected && certificate is not null)
        {
            var actual = expected.Length == 64
                ? BitConverter.ToString(SHA256Hash(certificate.RawData)).Replace("-", string.Empty)
                : certificate.Thumbprint?.ToUpperInvariant();
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        return errors == SslPolicyErrors.None;
    }

    private static byte[] SHA256Hash(byte[] data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
    }

    public void Dispose()
    {
        _http.Dispose();
        _lock.Dispose();
    }
}
