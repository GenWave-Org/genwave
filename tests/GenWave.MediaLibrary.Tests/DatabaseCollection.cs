namespace GenWave.MediaLibrary.Tests;

/// <summary>Shares one disposable database across all integration test classes.</summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "library-db";
}
