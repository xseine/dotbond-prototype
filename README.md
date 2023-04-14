**This is a demo of the tool that consists of a Web API project, Angular project, and "Translator" console project.
Run the first two to see what the demo is about (or use Dockerfile), and then run Translator and start adding new Controllers and new queries!**

**If you are using Docker, use the http port.**

<br/>
<img align="left" src="https://i.imgur.com/qfZKQUJ.png" width="150" />

[![NuGet version](https://badge.fury.io/nu/xseine.dotbond.svg)](https://badge.fury.io/nu/xseine.dotbond)

<h2 style="margin-top: 0; line-height: 1">DotBond - intelligent tool for API development</h2>
    
DotBond is a proof of concept and is not ready for use in development.

The most important piece of design in the application is the API. It contains both the performance backend, and the developers intentions about the app.<br/>
The data, its organization and transfer (payload size, server events, chunked transfer, etc.) determine application's complexity and scalability.<br/>
These characteristics are dependant on whether API is understandable to developers, which this tool tries to manage by removing repetitive parts from the API
and allowing for custom client queries.<br/>
This is a code-first tool and its features are only possible on the .NET / TypeScript stack.
It provides:
- End-to-end type safety
- LINQ + GraphQL like features (server/client/hybrid execution)
- Query execution plan using TypeScript’s Type System
- Protection from writing bad client queries

The idea is that by having a well defined center in API,
and with this tool focusing on the rest of the interface,
the number of breaking changes and change requests in the APIs will be reduced.

Even though specific use cases of the API are tracked,
the downside is the API definition might become complicated by having second class queries from the client in it.
To address this, a built-in deciding mechanism for allowing server-side execution is implemented.

Currently just supports Angular.

Tool will be usable inside a single code repository or inside multiple using GitHub Actions.

<figure>
<img src="https://i.imgur.com/d0DVlfy.png" />
<figcaption align = "center"><b>Figure 1 - DotBond workflow</b></figcaption>
</figure>

## Instructions

After installation, create a `bond.json` file in the project root for the configuration ([example](https://raw.githubusercontent.com/xseine/dotbond-prototype/develop/BondPrototype/bond.json)). After
that is done, run the tool with the path to the .csproj file as argument.

## Features

Some features that are original to DotBond require usage of Compiler API, Expression trees, and an advanced type system,
so the stack used is the only that is supported.

**All required definitions are translated to the front-end.** <br/>
Besides class properties, class methods are also translated, along with portions of `System` namespace
that are most commonly used by the models: List methods, string methods and formatting, DateTime operations, etc.
C# 10 (supported) has a richer syntax, which is in part impossible/too difficult to translate (in/ref/out, operator overloading, checked/unchecked),
so apologies for these shortcomings. Using generators can be awkward because they can break existing code references, 
so having generator work in a less obtrusive manner will be addressed in the future.
Generated RPC methods give a much faster way of making calls in code, since they handle the method, url, parameters and the response type.

**Client queries** <br/>
Client queries are queries defined on the front-end, and can be executed on the server (_preferably_), client or on both.
In regards to syntax and functionality, they are similar to LINQ, meaning they support only expression bodies (no blocks) 
and have all the usual query operators (map, filter, join, groupjoin, take, skip, etc.)
At the start, clients fetch data from all referenced endpoints and execute the query on their own.
When the back-end gets the definition, it is provided with a default implementation of the query (which can be overriden/removed).
From then on, the query is executed server side. New queries without implementation that reference older server-side queries
will execute partially on the server and partially on the client (hybrid).
If a query definition is changed on the client it will reset to client execution, and won't use the out-of-date back-end implementation.

**Query limiter** <br/>
This component is used to put some reasonable limitations on the client,
to keep the queries aligned as much as possible with the original API. <br/>
One limitation
is that a client query cannot recreate relationship between entities if an original action already uses them.
In `1:n` relationship, parents are what the view should be about, and giving more weight to children by using properties from other endpoints 
would indicate a much more different use of the data than initially intended, perhaps grouping/catalogue of children entities.
In `1:1` relationship using more properties could abuse the relationship of the entities.
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
This is achieved by using this tool as a CLI tool `dotnet tool run dotbond <path to .csproj>`. 
