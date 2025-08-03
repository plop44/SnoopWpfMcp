# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

### Main Solution
```bash
# Build the entire solution
dotnet build SnoopMcp.sln

# Build in Release mode
dotnet build SnoopMcp.sln --configuration Release

# Clean and rebuild
dotnet clean SnoopMcp.sln
dotnet build SnoopMcp.sln
```

### Individual Projects
```bash
# Build MCP Server (main entry point)
dotnet build MCP/Injector/SnoopWpfMcpServer.csproj

# Build WpfInspector library
dotnet build MCP/WpfInspector/WpfInspector.csproj

# Build TestApp for testing
dotnet build MCP/TestApp/TestApp.csproj
```

### Running the MCP Server
```bash
# Run as MCP server (primary use case) - uses official MCP C# SDK with HTTP transport
dotnet run --project MCP/Injector/SnoopWpfMcpServer.csproj

# Run test WPF application
dotnet run --project MCP/TestApp/TestApp.csproj
```

### Running Integration Tests
```bash
# Build and run integration tests
dotnet build MCP/IntegrationTests/IntegrationTests.csproj
dotnet test MCP/IntegrationTests/IntegrationTests.csproj

# Run tests with verbose output
dotnet test MCP/IntegrationTests/IntegrationTests.csproj --logger:console;verbosity=detailed
```

## Architecture Overview

This is a Model Context Protocol (MCP) infrastructure for WPF application inspection and automation. The system uses SnoopWPF injection techniques to communicate with running WPF processes.

### Core Components

1. **MCP/Injector** (SnoopWpfMcpServer) - The main MCP server that exposes WPF inspection capabilities to AI agents
   - Implements MCP protocol via HTTP with both JSON-RPC and REST endpoints
   - Uses Microsoft.SemanticKernel with modern `Kernel.CreateBuilder()` pattern for function definitions
   - Provides process discovery, injection, and communication orchestration

2. **MCP/WpfInspector** - Injectable .NET library that runs inside target WPF processes
   - Creates Named Pipe server for inter-process communication
   - Provides visual tree inspection, UI automation, and screenshot capabilities
   - Implements the `Inspector` class with core inspection logic

3. **MCP/TestApp** - Simple WPF test application for development and testing

4. **MCP/IntegrationTests** - NUnit integration tests that verify end-to-end functionality
   - Starts TestApp and Injector processes
   - Tests HTTP API endpoints for process discovery and visual tree inspection
   - Validates responses against expected JSON structure

5. **snoopwpf/** - Git submodule containing SnoopWPF infrastructure
   - Provides injection capabilities via `Snoop.InjectorLauncher`
   - Contains native C++ injector (`Snoop.GenericInjector`)
   - Core WPF inspection libraries (`Snoop.Core`)

### Communication Flow
```
MCP Client <-> HTTP <-> MCP Server (SnoopWpfMcpServer) <-> Named Pipes <-> WpfInspector (injected into WPF app)
```

### MCP Endpoints
- `GET /mcp/initialize` - Initialize MCP session
- `GET /mcp/tools` - List available tools
- `POST /mcp/tools/{toolName}` - Call a specific tool (REST-style)
- `POST /mcp/rpc` - JSON-RPC endpoint
- `GET /mcp/health` - Health check
- `GET /swagger` - API documentation (development only)

### Key MCP Functions
- `get_wpf_processes`: Discover running WPF applications
- `ping`: Establish communication with target process
- `invoke_automation_peer`: Execute automation peer actions on UI elements using Windows UI Automation patterns
- `get_visual_tree`: Retrieve complete UI hierarchy as JSON with optimized DataContext handling
- `get_element_by_hashcode`: Retrieve a single UI element by type and hashcode (much faster than get_visual_tree for targeted inspection)
- `take_wpf_screenshot`: Capture application screenshots

#### get_element_by_hashcode Usage
The `get_element_by_hashcode` function allows targeted retrieval of a single UI element without the overhead of scanning the entire visual tree. This is particularly useful for checking element state changes after performing automation actions:

```json
{
  "processId": 1234,
  "type": "System.Windows.Controls.Button",
  "hashcode": 567890
}
```

**Use Cases:**
- Check element state after clicking a button
- Verify property changes after automation actions
- Monitor specific control values during testing
- Efficiently inspect individual elements without full tree overhead

**Performance:** Much faster than `get_visual_tree` when you only need one specific element.

### Visual Tree JSON Structure
The `get_visual_tree` function returns a JSON structure with optimized DataContext handling:

```json
{
  "success": true,
  "visualTree": {
    "type": "MainWindow",
    "hashCode": 12345,
    "dataContextId": "dc_67890",  // Only when different from parent
    "properties": {
      "Width": {
        "type": "value",
        "value": 800
      },
      "Height": {
        "type": "binding",
        "path": "WindowHeight",
        "source": "DataContext",
        "mode": "TwoWay",
        "resolvedValue": 600,
        "hasError": false
      },
      "Title": {
        "type": "binding", 
        "path": "WindowTitle",
        "source": "DataContext",
        "mode": "OneWay",
        "hasError": true,
        "error": "Cannot resolve property 'WindowTitle' on DataContext"
      }
      // Rich binding information for debugging
    },
    "children": [
      {
        "type": "Button",
        "hashCode": 54321,
        // No dataContextId = inherits from parent
        "properties": { 
          "Content": {
            "type": "binding",
            "path": "ButtonText", 
            "source": "DataContext",
            "mode": "OneWay",
            "resolvedValue": "Click Me"
          }
        }
      },
      {
        "type": "ComboBoxItem",
        "hashCode": 98765,
        "dataContext": "Option 1",  // Simple string DataContext inlined
        "properties": { ... }
      },
      {
        "type": "UserControl", 
        "hashCode": 11111,
        "dataContextId": "dc_22222",  // Complex DataContext referenced
        "properties": { ... }
      }
    ]
  },
  "dataContexts": {
    "dc_67890": {
      "type": "MyApp.ViewModels.MainViewModel",
      "hashCode": 67890,
      "properties": {
        "Title": "Main Window",
        "IsLoading": false
        // Non-default dependency properties of the DataContext
      }
    },
    "dc_22222": {
      "type": "TestApp.PersonViewModel", 
      "hashCode": 22222,
      "toString": "TestApp.PersonViewModel",
      "properties": {
        "FirstName": {
          "value": "John",
          "isReadOnly": false,
          "propertyType": "String"
        },
        "LastName": {
          "value": "Doe", 
          "isReadOnly": false,
          "propertyType": "String"
        },
        "Age": {
          "value": 30,
          "isReadOnly": false,
          "propertyType": "Int32"
        },
        "FullName": {
          "value": "John Doe",
          "isReadOnly": true,
          "propertyType": "String"
        },
        "Items": {
          "value": "ObservableCollection<String>",
          "isReadOnly": true,
          "propertyType": "ObservableCollection<String>"
        }
      }
    }
    // Note: Simple DataContexts like "Option 1" are inlined, not referenced here
  }
  }
}
```

#### Key Features:
- **Binding-Aware Properties**: Properties show binding information instead of just resolved values
  - `type: "binding"` - Property has data binding with path, source, mode, and error information
  - `type: "value"` - Property has a direct value (no binding)
  - `type: "multiBinding"` - Property uses MultiBinding with multiple sources
  - `type: "priorityBinding"` - Property uses PriorityBinding with fallback sources
- **Binding Error Detection**: Shows binding errors and validation failures
  - `hasError: true` - Binding has errors
  - `error: "message"` - Detailed error message
  - `resolvedValue` - Current resolved value (even if binding has errors)
- **DataContext Optimization**: 
  - **Complex DataContexts** (ViewModels, objects): Referenced via `dataContextId` in `dataContexts` section with full property analysis
  - **Simple DataContexts** (strings, numbers, primitives): Inlined directly as `dataContext` property
  - Missing both `dataContextId` and `dataContext` means inherited from parent (follows WPF inheritance pattern)
- **Complete Property Analysis**: For DataContext objects, shows all properties with:
  - `value` - Current property value
  - `isReadOnly` - Whether property has a public setter
  - `propertyType` - Full type information including generics
  - `error` - If property couldn't be read
- **Property Ordering**: Properties ordered from most specific type to base classes
- **Type Information**: Full type names and hash codes for precise element identification

#### Property Types and Structure:
```json
// Simple value (no binding)
"Width": {
  "type": "value",
  "value": 800
}

// Successful binding
"Text": {
  "type": "binding",
  "path": "UserName",
  "source": "DataContext", 
  "mode": "TwoWay",
  "resolvedValue": "John Doe"
}

// Binding with error
"Title": {
  "type": "binding",
  "path": "NonExistentProperty",
  "source": "DataContext",
  "mode": "OneWay", 
  "hasError": true,
  "error": "Cannot resolve property 'NonExistentProperty'"
}

// Element name binding
"IsEnabled": {
  "type": "binding",
  "path": "IsChecked",
  "elementName": "MyCheckBox",
  "mode": "OneWay",
  "resolvedValue": true
}

// Converter binding
"Visibility": {
  "type": "binding",
  "path": "IsVisible",
  "source": "DataContext",
  "mode": "OneWay",
  "converter": "BooleanToVisibilityConverter",
  "resolvedValue": "Visible"
}
```

#### DataContext Property Analysis:
```json
// ViewModel properties with detailed information
"properties": {
  "FirstName": {
    "value": "John",
    "isReadOnly": false,       // Has public setter
    "propertyType": "String"
  },
  "FullName": {
    "value": "John Doe", 
    "isReadOnly": true,        // Computed property, no setter
    "propertyType": "String"
  },
  "Items": {
    "value": "ObservableCollection<String>",
    "isReadOnly": true,        // Collection property, no setter
    "propertyType": "ObservableCollection<String>"
  },
  "ErrorProperty": {
    "error": "Property threw exception during read",
    "isReadOnly": false,
    "propertyType": "String"
  }
}
```

## Development Notes

### Project Structure
- Solution uses both .NET managed projects and C++ native components
- The `snoopwpf` submodule provides the low-level injection infrastructure
- All MCP-specific code is in the `MCP/` directory

### Key Dependencies
- Microsoft.SemanticKernel - Function definitions and plugin system with modern Kernel.CreateBuilder()
- Microsoft.Extensions.Hosting - ASP.NET Core web hosting
- System.Management - Process information
- SnoopWPF submodule - Injection infrastructure

### Testing
- Use `MCP/TestApp` as a target WPF application for testing injection
- The MCP server logs to console for debugging
- Logs from injected processes go to `%TEMP%\WpfInspector.log`

### Security Considerations
- Process injection requires elevated privileges on some systems
- Only targets WPF processes (filters out system processes like explorer.exe)
- Named pipes use process-specific naming for isolation