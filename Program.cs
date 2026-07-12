using Aetna.MER.Member.Comm.API.Common;
using Serilog;


//1. Logging Setup (must be at top of Program.cs)
Log.Logger = LoggingSetup.GetSerilogger();//Assign to Static Serilog 'Log' object.

//2. Create WebHost and ConfigureServices and ConfigureApp
try
{
    Log.Information("Starting ASP.NET API web host");

    //NOTE: Builder, ConfigureServices, App and ConfigureApp must be in this very particular order. Do not change order!
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog(); //initialize serilog (overrides built-in asp.net logging)
    builder.ConfigureServices();

    var app = builder.Build();
    app.ConfigureApp(builder.Configuration);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}




