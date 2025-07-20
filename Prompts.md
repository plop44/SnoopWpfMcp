List of prompts used to generate this project:

First prompt: 

My root folder has two folders: 
- .\snoopwpf : that is a external github repo, we should try not update it as much as possible
- .\MCP : that is the folder with the new project we want to create, we can change its content as much as we want

There is an existing tool called "snoopwpf" with a description of what it does there: .\snoopwpf\README.md, please read it.

At the moment snoppwpf is injecting a windows into the remote process.
We want to add a new usage: we want to inject some code into the remote process, this code should listen to incoming queries and replies to them.
We should use the same way snoopwpf is doing process injection, but instead we should inject our own new code "WpfInspector"

We should start with a command "Ping" and the injected code should reply "Pong".

We want to use Named Pipe for the communication.

We want here two parts:
- the injected code: its name is WpfInspector
- the Injector: it should take as an argument a Process Id, then it should:
    1/ inject the WpfInspector
    2/ listen to reply then send a Ping request
    3/ display the reply. We want to add a timeout in case reply never arrive.
- a WPF basic test app that we will use to test our new tool.

The terminal uses Powershell, just you know if you have to run commands.
In our new code, we don't wan to use any command line argument nuger package, just plain code parsing arguments.

Please implement that.

The solution file should be in folder C:\Users\guenn\source\perso\SnoopMcp, and we should have projects from folder MCP and snoopwpf in our solution (let put snoopwpf projects in a folder called Externals to make it clear)

We want to add a debug profile to start our infrastructure.

Second prompt:
Currently Injector.csproj is a standard console app.
Now I want to make it a MCP server.
It should have two methods:
- Get all Wpf process running in current PC
- Given a Process Id, inject WpfInspector into this remote process if it's not already done, then send Ping to it. And send back to the Agent contacting the MCP the reply back of the Wpf App.

Currently there is nothing about MCP, please use Microsoft.SemanticKernel

A graph of communication would be:
AI Agent <=> Mcp Server (it is current Injector project but repurposed) <=> Wpf App

Third prompt:
Now I want to add a new tool, where I can click a button.
The input should be a PID and a button text.
The strategy here, in the WPFInspector:
1/ check if the process exist and is WPF
2/ check if the WPfInspector has been injected
3/ get the MainWindow and get a visual tree helper to the child of type button with the searched text.

Fourth prompt:
I want to enhance the button clicking functionality. When a button with the specified text is not found, the system should automatically list the first 10 available button texts to help with debugging. This information should be injected into the Agent context (not returned as JSON) to provide helpful debugging information.

The enhancement should:
1/ When button is not found, collect all button texts from the visual tree
2/ Return the first 10 button texts in the error message for debugging
3/ Make the error message more helpful by showing what buttons are actually available
4/ This helps users identify the correct button text when their initial search fails

Fifth prompt:
Look at attached prompt, to resolve it the agent is making 3 tools calls.
Instead I just want one tool call, we should pass either a PID either or an app name and a button text, and under the hood the injector should try find the process, inject the WpfInspector if it's not already done and find the button and click it.

6th prompt
Now I want to add the ability to take a screen capture of the main window of a wpf app and to return it to the agent.

7th prompt
I wan to add a tool call to get the current visual tree. It should return it as a json. It is taking in input a process id.
