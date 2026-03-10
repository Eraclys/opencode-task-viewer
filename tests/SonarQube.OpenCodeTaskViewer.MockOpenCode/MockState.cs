namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class MockState
{
    public int NextSessionIndex { get; set; } = 1;
    public int FailSessionCreateCount { get; set; }
    public int FailPromptAsyncCount { get; set; }
    public int PromptDelayMs { get; set; }
    public List<ProjectRecord> Projects { get; set; } = [];
    public List<SessionRecord> Sessions { get; set; } = [];
    public Dictionary<string, List<TodoRecord>> TodosBySessionId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<MessageRecord>> MessagesBySessionId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, StatusRecord>> StatusByDirectory { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static MockState BuildDefault()
    {
        var now = DateTimeOffset.UtcNow;
        var baseCreated = now.AddHours(-1).ToString("O");

        var alphaWorktree = @"C:\Work\Alpha";
        var betaWorktree = @"C:\Work\Beta";
        var gammaWorktree = @"C:\Work\Gamma";

        const string alphaDir = "C:/Work/Alpha";
        const string betaDir = "C:/Work/Beta";
        const string gammaDir = "C:/Work/Gamma";

        var state = new MockState
        {
            Projects =
            [
                new ProjectRecord
                {
                    Id = "global",
                    Worktree = "/",
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-5).ToString("O")
                    }
                },
                new ProjectRecord
                {
                    Id = "p-alpha",
                    Worktree = alphaWorktree,
                    Sandboxes = [@"C:\Work\Alpha\SandboxOnly"],
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-5).ToString("O")
                    }
                },
                new ProjectRecord
                {
                    Id = "p-beta",
                    Worktree = betaWorktree,
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-5).ToString("O")
                    }
                },
                new ProjectRecord
                {
                    Id = "p-gamma",
                    Worktree = gammaWorktree,
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-5).ToString("O")
                    }
                }
            ],
            Sessions =
            [
                new SessionRecord
                {
                    Id = "sess-busy",
                    Title = "Busy Session",
                    Directory = betaDir,
                    Project = new SessionProjectRecord
                    {
                        Worktree = betaWorktree
                    },
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-20).ToString("O")
                    }
                },
                new SessionRecord
                {
                    Id = "sess-retry",
                    Title = "Retrying Session",
                    Directory = alphaDir,
                    Project = new SessionProjectRecord
                    {
                        Worktree = alphaWorktree
                    },
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-45).ToString("O")
                    }
                },
                new SessionRecord
                {
                    Id = "sess-recent",
                    Title = "Recently Updated",
                    Directory = gammaDir,
                    Project = new SessionProjectRecord
                    {
                        Worktree = gammaWorktree
                    },
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddMinutes(-2).ToString("O")
                    }
                },
                new SessionRecord
                {
                    Id = "sess-stale",
                    Title = "Stale Session",
                    Directory = gammaDir,
                    Project = new SessionProjectRecord
                    {
                        Worktree = gammaWorktree
                    },
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddMinutes(-10).ToString("O")
                    }
                },
                new SessionRecord
                {
                    Id = "sess-archived",
                    Title = "Archived Session (Should Not Show)",
                    Directory = gammaDir,
                    Project = new SessionProjectRecord
                    {
                        Worktree = gammaWorktree
                    },
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddMinutes(-30).ToString("O"),
                        Archived = now.AddMinutes(-25).ToUnixTimeMilliseconds()
                    }
                }
            ],
            TodosBySessionId = new Dictionary<string, List<TodoRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                ["sess-busy"] = [],
                ["sess-retry"] = [],
                ["sess-recent"] = [],
                ["sess-stale"] = []
            },
            MessagesBySessionId = new Dictionary<string, List<MessageRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                ["sess-busy"] =
                [
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m1",
                            Role = "user",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddSeconds(-30).ToUnixTimeMilliseconds()
                            }
                        },
                        Content =
                        [
                            new MessageContentRecord
                            {
                                Type = "text",
                                Text = "Run the worker."
                            }
                        ]
                    },
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m2",
                            Role = "assistant",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddSeconds(-29).ToUnixTimeMilliseconds()
                            }
                        },
                        Content =
                        [
                            new MessageContentRecord
                            {
                                Type = "text",
                                Text = "Worker is running now."
                            }
                        ]
                    }
                ],
                ["sess-retry"] =
                [
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m3",
                            Role = "user",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddSeconds(-60).ToUnixTimeMilliseconds()
                            }
                        },
                        Text = "Try the migration again."
                    },
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m4",
                            Role = "assistant",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddSeconds(-59).ToUnixTimeMilliseconds()
                            }
                        },
                        Text = "Retrying migration with backoff."
                    }
                ],
                ["sess-recent"] =
                [
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m5",
                            Role = "user",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddMinutes(-2).ToUnixTimeMilliseconds()
                            }
                        },
                        Text = "Can you inspect this issue?"
                    }
                ],
                ["sess-stale"] =
                [
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m6",
                            Role = "user",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddHours(-1).ToUnixTimeMilliseconds()
                            }
                        },
                        Text = "Please summarize the diagnostics."
                    },
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m7",
                            Role = "assistant",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddHours(-1).AddSeconds(1).ToUnixTimeMilliseconds()
                            }
                        },
                        Text = "Diagnostics complete; all checks passed."
                    }
                ]
            },
            StatusByDirectory = new Dictionary<string, Dictionary<string, StatusRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                [alphaDir] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["sess-retry"] = new StatusRecord
                    {
                        Type = "retry"
                    }
                },
                [betaDir] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["sess-busy"] = new StatusRecord
                    {
                        Type = "busy"
                    }
                },
                [gammaDir] = new(StringComparer.OrdinalIgnoreCase)
            }
        };

        return state;
    }
}
