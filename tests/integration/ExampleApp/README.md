# ExampleApp - LSP Testing Application

## Purpose

ExampleApp is a **representative C# application** designed specifically to test and validate Language Server Protocol (LSP) functionality. It serves as a comprehensive test case that exercises various aspects of C# code analysis, symbol resolution, and architectural pattern recognition.

## Why This Application Exists

This application provides a **realistic codebase** with common enterprise patterns to ensure that LSP tools can correctly:

- **Analyze modern C# architectures** (MVC, CQRS, DI, Entity Framework)
- **Resolve complex call chains** through frameworks like MediatR 
- **Navigate cross-project dependencies** between multiple assemblies
- **Find interface implementations** across different service layers
- **Understand dependency injection patterns** with multiple containers (built-in DI + Autofac)
- **Trace execution flows** from controllers through handlers to data access
- **Analyze database interactions** via Entity Framework Core
- **Handle asynchronous programming** patterns throughout the stack

## Key Testing Scenarios

The application is intentionally designed to test LSP capabilities around:

### 📋 **CQRS Pattern Recognition**
- MediatR requests/commands mapped to handlers
- Complex handler chains with multiple dependencies
- Query/Command separation with different result types

### 🔌 **Interface Implementation Discovery** 
- Service interfaces with multiple concrete implementations
- Dependency injection registration patterns
- Abstract service layers with various adapters

### 🚀 **Framework Integration Analysis**
- ASP.NET Core MVC controller actions
- Entity Framework navigation and relationships  
- Third-party library usage (Autofac, MediatR)

### 🎯 **Call Graph Traversal**
- HTTP requests → Controllers → MediatR → Handlers → Database
- Cross-cutting concerns (logging, authentication, permissions)
- Service-to-service communication patterns

### 📦 **Multi-Project Solutions**
- Project references and assembly boundaries
- Shared contracts and cross-project symbol resolution
- Package dependencies and third-party integrations

## Target LSP Features

This codebase is specifically designed to validate:

- **Go to Definition** across projects and frameworks
- **Find All References** including indirect framework calls
- **Find Implementations** of interfaces and abstract classes  
- **Call Hierarchy** through complex execution paths
- **Symbol Resolution** in dependency injection scenarios
- **Type Information** for generic types and complex hierarchies
- **Diagnostic Analysis** for realistic business logic

## Architecture Overview

- **ASP.NET Core 8.0** - Modern web framework
- **MediatR** - CQRS implementation with request/response pattern
- **Entity Framework Core** - ORM with SQLite database
- **Autofac** - Advanced dependency injection container
- **Multi-project solution** - Demonstrates assembly references

The application intentionally uses **multiple architectural patterns** within a single codebase to create realistic complexity that LSP tools encounter in production environments.
