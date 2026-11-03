using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GroupPilot.Infrastructure;

public sealed class PilotDbDesignFactory : IDesignTimeDbContextFactory<PilotDb>
{
    public PilotDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PilotDb>().UseSqlite("Data Source=grouppilot-design.db").Options);
}
