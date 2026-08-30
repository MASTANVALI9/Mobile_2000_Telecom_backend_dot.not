using Microsoft.EntityFrameworkCore;
using MockTelecomApi.Models;

namespace MockTelecomApi.Data
{
    public class MockDbContext : DbContext
    {
        public MockDbContext(DbContextOptions<MockDbContext> options) : base(options)
        {
        }

        public DbSet<MockProviderTransaction> MockProviderTransactions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<MockProviderTransaction>(entity =>
            {
                entity.HasIndex(e => e.ReferenceId).IsUnique();
            });
        }
    }
}
