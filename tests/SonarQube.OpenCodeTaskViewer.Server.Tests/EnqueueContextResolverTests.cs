using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class EnqueueContextResolverTests
{
    [Fact]
    public async Task ResolveAsync_ThrowsWhenMappingMissingOrDisabled()
    {
        var repo = new FakeMappingRepository
        {
            Mapping = null
        };

        var sut = new EnqueueContextResolver(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ResolveAsync(12, SonarIssueType.CodeSmell, null));

        repo.Mapping = new MappingRecord
        {
            Id = 12,
            SonarProjectKey = "alpha",
            Directory = "C:/Work/Alpha",
            Enabled = false,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ResolveAsync(12, SonarIssueType.CodeSmell, null));
    }

    [Fact]
    public async Task ResolveAsync_UsesProfileInstructionAndPersistsWhenTypePresent()
    {
        var repo = new FakeMappingRepository
        {
            Mapping = new MappingRecord
            {
                Id = 7,
                SonarProjectKey = "alpha",
                Directory = "C:/Work/Alpha",
                Enabled = true,
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            },
            Profile = new InstructionProfileRecord
            {
                Id = 1,
                MappingId = 7,
                IssueType = "CODE_SMELL",
                Instructions = "  default fix text  ",
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            }
        };

        var sut = new EnqueueContextResolver(repo);
        var result = await sut.ResolveAsync(7, SonarIssueType.FromRaw("code_smell"), "  ");

        Assert.Equal(SonarIssueType.CodeSmell, result.Type);
        Assert.Equal("default fix text", result.InstructionText);
        Assert.Equal(7, repo.UpsertMappingId);
        Assert.Equal(SonarIssueType.CodeSmell, repo.UpsertIssueType);
        Assert.Equal("default fix text", repo.UpsertInstructions);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotPersistWhenIssueTypeMissing()
    {
        var repo = new FakeMappingRepository
        {
            Mapping = new MappingRecord
            {
                Id = 3,
                SonarProjectKey = "alpha",
                Directory = "C:/Work/Alpha",
                Enabled = true,
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            }
        };

        var sut = new EnqueueContextResolver(repo);
        var result = await sut.ResolveAsync(3, default, " explicit ");

        Assert.False(result.Type.HasValue);
        Assert.Equal("explicit", result.InstructionText);
        Assert.Null(repo.UpsertMappingId);
    }

    sealed class FakeMappingRepository : IMappingRepository
    {
        public MappingRecord? Mapping { get; set; }
        public InstructionProfileRecord? Profile { get; set; }
        public int? UpsertMappingId { get; private set; }
        public SonarIssueType UpsertIssueType { get; private set; }
        public string? UpsertInstructions { get; private set; }

        public Task<List<MappingRecord>> ListMappings(CancellationToken cancellationToken = default) => Task.FromResult(new List<MappingRecord>());

        public Task<MappingRecord?> GetMappingById(int id, CancellationToken cancellationToken = default) => Task.FromResult(Mapping is not null && Mapping.Id == id ? Mapping : null);

        public Task<bool> DeleteMapping(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MappingRecord> UpsertMapping(
            int? id,
            string sonarProjectKey,
            string directory,
            string? branch,
            bool enabled,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InstructionProfileRecord?> GetInstructionProfile(int mappingId, SonarIssueType issueType, CancellationToken cancellationToken = default) => Task.FromResult(Profile);

        public Task<InstructionProfileRecord> UpsertInstructionProfile(
            int mappingId,
            SonarIssueType issueType,
            string instructions,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            UpsertMappingId = mappingId;
            UpsertIssueType = issueType;
            UpsertInstructions = instructions;

            return Task.FromResult(
                new InstructionProfileRecord
                {
                    Id = 1,
                    MappingId = mappingId,
                    IssueType = issueType.Value,
                    Instructions = instructions,
                    CreatedAt = now,
                    UpdatedAt = now
                });
        }

        public Task<List<string>> ListEnabledMappingDirectories(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
