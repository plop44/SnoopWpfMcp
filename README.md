# SnoopWpfMcp - AI agent driven WPF inspector

![SnoopWpfMcp Demo](SnoopWpfMcp.gif)

## What Can You Do?

This MCP server gives AI agents the ability to understand and control any running WPF application - **the AI can do anything you can do with your mouse and keyboard, just by giving it the right prompt**.

For example, you can turn any AI agent into a **QA tester** with this simple prompt:
> *"Can you use snoop-wpf-inspector to interact with my app? You are QA trying to find some bugs, you should interact with all controls across all tabs and give me a summary of what worked and what did not."*

The AI agent can:
- **Discover and inspect** any WPF app's complete UI structure 
- **Interact with UI elements** - click buttons, enter text, select items
- **Take screenshots** of application windows
- **Debug UI issues** by examining data bindings and element properties

**Best results achieved with Claude Sonnet 4**.

## Architecture

```
┌─────────────────┐        ┌──────────────────┐        ┌──────────────────┐
│   AI Agent      │  HTTP  │   SnoopWpfMcp    │Process │     WPF App 1    │
│ (GitHub Copilot,│◄──────►│   MCP Server     │Inject  │                  │
│  Cursor, etc.)  │JSON-RPC│                  │   +    │ ┌──────────────┐ │
│                 │        │ Tools:           │Named   │ │WpfInspector  │ │
│                 │        │ • get_processes  │Pipes   │ │  (injected)  │ │
│                 │        │ • ping           │◄──────►│ │• UI Tree     │ │
│                 │        │ • get_visual_tree│        │ │• Automation  │ │
│                 │        │ • invoke_automation       │ │• Screenshots │ │
│                 │        │ • screenshot     │        │ └──────────────┘ │
└─────────────────┘        └──────────────────┘        └──────────────────┘
                                      │                           
                                      │Process                    
                                      │Inject                     
                                      │   +                       
                                      │Named                      
                                      │Pipes                      
                                      ▼                           
                           ┌──────────────────┐                  
                           │     WPF App 2    │                  
                           │                  │                  
                           │ ┌──────────────┐ │                  
                           │ │WpfInspector  │ │                  
                           │ │  (injected)  │ │                  
                           │ │• UI Tree     │ │                  
                           │ │• Automation  │ │                  
                           │ │• Screenshots │ │                  
                           │ └──────────────┘ │                  
                           └──────────────────┘                  
```

**Flow:**
1. AI agent calls MCP tools via HTTP/JSON-RPC
2. MCP server discovers and injects into target WPF processes  
3. Injected `WpfInspector` provides UI inspection via Named Pipes
4. Real-time interaction: inspect → act → verify → repeat

## Quick Start

### As MCP Server
First run your MCP server:
```
dotnet run --project .\MCP\Injector\SnoopWpfMcpServer.csproj 
```

Add this configuration to your MCP client's `mcp.json`:

```json
{
      "servers": {
          "snoop-wpf-inspector": {
              "type": "http",
              "url": "http://localhost:8080/mcp/rpc",
              "capabilities": ["tools", "prompts"],
              "env": {
                  "DOTNET_ENVIRONMENT": "Development"
              }                                                                                                                                                                                                                                                                                                                                                      
          }                                                                                                                                                                                                                                                                                                                                                          
      },                                                                                                                                                                                                                                                                                                                                                             
      "inputs": []                                                                                                                                                                                                                                                                                                                                                   
}
```

## Available Tools

### Discovery & Setup
- **`get_wpf_processes`**: Find all running WPF applications ready for inspection
- **`ping`**: Connect to and verify communication with a target WPF app

### UI Interaction  
- **`invoke_automation_peer`**: Interact with any UI element - click buttons, enter text, select items, toggle switches, and more using Windows UI Automation patterns

### Inspection & Analysis
- **`get_visual_tree`**: Get the complete UI structure as detailed JSON - see all controls, properties, data bindings, and hierarchy
- **`take_wpf_screenshot`**: Capture visual screenshots of application windows (⚠️ slower than visual tree inspection)

## Logs & Troubleshooting

- Logs: `%TEMP%\WpfInspector.log`

---

## Technical Details

### Components
- **SnoopWpfMcpServer**: MCP server that handles HTTP requests and manages WPF process interactions 
- **WpfInspector**: Injected .NET library providing UI automation via Named Pipes
- **TestApp**: Simple WPF test application

### Key Features
- Process injection using snoopwpf infrastructure
- Visual tree inspection with element details
- UI element interaction (click, text input, properties)
- Screenshot capture (PNG/base64)

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

**Note**: This project includes SnoopWPF as a submodule, which is licensed under the Microsoft Public License (Ms-PL). See the [SnoopWPF license](snoopwpf/License.txt) for details.
