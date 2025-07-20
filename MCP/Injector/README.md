# WpfInspector MCP Server

This project has been transformed from a simple console injector into a Model Context Protocol (MCP) server using Microsoft.SemanticKernel. The MCP server provides AI agents with the ability to discover and interact with WPF applications running on the system.

## Overview

The communication flow is:
```
AI Agent <=> MCP Server (Injector project) <=> WPF Application
```

## Available MCP Functions

### 1. `get_wpf_processes`
- **Description**: Gets all WPF processes currently running on the PC
- **Parameters**: None
- **Returns**: JSON array with process information including:
  - Process ID
  - Process Name
  - Main Window Title
  - File Name
  - Working Directory
  - WPF Application status
  - Main Window status
  - Start Time

### 2. `inject_and_ping`
- **Description**: Injects WpfInspector into a specified WPF process (by Process ID) if not already injected, then sends a ping command and returns the response
- **Parameters**: 
  - `processId` (integer): The Process ID of the target WPF application
- **Returns**: JSON object with:
  - Success status
  - Process ID
  - Message
  - Response from WPF application
  - Error information (if any)
  - Whether injection was already done

### 3. `get_process_info`
- **Description**: Gets detailed information about a specific process by Process ID
- **Parameters**:
  - `processId` (integer): The Process ID to get information about
- **Returns**: JSON object with detailed process information

## How It Works

1. **Process Discovery**: The server scans running processes to identify WPF applications by checking for loaded WPF assemblies and main windows.

2. **Injection**: When requested, the server uses the Snoop.InjectorLauncher to inject the WpfInspector.dll into the target WPF process.

3. **Communication**: Once injected, the WpfInspector creates a named pipe server inside the target process. The MCP server can then communicate with it.

4. **MCP Protocol**: The server implements the Model Context Protocol, exposing the functions as tools that AI agents can call.

## Running the MCP Server

```bash
dotnet run --project MCP/Injector/Injector.csproj
```

The server will start and listen for JSON-RPC requests on stdin/stdout, following the MCP protocol.

## Configuration with AI Agents

To use this MCP server with an AI agent, configure it as an MCP server in your AI agent's configuration. The server communicates via JSON-RPC over stdin/stdout.

Example MCP configuration entry:
```json
{
  "mcpServers": {
    "wpf-inspector": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/MCP/Injector/Injector.csproj"],
      "env": {}
    }
  }
}
```

## Dependencies

- Microsoft.SemanticKernel: For MCP function definitions and kernel functionality
- Microsoft.Extensions.Hosting: For background service hosting
- Microsoft.Extensions.Logging: For logging functionality
- System.Management: For enhanced process information gathering
- Snoop.InjectorLauncher: For injecting into target processes
- WpfInspector: The component that gets injected into WPF applications

## Security Considerations

- The server requires appropriate permissions to access other processes
- Injection capabilities require the server to run with sufficient privileges
- Only WPF processes are targeted for injection

## Logging

The server logs important events to the console. For debugging, additional logging can be enabled by modifying the logging configuration in Program.cs.

## Error Handling

The MCP server includes comprehensive error handling and will return appropriate error responses for:
- Process not found
- Injection failures
- Communication timeouts
- Permission errors
- Invalid parameters
