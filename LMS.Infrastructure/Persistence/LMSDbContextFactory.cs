using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
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
    }
}
