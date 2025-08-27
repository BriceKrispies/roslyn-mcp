# .NET LSP MCP Server

A Model Context Protocol (MCP) server that provides C# code analysis tools using Roslyn. This server enables AI assistants to analyze .NET solutions, projects, and files with comprehensive semantic understanding.

## Installation

### Prerequisites
- .NET 8.0 SDK
- A compatible MCP client (like Cursor)

### Setup
1. Clone this repository
2. Build the server:
   ```bash
   dotnet build dotnet-lsp-mcp/server/server.csproj
   ```
3. Configure your MCP client to use this server:
   ```json
   {
     "mcp-csharp": {
       "type": "stdio",
       "command": "dotnet",
       "args": ["run", "--project", "path/to/dotnet-lsp-mcp/server/server.csproj"],
       "env": {
         "SolutionPath": "path/to/your/solution.sln"
       }
     }
   }
   ```

## Available Tools

| Tool Name | Description | Parameters |
|-----------|-------------|------------|
| **LoadSolution** | Loads a .NET solution and prepares it for analysis | `solutionPath` |
| **LoadProject** | Loads a single .NET project and prepares it for analysis | `projectPath` |
| **AnalyzeProject** | Analyzes a project and returns comprehensive analysis results including diagnostics, symbols, and metrics | `projectPath` |
| **AnalyzeSolution** | Analyzes a solution and returns analysis for all projects within it | `solutionPath` |
| **AnalyzeFile** | Analyzes a specific file and returns detailed information about symbols, diagnostics, and structure | `filePath` |
| **GetSymbols** | Gets all symbols of a specified kind from a project | `projectName`, `symbolKind` (optional: Class, Method, Property, Field, etc.) |
| **FindReferences** | Finds all references to a symbol across the loaded workspace | `symbol`, `projectName` (optional) |
| **FindImplementations** | Finds all implementations of an interface across the loaded workspace | `interfaceName`, `projectName` (optional) |
| **GetDiagnostics** | Gets compiler diagnostics (errors, warnings, info) for a project or all projects | `projectName` (optional) |
| **GetWorkspaceStatus** | Gets the current workspace status including loaded projects and their basic information | None |
| **ClearWorkspace** | Clears the current workspace and invalidates all caches | None |
| **FindCallersFromLocation** | Find all callers (controllers/endpoints) that eventually reach the method at the given location | `filePath`, `line`, `column`, `maxDepth` (default: 5), `limit` (default: 100) |
| **FindCalleesFromLocation** | Find all methods and database operations called from the given location | `filePath`, `line`, `column`, `maxDepth` (default: 5), `limit` (default: 200) |
| **GetMediatRMappings** | Gets MediatR handler mappings showing relationships between requests/commands and their handlers | None |
| **EchoSolutionPath** | Echoes the SolutionPath environment variable | None |

## Features

- **Semantic Analysis**: Deep understanding of C# code using Roslyn
- **Call Graph Analysis**: Find callers and callees with configurable depth
- **MediatR Support**: Special handling for MediatR request/handler patterns  
- **Diagnostics**: Access to compiler errors, warnings, and suggestions
- **Symbol Navigation**: Find definitions, references, and symbol information
- **Project Management**: Load and analyze entire solutions or individual projects
- **Caching**: Intelligent caching for improved performance

## Architecture

The server is built with:
- **Roslyn**: Microsoft's C# compiler platform for semantic analysis
- **Model Context Protocol**: Standard protocol for AI tool integration
