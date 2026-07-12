using Serilog.Events;
using Serilog.Sinks.MSSqlServer;
using Serilog;
using System.Data;
using System.Diagnostics;
using Serilog.Filters;

namespace Aetna.MER.Member.Comm.API.Common
{
    public static class LoggingSetup
    {
        /// <summary>
        /// Configure Serilog for this App. Called from Program.cs.
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="isDevEnvironment"></param>
        /// <remarks>
        /// This one method is all that's needed to wire-up all the logging for this app. No combination of appSettings.etc.json files needed for logging setup.
        /// Only thing different PER environment is the default connection string found in the appSettings files. Except if 'IsDevelopment' is on, then internal serilog errs shown.
        /// </remarks>
        /// <returns></returns>
        public static Serilog.Core.Logger GetSerilogger()
        {
            //If local development machine, turn on show serilog internal errors. ex: if you wired up columns wrong, serilog will show a helpful error if this SelfLog is enabled:
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
            {
                Trace.Listeners.Add(new ConsoleTraceListener());
                Serilog.Debugging.SelfLog.Enable(msg => System.Diagnostics.Trace.WriteLine(msg));
            }

            //DEV-NOTE: This configBuilder is needed because at this point in lifecycle of program/startup process this info hasn't been loaded. That occurs with builder.Host.Build().
            var loggerInitialConfig = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "PROD3"}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            //Wire up default serilog settings and overrides (most are applicable to asp.net web, vs API proj)
            var configLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                //.ReadFrom.Configuration(loggerInitialConfig) //Load any initial settings from appSettings and then override below.          
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning) //blocks out a TON of info log entries from EF core by setting this to Warning.
                .MinimumLevel.Override("Microsoft.AspNetCore.Mvc.ViewFeatures.Filters.ValidateAntiforgeryTokenAuthorizationFilter", LogEventLevel.Information) //404 val token issues are shown with this.               
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning) //Filter out ASP.NET Core infrastructure logs that are information and below
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning) //Filter out ASP.NET Core infrastructure logs that are information and below                                                                                            
                .Enrich.FromLogContext() //Enables Serilog to add 'using BeginScope()' values to log context as well as data from Request enricher (wired up in startup.cs).                                     
                .WriteTo.Logger(lc => lc
                    //--- 1. Add [Information] Table here-------------------------------------------
                    .WriteTo.MSSqlServer(
                        connectionString: loggerInitialConfig.GetConnectionString("Member_Communication_Pref_API_Database"),
                        sinkOptions: new MSSqlServerSinkOptions { TableName = "InformationLogs", AutoCreateSqlTable = false },
                        columnOptions: GetSerilogInfoDBColumns())
                    .Filter.ByIncludingOnly(x => x.Level == LogEventLevel.Information)//&& x.Properties["Audit"] == null
                    .Filter.ByExcluding(Matching.WithProperty("Audit"))
                )
                .WriteTo.Logger(lc => lc
                    //--- 2. Add [Exception] Table here (handles err, fatal and warn)----------------
                    .WriteTo.MSSqlServer(
                        connectionString: loggerInitialConfig.GetConnectionString("Member_Communication_Pref_API_Database"),
                        sinkOptions: new MSSqlServerSinkOptions { TableName = "ExceptionLogs", AutoCreateSqlTable = false },
                        columnOptions: GetSerilogExceptionDBColumns())
                    .Filter.ByIncludingOnly(x => x.Level == LogEventLevel.Error || x.Level == LogEventLevel.Fatal || x.Level == LogEventLevel.Warning)
                );

            //Loop through AuditTables enum and wire-up serilog db sink to a table name that matches enum name. To add new audit tables, just add a new enum, that's it!
            foreach (var auditTableName in Enum.GetNames(typeof(AuditTables)))
            {
                configLogger.WriteTo.Logger(lc => lc
                    //--- 3. Add Audit Tables here-----------------------------------------------------
                    .WriteTo.MSSqlServer(
                        restrictedToMinimumLevel: LogEventLevel.Information, //Information or higher logs go to SQL Server DB Log table!
                        connectionString: loggerInitialConfig.GetConnectionString("Member_Communication_Pref_API_Database"),
                        sinkOptions: new MSSqlServerSinkOptions { TableName = auditTableName, AutoCreateSqlTable = false },
                        columnOptions: GetSerilogAuditDBColumns())
                    .Filter.ByIncludingOnly(Matching.WithProperty("Table", auditTableName))
                );
            }
            return configLogger.CreateLogger();
        }

        #region Private Helper Methods

        /// <summary>
        /// Get column definitions for 'InformationLogs' db table.
        /// </summary>
        /// <returns></returns>
        private static ColumnOptions GetSerilogInfoDBColumns()
        {            
            var columnOptions = new ColumnOptions { AdditionalColumns = new List<SqlColumn>() };
            //Add custom table columns
            columnOptions.AdditionalColumns.Add(new SqlColumn { ColumnName = "ClientId", DataType = SqlDbType.VarChar, DataLength = 150, AllowNull = true });
            columnOptions.AdditionalColumns.Add(new SqlColumn { ColumnName = "TransactionId", DataType = SqlDbType.Int, AllowNull = true });
            columnOptions.AdditionalColumns.Add(new SqlColumn { ColumnName = "SessionId", DataType = SqlDbType.VarChar, DataLength = 150, AllowNull = true });

            //Remove standard columns we don't want to use in db table
            columnOptions.Store.Remove(StandardColumn.Exception);
            columnOptions.Store.Remove(StandardColumn.Level);
            columnOptions.Store.Remove(StandardColumn.MessageTemplate); 

            //This removes any duplicates from the JSON field if we store in their own column
            columnOptions.LogEvent.ExcludeStandardColumns = true;
            columnOptions.LogEvent.ExcludeAdditionalProperties = true;

            //Set either log table's primary key 'Id' column to a BigInt datatype on the SQL Server table.
            columnOptions.Id.DataType = SqlDbType.Int;

            //columnOptions.Properties..Filter.ByExcluding(Matching.FromSource<Serilog.AspNetCore.RequestLoggingMiddleware>())

            return columnOptions;
        }

        /// <summary>
        /// Get column definitions 'ExceptionLogs' db table
        /// </summary>
        /// <returns></returns>
        private static ColumnOptions GetSerilogExceptionDBColumns()
        {            
            var columnOptions = new ColumnOptions { AdditionalColumns = new List<SqlColumn>() };
            //Add custom table columns
            columnOptions.AdditionalColumns.Add(new SqlColumn { ColumnName = "StackTrace", DataType = SqlDbType.NVarChar, AllowNull = true });
            columnOptions.AdditionalColumns.Add(new SqlColumn { ColumnName = "ClientId", DataType = SqlDbType.VarChar, DataLength = 150, AllowNull = true });
            columnOptions.AdditionalColumns.Add(new SqlColumn { ColumnName = "TransactionId", DataType = SqlDbType.Int, AllowNull = true });
            columnOptions.AdditionalColumns.Add(new SqlColumn { ColumnName = "SessionId", DataType = SqlDbType.VarChar, DataLength = 150, AllowNull = true });

            //Remove standard columns we don't want to use in db table
            columnOptions.Store.Remove(StandardColumn.Exception);
            columnOptions.Store.Remove(StandardColumn.MessageTemplate);
            columnOptions.Store.Remove(StandardColumn.Properties);//xml data   

            //This removes any duplicates from the JSON field if we store in their own column
            columnOptions.LogEvent.ExcludeStandardColumns = true;
            columnOptions.LogEvent.ExcludeAdditionalProperties = true;

            //Set either log table's primary key 'Id' column to a BigInt datatype on the SQL Server table.
            columnOptions.Id.DataType = SqlDbType.Int;

            return columnOptions;
        }

        /// <summary>
        /// Configure the standard AUDIT tables. All Audit tables have the same columns. They store a JSON serialized version of the ViewModel response from Controller in 'Message' db col.
        /// </summary>
        /// <returns></returns>
        private static ColumnOptions GetSerilogAuditDBColumns()
        {            
            var columnOptions = new ColumnOptions { AdditionalColumns = new List<SqlColumn>() };
            //Add table custom columns
            columnOptions.AdditionalColumns.Add(new SqlColumn { ColumnName = "TransactionId", DataType = SqlDbType.Int, AllowNull = true });

            //Remove standard columns we don't want to use in db table
            columnOptions.Store.Remove(StandardColumn.Exception);
            columnOptions.Store.Remove(StandardColumn.Level);
            columnOptions.Store.Remove(StandardColumn.MessageTemplate);
            columnOptions.Store.Remove(StandardColumn.Properties);//xml data   

            //This removes any duplicates from the JSON field if we store in their own column
            columnOptions.LogEvent.ExcludeStandardColumns = true;
            columnOptions.LogEvent.ExcludeAdditionalProperties = true;

            //Set either log table's primary key 'Id' column to a BigInt datatype on the SQL Server table.
            columnOptions.Id.DataType = SqlDbType.Int;

            return columnOptions;
        }

        #endregion
    }
}
