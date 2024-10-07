using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using tempus.service.core.api.Data.Entities;
using tempus.service.core.api.Models;
using tempus.service.core.api.Services;

namespace tempus.service.core.api.Data
{
    public class STRDMSContext : DbContext
    {
        public STRDMSContext(DbContextOptions options) { }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                IConfigurationRoot configuration = new ConfigurationBuilder()
                   .SetBasePath(Directory.GetCurrentDirectory())
                   .AddJsonFile("appsettings.Development.json")
                   .Build();

                var connectionString = configuration.GetConnectionString("STRDMSDB");

                var builder = new SqlConnectionStringBuilder(connectionString)
                {
                    Password = EncryptDecrypt.Decrypt(configuration["DBPASSWORD"])
                };

                optionsBuilder.UseSqlServer(builder.ConnectionString);
            }
        }

        public DbSet<Location> Locations { get; set; }

        public DbSet<PosInvoice> POSInvoices { get; set; }

        public DbSet<SISPosInvoice> SISPOSInvoices { get; set; }
    }
}
