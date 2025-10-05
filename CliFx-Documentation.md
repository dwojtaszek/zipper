# CliFx Documentation

## Overview

CliFx is a simple to use, yet powerful framework for building command-line applications. It aims to completely take over the user input layer.

## Features

- **Minimum boilerplate and easy to get started**
- **Class-first configuration via attributes**
- **Comprehensive auto-generated help text**
- **Support for deeply nested command hierarchies**
- **Graceful cancellation via interrupt signals**
- **Testable console interaction layer**
- **Built-in analyzers to catch configuration issues**
- **Targets .NET Standard 2.0+**
- **No external dependencies**

## Setup

Install via NuGet:
```bash
dotnet add package CliFx
```

## Basic Usage

### Modify Program.cs

Replace your `Main()` method with CliFx bootstrapping:

```csharp
using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;

public static async Task<int> Main() =>
    await new CliApplicationBuilder()
        .AddCommandsFromThisAssembly()
        .Build()
        .RunAsync();
```

### Define Commands

Create commands by implementing `ICommand` and using attributes:

```csharp
[Command("hello", Description = "Says hello to someone")]
public class HelloWorldCommand : ICommand
{
    [CommandParameter(0, Name = "name", Description = "Name of the person to greet")]
    public string Name { get; set; } = "World";

    [CommandOption("count", 'c', Description = "Number of times to say hello")]
    public int Count { get; set; } = 1;

    public ValueTask ExecuteAsync(IConsole console)
    {
        for (var i = 0; i < Count; i++)
            console.Output.WriteLine($"Hello, {Name}!");

        return default;
    }
}
```

## Argument Types

### Parameters

- Positional arguments
- Required by default
- Indexed by position (0, 1, 2, etc.)

```csharp
[CommandParameter(0, Name = "source")]
public string SourceFile { get; set; }

[CommandParameter(1, Name = "destination")]
public string DestinationFile { get; set; }
```

### Options

- Named arguments with flags
- Optional by default
- Support short and long names

```csharp
[CommandOption("verbose", 'v', Description = "Enable verbose output")]
public bool IsVerbose { get; set; }

[CommandOption("output", 'o', Description = "Output file path")]
public string OutputPath { get; set; }
```

### Supported Types

CliFx supports various argument types:

- **Basic types**: `int`, `bool`, `double`, `string`
- **Date/time types**: `DateTime`, `TimeSpan`
- **Enums**
- **String-initializable types**: `FileInfo`, `DirectoryInfo`
- **Collections**: `IReadOnlyList<T>`, `string[]`

```csharp
[CommandOption("files", 'f')]
public IReadOnlyList<FileInfo> InputFiles { get; set; }

[CommandOption("mode", 'm')]
public ProcessingMode Mode { get; set; } = ProcessingMode.Default;
```

## Validation

### Built-in Validation

CliFx provides automatic validation for common scenarios:

```csharp
[CommandParameter(0, IsRequired = true)]
public string RequiredParameter { get; set; }

[CommandOption("count", Validators = new[] { typeof(RangeValidator) })]
public int Count { get; set; }
```

### Custom Validators

Create custom validation by implementing `IArgumentValidator`:

```csharp
public class RangeValidator : IArgumentValidator<int>
{
    public string Validate(int value)
    {
        return value switch
        {
            < 0 => "Value cannot be negative",
            > 100 => "Value cannot exceed 100",
            _ => null
        };
    }
}
```

## Error Handling

### Automatic Error Messages

CliFx generates clear error messages for:

- Missing required parameters
- Invalid argument formats
- Validation failures
- Unrecognized options

### Custom Error Handling

Use exceptions within command execution:

```csharp
public ValueTask ExecuteAsync(IConsole console)
{
    if (!File.Exists(FilePath))
        throw new CommandException($"File not found: {FilePath}", 2);

    return default;
}
```

## Help System

### Auto-generated Help

CliFx automatically generates comprehensive help text:

```bash
myapp --help
myapp hello --help
```

### Custom Help Text

Add descriptions to commands, parameters, and options:

```csharp
[Command("calculate", Description = "Performs mathematical calculations")]
public class CalculateCommand : ICommand
{
    [CommandParameter(0, Description = "First operand")]
    public double Operand1 { get; set; }

    [CommandOption("operation", Description = "Mathematical operation to perform")]
    public MathOperation Operation { get; set; } = MathOperation.Add;
}
```

## Advanced Features

### Command Hierarchies

Support for nested subcommands:

```csharp
[Command("git commit", Description = "Record changes to the repository")]
public class GitCommitCommand : ICommand { /* ... */ }

[Command("git push", Description = "Update remote refs")]
public class GitPushCommand : ICommand { /* ... */ }
```

### Default Commands

Set a default command for the application:

```csharp
[Command("default", IsDefault = true)]
public class DefaultCommand : ICommand { /* ... */ }
```

### Environment Variables

Support for environment variable binding:

```csharp
[CommandOption("api-key", EnvironmentVariable = "MY_APP_API_KEY")]
public string ApiKey { get; set; }
```

## Testing

### Console Abstraction

CliFx provides `IConsole` interface for testing:

```csharp
public class MyCommandTests
{
    [Fact]
    public void MyCommand_ShouldOutputGreeting()
    {
        // Arrange
        var console = new FakeConsole();
        var command = new MyCommand { Name = "Test" };

        // Act
        command.ExecuteAsync(console);

        // Assert
        Assert.Equal("Hello, Test!", console.ReadOutputString());
    }
}
```

## Integration

### Spectre.Console

CliFx integrates well with Spectre.Console for enhanced UI:

```csharp
public ValueTask ExecuteAsync(IConsole console)
{
    var table = new Table();
    table.AddColumn("Property");
    table.AddColumn("Value");

    // Use with CliFx console
    AnsiConsole.Console.Write(table);

    return default;
}
```

### ConsoleTables

Integration with ConsoleTables for tabular output:

```csharp
public ValueTask ExecuteAsync(IConsole console)
{
    var table = ConsoleTables.ConsoleTable.From(data);
    console.Output.Write(table.ToString());

    return default;
}
```

## Best Practices

1. **Use descriptive names** for parameters and options
2. **Provide clear descriptions** for auto-generated help
3. **Validate input** both automatically and with custom validators
4. **Handle errors gracefully** with meaningful messages
5. **Test commands** using the console abstraction
6. **Use appropriate types** for automatic parsing
7. **Group related options** logically
8. **Consider default values** for optional parameters

## Migration Notes

When migrating from manual argument parsing:

1. Replace manual `args` parsing with CliFx attributes
2. Move validation logic into validators
3. Use `CommandException` for error handling
4. Leverage auto-generated help instead of manual help text
5. Test existing command-line scenarios to ensure compatibility