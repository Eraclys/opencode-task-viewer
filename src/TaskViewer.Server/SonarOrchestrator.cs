using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Data.Sqlite;

namespace TaskViewer.Server;

public sealed class SonarOrchestrator : IAsyncDisposable
{
  private const int MaxEnqueueBatch = 1000;
  private const int MaxRuleScanIssues = 5000;
  private const int MaxEnqueueAllScanIssues = 20_000;

  private readonly SonarOrchestratorOptions _options;
  private readonly SemaphoreSlim _dbLock = new(1, 1);
  private readonly HashSet<string> _inFlight = [];
  private readonly ConcurrentDictionary<string, string> _ruleNameCache = new(StringComparer.OrdinalIgnoreCase);
  private PeriodicTimer? _timer;
  private Task? _loopTask;
  private volatile bool _tickRunning;
  private volatile bool _disposed;
  private volatile bool _workloadPaused;
  private volatile int _latestWorkingCount;
  private DateTimeOffset? _latestWorkingSampleAt;
  private (DateTimeOffset Ts, int Count) _cachedWorkingSample = (DateTimeOffset.MinValue, 0);

  public SonarOrchestrator(SonarOrchestratorOptions options)
  {
    _options = options;
    Directory.CreateDirectory(Path.GetDirectoryName(_options.DbPath) ?? ".");
    InitializeSchema();
  }

  private static string NowIso() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

  private static int ParseIntSafe(object? value, int fallback)
  {
    if (value is null) return fallback;
    if (value is int i) return i;
    if (value is long l && l is >= int.MinValue and <= int.MaxValue) return (int)l;
    var s = Convert.ToString(value, CultureInfo.InvariantCulture);
    return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
  }

  private static int? ParseIntNullable(object? value)
  {
    if (value is null) return null;
    if (value is int i) return i;
    if (value is long l && l is >= int.MinValue and <= int.MaxValue) return (int)l;
    var s = Convert.ToString(value, CultureInfo.InvariantCulture);
    return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
  }

  private static string? NormalizeIssueType(string? value)
  {
    var v = (value ?? string.Empty).Trim().ToUpperInvariant();
    return string.IsNullOrWhiteSpace(v) ? null : v;
  }

  private static List<string> NormalizeRuleKeys(object? value)
  {
    var set = new HashSet<string>(StringComparer.Ordinal);
    if (value is JsonArray arr)
    {
      foreach (var n in arr)
      {
        var key = n?.ToString()?.Trim();
        if (!string.IsNullOrWhiteSpace(key)) set.Add(key);
      }
      return [.. set];
    }

    var csv = value?.ToString() ?? string.Empty;
    foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      if (!string.IsNullOrWhiteSpace(part)) set.Add(part);
    }

    return [.. set];
  }

  private static List<string> NormalizeQueueStateList(object? states)
  {
    var allowed = new HashSet<string>(StringComparer.Ordinal)
    {
      "queued", "dispatching", "session_created", "done", "failed", "cancelled"
    };
    var result = new HashSet<string>(StringComparer.Ordinal);

    if (states is JsonArray a)
    {
      foreach (var n in a)
      {
        var v = n?.ToString()?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(v) && allowed.Contains(v)) result.Add(v);
      }
      return [.. result];
    }

    var csv = states?.ToString() ?? string.Empty;
    foreach (var p in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      var v = p.ToLowerInvariant();
      if (allowed.Contains(v)) result.Add(v);
    }

    return [.. result];
  }

  private static bool IsRunningStatusType(string? value)
  {
    var t = (value ?? string.Empty).Trim().ToLowerInvariant();
    return t is "busy" or "retry" or "running";
  }

  private static int MakeBackoffMs(int attempt)
  {
    var n = Math.Max(1, attempt);
    var backoff = 2500 * Math.Pow(2, n - 1);
    return (int)Math.Min(60_000, backoff);
  }

  private SqliteConnection OpenConnection()
  {
    var builder = new SqliteConnectionStringBuilder
    {
      DataSource = _options.DbPath,
      Mode = SqliteOpenMode.ReadWriteCreate,
      Cache = SqliteCacheMode.Shared
    };
    var conn = new SqliteConnection(builder.ConnectionString);
    conn.Open();
    return conn;
  }

  private void InitializeSchema()
  {
    using var conn = OpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
      CREATE TABLE IF NOT EXISTS project_mappings (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        sonar_project_key TEXT NOT NULL UNIQUE,
        directory TEXT NOT NULL,
        branch TEXT,
        enabled INTEGER NOT NULL DEFAULT 1,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS instruction_profiles (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        mapping_id INTEGER NOT NULL,
        issue_type TEXT NOT NULL,
        instructions TEXT NOT NULL,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL,
        UNIQUE(mapping_id, issue_type)
      );

      CREATE TABLE IF NOT EXISTS queue_items (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        issue_key TEXT NOT NULL,
        mapping_id INTEGER NOT NULL,
        sonar_project_key TEXT NOT NULL,
        directory TEXT NOT NULL,
        branch TEXT,
        issue_type TEXT,
        severity TEXT,
        rule TEXT,
        message TEXT,
        component TEXT,
        relative_path TEXT,
        absolute_path TEXT,
        line INTEGER,
        issue_status TEXT,
        instructions_snapshot TEXT,
        state TEXT NOT NULL,
        attempt_count INTEGER NOT NULL DEFAULT 0,
        max_attempts INTEGER NOT NULL DEFAULT 3,
        next_attempt_at TEXT,
        session_id TEXT,
        open_code_url TEXT,
        last_error TEXT,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL,
        dispatched_at TEXT,
        completed_at TEXT,
        cancelled_at TEXT
      );

      CREATE INDEX IF NOT EXISTS idx_queue_state_next_attempt ON queue_items(state, next_attempt_at, created_at);
      CREATE INDEX IF NOT EXISTS idx_queue_issue_key ON queue_items(issue_key);
      CREATE INDEX IF NOT EXISTS idx_queue_mapping_state ON queue_items(mapping_id, state, created_at);
    ";
    cmd.ExecuteNonQuery();
  }

  private static MappingRecord? MapMapping(SqliteDataReader reader)
  {
    return new MappingRecord
    {
      Id = reader.GetInt32(reader.GetOrdinal("id")),
      SonarProjectKey = reader.GetString(reader.GetOrdinal("sonar_project_key")),
      Directory = reader.GetString(reader.GetOrdinal("directory")),
      Branch = reader.IsDBNull(reader.GetOrdinal("branch")) ? null : reader.GetString(reader.GetOrdinal("branch")),
      Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) == 1,
      CreatedAt = reader.GetString(reader.GetOrdinal("created_at")),
      UpdatedAt = reader.GetString(reader.GetOrdinal("updated_at"))
    };
  }

  private static QueueItemRecord MapQueue(SqliteDataReader reader)
  {
    int Col(string name) => reader.GetOrdinal(name);
    string? Str(string name) => reader.IsDBNull(Col(name)) ? null : reader.GetString(Col(name));
    int? IntN(string name) => reader.IsDBNull(Col(name)) ? null : reader.GetInt32(Col(name));

    return new QueueItemRecord
    {
      Id = reader.GetInt32(Col("id")),
      IssueKey = Str("issue_key") ?? string.Empty,
      MappingId = reader.GetInt32(Col("mapping_id")),
      SonarProjectKey = Str("sonar_project_key") ?? string.Empty,
      Directory = Str("directory") ?? string.Empty,
      Branch = Str("branch"),
      IssueType = Str("issue_type"),
      Severity = Str("severity"),
      Rule = Str("rule"),
      Message = Str("message"),
      Component = Str("component"),
      RelativePath = Str("relative_path"),
      AbsolutePath = Str("absolute_path"),
      Line = IntN("line"),
      IssueStatus = Str("issue_status"),
      Instructions = Str("instructions_snapshot"),
      State = Str("state") ?? "queued",
      AttemptCount = IntN("attempt_count") ?? 0,
      MaxAttempts = IntN("max_attempts") ?? 3,
      NextAttemptAt = Str("next_attempt_at"),
      SessionId = Str("session_id"),
      OpenCodeUrl = Str("open_code_url"),
      LastError = Str("last_error"),
      CreatedAt = Str("created_at") ?? string.Empty,
      UpdatedAt = Str("updated_at") ?? string.Empty,
      DispatchedAt = Str("dispatched_at"),
      CompletedAt = Str("completed_at"),
      CancelledAt = Str("cancelled_at")
    };
  }

  public bool IsConfigured()
  {
    return !string.IsNullOrWhiteSpace(_options.SonarUrl)
      && !string.IsNullOrWhiteSpace(_options.SonarToken);
  }

  public object GetPublicConfig()
  {
    return new
    {
      configured = IsConfigured(),
      maxActive = _options.MaxActive,
      pollMs = _options.PollMs,
      maxAttempts = _options.MaxAttempts,
      maxWorkingGlobal = _options.MaxWorkingGlobal,
      workingResumeBelow = _options.WorkingResumeBelow
    };
  }

  public async Task<List<MappingRecord>> ListMappings()
  {
    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      using var cmd = conn.CreateCommand();
      cmd.CommandText = "SELECT id, sonar_project_key, directory, branch, enabled, created_at, updated_at FROM project_mappings ORDER BY sonar_project_key COLLATE NOCASE ASC";
      using var reader = cmd.ExecuteReader();
      var list = new List<MappingRecord>();
      while (reader.Read())
      {
        var row = MapMapping(reader);
        if (row is not null) list.Add(row);
      }
      return list;
    }
    finally
    {
      _dbLock.Release();
    }
  }

  public async Task<MappingRecord?> GetMappingById(object? mappingId)
  {
    var id = ParseIntSafe(mappingId, -1);
    if (id <= 0) return null;

    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      using var cmd = conn.CreateCommand();
      cmd.CommandText = "SELECT id, sonar_project_key, directory, branch, enabled, created_at, updated_at FROM project_mappings WHERE id = $id LIMIT 1";
      cmd.Parameters.AddWithValue("$id", id);
      using var reader = cmd.ExecuteReader();
      return reader.Read() ? MapMapping(reader) : null;
    }
    finally
    {
      _dbLock.Release();
    }
  }

  public async Task<MappingRecord> UpsertMapping(JsonNode? payload)
  {
    var sonarProjectKey = payload?["sonarProjectKey"]?.ToString()?.Trim()
      ?? payload?["sonar_project_key"]?.ToString()?.Trim()
      ?? string.Empty;
    var directory = payload?["directory"]?.ToString()?.Trim() ?? string.Empty;
    var branch = payload?["branch"]?.ToString()?.Trim();
    if (string.IsNullOrWhiteSpace(branch)) branch = null;
    var enabled = payload?["enabled"] is null || payload?["enabled"]?.GetValue<bool>() != false;

    if (string.IsNullOrWhiteSpace(sonarProjectKey)) throw new InvalidOperationException("Missing sonarProjectKey");
    if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Missing directory");

    directory = _options.NormalizeDirectory(directory) ?? directory.Replace('\\', '/');
    var id = ParseIntSafe(payload?["id"]?.ToString(), -1);
    var now = NowIso();

    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      if (id > 0)
      {
        using var update = conn.CreateCommand();
        update.CommandText = @"
          UPDATE project_mappings
          SET sonar_project_key = $key,
              directory = $dir,
              branch = $branch,
              enabled = $enabled,
              updated_at = $updated
          WHERE id = $id";
        update.Parameters.AddWithValue("$key", sonarProjectKey);
        update.Parameters.AddWithValue("$dir", directory);
        update.Parameters.AddWithValue("$branch", (object?)branch ?? DBNull.Value);
        update.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        update.Parameters.AddWithValue("$updated", now);
        update.Parameters.AddWithValue("$id", id);
        var changed = update.ExecuteNonQuery();
        if (changed == 0) throw new InvalidOperationException("Mapping not found");

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT id, sonar_project_key, directory, branch, enabled, created_at, updated_at FROM project_mappings WHERE id = $id LIMIT 1";
        select.Parameters.AddWithValue("$id", id);
        using var reader = select.ExecuteReader();
        if (reader.Read()) return MapMapping(reader)!;
        throw new InvalidOperationException("Mapping not found");
      }

      using var upsert = conn.CreateCommand();
      upsert.CommandText = @"
        INSERT INTO project_mappings (sonar_project_key, directory, branch, enabled, created_at, updated_at)
        VALUES ($key, $dir, $branch, $enabled, $created, $updated)
        ON CONFLICT(sonar_project_key) DO UPDATE SET
          directory = excluded.directory,
          branch = excluded.branch,
          enabled = excluded.enabled,
          updated_at = excluded.updated_at";
      upsert.Parameters.AddWithValue("$key", sonarProjectKey);
      upsert.Parameters.AddWithValue("$dir", directory);
      upsert.Parameters.AddWithValue("$branch", (object?)branch ?? DBNull.Value);
      upsert.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
      upsert.Parameters.AddWithValue("$created", now);
      upsert.Parameters.AddWithValue("$updated", now);
      upsert.ExecuteNonQuery();

      using var select2 = conn.CreateCommand();
      select2.CommandText = "SELECT id, sonar_project_key, directory, branch, enabled, created_at, updated_at FROM project_mappings WHERE sonar_project_key = $key LIMIT 1";
      select2.Parameters.AddWithValue("$key", sonarProjectKey);
      using var reader2 = select2.ExecuteReader();
      if (reader2.Read()) return MapMapping(reader2)!;
      throw new InvalidOperationException("Failed to save mapping");
    }
    finally
    {
      _dbLock.Release();
      _options.OnChange();
    }
  }

  public async Task<JsonObject?> GetInstructionProfile(object? mappingId, string? issueType)
  {
    var mapping = await GetMappingById(mappingId);
    if (mapping is null) return null;
    var type = NormalizeIssueType(issueType);
    if (type is null) return null;

    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      using var cmd = conn.CreateCommand();
      cmd.CommandText = "SELECT id, mapping_id, issue_type, instructions, created_at, updated_at FROM instruction_profiles WHERE mapping_id = $mid AND issue_type = $type LIMIT 1";
      cmd.Parameters.AddWithValue("$mid", mapping.Id);
      cmd.Parameters.AddWithValue("$type", type);
      using var reader = cmd.ExecuteReader();
      if (!reader.Read()) return null;
      return new JsonObject
      {
        ["id"] = reader.GetInt32(reader.GetOrdinal("id")),
        ["mapping_id"] = reader.GetInt32(reader.GetOrdinal("mapping_id")),
        ["issue_type"] = reader.GetString(reader.GetOrdinal("issue_type")),
        ["instructions"] = reader.GetString(reader.GetOrdinal("instructions")),
        ["created_at"] = reader.GetString(reader.GetOrdinal("created_at")),
        ["updated_at"] = reader.GetString(reader.GetOrdinal("updated_at"))
      };
    }
    finally
    {
      _dbLock.Release();
    }
  }

  public async Task<JsonObject> UpsertInstructionProfile(object? mappingId, string? issueType, string? instructions)
  {
    var mapping = await GetMappingById(mappingId);
    if (mapping is null) throw new InvalidOperationException("Mapping not found");
    var type = NormalizeIssueType(issueType);
    if (type is null) throw new InvalidOperationException("Missing issueType");
    var text = (instructions ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Missing instructions");
    var now = NowIso();

    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      using var upsert = conn.CreateCommand();
      upsert.CommandText = @"
        INSERT INTO instruction_profiles (mapping_id, issue_type, instructions, created_at, updated_at)
        VALUES ($mid, $type, $instructions, $created, $updated)
        ON CONFLICT(mapping_id, issue_type) DO UPDATE SET
          instructions = excluded.instructions,
          updated_at = excluded.updated_at";
      upsert.Parameters.AddWithValue("$mid", mapping.Id);
      upsert.Parameters.AddWithValue("$type", type);
      upsert.Parameters.AddWithValue("$instructions", text);
      upsert.Parameters.AddWithValue("$created", now);
      upsert.Parameters.AddWithValue("$updated", now);
      upsert.ExecuteNonQuery();

      using var select = conn.CreateCommand();
      select.CommandText = "SELECT id, mapping_id, issue_type, instructions, created_at, updated_at FROM instruction_profiles WHERE mapping_id = $mid AND issue_type = $type LIMIT 1";
      select.Parameters.AddWithValue("$mid", mapping.Id);
      select.Parameters.AddWithValue("$type", type);
      using var reader = select.ExecuteReader();
      if (!reader.Read()) throw new InvalidOperationException("Failed to save instruction profile");

      return new JsonObject
      {
        ["id"] = reader.GetInt32(reader.GetOrdinal("id")),
        ["mapping_id"] = reader.GetInt32(reader.GetOrdinal("mapping_id")),
        ["issue_type"] = reader.GetString(reader.GetOrdinal("issue_type")),
        ["instructions"] = reader.GetString(reader.GetOrdinal("instructions")),
        ["created_at"] = reader.GetString(reader.GetOrdinal("created_at")),
        ["updated_at"] = reader.GetString(reader.GetOrdinal("updated_at"))
      };
    }
    finally
    {
      _dbLock.Release();
      _options.OnChange();
    }
  }

  private async Task<JsonNode?> SonarFetch(string endpointPath, Dictionary<string, string?> query)
  {
    if (string.IsNullOrWhiteSpace(_options.SonarUrl) || string.IsNullOrWhiteSpace(_options.SonarToken))
      throw new InvalidOperationException("SonarQube is not configured");

    var url = new Uri(new Uri(_options.SonarUrl), endpointPath);
    var ub = new UriBuilder(url);
    var qp = System.Web.HttpUtility.ParseQueryString(ub.Query);
    foreach (var (k, v) in query)
    {
      if (string.IsNullOrWhiteSpace(v)) continue;
      qp[k] = v;
    }
    ub.Query = qp.ToString() ?? string.Empty;

    using var client = new HttpClient();
    var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.SonarToken}:"));
    using var req = new HttpRequestMessage(HttpMethod.Get, ub.Uri);
    req.Headers.TryAddWithoutValidation("Accept", "application/json");
    req.Headers.TryAddWithoutValidation("Authorization", $"Basic {token}");
    using var res = await client.SendAsync(req);
    var text = await res.Content.ReadAsStringAsync();
    if (!res.IsSuccessStatusCode)
      throw new InvalidOperationException($"SonarQube request failed: {(int)res.StatusCode} {res.ReasonPhrase}");
    return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
  }

  private async Task<string> GetRuleDisplayName(string key)
  {
    var ruleKey = (key ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(ruleKey)) return string.Empty;
    if (_ruleNameCache.TryGetValue(ruleKey, out var cached)) return cached;

    try
    {
      var data = await SonarFetch("/api/rules/show", new Dictionary<string, string?> { ["key"] = ruleKey });
      var name = data?["rule"]?["name"]?.ToString()?.Trim();
      if (string.IsNullOrWhiteSpace(name)) name = ruleKey;
      _ruleNameCache[ruleKey] = name;
      return name;
    }
    catch
    {
      _ruleNameCache[ruleKey] = ruleKey;
      return ruleKey;
    }
  }

  private static NormalizedIssue? NormalizeIssueForQueue(JsonNode? rawNode, MappingRecord mapping)
  {
    if (rawNode is not JsonObject raw) return null;
    var key = raw["key"]?.ToString()?.Trim() ?? raw["issueKey"]?.ToString()?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(key)) return null;

    var type = NormalizeIssueType(raw["type"]?.ToString() ?? raw["issueType"]?.ToString() ?? "CODE_SMELL") ?? "CODE_SMELL";
    var severity = raw["severity"]?.ToString()?.Trim()?.ToUpperInvariant();
    var rule = raw["rule"]?.ToString()?.Trim();
    var message = raw["message"]?.ToString()?.Trim();
    var line = ParseIntNullable(raw["line"]?.ToString());
    var status = raw["status"]?.ToString()?.Trim();
    var component = raw["component"]?.ToString()?.Trim() ?? raw["file"]?.ToString()?.Trim();

    var projectKey = mapping.SonarProjectKey?.Trim() ?? string.Empty;
    string? relativePath = null;
    if (!string.IsNullOrWhiteSpace(component))
    {
      if (!string.IsNullOrWhiteSpace(projectKey) && component.StartsWith(projectKey + ":", StringComparison.Ordinal))
      {
        relativePath = component[(projectKey.Length + 1)..];
      }
      else
      {
        var idx = component.IndexOf(':');
        relativePath = idx >= 0 ? component[(idx + 1)..] : component;
      }
    }

    relativePath = relativePath?.Replace('\\', '/').TrimStart('/');
    var absolutePath = !string.IsNullOrWhiteSpace(relativePath)
      ? $"{mapping.Directory.TrimEnd('/')}/{relativePath}"
      : null;

    return new NormalizedIssue
    {
      Key = key,
      Type = type,
      Severity = string.IsNullOrWhiteSpace(severity) ? null : severity,
      Rule = string.IsNullOrWhiteSpace(rule) ? null : rule,
      Message = string.IsNullOrWhiteSpace(message) ? null : message,
      Line = line,
      Status = string.IsNullOrWhiteSpace(status) ? null : status,
      Component = string.IsNullOrWhiteSpace(component) ? null : component,
      RelativePath = string.IsNullOrWhiteSpace(relativePath) ? null : relativePath,
      AbsolutePath = string.IsNullOrWhiteSpace(absolutePath) ? null : absolutePath
    };
  }

  public async Task<object> ListRules(object? mappingId, string? issueType, string? issueStatus)
  {
    var mapping = await GetMappingById(mappingId);
    if (mapping is null || !mapping.Enabled) throw new InvalidOperationException("Mapping not found or disabled");

    var type = NormalizeIssueType(issueType);
    var status = (issueStatus ?? string.Empty).Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(status)) status = string.Empty;

    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    var pageSize = 500;
    var page = 1;
    var scanned = 0;
    int? total = null;

    while (true)
    {
      var query = new Dictionary<string, string?>
      {
        ["componentKeys"] = mapping.SonarProjectKey,
        ["p"] = page.ToString(CultureInfo.InvariantCulture),
        ["ps"] = pageSize.ToString(CultureInfo.InvariantCulture)
      };
      if (!string.IsNullOrWhiteSpace(type)) query["types"] = type;
      if (!string.IsNullOrWhiteSpace(status)) query["statuses"] = status;
      if (!string.IsNullOrWhiteSpace(mapping.Branch)) query["branch"] = mapping.Branch;

      var data = await SonarFetch("/api/issues/search", query);
      var issues = data?["issues"] as JsonArray ?? [];
      total ??= ParseIntNullable(data?["paging"]?["total"]?.ToString());

      foreach (var issueNode in issues)
      {
        var key = issueNode?["rule"]?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(key)) continue;
        counts.TryGetValue(key, out var current);
        counts[key] = current + 1;
        scanned += 1;
      }

      var endReached = issues.Count < pageSize
        || (total.HasValue && page * pageSize >= total.Value)
        || scanned >= MaxRuleScanIssues;
      if (endReached) break;
      page += 1;
    }

    var rules = new List<object>();
    foreach (var key in counts.Keys)
    {
      var name = await GetRuleDisplayName(key);
      rules.Add(new { key, name = string.IsNullOrWhiteSpace(name) ? key : name, count = counts[key] });
    }

    rules = rules
      .OrderByDescending(x => (int)x.GetType().GetProperty("count")!.GetValue(x)!)
      .ThenBy(x => (string)x.GetType().GetProperty("name")!.GetValue(x)!, StringComparer.OrdinalIgnoreCase)
      .ThenBy(x => (string)x.GetType().GetProperty("key")!.GetValue(x)!, StringComparer.OrdinalIgnoreCase)
      .ToList();

    return new
    {
      mapping,
      issueType = string.IsNullOrWhiteSpace(type) ? null : type,
      issueStatus = string.IsNullOrWhiteSpace(status) ? null : status,
      scannedIssues = scanned,
      truncated = scanned >= MaxRuleScanIssues,
      rules
    };
  }

  public async Task<object> ListIssues(object? mappingId, string? issueType, string? severity, string? issueStatus, object? page, object? pageSize, object? ruleKeys)
  {
    var mapping = await GetMappingById(mappingId);
    if (mapping is null || !mapping.Enabled) throw new InvalidOperationException("Mapping not found or disabled");

    var type = NormalizeIssueType(issueType);
    var sev = (severity ?? string.Empty).Trim().ToUpperInvariant();
    var status = (issueStatus ?? string.Empty).Trim().ToUpperInvariant();
    var rules = NormalizeRuleKeys(ruleKeys);
    var p = Math.Clamp(ParseIntSafe(page, 1), 1, int.MaxValue);
    var ps = Math.Clamp(ParseIntSafe(pageSize, 100), 1, 500);

    var query = new Dictionary<string, string?>
    {
      ["componentKeys"] = mapping.SonarProjectKey,
      ["p"] = p.ToString(CultureInfo.InvariantCulture),
      ["ps"] = ps.ToString(CultureInfo.InvariantCulture)
    };
    if (!string.IsNullOrWhiteSpace(type)) query["types"] = type;
    if (!string.IsNullOrWhiteSpace(sev)) query["severities"] = sev;
    if (!string.IsNullOrWhiteSpace(status)) query["statuses"] = status;
    if (rules.Count > 0) query["rules"] = string.Join(',', rules);
    if (!string.IsNullOrWhiteSpace(mapping.Branch)) query["branch"] = mapping.Branch;

    var data = await SonarFetch("/api/issues/search", query);
    var rawIssues = data?["issues"] as JsonArray ?? [];
    var issues = new List<object>();
    foreach (var raw in rawIssues)
    {
      var issue = NormalizeIssueForQueue(raw, mapping);
      if (issue is null) continue;
      issues.Add(new
      {
        key = issue.Key,
        type = issue.Type,
        severity = issue.Severity,
        rule = issue.Rule,
        message = issue.Message,
        component = issue.Component,
        line = issue.Line,
        status = issue.Status,
        relativePath = issue.RelativePath,
        absolutePath = issue.AbsolutePath
      });
    }

    var pageIndex = ParseIntSafe(data?["paging"]?["pageIndex"]?.ToString(), p);
    var psize = ParseIntSafe(data?["paging"]?["pageSize"]?.ToString(), ps);
    var total = ParseIntSafe(data?["paging"]?["total"]?.ToString(), issues.Count);

    return new
    {
      mapping,
      paging = new { pageIndex, pageSize = psize, total },
      issues
    };
  }

  private async Task<(MappingRecord Mapping, string? Type, string InstructionText)> ResolveEnqueueContext(object? mappingId, string? issueType, string? instructions)
  {
    var mapping = await GetMappingById(mappingId);
    if (mapping is null || !mapping.Enabled) throw new InvalidOperationException("Mapping not found or disabled");

    var type = NormalizeIssueType(issueType);
    var profile = await GetInstructionProfile(mapping.Id, type ?? string.Empty);
    var defaultInstruction = profile?["instructions"]?.ToString() ?? string.Empty;
    var instructionText = string.IsNullOrWhiteSpace(instructions) ? defaultInstruction.Trim() : instructions.Trim();

    if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(instructionText))
    {
      await UpsertInstructionProfile(mapping.Id, type, instructionText);
    }

    return (mapping, type, instructionText);
  }

  private async Task<(List<QueueItemRecord> CreatedItems, List<object> Skipped)> EnqueueRawIssues(MappingRecord mapping, string? type, string instructionText, List<JsonNode?> rawIssues)
  {
    var createdItems = new List<QueueItemRecord>();
    var skipped = new List<object>();
    var now = NowIso();

    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      foreach (var rawIssue in rawIssues)
      {
        var issue = NormalizeIssueForQueue(rawIssue, mapping);
        if (issue is null)
        {
          skipped.Add(new { issueKey = (string?)null, reason = "invalid-issue" });
          continue;
        }

        using var existingCmd = conn.CreateCommand();
        existingCmd.CommandText = "SELECT id, state FROM queue_items WHERE mapping_id = $mid AND issue_key = $issueKey AND state IN ('queued', 'dispatching') LIMIT 1";
        existingCmd.Parameters.AddWithValue("$mid", mapping.Id);
        existingCmd.Parameters.AddWithValue("$issueKey", issue.Key);
        using var existingReader = existingCmd.ExecuteReader();
        if (existingReader.Read())
        {
          var state = existingReader.GetString(existingReader.GetOrdinal("state"));
          skipped.Add(new { issueKey = issue.Key, reason = $"already-{state}" });
          continue;
        }

        using var insert = conn.CreateCommand();
        insert.CommandText = @"
          INSERT INTO queue_items (
            issue_key, mapping_id, sonar_project_key, directory, branch,
            issue_type, severity, rule, message,
            component, relative_path, absolute_path, line, issue_status,
            instructions_snapshot,
            state, attempt_count, max_attempts, next_attempt_at,
            created_at, updated_at
          ) VALUES (
            $issue_key, $mapping_id, $sonar_key, $directory, $branch,
            $issue_type, $severity, $rule, $message,
            $component, $relative_path, $absolute_path, $line, $issue_status,
            $instructions,
            'queued', 0, $max_attempts, $next_attempt_at,
            $created_at, $updated_at
          )";
        insert.Parameters.AddWithValue("$issue_key", issue.Key);
        insert.Parameters.AddWithValue("$mapping_id", mapping.Id);
        insert.Parameters.AddWithValue("$sonar_key", mapping.SonarProjectKey);
        insert.Parameters.AddWithValue("$directory", mapping.Directory);
        insert.Parameters.AddWithValue("$branch", (object?)mapping.Branch ?? DBNull.Value);
        insert.Parameters.AddWithValue("$issue_type", (object?)(type ?? issue.Type) ?? DBNull.Value);
        insert.Parameters.AddWithValue("$severity", (object?)issue.Severity ?? DBNull.Value);
        insert.Parameters.AddWithValue("$rule", (object?)issue.Rule ?? DBNull.Value);
        insert.Parameters.AddWithValue("$message", (object?)issue.Message ?? DBNull.Value);
        insert.Parameters.AddWithValue("$component", (object?)issue.Component ?? DBNull.Value);
        insert.Parameters.AddWithValue("$relative_path", (object?)issue.RelativePath ?? DBNull.Value);
        insert.Parameters.AddWithValue("$absolute_path", (object?)issue.AbsolutePath ?? DBNull.Value);
        insert.Parameters.AddWithValue("$line", (object?)issue.Line ?? DBNull.Value);
        insert.Parameters.AddWithValue("$issue_status", (object?)issue.Status ?? DBNull.Value);
        insert.Parameters.AddWithValue("$instructions", string.IsNullOrWhiteSpace(instructionText) ? DBNull.Value : instructionText);
        insert.Parameters.AddWithValue("$max_attempts", _options.MaxAttempts);
        insert.Parameters.AddWithValue("$next_attempt_at", now);
        insert.Parameters.AddWithValue("$created_at", now);
        insert.Parameters.AddWithValue("$updated_at", now);
        insert.ExecuteNonQuery();

        using var readInserted = conn.CreateCommand();
        readInserted.CommandText = "SELECT * FROM queue_items WHERE id = last_insert_rowid()";
        using var insertedReader = readInserted.ExecuteReader();
        if (insertedReader.Read()) createdItems.Add(MapQueue(insertedReader));
      }
    }
    finally
    {
      _dbLock.Release();
    }

    return (createdItems, skipped);
  }

  private async Task<(List<JsonNode?> Issues, int Matched, bool Truncated)> CollectIssuesForEnqueueAll(MappingRecord mapping, string? issueType, string? severity, string? issueStatus, List<string> ruleKeys)
  {
    var type = NormalizeIssueType(issueType);
    var sev = (severity ?? string.Empty).Trim().ToUpperInvariant();
    var status = (issueStatus ?? string.Empty).Trim().ToUpperInvariant();

    var pageSize = 500;
    var page = 1;
    int? total = null;
    var allIssues = new List<JsonNode?>();

    while (allIssues.Count < MaxEnqueueAllScanIssues)
    {
      var query = new Dictionary<string, string?>
      {
        ["componentKeys"] = mapping.SonarProjectKey,
        ["p"] = page.ToString(CultureInfo.InvariantCulture),
        ["ps"] = pageSize.ToString(CultureInfo.InvariantCulture)
      };
      if (!string.IsNullOrWhiteSpace(type)) query["types"] = type;
      if (!string.IsNullOrWhiteSpace(sev)) query["severities"] = sev;
      if (!string.IsNullOrWhiteSpace(status)) query["statuses"] = status;
      if (ruleKeys.Count > 0) query["rules"] = string.Join(',', ruleKeys);
      if (!string.IsNullOrWhiteSpace(mapping.Branch)) query["branch"] = mapping.Branch;

      var data = await SonarFetch("/api/issues/search", query);
      total ??= ParseIntNullable(data?["paging"]?["total"]?.ToString());
      var issuesRaw = data?["issues"] as JsonArray ?? [];
      foreach (var issue in issuesRaw)
      {
        if (allIssues.Count >= MaxEnqueueAllScanIssues) break;
        allIssues.Add(issue);
      }

      var endReached = issuesRaw.Count < pageSize
        || (total.HasValue && page * pageSize >= total.Value)
        || allIssues.Count >= MaxEnqueueAllScanIssues;
      if (endReached) break;
      page += 1;
    }

    return (allIssues, total ?? allIssues.Count, allIssues.Count >= MaxEnqueueAllScanIssues);
  }

  public async Task<object> EnqueueIssues(object? mappingId, string? issueType, string? instructions, JsonArray? issues)
  {
    var rawIssues = issues?.Take(MaxEnqueueBatch).Cast<JsonNode?>().ToList() ?? [];
    if (rawIssues.Count == 0) throw new InvalidOperationException("No issues provided");

    var context = await ResolveEnqueueContext(mappingId, issueType, instructions);
    var (createdItems, skipped) = await EnqueueRawIssues(context.Mapping, context.Type, context.InstructionText, rawIssues);
    if (createdItems.Count > 0) _options.OnChange();

    return new
    {
      created = createdItems.Count,
      skipped,
      items = createdItems
    };
  }

  public async Task<object> EnqueueAllMatching(object? mappingId, string? issueType, object? ruleKeys, string? issueStatus, string? severity, string? instructions)
  {
    var rules = NormalizeRuleKeys(ruleKeys);
    var hasSingleSpecificRule = rules.Count == 1 && !string.Equals(rules[0], "all", StringComparison.OrdinalIgnoreCase);
    if (!hasSingleSpecificRule) throw new InvalidOperationException("A specific rule key is required to queue all matching issues");

    var context = await ResolveEnqueueContext(mappingId, issueType, instructions);
    var collected = await CollectIssuesForEnqueueAll(context.Mapping, context.Type, severity, issueStatus, rules);
    var (createdItems, skipped) = await EnqueueRawIssues(context.Mapping, context.Type, context.InstructionText, collected.Issues);
    if (createdItems.Count > 0) _options.OnChange();

    return new
    {
      matched = collected.Matched,
      created = createdItems.Count,
      skipped,
      truncated = collected.Truncated,
      items = createdItems
    };
  }

  public async Task<List<QueueItemRecord>> ListQueue(object? states, object? limit)
  {
    var selectedStates = NormalizeQueueStateList(states);
    var n = Math.Clamp(ParseIntSafe(limit, 250), 1, 5000);

    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      using var cmd = conn.CreateCommand();

      var where = string.Empty;
      if (selectedStates.Count > 0)
      {
        var names = new List<string>();
        for (var i = 0; i < selectedStates.Count; i++)
        {
          var p = $"$s{i}";
          names.Add(p);
          cmd.Parameters.AddWithValue(p, selectedStates[i]);
        }
        where = $"WHERE state IN ({string.Join(", ", names)})";
      }

      cmd.CommandText = $"SELECT * FROM queue_items {where} ORDER BY datetime(updated_at) DESC, id DESC LIMIT $limit";
      cmd.Parameters.AddWithValue("$limit", n);

      using var reader = cmd.ExecuteReader();
      var items = new List<QueueItemRecord>();
      while (reader.Read()) items.Add(MapQueue(reader));
      return items;
    }
    finally
    {
      _dbLock.Release();
    }
  }

  public async Task<object> GetQueueStats()
  {
    var stats = new Dictionary<string, int>(StringComparer.Ordinal)
    {
      ["queued"] = 0,
      ["dispatching"] = 0,
      ["session_created"] = 0,
      ["done"] = 0,
      ["failed"] = 0,
      ["cancelled"] = 0
    };

    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      using var cmd = conn.CreateCommand();
      cmd.CommandText = "SELECT state, COUNT(*) AS count FROM queue_items GROUP BY state";
      using var reader = cmd.ExecuteReader();
      while (reader.Read())
      {
        var state = reader.GetString(reader.GetOrdinal("state"));
        if (!stats.ContainsKey(state)) continue;
        stats[state] = reader.GetInt32(reader.GetOrdinal("count"));
      }
    }
    finally
    {
      _dbLock.Release();
    }

    return new
    {
      queued = stats["queued"],
      dispatching = stats["dispatching"],
      session_created = stats["session_created"],
      done = stats["done"],
      failed = stats["failed"],
      cancelled = stats["cancelled"]
    };
  }

  public async Task<object> GetWorkerState()
  {
    var backpressure = await EvaluateWorkloadBackpressure(false);
    return new
    {
      inFlightDispatches = _inFlight.Count,
      maxActiveDispatches = _options.MaxActive,
      pausedByWorking = backpressure.Paused,
      workingCount = backpressure.WorkingCount,
      maxWorkingGlobal = _options.MaxWorkingGlobal,
      workingResumeBelow = _options.WorkingResumeBelow,
      workingSampleAt = backpressure.SampleAt
    };
  }

  public async Task<bool> CancelQueueItem(object? queueId)
  {
    var id = ParseIntSafe(queueId, -1);
    if (id <= 0) throw new InvalidOperationException("Invalid queue id");
    var now = NowIso();

    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      using var cmd = conn.CreateCommand();
      cmd.CommandText = @"
        UPDATE queue_items
        SET state = 'cancelled', cancelled_at = $now, updated_at = $now
        WHERE id = $id AND state IN ('queued', 'dispatching')";
      cmd.Parameters.AddWithValue("$now", now);
      cmd.Parameters.AddWithValue("$id", id);
      var changed = cmd.ExecuteNonQuery();
      if (changed > 0)
      {
        _options.OnChange();
        return true;
      }
      return false;
    }
    finally
    {
      _dbLock.Release();
    }
  }

  public async Task<int> RetryFailed()
  {
    var now = NowIso();
    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      using var cmd = conn.CreateCommand();
      cmd.CommandText = @"
        UPDATE queue_items
        SET state = 'queued', next_attempt_at = $now, updated_at = $now, last_error = NULL
        WHERE state = 'failed'";
      cmd.Parameters.AddWithValue("$now", now);
      var changed = cmd.ExecuteNonQuery();
      if (changed > 0) _options.OnChange();
      return changed;
    }
    finally
    {
      _dbLock.Release();
    }
  }

  public async Task<int> ClearQueued()
  {
    var now = NowIso();
    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      using var cmd = conn.CreateCommand();
      cmd.CommandText = @"
        UPDATE queue_items
        SET state = 'cancelled', cancelled_at = $now, updated_at = $now
        WHERE state = 'queued'";
      cmd.Parameters.AddWithValue("$now", now);
      var changed = cmd.ExecuteNonQuery();
      if (changed > 0) _options.OnChange();
      return changed;
    }
    finally
    {
      _dbLock.Release();
    }
  }

  private async Task<List<string>> ListEnabledMappingDirectories()
  {
    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      using var cmd = conn.CreateCommand();
      cmd.CommandText = "SELECT directory FROM project_mappings WHERE enabled = 1";
      using var reader = cmd.ExecuteReader();
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var dirs = new List<string>();
      while (reader.Read())
      {
        var d = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim();
        if (string.IsNullOrWhiteSpace(d)) continue;
        var key = d.Replace('\\', '/');
        if (seen.Add(key)) dirs.Add(d);
      }
      return dirs;
    }
    finally
    {
      _dbLock.Release();
    }
  }

  private static List<string> GetDirectoryVariants(string? directory)
  {
    var dir = (directory ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(dir)) return [];
    if (dir.Length > 1 && (dir.EndsWith('/') || dir.EndsWith('\\'))) dir = dir.TrimEnd('/', '\\');
    var variants = new List<string> { dir };
    var forward = dir.Replace('\\', '/');
    var backward = dir.Replace('/', '\\');
    if (!variants.Contains(forward, StringComparer.Ordinal)) variants.Add(forward);
    if (!variants.Contains(backward, StringComparer.Ordinal)) variants.Add(backward);
    return variants;
  }

  private async Task<Dictionary<string, string>> FetchStatusMapForDirectory(string directory)
  {
    var variants = GetDirectoryVariants(directory);
    if (variants.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);

    Dictionary<string, string> fallback = new(StringComparer.Ordinal);
    foreach (var variant in variants)
    {
      try
      {
        var data = await _options.OpenCodeFetch("/session/status", new OpenCodeRequest { Directory = variant });
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (data is JsonObject obj)
        {
          foreach (var kv in obj)
          {
            var statusType = kv.Value?["type"]?.ToString()?.Trim()?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(statusType)) continue;
            map[kv.Key] = statusType;
          }
        }
        if (map.Count > 0) return map;
        if (fallback.Count == 0) fallback = map;
      }
      catch
      {
      }
    }

    return fallback;
  }

  private async Task<(bool Paused, int WorkingCount, int MaxWorkingGlobal, int WorkingResumeBelow, string? SampleAt)> EvaluateWorkloadBackpressure(bool forceRefresh)
  {
    if (_options.MaxWorkingGlobal <= 0)
    {
      _workloadPaused = false;
      return (false, 0, _options.MaxWorkingGlobal, _options.WorkingResumeBelow, _latestWorkingSampleAt?.ToString("O"));
    }

    var sample = await GetWorkingSessionsCount(forceRefresh);
    var count = sample.Count;
    var nextPaused = _workloadPaused;

    if (!nextPaused && count >= _options.MaxWorkingGlobal) nextPaused = true;
    else if (nextPaused && count < _options.WorkingResumeBelow) nextPaused = false;

    if (nextPaused != _workloadPaused)
    {
      _workloadPaused = nextPaused;
      _options.OnChange();
    }

    return (_workloadPaused, count, _options.MaxWorkingGlobal, _options.WorkingResumeBelow, _latestWorkingSampleAt?.ToString("O"));
  }

  private async Task<(DateTimeOffset Ts, int Count)> GetWorkingSessionsCount(bool forceRefresh)
  {
    var now = DateTimeOffset.UtcNow;
    var cacheTtlMs = Math.Clamp(_options.PollMs, 500, 5000);
    if (!forceRefresh && (now - _cachedWorkingSample.Ts).TotalMilliseconds < cacheTtlMs)
      return _cachedWorkingSample;

    var dirs = await ListEnabledMappingDirectories();
    var totalRunning = 0;
    foreach (var dir in dirs)
    {
      var map = await FetchStatusMapForDirectory(dir);
      totalRunning += map.Values.Count(IsRunningStatusType);
    }

    _cachedWorkingSample = (now, totalRunning);
    _latestWorkingCount = totalRunning;
    _latestWorkingSampleAt = now;
    return _cachedWorkingSample;
  }

  private string ComposePrompt(QueueItemRecord item)
  {
    var lines = new List<string>
    {
      "Resolve the following SonarQube warning with a minimal, targeted change.",
      string.Empty,
      $"Issue key: {item.IssueKey}"
    };

    if (!string.IsNullOrWhiteSpace(item.IssueType)) lines.Add($"Issue type: {item.IssueType}");
    if (!string.IsNullOrWhiteSpace(item.Severity)) lines.Add($"Severity: {item.Severity}");
    if (!string.IsNullOrWhiteSpace(item.Rule)) lines.Add($"Rule: {item.Rule}");
    if (!string.IsNullOrWhiteSpace(item.IssueStatus)) lines.Add($"Issue status: {item.IssueStatus}");
    if (!string.IsNullOrWhiteSpace(item.RelativePath)) lines.Add($"File: {item.RelativePath}");
    if (item.Line.HasValue) lines.Add($"Line: {item.Line.Value}");
    if (!string.IsNullOrWhiteSpace(item.Message)) lines.Add($"Message: {item.Message}");

    lines.Add(string.Empty);
    lines.Add("Constraints:");
    lines.Add("- Fix only this issue; avoid unrelated refactors.");
    lines.Add("- Preserve behavior and public contracts.");
    lines.Add("- If the issue is not actionable, explain why and propose the safest alternative.");

    var extra = (item.Instructions ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(extra))
    {
      lines.Add(string.Empty);
      lines.Add("Additional instructions:");
      lines.Add(extra);
    }

    return string.Join('\n', lines);
  }

  private async Task<QueueItemRecord?> ClaimNextQueuedItem()
  {
    var now = NowIso();

    await _dbLock.WaitAsync();
    try
    {
      using var conn = OpenConnection();
      using var select = conn.CreateCommand();
      select.CommandText = @"
        SELECT *
        FROM queue_items
        WHERE state = 'queued'
          AND (next_attempt_at IS NULL OR next_attempt_at <= $now)
        ORDER BY datetime(created_at) ASC, id ASC
        LIMIT 1";
      select.Parameters.AddWithValue("$now", now);
      using var reader = select.ExecuteReader();
      if (!reader.Read()) return null;
      var id = reader.GetInt32(reader.GetOrdinal("id"));

      using var claim = conn.CreateCommand();
      claim.CommandText = @"
        UPDATE queue_items
        SET state = 'dispatching',
            attempt_count = attempt_count + 1,
            updated_at = $now,
            dispatched_at = COALESCE(dispatched_at, $now),
            last_error = NULL
        WHERE id = $id AND state = 'queued'";
      claim.Parameters.AddWithValue("$now", now);
      claim.Parameters.AddWithValue("$id", id);
      var changed = claim.ExecuteNonQuery();
      if (changed == 0) return null;

      using var readClaimed = conn.CreateCommand();
      readClaimed.CommandText = "SELECT * FROM queue_items WHERE id = $id";
      readClaimed.Parameters.AddWithValue("$id", id);
      using var claimedReader = readClaimed.ExecuteReader();
      return claimedReader.Read() ? MapQueue(claimedReader) : null;
    }
    finally
    {
      _dbLock.Release();
    }
  }

  private async Task DispatchQueueItem(QueueItemRecord item)
  {
    try
    {
      var title = $"[{item.IssueType ?? "ISSUE"}] {item.IssueKey}";
      var created = await _options.OpenCodeFetch("/session", new OpenCodeRequest
      {
        Method = "POST",
        Directory = item.Directory,
        JsonBody = new JsonObject { ["title"] = title }
      });

      var sessionId = created?["id"]?.ToString()?.Trim();
      if (string.IsNullOrWhiteSpace(sessionId)) throw new InvalidOperationException("OpenCode did not return a session id");

      var prompt = ComposePrompt(item);
      await _options.OpenCodeFetch($"/session/{Uri.EscapeDataString(sessionId)}/prompt_async", new OpenCodeRequest
      {
        Method = "POST",
        Directory = item.Directory,
        JsonBody = new JsonObject
        {
          ["parts"] = new JsonArray
          {
            new JsonObject
            {
              ["type"] = "text",
              ["text"] = prompt
            }
          }
        }
      });

      var ts = NowIso();
      var openCodeUrl = _options.BuildOpenCodeSessionUrl(sessionId, item.Directory);

      await _dbLock.WaitAsync();
      try
      {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
          UPDATE queue_items
          SET state = 'session_created',
              session_id = $sid,
              open_code_url = $url,
              completed_at = $ts,
              updated_at = $ts,
              next_attempt_at = NULL,
              last_error = NULL
          WHERE id = $id AND state = 'dispatching'";
        cmd.Parameters.AddWithValue("$sid", sessionId);
        cmd.Parameters.AddWithValue("$url", (object?)openCodeUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", ts);
        cmd.Parameters.AddWithValue("$id", item.Id);
        var changed = cmd.ExecuteNonQuery();
        if (changed > 0) _options.OnChange();
      }
      finally
      {
        _dbLock.Release();
      }
    }
    catch (Exception ex)
    {
      await _dbLock.WaitAsync();
      try
      {
        using var conn = OpenConnection();
        using var read = conn.CreateCommand();
        read.CommandText = "SELECT attempt_count, max_attempts FROM queue_items WHERE id = $id";
        read.Parameters.AddWithValue("$id", item.Id);
        using var reader = read.ExecuteReader();
        var attemptCount = item.AttemptCount;
        var maxAttempts = item.MaxAttempts;
        if (reader.Read())
        {
          attemptCount = ParseIntSafe(reader["attempt_count"], attemptCount);
          maxAttempts = ParseIntSafe(reader["max_attempts"], maxAttempts);
        }

        var exhausted = attemptCount >= maxAttempts;
        var nextAttemptAt = exhausted ? null : DateTimeOffset.UtcNow.AddMilliseconds(MakeBackoffMs(attemptCount)).ToString("O");
        var state = exhausted ? "failed" : "queued";
        var lastError = ex.Message;

        using var update = conn.CreateCommand();
        update.CommandText = @"
          UPDATE queue_items
          SET state = $state,
              next_attempt_at = $next,
              last_error = $error,
              updated_at = $updated
          WHERE id = $id AND state = 'dispatching'";
        update.Parameters.AddWithValue("$state", state);
        update.Parameters.AddWithValue("$next", (object?)nextAttemptAt ?? DBNull.Value);
        update.Parameters.AddWithValue("$error", lastError);
        update.Parameters.AddWithValue("$updated", NowIso());
        update.Parameters.AddWithValue("$id", item.Id);
        var changed = update.ExecuteNonQuery();
        if (changed > 0) _options.OnChange();
      }
      finally
      {
        _dbLock.Release();
      }
    }
  }

  public async Task Tick()
  {
    if (_tickRunning || _disposed) return;
    _tickRunning = true;
    try
    {
      if (!IsConfigured()) return;
      var workload = await EvaluateWorkloadBackpressure(true);
      if (workload.Paused) return;

      while (_inFlight.Count < _options.MaxActive)
      {
        var claim = await ClaimNextQueuedItem();
        if (claim is null) break;

        var key = claim.Id.ToString(CultureInfo.InvariantCulture);
        _inFlight.Add(key);
        _ = DispatchQueueItem(claim).ContinueWith(_ =>
        {
          _inFlight.Remove(key);
          _options.OnChange();
        });
      }
    }
    finally
    {
      _tickRunning = false;
    }
  }

  public void Start(CancellationToken stoppingToken)
  {
    if (_timer is not null) return;
    _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PollMs));
    _loopTask = Task.Run(async () =>
    {
      await Tick();
      while (!stoppingToken.IsCancellationRequested && _timer is not null)
      {
        try
        {
          if (!await _timer.WaitForNextTickAsync(stoppingToken)) break;
          await Tick();
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch
        {
        }
      }
    }, stoppingToken);
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;
    if (_timer is not null)
    {
      _timer.Dispose();
      _timer = null;
    }
    if (_loopTask is not null)
    {
      try
      {
        await _loopTask;
      }
      catch
      {
      }
    }
    _dbLock.Dispose();
  }
}
