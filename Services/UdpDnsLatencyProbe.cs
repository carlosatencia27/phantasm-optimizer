using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PC_Optimizer.Services;

/// <summary>Qué contestó el servidor a un nombre que NO existe (inventado en cada intento).</summary>
public enum DnsReplyKind
{
    /// <summary>Sin respuesta dentro del tiempo máximo.</summary>
    Timeout,
    /// <summary>
    /// Respuesta negativa legítima del servidor autoritativo: «el nombre no existe» (NXDOMAIN) o «sin datos»
    /// (sin respuesta y con registro de autoridad). Es lo esperado para un nombre inventado.
    /// </summary>
    NameNotFound,
    /// <summary>Devolvió una dirección para un nombre inexistente: respuesta alterada (p. ej. redirección del operador).</summary>
    Rewritten,
    /// <summary>Otro código (error del servidor, rechazo, sin datos...).</summary>
    OtherCode
}

public sealed record UniqueNameSample(DnsReplyKind Kind, double Ms, int ResponseCode);

/// <summary>Medición con nombres nuevos en cada consulta: ninguna caché puede contestarlos.</summary>
public interface IUniqueNameDnsProbe
{
    IReadOnlyList<UniqueNameSample> Probe(string server, string baseDomain, int count, int timeoutMs, CancellationToken cancellation);
}

/// <summary>
/// Mide cuánto tarda un servidor DNS en contestar enviándole una consulta A por UDP (puerto 53, solo
/// lectura de la red). No pasa por el resolver de Windows, así que no hay caché local que falsee la
/// medida. Consultas una detrás de otra para no sumar ruido; si ninguna obtiene respuesta devuelve null.
/// </summary>
public sealed class UdpDnsLatencyProbe : IDnsServerLatencyProbe, IUniqueNameDnsProbe
{
    /// <summary>Tope total por medición (varias consultas seguidas con tiempo máximo cada una).</summary>
    public static readonly TimeSpan OverallCap = TimeSpan.FromSeconds(20);

    private readonly int _port;

    public UdpDnsLatencyProbe(int port = 53) => _port = port;

    public double? MeasureMedian(string server, IReadOnlyList<string> domains, int repetitions, int timeoutMs,
        CancellationToken cancellation)
    {
        if (!IPAddress.TryParse(server, out var address)) return null;

        var samples = new List<double>();
        var overall = Stopwatch.StartNew();
        foreach (var domain in domains)
        {
            for (var round = 0; round < Math.Max(1, repetitions); round++)
            {
                if (cancellation.IsCancellationRequested || overall.Elapsed > OverallCap)
                    return Median(samples);

                var ms = QueryOnce(address, domain, timeoutMs, cancellation);
                if (ms.HasValue) samples.Add(ms.Value);
            }
        }
        return Median(samples);
    }

    /// <summary>Tope total de una tanda de nombres únicos.</summary>
    public static readonly TimeSpan UniqueNamesCap = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Pregunta por <c>etiqueta-aleatoria.dominio-base</c> (hoy iana.org): el nombre no existe ni se ha
    /// preguntado nunca, así que el servidor no puede responder desde su caché y tiene que consultar hacia
    /// afuera. Cada muestra dice qué contestó y cuánto tardó. Como mucho 8 consultas por tanda.
    /// </summary>
    public IReadOnlyList<UniqueNameSample> Probe(string server, string baseDomain, int count, int timeoutMs,
        CancellationToken cancellation)
    {
        var samples = new List<UniqueNameSample>();
        if (!IPAddress.TryParse(server, out var address)) return samples;

        var overall = Stopwatch.StartNew();
        for (var i = 0; i < Math.Clamp(count, 1, 8); i++)
        {
            if (cancellation.IsCancellationRequested || overall.Elapsed > UniqueNamesCap) break;
            samples.Add(QueryUnique(address, BuildUniqueName(baseDomain), timeoutMs, cancellation));
        }
        return samples;
    }

    /// <summary>Etiqueta aleatoria (pcopt- + 12 hexadecimales) bajo el dominio base.</summary>
    public static string BuildUniqueName(string baseDomain)
        => "pcopt-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant() + "." + baseDomain.Trim('.');

    /// <summary>Clasifica una respuesta a un nombre que no existe. null = no es una respuesta a esa consulta.</summary>
    public static (DnsReplyKind Kind, int ResponseCode)? ClassifyUniqueReply(byte[] buffer, int length, ushort id)
    {
        if (buffer == null || length < 12) return null;
        if ((ushort)((buffer[0] << 8) | buffer[1]) != id) return null;
        if ((buffer[2] & 0x80) == 0) return null;               // no es una respuesta
        var rcode = buffer[3] & 0x0F;
        var answers = (buffer[6] << 8) | buffer[7];
        var authority = (buffer[8] << 8) | buffer[9];
        if (rcode == 3) return (DnsReplyKind.NameNotFound, rcode);                    // NXDOMAIN
        if (rcode == 0 && answers > 0) return (DnsReplyKind.Rewritten, rcode);        // dirección para un nombre inexistente
        if (rcode == 0 && authority > 0) return (DnsReplyKind.NameNotFound, rcode);   // «sin datos» con SOA: negativa legítima
        return (DnsReplyKind.OtherCode, rcode);
    }

    private UniqueNameSample QueryUnique(IPAddress server, string name, int timeoutMs, CancellationToken cancellation)
    {
        try
        {
            var id = (ushort)RandomNumberGenerator.GetInt32(0, 65536);
            var packet = BuildQuery(id, name);
            using var socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveTimeout = Math.Min(200, Math.Max(1, timeoutMs)),
                SendTimeout = timeoutMs
            };
            socket.Connect(new IPEndPoint(server, _port));

            var buffer = new byte[1500];
            var watch = Stopwatch.StartNew();
            socket.Send(packet);
            while (watch.ElapsedMilliseconds < timeoutMs && !cancellation.IsCancellationRequested)
            {
                int length;
                try { length = socket.Receive(buffer); }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut) { continue; }

                var elapsed = watch.Elapsed.TotalMilliseconds;
                var reply = ClassifyUniqueReply(buffer, length, id);
                if (reply.HasValue) return new UniqueNameSample(reply.Value.Kind, elapsed, reply.Value.ResponseCode);
            }
            return new UniqueNameSample(DnsReplyKind.Timeout, timeoutMs, -1);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or ArgumentException)
        {
            return new UniqueNameSample(DnsReplyKind.Timeout, 0, -1);
        }
    }

    private double? QueryOnce(IPAddress server, string domain, int timeoutMs, CancellationToken cancellation)
    {
        try
        {
            var id = (ushort)RandomNumberGenerator.GetInt32(0, 65536);
            var packet = BuildQuery(id, domain);
            using var socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
            {
                // Se espera en tramos cortos para poder atender «Cancelar» sin quedar colgado hasta el tiempo máximo.
                ReceiveTimeout = Math.Min(200, Math.Max(1, timeoutMs)),
                SendTimeout = timeoutMs
            };
            socket.Connect(new IPEndPoint(server, _port));

            var buffer = new byte[1500];
            var watch = Stopwatch.StartNew();
            socket.Send(packet);
            while (watch.ElapsedMilliseconds < timeoutMs && !cancellation.IsCancellationRequested)
            {
                int length;
                try { length = socket.Receive(buffer); }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut) { continue; }

                var elapsed = watch.Elapsed.TotalMilliseconds;
                if (IsSuccessfulResponse(buffer, length, id)) return elapsed;
                // respuesta ajena o con error: se sigue esperando hasta el tiempo máximo
            }
            return null;
        }
        catch (SocketException) { return null; }
        catch (ObjectDisposedException) { return null; }
        catch (ArgumentException) { return null; } // nombre de dominio no válido: no se mide
    }

    /// <summary>Consulta DNS estándar (RFC 1035): cabecera con recursión deseada y una pregunta tipo A.</summary>
    public static byte[] BuildQuery(ushort id, string name)
    {
        var bytes = new List<byte>
        {
            (byte)(id >> 8), (byte)id,   // identificador
            0x01, 0x00,                  // banderas: consulta estándar, recursión deseada
            0x00, 0x01,                  // una pregunta
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        foreach (var label in name.Trim('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = Encoding.ASCII.GetBytes(label);
            if (text.Length is 0 or > 63) throw new ArgumentException("Etiqueta DNS no válida.", nameof(name));
            bytes.Add((byte)text.Length);
            bytes.AddRange(text);
        }

        bytes.Add(0);                    // fin del nombre
        bytes.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // tipo A, clase IN
        return bytes.ToArray();
    }

    /// <summary>true si es la respuesta a esa consulta (mismo id, bit de respuesta) y Windows/servidor dicen «sin error».</summary>
    public static bool IsSuccessfulResponse(byte[] buffer, int length, ushort id)
    {
        if (buffer == null || length < 12) return false;
        var responseId = (ushort)((buffer[0] << 8) | buffer[1]);
        if (responseId != id) return false;
        var isResponse = (buffer[2] & 0x80) != 0;
        var rcode = buffer[3] & 0x0F;
        return isResponse && rcode == 0;
    }

    private static double? Median(List<double> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.OrderBy(v => v).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }
}
