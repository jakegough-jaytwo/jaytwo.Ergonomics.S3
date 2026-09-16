using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace jaytwo.Ergonomics.S3.Tests;

public class TestFixture
{
    public TestFixture()
    {
        var assemblyLocation = GetType().Assembly.Location;
        var basePath = new FileInfo(assemblyLocation!).Directory!.FullName;

        TestEnvironment = Environment.GetEnvironmentVariable("TEST_ENV");

        Configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("testsettings.json")
            .AddJsonFile($"testsettings.{TestEnvironment}.json", optional: true)
            .Build();

        Minio = new MinioServer(Configuration);
    }

    public IConfiguration Configuration { get; }

    public string? TestEnvironment { get; }

    public MinioServer Minio { get; }
}
