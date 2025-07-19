List of prompts used to generate this project:

My base folder has two folders: 
- .\snoopwpf : that is a external github repo, we should try not update it as much as possible
- .\MCP : that is the folder with the new project we want to create, we can change its content as much as we want

There is an existing tool called "snoopwpf" with a description of what it does there: .\snoopwpf\README.md, please read it.

At the moment snoppwpf is injecting a windows into the remote process.
We want to add a new usage: we want to inject some code into the remote process, this code should listen to incoming queries and replies to them.

We should start with a command "Ping" and the injected code should reply "Pong".

We want to use Named Pipe for the communication.

We want here two parts:
- the injected code: its name is WpfInspector
- the Injector: it should take as an argument a Process Id, then it should:
    1/ inject the WpfInspector
    2/ listen to reply then send a Ping request
    3/ display the reply. We want to add a timeout in case reply never arrive.

    Please implement that.