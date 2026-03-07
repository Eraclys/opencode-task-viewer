namespace TaskViewer.E2E.Tests;

[CollectionDefinition(Name)]
public sealed class E2eCollection : ICollectionFixture<E2eEnvironmentFixture>
{
    public const string Name = "E2E";
}
