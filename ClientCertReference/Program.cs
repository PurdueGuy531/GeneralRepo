using Aetna.MER.MCAuthAPI.Common;
using Aetna.MER.MCAuthAPI.DataAccess.Configuration;
using Aetna.MER.MCAuthAPI.DataAccess.Interfaces;
using Aetna.MER.MCAuthAPI.DataAccess.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Authentication Schemes
var securityConfig = builder.Configuration.GetSection("Security").Get<Security>();
var clientCertEnabled = securityConfig?.EnableClientCertificate == true;

if (clientCertEnabled)
{
    builder.Services.ConfigureAuthAndClientCerts(builder.Configuration);

    // Actually REQUIRE an authenticated (certificate) user on every endpoint.
    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });
}

builder.Services.AddControllers();
builder.Services.AddOpenApi(); // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();

// Data Access - Repositories
// ActiveDirectoryRepository is Windows-only (System.DirectoryServices.AccountManagement). This API
// is hosted on Windows Server, so we guard the registration and fail fast on any other platform.
if (OperatingSystem.IsWindows())
{
    // Active Directory service-account credentials (optional) - used only to bind the post-authentication
    // account lookup (UserPrincipal.FindByIdentity), never for validating the end user's own credentials.
    // In production this is populated from the CustActiveDirectory__Username / CustActiveDirectory__Password
    // environment variables (the existing convention shared with other .NET services in this environment).
    builder.Services.Configure<CustActiveDirectory>(builder.Configuration.GetSection("CustActiveDirectory"));

    builder.Services.AddScoped<IActiveDirectoryRepository, ActiveDirectoryRepository>();
}
else
{
    throw new PlatformNotSupportedException("Active Directory authentication requires Windows.");
}

//Kestrel Settings - Only negotiate client certs when the feature is enabled.
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.ConfigureHttpsDefaults(httpsOptions =>
        httpsOptions.ClientCertificateMode = clientCertEnabled
            ? ClientCertificateMode.AllowCertificate   // or RequireCertificate (see note), but that can have issues if behind proxy/fw
            : ClientCertificateMode.NoCertificate);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseApiSecretHeader(builder.Configuration);

if (clientCertEnabled)
{
    app.UseAuthentication();   // <-- was missing
}

//if (!app.Environment.IsDevelopment())
//{
//    app.UseAuthentication();
//}
app.UseAuthorization();

app.MapControllers();

app.Run();
