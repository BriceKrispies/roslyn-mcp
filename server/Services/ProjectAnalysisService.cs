using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System.Text;

namespace mcp_server.Services;

public class ProjectAnalysisService : IProjectAnalysisService
{
    private readonly IRoslynWorkspaceService _workspaceService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<ProjectAnalysisService> _logger;

    public ProjectAnalysisService(
        IRoslynWorkspaceService workspaceService,
        ICacheService cacheService,
        ILogger<ProjectAnalysisService> logger)
    {
        _workspaceService = workspaceService;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<ProjectAnalysisResult> AnalyzeProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"project_analysis_{Path.GetFileNameWithoutExtension(projectPath)}_{File.GetLastWriteTime(projectPath).Ticks}";
        
        var cached = await _cacheService.GetAsync<ProjectAnalysisResult>(cacheKey, cancellationToken);
        if (cached != null)
        {
            _logger.LogDebug("Returning cached project analysis for: {ProjectPath}", projectPath);
            return cached;
        }

        _logger.LogInformation("Analyzing project: {ProjectPath}", projectPath);

        await _workspaceService.LoadProjectAsync(projectPath, cancellationToken);
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var project = await _workspaceService.GetProjectByNameAsync(projectName, cancellationToken);

        if (project == null)
        {
            throw new InvalidOperationException($"Project not found: {projectName}");
        }

        var diagnostics = await _workspaceService.GetDiagnosticsAsync(projectName, cancellationToken);
        var sourceFiles = project.Documents.Where(d => d.FilePath != null).Select(d => d.FilePath!).ToList();
        var dependencies = project.MetadataReferences.Select(r => r.Display ?? "Unknown").ToList();

        var totalLines = 0;
        foreach (var document in project.Documents)
        {
            var text = await document.GetTextAsync(cancellationToken);
            totalLines += text.Lines.Count;
        }

        var result = new ProjectAnalysisResult
        {
            ProjectName = project.Name,
            ProjectPath = projectPath,
            SourceFiles = sourceFiles,
            Diagnostics = diagnostics.ToList(),
            Dependencies = dependencies,
            TotalLines = totalLines,
            AnalyzedAt = DateTime.UtcNow
        };

        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(2), cancellationToken);
        
        _logger.LogInformation("Project analysis completed: {ProjectName} ({SourceFileCount} files, {DiagnosticCount} diagnostics)", 
            project.Name, sourceFiles.Count, diagnostics.Count());

        return result;
    }

    public async Task<SolutionAnalysisResult> AnalyzeSolutionAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"solution_analysis_{Path.GetFileNameWithoutExtension(solutionPath)}_{File.GetLastWriteTime(solutionPath).Ticks}";
        
        var cached = await _cacheService.GetAsync<SolutionAnalysisResult>(cacheKey, cancellationToken);
        if (cached != null)
        {
            _logger.LogDebug("Returning cached solution analysis for: {SolutionPath}", solutionPath);
            return cached;
        }

        _logger.LogInformation("Analyzing solution: {SolutionPath}", solutionPath);

        await _workspaceService.LoadSolutionAsync(solutionPath, cancellationToken);
        var projects = await _workspaceService.GetProjectsAsync(cancellationToken);

        var projectResults = new List<ProjectAnalysisResult>();
        var allDiagnostics = new List<Diagnostic>();

        foreach (var project in projects)
        {
            try
            {
                var projectResult = await AnalyzeProjectAsync(project.FilePath!, cancellationToken);
                projectResults.Add(projectResult);
                allDiagnostics.AddRange(projectResult.Diagnostics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze project: {ProjectName}", project.Name);
            }
        }

        var result = new SolutionAnalysisResult
        {
            SolutionName = Path.GetFileNameWithoutExtension(solutionPath),
            SolutionPath = solutionPath,
            Projects = projectResults,
            AllDiagnostics = allDiagnostics,
            AnalyzedAt = DateTime.UtcNow
        };

        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(4), cancellationToken);
        
        _logger.LogInformation("Solution analysis completed: {SolutionName} ({ProjectCount} projects, {DiagnosticCount} total diagnostics)", 
            result.SolutionName, projectResults.Count, allDiagnostics.Count);

        return result;
    }

    public async Task<FileAnalysisResult> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"file_analysis_{filePath.GetHashCode()}_{File.GetLastWriteTime(filePath).Ticks}";
        
        var cached = await _cacheService.GetAsync<FileAnalysisResult>(cacheKey, cancellationToken);
        if (cached != null)
        {
            _logger.LogDebug("Returning cached file analysis for: {FilePath}", filePath);
            return cached;
        }

        _logger.LogDebug("Analyzing file: {FilePath}", filePath);

        var document = await _workspaceService.GetDocumentAsync(filePath, cancellationToken);
        if (document == null)
        {
            throw new InvalidOperationException($"Document not found: {filePath}");
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        var text = await document.GetTextAsync(cancellationToken);

        var diagnostics = semanticModel?.GetDiagnostics() ?? [];
        var symbols = await ExtractSymbolsFromFileAsync(document, semanticModel, syntaxTree, cancellationToken);

        var result = new FileAnalysisResult
        {
            FilePath = filePath,
            ProjectName = document.Project.Name,
            Diagnostics = diagnostics.ToList(),
            Symbols = symbols,
            LineCount = text.Lines.Count,
            AnalyzedAt = DateTime.UtcNow
        };

        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(1), cancellationToken);

        return result;
    }

    public async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync(string projectName, SymbolKind? symbolKind = null, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"symbols_{projectName}_{symbolKind?.ToString() ?? "all"}";
        
        var cached = await _cacheService.GetAsync<List<SymbolInfo>>(cacheKey, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        var project = await _workspaceService.GetProjectByNameAsync(projectName, cancellationToken);
        if (project == null)
        {
            return [];
        }

        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation == null)
        {
            return [];
        }

        var symbols = new List<SymbolInfo>();

        foreach (var document in project.Documents)
        {
            try
            {
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
                
                if (semanticModel != null && syntaxTree != null)
                {
                    var fileSymbols = await ExtractSymbolsFromFileAsync(document, semanticModel, syntaxTree, cancellationToken);
                    
                    if (symbolKind.HasValue)
                    {
                        fileSymbols = fileSymbols.Where(s => s.Kind == symbolKind.Value);
                    }
                    
                    symbols.AddRange(fileSymbols);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract symbols from document: {DocumentName}", document.Name);
            }
        }

        await _cacheService.SetAsync(cacheKey, symbols, TimeSpan.FromHours(2), cancellationToken);
        return symbols;
    }

    public async Task<IEnumerable<ReferenceInfo>> FindReferencesAsync(string symbol, string? projectName = null, CancellationToken cancellationToken = default)
    {
        var references = new List<ReferenceInfo>();
        var projects = string.IsNullOrEmpty(projectName)
            ? await _workspaceService.GetProjectsAsync(cancellationToken)
            : new[] { await _workspaceService.GetProjectByNameAsync(projectName, cancellationToken) }.Where(p => p != null).Cast<Project>();

        foreach (var project in projects)
        {
            try
            {
                foreach (var document in project.Documents)
                {
                    var text = await document.GetTextAsync(cancellationToken);
                    var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
                    
                    if (syntaxTree == null) continue;

                    var lines = text.Lines;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        var line = lines[i];
                        var lineText = text.ToString(line.Span);
                        
                        var index = lineText.IndexOf(symbol, StringComparison.OrdinalIgnoreCase);
                        while (index >= 0)
                        {
                            references.Add(new ReferenceInfo
                            {
                                Symbol = symbol,
                                FilePath = document.FilePath ?? string.Empty,
                                Line = i + 1,
                                Column = index + 1,
                                Context = lineText.Trim(),
                                Kind = DetermineReferenceKind(lineText, symbol, index)
                            });
                            
                            index = lineText.IndexOf(symbol, index + 1, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to find references in project: {ProjectName}", project.Name);
            }
        }

        return references;
    }

    public async Task InvalidateCacheAsync(string? projectName = null)
    {
        if (string.IsNullOrEmpty(projectName))
        {
            await _cacheService.ClearAsync();
            _logger.LogInformation("All analysis cache invalidated");
        }
        else
        {
            // Would need to implement pattern-based cache removal
            _logger.LogInformation("Cache invalidated for project: {ProjectName}", projectName);
        }
    }

    private async Task<IEnumerable<SymbolInfo>> ExtractSymbolsFromFileAsync(Document document, SemanticModel? semanticModel, SyntaxTree? syntaxTree, CancellationToken cancellationToken)
    {
        if (semanticModel == null || syntaxTree == null)
        {
            return [];
        }

        var symbols = new List<SymbolInfo>();
        var root = await syntaxTree.GetRootAsync(cancellationToken);

        // Extract various symbol types
        var declarations = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>();
        foreach (var declaration in declarations)
        {
            var symbol = semanticModel.GetDeclaredSymbol(declaration);
            if (symbol != null)
            {
                symbols.Add(CreateSymbolInfo(symbol, declaration, document.FilePath ?? string.Empty));
            }
        }

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        foreach (var method in methods)
        {
            var symbol = semanticModel.GetDeclaredSymbol(method);
            if (symbol != null)
            {
                symbols.Add(CreateSymbolInfo(symbol, method, document.FilePath ?? string.Empty));
            }
        }

        var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>();
        foreach (var property in properties)
        {
            var symbol = semanticModel.GetDeclaredSymbol(property);
            if (symbol != null)
            {
                symbols.Add(CreateSymbolInfo(symbol, property, document.FilePath ?? string.Empty));
            }
        }

        var fields = root.DescendantNodes().OfType<FieldDeclarationSyntax>();
        foreach (var field in fields)
        {
            foreach (var variable in field.Declaration.Variables)
            {
                var symbol = semanticModel.GetDeclaredSymbol(variable);
                if (symbol != null)
                {
                    symbols.Add(CreateSymbolInfo(symbol, variable, document.FilePath ?? string.Empty));
                }
            }
        }

        return symbols;
    }

    private SymbolInfo CreateSymbolInfo(ISymbol symbol, SyntaxNode node, string filePath)
    {
        var span = node.GetLocation().GetLineSpan();
        
        return new SymbolInfo
        {
            Name = symbol.Name,
            Kind = symbol.Kind,
            ContainingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            ContainingType = symbol.ContainingType?.ToDisplayString() ?? string.Empty,
            FilePath = filePath,
            StartLine = span.StartLinePosition.Line + 1,
            StartColumn = span.StartLinePosition.Character + 1,
            EndLine = span.EndLinePosition.Line + 1,
            EndColumn = span.EndLinePosition.Character + 1,
            Signature = symbol.ToDisplayString(),
            Documentation = symbol.GetDocumentationCommentXml() ?? string.Empty,
            IsPublic = symbol.DeclaredAccessibility == Accessibility.Public
        };
    }

    private static ReferenceKind DetermineReferenceKind(string lineText, string symbol, int index)
    {
        var beforeSymbol = index > 0 ? lineText[..index].TrimEnd() : string.Empty;
        var afterSymbol = index + symbol.Length < lineText.Length ? lineText[(index + symbol.Length)..].TrimStart() : string.Empty;

        if (beforeSymbol.EndsWith("class") || beforeSymbol.EndsWith("interface") || beforeSymbol.EndsWith("struct"))
            return ReferenceKind.Declaration;
        
        if (afterSymbol.StartsWith("(") && (beforeSymbol.Contains("public") || beforeSymbol.Contains("private") || beforeSymbol.Contains("protected")))
            return ReferenceKind.Definition;

        return ReferenceKind.Reference;
    }
}
