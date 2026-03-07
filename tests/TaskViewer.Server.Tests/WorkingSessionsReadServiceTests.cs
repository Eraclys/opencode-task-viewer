using System.Text.Json.Nodes;
using TaskViewer.Server;
using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class WorkingSessionsReadServiceTests
{
    [Fact]
    public async Task GetWorkingSessionsCountAsync_UsesCacheWithinTtl()
    {
        var repo = new FakeMappingRepository(["C:/Work/Alpha"]);
        var requestCount = 0;

        var service = new WorkingSessionsReadService(
            repo,
            (_, _) =>
            {
                requestCount += 1;
                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["s1"] = new JsonObject { ["type"] = "busy" }
                });
            });

        var first = await service.GetWorkingSessionsCountAsync(forceRefresh: false, pollMs: 1000);
        var second = await service.GetWorkingSessionsCountAsync(forceRefresh: false, pollMs: 1000);

        Assert.Equal(1, first.Count);
        Assert.Equal(1, second.Count);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task GetWorkingSessionsCountAsync_UsesDirectoryVariantFallbackAndCountsRunningOnly()
    {
        var repo = new FakeMappingRepository(["C:/Work/Alpha/"]);
        var seenDirectories = new List<string>();

        var service = new WorkingSessionsReadService(
            repo,
            (_, request) =>
            {
                var dir = request.Directory ?? string.Empty;
                seenDirectories.Add(dir);

                if (string.Equals(dir, "C:/Work/Alpha", StringComparison.Ordinal))
                    throw new InvalidOperationException("first variant fails");

                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["s1"] = new JsonObject { ["type"] = "running" },
                    ["s2"] = new JsonObject { ["type"] = "retry" },
                    ["s3"] = new JsonObject { ["type"] = "done" }
                });
            });

        var result = await service.GetWorkingSessionsCountAsync(forceRefresh: true, pollMs: 1000);

        Assert.Equal(2, result.Count);
        Assert.True(seenDirectories.Count >= 2);
        Assert.Equal("C:/Work/Alpha", seenDirectories[0]);
        Assert.Equal("C:\\Work\\Alpha", seenDirectories[1]);
    }

    private sealed class FakeMappingRepository : IMappingRepository
    {
        private readonly List<string> _directories;

        public FakeMappingRepository(List<string> directories)
        {
            _directories = directories;
        }

        public Task<List<string>> ListEnabledMappingDirectories()
        {
            return Task.FromResult(_directories);
        }

        public Task<List<MappingRecord>> ListMappings() => throw new NotSupportedException();
        public Task<MappingRecord?> GetMappingById(int id) => throw new NotSupportedException();
        public Task<MappingRecord> UpsertMapping(int? id, string sonarProjectKey, string directory, string? branch, bool enabled, string now) => throw new NotSupportedException();
        public Task<JsonObject?> GetInstructionProfile(int mappingId, string issueType) => throw new NotSupportedException();
        public Task<JsonObject> UpsertInstructionProfile(int mappingId, string issueType, string instructions, string now) => throw new NotSupportedException();
    }
}
