namespace PC_Optimizer.Services.Licensing;

/// <summary>Solo la clave PÚBLICA (ECDSA P-256, SubjectPublicKeyInfo en Base64). La privada vive fuera del repositorio.</summary>
[System.Reflection.Obfuscation(Exclude = false)]
internal static class LicenseKeys
{
    /// <summary>
    /// Interruptor de licencias. false = la app funciona para todos, sin pantalla ni avisos de licencia.
    /// Para volver a exigir licencia: ponerlo en true y recompilar (ver docs/LICENCIAS.md, «Reactivar»).
    /// </summary>
    public const bool Enforced = false;

    public const string PublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEQSn/g2jH+mded90B9UKI+Euu6UyUmVOl+sh6OnPq+ZB3/9yOded9++jKz1+WF2Zw/YCGzyYfEPyb3l05NmTwqQ==";
}
