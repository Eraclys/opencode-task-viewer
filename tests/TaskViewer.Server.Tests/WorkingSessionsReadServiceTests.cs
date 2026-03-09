using TaskViewer.Application.Orchestration;
using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.OpenCode;

namespace TaskViewer.Server.Tests;

public sealed class WorkingSessionsReadServiceTests
{
    [Fact]
    public async Task GetWorkingSessionsCountAsync_UsesCacheWithinTtl()
    {
        var repo = new FakeMappingRepository(["C:/Work/Alpha"]);
        var statusReader = new FakeOpenCodeStatusReader(
            readMap: directory =>
            {
                Assert.Equal("C:/Work/Alpha", directory);
                return new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["s1"] = "busy"
                };
            });

        var service = new WorkingSessionsReadService(
            repo,
            statusReader);

        var first = await service.GetWorkingSessionsCountAsync(false, 1000);
        var second = await service.GetWorkingSessionsCountAsync(false, 1000);

        Assert.Equal(1, first.Count);
        Assert.Equal(1, second.Count);
        Assert.Equal(1, statusReader.RequestCount);
    }

    [Fact]
    public async Task GetWorkingSessionsCountAsync_UsesDirectoryVariantFallbackAndCountsRunningOnly()
    {
        var repo = new FakeMappingRepository(["C:/Work/Alpha/"]);
        var seenDirectories = new List<string>();

        var service = new WorkingSessionsReadService(
            repo,
            new FakeOpenCodeStatusReader(
                readMap: dir =>
            {
                seenDirectories.Add(dir);

                if (string.Equals(dir, "C:/Work/Alpha", StringComparison.Ordinal))
                    throw new InvalidOperationException("first variant fails");

                return new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["s1"] = "running",
                    ["s2"] = "retry",
                    ["s3"] = "done"
                };
            }));

        var result = await service.GetWorkingSessionsCountAsync(true, 1000);

        Assert.Equal(2, result.Count);
        Assert.True(seenDirectories.Count >= 2);
        Assert.Equal("C:/Work/Alpha", seenDirectories[0]);
        Assert.Equal("C:\\Work\\Alpha", seenDirectories[1]);
    }

    sealed class FakeMappingRepository : IMappingRepository
    {
        readonly List<string> _directories;

        public FakeMappingRepository(List<string> directories)
        {
            _directories = directories;
        }

        public Task<List<string>> ListEnabledMappingDirectories() => Task.FromResult(_directories);

        public Task<List<MappingRecord>> ListMappings() => throw new NotSupportedException();
        public Task<MappingRecord?> GetMappingById(int id) => throw new NotSupportedException();
        public Task<bool> DeleteMapping(int id) => throw new NotSupportedException();

        public Task<MappingRecord> UpsertMapping(
            int? id,
            string sonarProjectKey,
            string directory,
            string? branch,
            bool enabled,
            DateTimeOffset now) => throw new NotSupportedException();

        public Task<InstructionProfileRecord?> GetInstructionProfile(int mappingId, string issueType) => throw new NotSupportedException();

        public Task<InstructionProfileRecord> UpsertInstructionProfile(
            int mappingId,
            string issueType,
            string instructions,
            DateTimeOffset now) => throw new NotSupportedException();
    }

    sealed class FakeOpenCodeStatusReader : IOpenCodeStatusReader
    {
        readonly Func<string, Dictionary<string, string>> _readMap;

        public FakeOpenCodeStatusReader(Func<string, Dictionary<string, string>> readMap)
        {
            _readMap = readMap;
        }

        public int RequestCount { get; private set; }

        public Task<Dictionary<string, string>> ReadWorkingStatusMapAsync(string directory)
        {
            RequestCount += 1;
            return Task.FromResult(_readMap(directory));
        }
    }
}
