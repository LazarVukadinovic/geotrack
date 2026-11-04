using Geotrack.Application.Security;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.Infrastructure.Services;
using Geotrack.IntegrationTests.Infrastructure;
using Geotrack.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class BootstrapSecurityTests
{
    private const string TestPassword = "Synthetic!Bootstrap7832";

    internal static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["Seed:EnableOperationalSeed"] = "true",
        ["Seed:OperationalUsers:Admin:Enabled"] = "true",
        ["Seed:OperationalUsers:Admin:Email"] = "bootstrap@example.test",
        ["Seed:OperationalUsers:Admin:Username"] = "initial-admin",
        ["Seed:OperationalUsers:Admin:Password"] = TestPassword
    };

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task No_environment_has_implicit_accounts_or_production_demo_override(string environment)
    {
        var config = new ConfigurationBuilder().Build();
        var host = new TestEnvironment { EnvironmentName = environment };
        Assert.False(OperationalUserSeeder.IsEnabled(host, config));
        Assert.Empty(BootstrapAccountConfiguration.Read(config));
        Assert.Equal(0, (await OperationalUserSeeder.EnsureAsync(null!, config)).AdminUserId);
        var explicitConfiguration = new ConfigurationBuilder().AddInMemoryCollection(ValidConfiguration()).Build();
        Assert.True(OperationalUserSeeder.IsEnabled(host, explicitConfiguration));
        Assert.Single(BootstrapAccountConfiguration.Read(explicitConfiguration));
        if (environment != Environments.Development)
            await Assert.ThrowsAsync<InvalidOperationException>(() => SeedData.InitializeAsync(null!, host));
    }

    [Theory]
    [InlineData("Password", null)]
    [InlineData("Password", "")]
    [InlineData("Password", "Short12!")]
    [InlineData("Password", "AAAAAAAAAAAAAAA1!")]
    [InlineData("Password", "long-but-no-digits-or-uppercase")]
    [InlineData("Password", "initial-admin-Password789!")]
    [InlineData("Password", "<Example-Password789!>")]
    [InlineData("Email", "invalid")]
    [InlineData("Email", "Display Name <owner@example.test>")]
    [InlineData("Username", "")]
    public async Task Unsafe_configuration_fails_before_database_access_without_echoing_values(string property, string? value)
    {
        var values = ValidConfiguration();
        values[$"Seed:OperationalUsers:Admin:{property}"] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => OperationalUserSeeder.EnsureAsync(null!, config));
        Assert.DoesNotContain(TestPassword, exception.ToString());
        if (!string.IsNullOrEmpty(value)) Assert.DoesNotContain(value, exception.Message);
    }

    [Fact]
    public void Global_opt_in_alone_is_insufficient_and_legacy_passwords_are_not_fallbacks()
    {
        var values = ValidConfiguration();
        values["Seed:OperationalUsers:Admin:Enabled"] = "false";
        Assert.Throws<InvalidOperationException>(() => BootstrapAccountConfiguration.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
        values["Seed:OperationalUsers:Admin:Enabled"] = "true";
        values.Remove("Seed:OperationalUsers:Admin:Password");
        values["Seed:DemoAdmin:Password"] = TestPassword;
        Assert.Throws<InvalidOperationException>(() => BootstrapAccountConfiguration.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    [Fact]
    public void Duplicate_account_identifiers_are_rejected()
    {
        var values = ValidConfiguration();
        foreach (var property in new[] { "Enabled", "Email", "Username", "Password" })
            values[$"Seed:OperationalUsers:ContentModerator:{property}"] = values[$"Seed:OperationalUsers:Admin:{property}"];
        Assert.Throws<InvalidOperationException>(() => BootstrapAccountConfiguration.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    [Fact]
    public void Credentials_can_be_injected_from_environment_without_changing_password_whitespace()
    {
        var prefix = $"GEOTRACK_TEST_{Guid.NewGuid():N}_";
        var values = ValidConfiguration();
        values["Seed:OperationalUsers:Admin:Password"] = " " + TestPassword + " ";
        try
        {
            foreach (var pair in values) Environment.SetEnvironmentVariable(prefix + pair.Key.Replace(":", "__"), pair.Value);
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var account = Assert.Single(BootstrapAccountConfiguration.Read(configuration));
            Assert.Equal(" " + TestPassword + " ", account.Password);
            Assert.DoesNotContain(TestPassword, account.ToString());
        }
        finally
        {
            foreach (var key in values.Keys) Environment.SetEnvironmentVariable(prefix + key.Replace(":", "__"), null);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("<secure-signing-key-placeholder-12345678>")]
    public void Jwt_signing_material_is_validated_without_echoing_it(string? key)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = key }).Build();
        var exception = Assert.Throws<InvalidOperationException>(() => JwtConfiguration.RequireSigningKey(config));
        if (!string.IsNullOrEmpty(key)) Assert.DoesNotContain(key, exception.Message);
    }

    [Fact]
    public async Task Bootstrap_hashes_once_preserves_disabled_accounts_and_refuses_role_escalation()
    {
        await using var app = await GeotrackApiFactory.CreateAsync(Environments.Production, NoSeeds());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(ValidConfiguration()).Build();
        var log = new SecurityCaptureLogger<object>();
        var first = await OperationalUserSeeder.EnsureAsync(db, configuration, log);
        var user = await db.Users.SingleAsync();
        Assert.Equal(first.AdminUserId, user.Id);
        Assert.NotEqual(TestPassword, user.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, TestPassword));
        var hash = user.PasswordHash;
        user.IsDeleted = true;
        user.IsSuspended = true;
        user.IsEmailConfirmed = false;
        user.DisplayName = "Keep this profile";
        await db.SaveChangesAsync();

        var changed = ValidConfiguration();
        changed["Seed:OperationalUsers:Admin:Password"] = "Different!Synthetic7931";
        await OperationalUserSeeder.EnsureAsync(db, new ConfigurationBuilder().AddInMemoryCollection(changed).Build(), log);
        db.ChangeTracker.Clear();
        user = await db.Users.SingleAsync();
        Assert.Equal(hash, user.PasswordHash);
        Assert.True(user.IsDeleted);
        Assert.True(user.IsSuspended);
        Assert.False(user.IsEmailConfirmed);
        Assert.Equal("Keep this profile", user.DisplayName);
        Assert.Single(await db.UserRoles.ToListAsync());
        // A missing password hash is preserved too; bootstrap is not a password recovery tool.
        user.PasswordHash = "";
        await db.SaveChangesAsync();
        await OperationalUserSeeder.EnsureAsync(db, configuration, log);
        Assert.Equal("", user.PasswordHash);
        db.UserRoles.RemoveRange(await db.UserRoles.ToListAsync());
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => OperationalUserSeeder.EnsureAsync(db, configuration, log));
        Assert.Empty(await db.UserRoles.ToListAsync());
        Assert.DoesNotContain(TestPassword, log.AllOutput);
        Assert.DoesNotContain(user.Email, log.AllOutput);
    }

    [Fact]
    public async Task Production_public_content_seed_creates_no_accounts()
    {
        var values = NoSeeds();
        values["Seed:EnablePublicContentSeed"] = "true";
        values["Seed:RunPublicContentInProduction"] = "true";
        await using var app = await GeotrackApiFactory.CreateAsync(Environments.Production, values);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Destinations.AnyAsync());
        Assert.False(await db.Users.AnyAsync());
        Assert.False(await db.UserRoles.AnyAsync());
    }

    [Fact]
    public void Legacy_demo_seeders_are_absent_from_the_production_assembly()
    {
        foreach (var type in new[] { "DemoAdminSeeder", "DemoContentModeratorSeeder", "DemoTerritoryManagerSeeder", "DemoRegionalManagerSeeder" })
            Assert.Null(typeof(OperationalUserSeeder).Assembly.GetType($"Geotrack.Server.{type}"));
    }

    [Fact]
    public async Task Concurrent_bootstrap_creates_one_admin_and_rollback_leaves_conflicting_users_untouched()
    {
        await using var app = await GeotrackApiFactory.CreateAsync(Environments.Production, NoSeeds());
        await using var firstScope = app.Services.CreateAsyncScope();
        await using var secondScope = app.Services.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(ValidConfiguration()).Build();
        var results = await Task.WhenAll(OperationalUserSeeder.EnsureAsync(firstDb, configuration), OperationalUserSeeder.EnsureAsync(secondDb, configuration));
        Assert.Equal(results[0].AdminUserId, results[1].AdminUserId);
        Assert.Equal(1, await firstDb.Users.CountAsync());
        Assert.Equal(1, await firstDb.UserRoles.CountAsync());

        var user = new User { Email = "conflict@example.test", Username = "existing-regular", CreatedAt = DateTime.UtcNow };
        firstDb.Users.Add(user);
        await firstDb.SaveChangesAsync();
        var values = ValidConfiguration();
        values["Seed:OperationalUsers:Admin:Email"] = "another-admin@example.test";
        values["Seed:OperationalUsers:Admin:Username"] = "another-admin";
        values["Seed:OperationalUsers:ContentModerator:Enabled"] = "true";
        values["Seed:OperationalUsers:ContentModerator:Email"] = user.Email;
        values["Seed:OperationalUsers:ContentModerator:Username"] = "new-moderator";
        values["Seed:OperationalUsers:ContentModerator:Password"] = "Synthetic-Other9284!";
        await Assert.ThrowsAsync<InvalidOperationException>(() => OperationalUserSeeder.EnsureAsync(firstDb, new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
        firstDb.ChangeTracker.Clear();
        Assert.Equal(2, await firstDb.Users.CountAsync());
        Assert.Equal(1, await firstDb.UserRoles.CountAsync());
        Assert.False(await firstDb.Users.AnyAsync(u => u.Email == "another-admin@example.test"));
    }

    private static Dictionary<string, string?> NoSeeds() => new()
    {
        ["Seed:EnableOperationalSeed"] = "false",
        ["Seed:EnablePublicContentSeed"] = "false",
        ["Seed:EnableDemoContentSeed"] = "false",
        ["Seed:SyncAdm1Regions:Enabled"] = "false",
        ["Database:AutoMigrate"] = "true"
    };

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
