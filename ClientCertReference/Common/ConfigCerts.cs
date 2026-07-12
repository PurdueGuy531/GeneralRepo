using Microsoft.AspNetCore.Authentication.Certificate;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;

namespace Aetna.MER.MCAuthAPI.Common
{
    /// <summary>
    /// Description of the client certificate approach in this application:
    /// 
    /// Our aim is to remove any network dependencies, keeping it offline, while still having strong controls. Those controls are:
    ///• Root pinning to CVSHealthRoot(only our corporate PKI is accepted).
    ///• Expiration + signature validation on the full chain(enforced by chain.Build below).
    ///• Subject-CN allow-list in appsettings.
    ///• Optional "install-gate-check"(the "RequireInstalledClientCertificate" in appsettings) as our immediate revoke mechanism — no network needed.
    ///  Just remove the cert from the host store → denied next request. This is the answer to "how do I revoke a compromised cert without CRL?"
    ///  
    /// The only residual gap is the automatic-revocation window between compromise and natural expiry, which we mitigate with(a) the install-gate kill switch and(b) reasonably short cert lifetimes.
    /// </summary>
    public static class ConfigCerts
    {
        /// <summary>
        /// Add X509 Cert Machine Store, and wire up and retrieve 'Roles' from header and add as user claim.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        public static IServiceCollection ConfigureAuthAndClientCerts(this IServiceCollection services, IConfiguration config)
        {
            //Authentication Schemes
            services.AddAuthentication(option => { option.DefaultScheme = "certificate"; })
                    .AddCertificate("certificate", options =>
                    {
                        options.AllowedCertificateTypes = CertificateTypes.Chained;//updated to 
                        options.RevocationMode = X509RevocationMode.NoCheck;

                        options.Events = new CertificateAuthenticationEvents
                        {
                            OnCertificateValidated = context =>
                            {
                                try
                                {
                                    var SecurityRoles = config.GetSection("Security").Get<Security>();

                                    if (SecurityRoles != null && SecurityRoles.CertificateSecurity != null)
                                    {
                                        var certificateSecurity = SecurityRoles.CertificateSecurity.FirstOrDefault(x => x.Subject != null && x.Subject.Equals(context.ClientCertificate.GetNameInfo(X509NameType.SimpleName, false), StringComparison.OrdinalIgnoreCase));

                                        if (certificateSecurity == null)
                                        {
                                            context.Fail("Invalid certificate");

                                            return Task.CompletedTask;
                                        }

                                        // Trust gate: cert must cryptographically chain to a host-trusted root whose CN is allow-listed.
                                        var trustedRoots = SecurityRoles.TrustedRootCommonNames ?? [];

                                        if (trustedRoots.Count == 0 || !ChainsToTrustedRoot(context.ClientCertificate, trustedRoots))
                                        {
                                            context.Fail("Certificate does not chain to a trusted corporate root");

                                            return Task.CompletedTask;
                                        }

                                        // Belt-and-suspenders gate (Option C): optionally require the caller's exact certificate
                                        // (matched by thumbprint) to be installed in the server's store. Fail-closed: any error
                                        // or a missing certificate results in a clean deny, never an exception into the pipeline.
                                        if (SecurityRoles.RequireInstalledClientCertificate == true
                                            && !IsInstalledInStore(context.ClientCertificate))
                                        {
                                            context.Fail("Certificate is not installed in the server certificate store");

                                            return Task.CompletedTask;
                                        }

                                        if (!string.IsNullOrEmpty(certificateSecurity.Role))
                                        {
                                            var claims = new List<System.Security.Claims.Claim>() { new System.Security.Claims.Claim(ClaimTypes.Role, certificateSecurity.Role) };
                                            context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
                                        }

                                        context.Success();
                                    }
                                    else
                                    {
                                        context.Fail("Invalid certificate");
                                    }

                                }
                                catch (Exception exception)
                                {
                                    //Log.ForContext("StackTrace", exception.StackTrace).Error(exception.Message);
                                    context.Fail("Invalid certificate");
                                }

                                return Task.CompletedTask;
                            },
                            OnAuthenticationFailed = context =>
                            {

                                context.Fail("Invalid certificate");

                                return Task.CompletedTask;
                            }
                        };
                    })
                    .AddCertificateCache(options =>
                    {
                        options.CacheSize = 1024;
                        options.CacheEntryExpiration = TimeSpan.FromMinutes(2);
                    });
            return services;
        }

        private static bool ChainsToTrustedRoot(X509Certificate2 clientCertificate, IReadOnlyCollection<string> trustedRootCommonNames)
        {
            using var chain = new X509Chain();
            // Revocation (CRL/OCSP) is intentionally NOT checked. This is an internal, corporate-network service using
            // PKI-issued client certs that are root-pinned below. Online revocation would make validation network-dependent
            // and fail-closed if the CA's CRL/OCSP endpoints are slow or unreachable. To revoke a caller immediately without
            // revocation infrastructure, enable 'RequireInstalledClientCertificate' (thumbprint install-gate) and remove the
            // caller's certificate from the server store. The chain build below still enforces expiration and signature validity.
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.System;       // must terminate in a host-trusted root. Aka, the client cert's ROOT cert must be installed and still valid on host machine. We also validate it's the correct expected cert by comparing it's name to the accepted one in appsettings.json.
            chain.ChainPolicy.DisableCertificateDownloads = true; // don't go to the network for missing intermediate certs (aka, an AIA fetch). Intermediate certs are installed on hosting machines.

            // Validate the whole chain FIRST; only a built chain terminates in a genuinely trusted root.
            if (!chain.Build(clientCertificate))
            {
                return false;
            }

            // ChainElements is ordered leaf -> intermediates -> root.
            var root = chain.ChainElements[^1].Certificate;
            var rootCommonName = root.GetNameInfo(X509NameType.SimpleName, forIssuer: false);

            return trustedRootCommonNames.Any(cn => string.Equals(cn, rootCommonName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Fail-closed check that the caller's certificate (matched by thumbprint) is installed in the
        /// server's certificate store (Local Computer \ Personal \ Certificates, i.e. LocalMachine\My).
        /// Never throws: any error (store unavailable, access denied, not found) results in <c>false</c>
        /// so the caller can issue a clean deny.
        /// </summary>
        private static bool IsInstalledInStore(X509Certificate2 clientCertificate)
        {
            try
            {
                // We ALWAYS install certificates in Local Computer \ Personal \ Certificates.
                using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

                // Match on thumbprint for an exact, cryptographic identity binding (not a subject substring).
                var matches = store.Certificates.Find(X509FindType.FindByThumbprint, clientCertificate.Thumbprint, validOnly: false);

                return matches.Count > 0;
            }
            catch
            {
                // Fail-closed: never allow on error, and never surface an exception into the auth pipeline.
                return false;
            }
        }
    }
}
