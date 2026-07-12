using System.Security.Cryptography;
using System.Text;

namespace Aetna.MER.MCAuthAPI.Common
{
    /// <summary>
    /// Wires up an independent, additive security gate: incoming requests must present a matching
    /// 'X-Api-Secret' header value, compared (constant-time) against the 'MCAuthApiSecret' configuration
    /// value. This check is entirely separate from client-certificate authentication - it is controlled
    /// solely by 'Security:RequireSecretHeader' and runs regardless of whether client-certificate auth
    /// is enabled.
    /// </summary>
    public static class ConfigSecret
    {
        private const string SecretHeaderName = "X-Api-Secret";

        /// <summary>
        /// Registers the 'X-Api-Secret' header-validation middleware when 'Security:RequireSecretHeader'
        /// is true. No-op (returns <paramref name="app"/> unchanged) when the feature is disabled.
        /// </summary>
        /// <param name="app"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown at startup (not per-request) when the feature is enabled but 'MCAuthApiSecret' has no
        /// configured value, so a misconfigured deployment fails fast and loudly.
        /// </exception>
        public static IApplicationBuilder UseApiSecretHeader(this IApplicationBuilder app, IConfiguration configuration)
        {
            var security = configuration.GetSection("Security").Get<Security>();

            if (security?.RequireSecretHeader != true)
            {
                // Feature disabled - don't wire any middleware, zero request-time overhead.
                return app;
            }

            var expectedSecret = security?.MCAuthApiSecret;

            if (string.IsNullOrWhiteSpace(expectedSecret))
            {
                throw new InvalidOperationException(
                    "Security:RequireSecretHeader is enabled but 'Security:MCAuthApiSecret' is not configured. " +
                    "Set it in appsettings (non-prod) or as the Security__MCAuthApiSecret environment variable (prod) " +
                    "before starting the service.");
            }

            var expectedSecretBytes = Encoding.UTF8.GetBytes(expectedSecret);

            return app.Use(async (context, next) =>
            {
                var providedSecret = context.Request.Headers[SecretHeaderName].ToString();

                if (string.IsNullOrEmpty(providedSecret) ||
                    !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(providedSecret), expectedSecretBytes))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Unauthorized");
                    return;
                }

                await next();
            });
        }
    }
}
