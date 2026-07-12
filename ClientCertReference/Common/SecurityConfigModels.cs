namespace Aetna.MER.MCAuthAPI.Common
{
    public sealed class Security
    {
        public bool? EnableClientCertificate { get; set; } = false;
        public List<CertificateSecurity>? CertificateSecurity { get; set; }

        // Root CA(s) we accept, matched by Subject CN on the cryptographically-validated chain.
        public List<string>? TrustedRootCommonNames { get; set; }

        // Belt-and-suspenders gate: when true, the caller's certificate (matched by thumbprint) must
        // also be installed in the server's certificate store (Local Computer \ Personal \ Certificates),
        // otherwise the request is denied. Fail-closed: if the cert is not found (or the store cannot be
        // read) the request is rejected.
        public bool? RequireInstalledClientCertificate { get; set; } = false;

        // Independent, additive security gate: when true, every incoming request must present a
        // matching 'X-Api-Secret' header value, compared against the 'MCAuthApiSecret' value below.
        // This is entirely separate from client-certificate authentication and runs regardless of
        // 'EnableClientCertificate'. Fails fast at startup if enabled but 'MCAuthApiSecret' has no
        // value configured.
        public bool? RequireSecretHeader { get; set; } = false;

        // Expected value of the 'X-Api-Secret' request header, only enforced when 'RequireSecretHeader'
        // above is true. In production this should be supplied via a 'Security__MCAuthApiSecret' Windows
        // Server system environment variable (double-underscore maps to this nested config key) rather
        // than committed to appsettings.Production.json.
        public string? MCAuthApiSecret { get; set; }
    }
    public sealed class CertificateSecurity
    {
        public string? Subject { get; set; }
        public string? Role { get; set; }       
    }
}
