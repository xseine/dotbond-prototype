**This is a demo of the tool that consists of a Web API project `BondPrototype`, Angular project (`BondPrototype/Angular`), and `DotBond` console project.
Run the first two to see the demo application (or use Dockerfile), and then run DotBond and start adding new actions!**<br/>
**If you want to use the Dockerfile, use the http port.**

<br/>
<img align="left" src="https://i.imgur.com/qfZKQUJ.png" width="150" />

[![NuGet version](https://badge.fury.io/nu/xseine.dotbond.svg)](https://badge.fury.io/nu/xseine.dotbond)

<h3 style="margin-top: 0; line-height: 1">DotBond - tool for dynamic API development</h3>

DotBond is currently in the proof of concept stage and is not yet suitable for use in development.

The most important piece of design in the application is the API. It contains both the performance backend, and the application's data.<br/>
The data, its organization and transfer (payload size, push events, chunked transfer, etc.) affect application's complexity and scalability.
This tool allows client apps to create additional endpoints (additional .NET Actions) on the server, based on original endpoints,
which are quick to generate, have relevant context and probably more tuned to the specific application's behavior.<br/>
As a two-way generator the biggest concern about this tool is handling breaking changes. More about this later —
but first, quick rundown of the features.<br/>
This is a code-first tool, working on the .NET / TypeScript stack.
It provides:
- End-to-end type safety
- LINQ + GraphQL like features (server/client/hybrid execution)
- Query execution plan using TypeScript’s Type System
- Protection from writing bad client queries

Currently just supports Angular, but can easily be made front-end agnostic.

Tool will be usable inside a single code repository or inside multiple using GitHub Actions.

<figure>
<img src="https://i.imgur.com/d0DVlfy.png" />
<figcaption align = "center"><b>Figure 1 - DotBond workflow</b></figcaption>
</figure>

## Instructions

Install dotbond as a global tool from nuget: [link](https://www.nuget.org/packages/xseine.DotBond/).<br/>
To start the tool, run `dotbond` in the project directory or `dotbond <path to .csproj>` in the terminal. Currently just supports monorepo
and it will try to automatically find the location of the front-end (Angular) root directory. 
You can use `BondPrototype` project from this repository or roll up your own.  

## Features

Quite a large feature list and the implementation is unfortunately somewhat complex and uses: RxNet, Roslyn, expression trees,
advanced types and bi-directional visitor pattern. At least, the TypeScript compiler/AST was avoided.

**All endpoint definitions are translated to the front-end.** <br/>
Besides class properties, class methods are also translated, along with portions of `System` namespace
that are most commonly used by the models: List methods, string methods and formatting, DateTime operations, etc.
C# 10 (supported) has a richer syntax, which is in part impossible/too difficult to translate (in/ref/out, operator overloading, checked/unchecked),
so apologies for these shortcomings.<br/>
RPC methods are also provided for making calls to the endpoints.<br/>
One of the pages in the demo is made to showcase capabilities of the translation engine, so give it a try.

**Client queries** <br/>
Client queries are queries defined on the front-end, and can be executed on the server (_preferably_), client or on both.
In regards to syntax and functionality, they are similar to LINQ, meaning they support only expression bodies (no block statements) 
and have all the usual query operators (map, filter, join, groupjoin, take, skip, etc.).
At the start, clients  fetch data from all referenced endpoints and execute the query on their own.
When the back-end gets the definition, it is provided with a default implementation of the query (which can be overriden/removed).
From then on, the query is executed server side. New queries without the implementation, yet, but that reference existing server-side queries
will execute partially on the server and partially on the client (hybrid).
If a query definition is changed on the client it will reset to client execution, and won't use the out-of-date back-end implementation.<br/>
Client queries are written inside `query.service.ts`, and are generated in `QueryImplementations.cs` file (used by `QueryController.cs`);

**Query limiter** <br/>
This component is used to put some reasonable limitations on the client,
to keep the queries aligned as much as possible with the original API. <br/>
One limitation
is that a client query cannot recreate relationship between entities (tables) if one of the original endpoints already uses them.
In `1:n` relationship, parents are what the view should be about, and giving more weight to children by using properties from other endpoints 
would indicate a much more different use of the data than initially intended, perhaps grouping/catalogue of children entities.
In `1:1` relationship using more properties could misuse the relationship of the entities.
For entities with undefined relationships between them, client can create queries with their data.
These are probably unambiguous, universal entities in the API.
Query limitations are injected into the TypeScript's type system, so the client developer will know whether or not a client query satisfies them.

<figure>
<img src="https://i.imgur.com/F4rWlFU.gif" />
<figcaption align = "center"><b>Figure 2 - Type update</b></figcaption>
</figure>

**A very fast File Watcher** <br/>
Even though a real-world usage of the tool might be between two different repositories, in a monorepo a File Watcher is required
to do work when a file is saved. It is able to successfully scan entire solution for the required translations, and to as well
translate front-end queries to C# counterparts. It is able to detect when a client query was implemented manually on the backend,
so no unintentional overrides, and also removes all outdated client query implementations that would prevent a back-end build.


**Formatting** <br/>
Pretty formatting of generated code is not built-in, but something like [Prettier](https://prettier.io/docs/en/editors.html) can be used.

## Breaking 

Using generators has its downsides as they might break more code references than expected, 
generated code can't be modified directly (*in most cases*), etc.
So having generator work in a less obtrusive manner will be addressed in the future.

Even though specific use cases of the API are tracked (in the generated code for actions),
the downside is the API definition might become complicated by having second-class queries from the client in it.
Query limiter is responsible for making correct decisions based on the analysis of the API and predefined rules
so the generated actions are ones that are quick and easy to upgrade.