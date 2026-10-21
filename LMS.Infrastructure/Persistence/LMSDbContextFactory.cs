using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Persistence
{
    public sealed class LMSDbContextFactory
        : IDesignTimeDbContextFactory<LMSDbContext>
    {
        public LMSDbContext CreateDbContext(string[] args)
        {
            // Offline model/migration tooling: no application configuration or host/seeder.
            if(args.Contains("--offline"))return new LMSDbContext(new DbContextOptionsBuilder<LMSDbContext>()
                .UseSqlServer("Server=unused;Database=OfflineModelOnly;Trusted_Connection=True;TrustServerCertificate=True").AddInterceptors(new OfflineConnections()).Options);
            var basePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "..",
                "LMS.Api");

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile(
                    "appsettings.json",
                    optional: false)
                .Build();

            var connectionString =
                configuration.GetConnectionString(
                    "DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "DefaultConnection was not found.");
            }

            var optionsBuilder =
                new DbContextOptionsBuilder<LMSDbContext>();

            optionsBuilder.UseSqlServer(
                connectionString);

            return new LMSDbContext(
                optionsBuilder.Options);
        }
        private sealed class OfflineConnections:DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection,ConnectionEventData eventData,InterceptionResult result)=>throw new InvalidOperationException("Offline tooling must not access a database.");
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,ConnectionEventData eventData,InterceptionResult result,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("Offline tooling must not access a database.");
        }
    }
}
