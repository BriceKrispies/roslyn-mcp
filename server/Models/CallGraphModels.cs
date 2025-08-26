using Microsoft.CodeAnalysis;

namespace mcp_server.Models;

public class CallersResult
{
    public string TargetMethod { get; set; } = string.Empty;
    public IEnumerable<CallerInfo> Callers { get; set; } = [];
    public int TotalCallers { get; set; }
    public bool MaxDepthReached { get; set; }
    public DateTime AnalyzedAt { get; set; }
}

public class CallerInfo
{
    public string Method { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public IEnumerable<string> CallChain { get; set; } = [];
    public EndpointInfo? EndpointInfo { get; set; }
    public int Depth { get; set; }
}

public class EndpointInfo
{
    public string Route { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public bool IsController { get; set; }
    public bool IsMinimalApi { get; set; }
    public string ControllerName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
}

public class CalleesResult
{
    public string SourceMethod { get; set; } = string.Empty;
    public IEnumerable<CalleeInfo> Callees { get; set; } = [];
    public IEnumerable<DatabaseOperation> DatabaseOperations { get; set; } = [];
    public IEnumerable<ExternalCall> ExternalCalls { get; set; } = [];
    public int TotalCallees { get; set; }
    public bool MaxDepthReached { get; set; }
    public DateTime AnalyzedAt { get; set; }
}

public class CalleeInfo
{
    public string Method { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string CallType { get; set; } = string.Empty; // "Method", "Database", "MediatR", "External"
    public string? TargetHandler { get; set; }
    public string? Operation { get; set; }
    public string? Entity { get; set; }
    public int Depth { get; set; }
}

public class DatabaseOperation
{
    public string Operation { get; set; } = string.Empty; // SELECT, INSERT, UPDATE, DELETE
    public string Table { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Read, Write
    public string Location { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? RawSql { get; set; }
}

public class ExternalCall
{
    public string Service { get; set; } = string.Empty;
    public IEnumerable<string> Operations { get; set; } = [];
    public string Type { get; set; } = string.Empty; // MediatR, HttpClient, etc.
    public IEnumerable<string> Locations { get; set; } = [];
}
