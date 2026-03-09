using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Tests;

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ResolveAsync(12, "CODE_SMELL", null));

        repo.Mapping = new MappingRecord
        {
            Id = 12,
            SonarProjectKey = "alpha",
            Directory = "C:/Work/Alpha",
            Enabled = false,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ResolveAsync(12, "CODE_SMELL", null));
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
        var result = await sut.ResolveAsync(7, "code_smell", "  ");

        Assert.Equal("CODE_SMELL", result.Type);
        Assert.Equal("default fix text", result.InstructionText);
        Assert.Equal(7, repo.UpsertMappingId);
        Assert.Equal("CODE_SMELL", repo.UpsertIssueType);
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
        var result = await sut.ResolveAsync(3, null, " explicit ");

        Assert.Null(result.Type);
        Assert.Equal("explicit", result.InstructionText);
        Assert.Null(repo.UpsertMappingId);
    }

    sealed class FakeMappingRepository : IMappingRepository
    {
        public MappingRecord? Mapping { get; set; }
        public InstructionProfileRecord? Profile { get; set; }
        public int? UpsertMappingId { get; private set; }
        public string? UpsertIssueType { get; private set; }
        public string? UpsertInstructions { get; private set; }

        public Task<List<MappingRecord>> ListMappings() => Task.FromResult(new List<MappingRecord>());

        public Task<MappingRecord?> GetMappingById(int id) => Task.FromResult(Mapping is not null && Mapping.Id == id ? Mapping : null);

        public Task<bool> DeleteMapping(int id) => throw new NotSupportedException();

        public Task<MappingRecord> UpsertMapping(
            int? id,
            string sonarProjectKey,
            string directory,
            string? branch,
            bool enabled,
            DateTimeOffset now) => throw new NotSupportedException();

        public Task<InstructionProfileRecord?> GetInstructionProfile(int mappingId, string issueType) => Task.FromResult(Profile);

        public Task<InstructionProfileRecord> UpsertInstructionProfile(
            int mappingId,
            string issueType,
            string instructions,
            DateTimeOffset now)
        {
            UpsertMappingId = mappingId;
            UpsertIssueType = issueType;
            UpsertInstructions = instructions;

            return Task.FromResult(
                new InstructionProfileRecord
                {
                    Id = 1,
                    MappingId = mappingId,
                    IssueType = issueType,
                    Instructions = instructions,
                    CreatedAt = now,
                    UpdatedAt = now
                });
        }

        public Task<List<string>> ListEnabledMappingDirectories() => throw new NotSupportedException();
    }
}
