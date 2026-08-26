using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Nostos.Core.Benchmark;

/// <param name="RoundTripMs">Round trip in milliseconds. Meaningless unless Succeeded.</param>
/// <param name="Succeeded">False for a timeout, a refusal, or an unreachable host.</param>
public readonly record struct LatencySample(double RoundTripMs, bool Succeeded);

/// <summary>How to reach the thing being measured.</summary>
public enum ProbeKind
{
    /// <summary>ICMP echo. What `ping` does.</summary>
    Icmp,

    /// <summary>Time to complete a TCP handshake to a port, then close it.</summary>
    TcpConnect,
}

/// <summary>
/// Takes latency samples, one at a time, without touching anything.
///
/// Read-only by construction: it opens outbound connections and nothing else. Nothing here needs
/// elevation and nothing here is in <c>Nostos.Win32</c>, because none of it is
/// Windows-specific -- which also means the statistics above it are testable on any runner.
///
/// <para><b>Two kinds, because ICMP alone is misleading.</b> Routers routinely handle ICMP on a
/// slow path, rate-limit it, or deprioritise it relative to real traffic, so an ICMP number can
/// be worse than your game's actual experience -- or, if the middle box answers on the router's
/// behalf, far better. A TCP handshake to a port something is really listening on travels the
/// path that game traffic travels. Neither is the truth on its own; disagreement between them is
/// itself informative.</para>
/// </summary>
public sealed class LatencyProbe
{
    /// <summary>
    /// Gap between samples.
    ///
    /// Not zero, and not tiny. Back-to-back probes measure how fast the local stack can loop,
    /// and they invite the rate limiter on every router in the path to start dropping them,
    /// which then reads as packet loss that is not there. A game sends tens of packets a second;
    /// this is the same order.
    /// </summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(50);

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Samples that are taken and thrown away before measuring starts.
    ///
    /// The first packet to a host pays for ARP or neighbour discovery, a DNS lookup, and on
    /// Wi-Fi possibly a power-save wake-up. Including it makes every run's maximum a
    /// measurement of cold start rather than of the connection.
    /// </summary>
    public const int WarmupSamples = 3;

    public async Task<IReadOnlyList<LatencySample>> RunAsync(
        string host,
        int port,
        ProbeKind kind,
        int samples,
        TimeSpan? interval = null,
        TimeSpan? timeout = null,
        IProgress<LatencySample>? progress = null,
        CancellationToken ct = default)
    {
        var gap = interval ?? DefaultInterval;
        var limit = timeout ?? DefaultTimeout;

        for (var i = 0; i < WarmupSamples; i++)
        {
            ct.ThrowIfCancellationRequested();
            await OneAsync(host, port, kind, limit, ct).ConfigureAwait(false);
            await Task.Delay(gap, ct).ConfigureAwait(false);
        }

        var taken = new List<LatencySample>(samples);
        for (var i = 0; i < samples; i++)
        {
            ct.ThrowIfCancellationRequested();

            var sample = await OneAsync(host, port, kind, limit, ct).ConfigureAwait(false);
            taken.Add(sample);
            progress?.Report(sample);

            if (i < samples - 1)
                await Task.Delay(gap, ct).ConfigureAwait(false);
        }

        return taken;
    }

    private static async Task<LatencySample> OneAsync(
        string host, int port, ProbeKind kind, TimeSpan timeout, CancellationToken ct)
        => kind == ProbeKind.Icmp
            ? await IcmpAsync(host, timeout, ct).ConfigureAwait(false)
            : await TcpAsync(host, port, timeout, ct).ConfigureAwait(false);

    private static async Task<LatencySample> IcmpAsync(string host, TimeSpan timeout, CancellationToken ct)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(host, timeout, cancellationToken: ct).ConfigureAwait(false);

            // Ping.RoundtripTime is whole milliseconds, which is far too coarse when a good
            // connection is 12 ms and the thing being measured moves it by less than one. The
            // stopwatch around the call includes a little managed overhead and is still the
            // better number; RoundtripTime is only consulted to know whether it worked.
            return reply.Status == IPStatus.Success
                ? new LatencySample(reply.RoundtripTime, true)
                : new LatencySample(0, false);
        }
        catch (Exception e) when (e is PingException or SocketException)
        {
            return new LatencySample(0, false);
        }
    }

    private static async Task<LatencySample> TcpAsync(
        string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

        // Nagle off on the measuring socket itself. It would not affect a bare handshake, but a
        // measurement tool that leaves a latency-adding option on while measuring latency is
        // asking to be quoted back at itself.
        socket.NoDelay = true;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await socket.ConnectAsync(host, port, deadline.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new LatencySample(stopwatch.Elapsed.TotalMilliseconds, true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new LatencySample(0, false);
        }
        catch (SocketException)
        {
            // A refusal still completed a round trip, but not the same one a handshake does,
            // and mixing the two would quietly bias the result. Counted as a loss instead.
            return new LatencySample(0, false);
        }
    }
}
