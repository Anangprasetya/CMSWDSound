using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WDSound.Web.Data;

public class SqlServerDesignFactory : IDesignTimeDbContextFactory<Store>
{
    public Store CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Catalog") ??
            "Server=localhost;Database=WDSound;Integrated Security=True;Encrypt=True";
        return new Store(new DbContextOptionsBuilder<Store>().UseSqlServer(connection).Options);
    }
}
