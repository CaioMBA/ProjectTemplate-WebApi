using CrossCutting.Configuration;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Models.Configuration;

namespace UnitTests.Platform;

public sealed class SecretResolverTests
{
    private const string SecretPath = "/run/secrets/db_password";

    [Theory]
    [InlineData("p@ssw0rd")]
    [InlineData("not-a-path")]
    [InlineData("guest")]
    [InlineData("sk_live_abc123")]
    [InlineData("a/b/c")]
    [InlineData("localhost:5432")]
    [InlineData("Host=localhost;Database=x;Password=y")]
    public void Resolve_TreatsNonPathShapedValuesAsLiterals(string raw)
    {
        var resolver = Build(new FakeFileSystem());

        resolver.Resolve(raw, "Settings:Password", required: false).ShouldBe(raw);
    }

    [Theory]
    [InlineData("/run/secrets/x")]
    [InlineData("./relative/x")]
    [InlineData("../parent/x")]
    [InlineData(@"C:\secrets\x")]
    [InlineData(@"c:/secrets/x")]
    [InlineData(@"\\server\share\x")]
    [InlineData(@".\windows-relative\x")]
    public void Resolve_TreatsPathShapedValuesAsSecretFiles(string raw)
    {
        var fileSystem = new FakeFileSystem().AddFile(raw, "resolved-secret");
        var resolver = Build(fileSystem);

        resolver.Resolve(raw, "Settings:Password", required: false).ShouldBe("resolved-secret");
    }

    [Fact]
    public void Resolve_ThrowsWhenAPathShapedValueHasNoFile()
    {
        var resolver = Build(new FakeFileSystem());

        var exception = Should.Throw<SecretResolutionException>(() =>
            resolver.Resolve(SecretPath, "Settings:Databases:0:Password", required: false));

        exception.ConfigurationKey.ShouldBe("Settings:Databases:0:Password");
        exception.Message.ShouldContain("no such file exists");
    }

    [Fact]
    public void Resolve_ThrowsWhenThePathIsADirectory()
    {
        var fileSystem = new FakeFileSystem().AddDirectory("/run/secrets");
        var resolver = Build(fileSystem);

        Should.Throw<SecretResolutionException>(() =>
                resolver.Resolve("/run/secrets", "Settings:Password", required: false))
            .Message.ShouldContain("directory");
    }

    [Fact]
    public void Resolve_ThrowsWhenTheSecretFileIsEmpty()
    {
        var fileSystem = new FakeFileSystem().AddFile(SecretPath, "   ");
        var resolver = Build(fileSystem);

        Should.Throw<SecretResolutionException>(() =>
            resolver.Resolve(SecretPath, "Settings:Password", required: false));
    }

    [Fact]
    public void Resolve_ThrowsWhenTheSecretFileExceedsTheSizeCap()
    {
        var fileSystem = new FakeFileSystem().AddFile(SecretPath, new string('x', 200));
        var resolver = Build(fileSystem, new SecretResolutionOptions { MaxFileSizeBytes = 64 });

        Should.Throw<SecretResolutionException>(() =>
                resolver.Resolve(SecretPath, "Settings:Password", required: false))
            .Message.ShouldContain("exceeds");
    }

    [Fact]
    public void Resolve_TrimsTheTrailingNewLineDockerSecretsCarry()
    {
        var fileSystem = new FakeFileSystem().AddFile(SecretPath, "s3cret\n");
        var resolver = Build(fileSystem);

        resolver.Resolve(SecretPath, "Settings:Password", required: false).ShouldBe("s3cret");
    }

    [Fact]
    public void Resolve_PreservesInternalWhitespaceAndTrailingSpaces()
    {
        var fileSystem = new FakeFileSystem().AddFile(SecretPath, "a b c \r\n");
        var resolver = Build(fileSystem);

        resolver.Resolve(SecretPath, "Settings:Password", required: false).ShouldBe("a b c ");
    }

    [Fact]
    public void Resolve_HonoursThePlainPrefixForPathShapedLiterals()
    {
        var resolver = Build(new FakeFileSystem());

        resolver
            .Resolve("plain:/etc/not-a-secret-file", "Settings:Password", required: false)
            .ShouldBe("/etc/not-a-secret-file");
    }

    [Fact]
    public void Resolve_ReturnsNullForAnAbsentOptionalSecret()
    {
        var resolver = Build(new FakeFileSystem());

        resolver.Resolve(null, "Settings:Password", required: false).ShouldBeNull();
        resolver.Resolve("  ", "Settings:Password", required: false).ShouldBeNull();
    }

    [Fact]
    public void Resolve_ThrowsForAnAbsentRequiredSecret()
    {
        var resolver = Build(new FakeFileSystem());

        Should.Throw<SecretResolutionException>(() =>
            resolver.Resolve(null, "Settings:NoSql:ConnectionString", required: true));
    }

    [Fact]
    public void Resolve_RejectsPathsOutsideTheConfiguredAllowedRoots()
    {
        var fileSystem = new FakeFileSystem().AddFile("/etc/passwd", "root");

        var resolver = Build(
            fileSystem,
            new SecretResolutionOptions { AllowedRoots = ["/run/secrets"] });

        Should.Throw<SecretResolutionException>(() =>
                resolver.Resolve("/etc/passwd", "Settings:Password", required: false))
            .Message.ShouldContain("AllowedRoots");
    }

    [Fact]
    public void Resolve_AcceptsPathsInsideTheConfiguredAllowedRoots()
    {
        var fileSystem = new FakeFileSystem().AddFile(SecretPath, "ok");

        var resolver = Build(
            fileSystem,
            new SecretResolutionOptions { AllowedRoots = ["/run/secrets"] });

        resolver.Resolve(SecretPath, "Settings:Password", required: false).ShouldBe("ok");
    }

    [Fact]
    public void ResolveGraph_ResolvesEverySecretAcrossNestedObjectsAndLists()
    {
        var fileSystem = new FakeFileSystem()
            .AddFile("/run/secrets/db", "db-secret")
            .AddFile("/run/secrets/cache", "cache-secret")
            .AddFile("/run/secrets/api", "api-secret");

        var resolver = Build(fileSystem);

        var settings = new AppSettings
        {
            Databases =
            [
                new DatabaseSettings
                {
                    Id = "DEFAULT",
                    Type = DatabaseType.Postgresql,
                    Password = "/run/secrets/db",
                },
            ],
            Apis =
            [
                new ApiSettings
                {
                    Id = "PARTNER",
                    AuthorizationValue = "/run/secrets/api",
                },
            ],
            Caches = [new CacheSettings { Id = "REDIS", Type = CacheType.Redis, Password = "/run/secrets/cache" }],
            Brokers = [new BrokerSettings { Id = "EVENTS", Password = "inline-literal" }],
        };

        resolver.ResolveGraph(settings, AppSettings.SectionName);

        settings.Databases[0].Password.ShouldBe("db-secret");
        settings.Apis[0].AuthorizationValue.ShouldBe("api-secret");
        settings.Caches[0].Password.ShouldBe("cache-secret");
        settings.Brokers[0].Password.ShouldBe("inline-literal");
    }

    [Fact]
    public void ResolveGraph_LeavesPathShapedNonSecretValuesUntouched()
    {
        var resolver = Build(new FakeFileSystem());

        var settings = new AppSettings
        {
            Brokers = [new BrokerSettings { Id = "EVENTS", RabbitMq = new RabbitMqBrokerOptions { VirtualHost = "/" } }],
            Apis =
            [
                new ApiSettings
                {
                    Id = "PARTNER",
                    HealthEndpoint = "/health",
                    Endpoints = [new ApiEndpointSettings { Id = "LIST", Path = "/api/v1/items" }],
                },
            ],
        };

        resolver.ResolveGraph(settings, AppSettings.SectionName);

        settings.Brokers[0].RabbitMq.VirtualHost.ShouldBe("/");
        settings.Apis[0].HealthEndpoint.ShouldBe("/health");
        settings.Apis[0].Endpoints[0].Path.ShouldBe("/api/v1/items");
    }

    [Fact]
    public void ResolveGraph_ReportsTheFullConfigurationKeyOfTheOffendingSecret()
    {
        var resolver = Build(new FakeFileSystem());

        var settings = new AppSettings
        {
            Databases =
            [
                new DatabaseSettings { Id = "A", Password = "plain:ok" },
                new DatabaseSettings { Id = "B", Password = "/run/secrets/missing" },
            ],
        };

        var exception = Should.Throw<SecretResolutionException>(() =>
            resolver.ResolveGraph(settings, AppSettings.SectionName));

        exception.ConfigurationKey.ShouldBe("Settings:Databases:1:Password");
    }

    [Fact]
    public void ResolveGraph_ResolvesSecretsInsideTheEngineBlocksOfADatabaseEntry()
    {
        var fileSystem = new FakeFileSystem()
            .AddFile("/run/secrets/aws-access", "access-key")
            .AddFile("/run/secrets/aws-secret", "secret-key")
            .AddFile("/run/secrets/raven-pfx-password", "pfx-password");

        var resolver = Build(fileSystem);

        var dynamo = new DatabaseSettings { Id = "EVENTS", Type = DatabaseType.DynamoDb };
        dynamo.Cloud.AccessKey = "/run/secrets/aws-access";
        dynamo.Cloud.SecretKey = "/run/secrets/aws-secret";

        var raven = new DatabaseSettings { Id = "RAVEN", Type = DatabaseType.RavenDb };
        raven.Cluster.Urls.Add("https://raven.internal");
        raven.Cluster.CertificatePassword = "/run/secrets/raven-pfx-password";

        var settings = new AppSettings { Databases = [dynamo, raven] };

        resolver.ResolveGraph(settings, AppSettings.SectionName);

        dynamo.Cloud.AccessKey.ShouldBe("access-key");
        dynamo.Cloud.SecretKey.ShouldBe("secret-key");
        raven.Cluster.CertificatePassword.ShouldBe("pfx-password");
        raven.Cluster.Urls.ShouldBe(["https://raven.internal"]);
    }

    [Fact]
    public void ResolveGraph_ResolvesCacheAndBrokerPasswordsAndReportsTheirKeys()
    {
        var fileSystem = new FakeFileSystem().AddFile("/run/secrets/redis", "redis-secret");

        var settings = new AppSettings
        {
            Caches = [new CacheSettings { Id = "REDIS", Type = CacheType.Redis, Password = "/run/secrets/redis" }],
            Brokers =
            [
                new BrokerSettings { Id = "EVENTS", Password = "plain:/literal" },
                new BrokerSettings { Id = "STREAM", Type = BrokerType.Kafka, Password = "/run/secrets/kafka" },
            ],
        };

        var exception = Should.Throw<SecretResolutionException>(() =>
            Build(fileSystem).ResolveGraph(settings, AppSettings.SectionName));

        exception.ConfigurationKey.ShouldBe("Settings:Brokers:1:Password");
        settings.Caches[0].Password.ShouldBe("redis-secret");
        settings.Brokers[0].Password.ShouldBe("/literal");
    }

    [Fact]
    public void ResolveGraph_ReportsTheNestedBlockKeyOfAMissingSecret()
    {
        var resolver = Build(new FakeFileSystem());

        var dynamo = new DatabaseSettings { Id = "EVENTS", Type = DatabaseType.DynamoDb };
        dynamo.Cloud.SecretKey = "/run/secrets/missing";

        var settings = new AppSettings
        {
            Databases = [new DatabaseSettings { Id = "DEFAULT" }, dynamo],
        };

        var exception = Should.Throw<SecretResolutionException>(() =>
            resolver.ResolveGraph(settings, AppSettings.SectionName));

        exception.ConfigurationKey.ShouldBe("Settings:Databases:1:Cloud:SecretKey");
    }

    private static SecretResolver Build(
        FakeFileSystem fileSystem,
        SecretResolutionOptions? options = null) =>
        new(fileSystem, options ?? new SecretResolutionOptions());
}
