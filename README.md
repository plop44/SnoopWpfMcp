# WpfInspector MCP Infrastructure

🚧 **Work in Progress** 🚧

![SnoopWpfMcp Demo](SnoopWpfMcp.gif)

> **Note**: This project was "Vibe coded" (AI-assisted rapid prototyping) but it's looking very promising! 

## Overview

A Model Context Protocol (MCP) infrastructure for WPF applications using snoopwpf injection techniques. Allows code injection into running WPF processes and communication via Named Pipes.

## Components

- **WpfInspector**: Injected .NET library providing UI automation via Named Pipes
- **Injector**: Console app that performs injection and communicates with target process  
- **TestApp**: Simple WPF test application

## Key Features

- ✅ Process injection using snoopwpf infrastructure
- ✅ JSON-based command processing
- ✅ Visual tree inspection with element details
- ✅ UI element interaction (click, text input, properties)
- ✅ Screenshot capture (PNG/base64)


## Quick Start

### As MCP Server
Add this configuration to your MCP client's `mcp.json`:

```json
{
    "servers": {
        "wpf-inspector": {
            "type": "stdio",
            "command": "dotnet",
            "args": [
                "run",
                "--project",
                "c:\\Users\\guenn\\source\\perso\\SnoopMcp\\MCP\\Injector\\Injector.csproj",
                "--configuration",
                "Debug"
            ],
            "env": {
                "DOTNET_ENVIRONMENT": "Development"
            }
        }
    },
    "inputs": []
}
```

## Commands

- **Basic**: `PING`, `STATUS`, `EXIT`
- **Advanced JSON**: `RUN_COMMAND`, `TAKE_SCREENSHOT`, `GET_VISUAL_TREE`

## Logs & Troubleshooting

- Logs: `%TEMP%\WpfInspector.log`
- Requires admin privileges for some processes
- Uses snoopwpf injection technique
