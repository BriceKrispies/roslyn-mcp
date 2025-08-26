using Microsoft.CodeAnalysis;

namespace mcp_server.Services;

public interface IProjectAnalysisService
{
    Task<ProjectAnalysisResult> AnalyzeProjectAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<SolutionAnalysisResult> AnalyzeSolutionAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task<FileAnalysisResult> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<IEnumerable<SymbolInfo>> GetSymbolsAsync(string projectName, SymbolKind? symbolKind = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<ReferenceInfo>> FindReferencesAsync(string symbol, string? projectName = null, CancellationToken cancellationToken = default);

    Task InvalidateCacheAsync(string? projectName = null);
}

public class ProjectAnalysisResult
{
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public IEnumerable<string> SourceFiles { get; set; } = [];
    public IEnumerable<Diagnostic> Diagnostics { get; set; } = [];
    public IEnumerable<string> Dependencies { get; set; } = [];
    public int TotalLines { get; set; }
    public DateTime AnalyzedAt { get; set; }
}

public class SolutionAnalysisResult
{
    public string SolutionName { get; set; } = string.Empty;
    public string SolutionPath { get; set; } = string.Empty;
    public IEnumerable<ProjectAnalysisResult> Projects { get; set; } = [];
    public IEnumerable<Diagnostic> AllDiagnostics { get; set; } = [];
    public DateTime AnalyzedAt { get; set; }
}

public class FileAnalysisResult
{
    public string FilePath { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public IEnumerable<Diagnostic> Diagnostics { get; set; } = [];
    public IEnumerable<SymbolInfo> Symbols { get; set; } = [];
    public int LineCount { get; set; }
    public DateTime AnalyzedAt { get; set; }
}

public class SymbolInfo
{
    public string Name { get; set; } = string.Empty;
    public SymbolKind Kind { get; set; }
    public string ContainingNamespace { get; set; } = string.Empty;
    public string ContainingType { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string Signature { get; set; } = string.Empty;
    public string Documentation { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
}

public class ReferenceInfo
{
    public string Symbol { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string Context { get; set; } = string.Empty;
    public ReferenceKind Kind { get; set; }
}

public enum ReferenceKind
{
    Declaration,
    Definition,
    Reference,
    Implementation
}