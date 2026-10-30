using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PC_Optimizer.Services.Licensing;

/// <summary>Contenido firmado de una licencia (docs/LICENCIAS.md, sección 2).</summary>
public sealed class LicensePayload
{
    [JsonPropertyName("v")] public int Version { get; set; } = 1;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("machine")] public string Machine { get; set; } = string.Empty;
    /// <summary>Fecha de emisión, yyyy-MM-dd (UTC).</summary>
    [JsonPropertyName("issued")] public string Issued { get; set; } = string.Empty;
    /// <summary>Último día de validez, yyyy-MM-dd (UTC); null = no vence.</summary>
    [JsonPropertyName("expires")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Expires { get; set; }
    /// <summary>Versión mínima de la app para la que vale la licencia.</summary>
    [JsonPropertyName("minVersion")] public string MinVersion { get; set; } = "1.0.0";
}

/// <summary>
/// Formato de texto: BASE64URL(carga JSON UTF-8) + "." + BASE64URL(firma ECDSA P-256/SHA-256, IEEE P1363).
/// La firma cubre los bytes exactos de la carga, sin volver a serializar.
/// Este archivo no depende de WPF: la herramienta generadora (fuera del repositorio) lo enlaza tal cual.
/// </summary>
[System.Reflection.Obfuscation(Exclude = false)]
public static class LicenseFormat
{
    public static string Encode(LicensePayload payload, ECDsa privateKey)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = privateKey.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return B64(bytes) + "." + B64(signature);
    }

    /// <summary>Separa y comprueba la firma. Devuelve false si el texto está incompleto, alterado o mal firmado.</summary>
    public static bool TryDecode(string? text, byte[] publicKeySpki, out LicensePayload payload)
    {
        payload = new LicensePayload();
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = string.Concat(text.Where(c => !char.IsWhiteSpace(c))).Split('.');
            if (parts.Length != 2) return false;
            var bytes = FromB64(parts[0]);
            var signature = FromB64(parts[1]);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
            if (!key.VerifyData(bytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return false;
            var parsed = JsonSerializer.Deserialize<LicensePayload>(Encoding.UTF8.GetString(bytes));
            if (parsed is null || parsed.Version != 1) return false;
            payload = parsed;
            return true;
        }
        catch { return false; }
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
