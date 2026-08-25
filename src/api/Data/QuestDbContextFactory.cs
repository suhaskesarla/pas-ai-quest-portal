using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PAS.AIQuestPortal.Api.Data;

public sealed class QuestDbContextFactory : IDesignTimeDbContextFactory<QuestDbContext>
{
    public QuestDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QuestDatabase")
            ?? "Server=localhost,1433;Database=PasAiQuest;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<QuestDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new QuestDbContext(options);
    }
}
