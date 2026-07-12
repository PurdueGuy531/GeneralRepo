using Aetna.Mer.Frameworks.Certificate;
using Aetna.Mer.Frameworks.Webde.Core.Handler;
using Aetna.Mer.Frameworks.Webde.Core.Interface;
using Aetna.Mer.Frameworks.Webde.Core.Log;
using Aetna.Mer.Frameworks.Webde.Core.RedBack;
using Aetna.Mer.Frameworks.Webde.Core.Response;
using Aetna.MER.Frameworks._1mage.Interface;
using Aetna.MER.Frameworks._1mage;
using Aetna.MER.Member.Comm.Infrastructure.Database;
using Aetna.MER.Member.Comm.Infrastructure.Repositories;
using Aetna.MER.Member.Comm.Infrastructure.Repositories.Database;
using Aetna.MER.Member.Comm.Infrastructure.Repositories.Webde;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Serilog;
using Serilog.Context;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using System;

namespace Aetna.MER.Member.Comm.API.Common
{
    public static class Startup
    {
        /// <summary>
        /// Wraps up all the Program.cs builder.Services calls.
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static WebApplicationBuilder ConfigureServices(this WebApplicationBuilder builder)
        {

            //Wire-up primary member services sql server db connection
            builder.Services.AddDbContext<DatabaseContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("Member_Communication_Pref_API_Database")));

            //Wire-up WebDE call -> notice use of 'CL_CSC', which refers to environment setup for that in "rgw5.ini" c:\u2\etc file. Examples of env include oz-dev, oz-test, etc. oz-test (QA) is local-dev default.
            builder.Services.AddTransient<IWebDeHandler<XDocument, Task<WebdeResponse>>>(x => new WebDeHandler(new RedBackNetAdapter("CL_CSC", "Services:DigitalExperience", "DEGetXMLData"), new Logger(builder.Configuration.GetConnectionString("Member_DG_Logging_Database")), "Member Communication"));
            builder.Services.AddTransient<IImageServer>(x => new OneImageServer(new OneImageApi(), builder.Configuration["ImageServerName"]!, builder.Configuration["1mageServerLogin"]!, builder.Configuration["1mageServerPassword"]!, builder.Configuration["DomainHostName"]!));
            builder.Services.AddTransient(options => new X509Store(StoreName.My, StoreLocation.LocalMachine));
            builder.Services.AddTransient<CertificateValidation>();

            builder.Services.AddTransient<Aetna.MER.Member.Comm.Infrastructure.Repositories.Interfaces.IAccountLetterRepo_v2, AccountLetterRepo_v2>();
            builder.Services.AddTransient<Aetna.MER.Member.Comm.Infrastructure.Repositories.Interfaces.IAccountLetterRepo, AccountLetterRepo>();
            builder.Services.AddTransient<Aetna.MER.Member.Comm.Infrastructure.Repositories.Interfaces.IUserRepo, UserRepo>();
            builder.Services.AddTransient<Aetna.MER.Member.Comm.Infrastructure.Repositories.Interfaces.ICommunicationPreferencesRepo, CommunicationPreferencesRepo>();
            builder.Services.AddTransient<Aetna.MER.Member.Comm.Infrastructure.Repositories.Interfaces.IMemberCommPrefRepo, MemberCommPrefRepo>();
            builder.Services.AddTransient<Aetna.MER.Member.Comm.Infrastructure.Repositories.Interfaces.IMemberRepo, MemberRepo>();
            builder.Services.AddTransient<Aetna.MER.Member.Comm.Infrastructure.Repositories.Interfaces.IMessageReadTrackingRepo, MessageReadTrackingRepo>();



            // MediatR registering the handlers
            builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Aetna.MER.Member.Comm.UseCases.HandlerException).Assembly));
            
            //Authentication Schemes
            builder.Services.ConfigureAuthAndClientCerts(builder.Configuration);

            // Versioning
            builder.Services.AddApiVersioning(options =>
            {
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.ReportApiVersions = true;
                options.ApiVersionReader = ApiVersionReader.Combine(new QueryStringApiVersionReader("api-version"));
            });

            builder.Services.Configure<JsonOptions>(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

            builder.Services.AddControllers();

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();

            // Swagger Settings
            builder.Services.AddSwaggerGen(options =>
            {
                options.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
                options.OperationFilter<SwaggerHeaderAttribute>();
            });

            //Kestrel Settings
            builder.Services.Configure<KestrelServerOptions>(options =>
            {
                options.ConfigureHttpsDefaults(options => options.ClientCertificateMode = ClientCertificateMode.AllowCertificate);
            });

            return builder;
        }

        /// <summary>
        /// Wraps up all the Program.cs app 'use' etc. calls.
        /// </summary>
        /// <param name="app"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static IApplicationBuilder ConfigureApp(this WebApplication app, IConfiguration configuration)
        {
            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // Used for serving load balance file
            app.UseStaticFiles();
            app.UseHttpsRedirection();
            app.UseRouting();

            // Grab request header values and set aside in Logging and Http Contexts; also generate random transactionId if needed.
            app.Use(async (context, next) =>
            {
                var clientId = context.Request.Headers["client-id"];
                if (string.IsNullOrEmpty(clientId))
                {
                    //IMPORTANT: ClientId MUST be present, otherwise return 400 bad request!
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonConvert.SerializeObject(new { Status = StatusCodes.Status400BadRequest, Message = "Missing client-id." }));
                    await context.Response.Body.FlushAsync();

                    Log.Information("No Client-Id found in header");
                }
                else
                {
                    //Grab header values and set aside in httpContext.Items for ref as needed. HttpContextExtensions.cs wraps up access to these values.
                    var sessionId = context.Request.Headers["session-id"];

                    var provider = RandomNumberGenerator.Create();
                    var byteArray = new byte[4];
                    provider.GetBytes(byteArray);

                    var transactionId = BitConverter.ToInt32(byteArray, 0);

                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        LogContext.PushProperty("SessionId", sessionId.ToString());
                        context.Items["SessionId"] = sessionId.ToString();
                    }
                    else
                    {
                        // Set transactionid to sessionid for calling CSC if none is provided
                        LogContext.PushProperty("SessionId", transactionId);
                        context.Items["SessionId"] = transactionId;
                    }

                    LogContext.PushProperty("ClientId", clientId.ToString());
                    context.Items["Client"] = clientId.ToString();

                    LogContext.PushProperty("TransactionId", transactionId);
                    context.Items["TransactionId"] = transactionId;

                    await next();
                }
            });

            //Serialog 'RequestLogger' enabled here. Similar to IIS level logs, for each endpoint request.
            app.UseSerilogRequestLogging();

            if (!app.Environment.IsDevelopment())
            {
                app.UseAuthentication();
            }
            app.Use(async (context, next) =>
            {
                if (context.User.Identity.IsAuthenticated)
                {

                    var headerRole = context.Request.Headers["role"];

                    if (!headerRole.IsNullOrEmpty())
                    {
                        var claims = new List<System.Security.Claims.Claim>() { new System.Security.Claims.Claim(ClaimTypes.Role, headerRole.ToString()) };
                        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims));
                    }
                }

                await next();
            });
            app.UseAuthorization();

            app.MapControllers();

            return app;
        }

        /// <summary>
        /// Add X509 Cert Machine Store, and wire up and retrieve 'Roles' from header and add as user claim.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        private static IServiceCollection ConfigureAuthAndClientCerts(this IServiceCollection services, IConfiguration config)
        {
            //Authentication Schemes
            services.AddTransient(options => new X509Store(StoreName.My, StoreLocation.LocalMachine));
            services.AddTransient<CertificateValidation>();
            services.AddAuthentication(option => { option.DefaultScheme = "certificate"; })
                    .AddCertificate("certificate", options =>
                    {
                        options.AllowedCertificateTypes = CertificateTypes.All;
                        options.RevocationMode = X509RevocationMode.NoCheck;

                        options.Events = new CertificateAuthenticationEvents
                        {
                            OnCertificateValidated = context =>
                            {
                                try
                                {
                                    var SecurityRoles = config.GetSection("Security").Get<Security>();

                                    var validationService = context.HttpContext.RequestServices.GetRequiredService<CertificateValidation>();

                                    if (SecurityRoles != null && SecurityRoles.CertificateSecurity != null)
                                    {
                                        var certificateSecurity = SecurityRoles.CertificateSecurity.FirstOrDefault(x => x.Subject != null && x.Subject.Equals(context.ClientCertificate.GetNameInfo(X509NameType.SimpleName, false), StringComparison.OrdinalIgnoreCase));
                                     
                                        if (certificateSecurity == null)
                                        {
                                            context.Fail("Invalid certificate");

                                            return Task.CompletedTask;
                                        }

                                        if (validationService.ValidateCertificateBySubjectName(context.ClientCertificate, certificateSecurity.Subject))
                                        {
                                            if (!string.IsNullOrEmpty(certificateSecurity.Role))
                                            {
                                                var claims = new List<System.Security.Claims.Claim>() { new System.Security.Claims.Claim(ClaimTypes.Role, certificateSecurity.Role) };
                                                context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
                                            }

                                            context.Success();
                                        }
                                    }
                                    else
                                    {
                                        context.Fail("Invalid certificate");
                                    }

                                }
                                catch (Exception exception)
                                {
                                    Log.ForContext("StackTrace", exception.StackTrace).Error(exception.Message);
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

    }
}
