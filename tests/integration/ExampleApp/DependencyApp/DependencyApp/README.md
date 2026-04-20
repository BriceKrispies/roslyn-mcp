# DependencyApp - Cross-Project Reference Library

## Purpose

DependencyApp is a **shared utility library** designed specifically to test and validate Language Server Protocol (LSP) functionality around **cross-project symbol resolution** and **assembly boundary navigation**. It serves as a dependency for the main ExampleApp to create realistic multi-project solution scenarios.

## Why This Library Exists

This library provides **cross-assembly utility functions** that are referenced by the main application to ensure that LSP tools can correctly:

- **Navigate across project boundaries** and assembly references
- **Resolve symbols in external dependencies** beyond the main project
- **Find references and implementations** across multiple assemblies
- **Handle project-to-project dependencies** in multi-project solutions
- **Analyze package references** and shared contract patterns
- **Trace call chains** that span multiple projects and assemblies

## Key Testing Scenarios

The library is intentionally designed to test LSP capabilities around:

### 🔗 **Cross-Project Symbol Resolution**
- Static utility methods called from external projects
- Public API surface discovery across assembly boundaries
- Symbol navigation between project references

### 📦 **Multi-Assembly Dependencies** 
- Project reference patterns in solution files
- Assembly loading and symbol resolution
- Package dependency analysis across projects

### 🔍 **Inter-Project Call Analysis**
- Method calls that cross assembly boundaries
- Reference tracking from consuming applications
- Call graph traversal across multiple projects

### 🎯 **Shared Contract Patterns**
- Common utility functions used by multiple projects
- Shared type definitions and interfaces
- Cross-cutting utility libraries

## Target LSP Features

This dependency library is specifically designed to validate:

- **Go to Definition** across project references
- **Find All References** spanning multiple assemblies
- **Symbol Resolution** in external dependencies
- **Project-to-Project Navigation** through assembly boundaries
- **Multi-Project Solution Analysis** with complex dependency graphs
- **Assembly Metadata Reading** for cross-project types

## Library Contents

### MessageUtilities Class
- **FormatGreeting()** - Message formatting utilities called by handlers
- **GetTimestamp()** - Timestamp generation used in view models
- Demonstrates simple but realistic cross-project utility patterns

## Architecture Integration

- **.NET 8.0 Class Library** - Modern framework compatibility
- **Referenced by ExampleApp** - Demonstrates project dependency patterns
- **Static Utility Pattern** - Common enterprise library design
- **Assembly Boundary Testing** - Validates LSP cross-project capabilities

This library intentionally provides **simple but realistic utility functions** that mirror common enterprise patterns for shared libraries, creating authentic complexity that LSP tools encounter when analyzing multi-project solutions.
