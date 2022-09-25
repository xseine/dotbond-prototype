**This is a demo of the tool that consists of a Web API project, Angular project, and "Translator" console project.
Run the first two to see what the demo is about (or use Dockerfile), and then run Translator and start adding new Controllers and new queries!**

**If you are using Docker, use the http port.**

<br/>
<img src="https://i.imgur.com/qfZKQUJ.png" width="150" />
<h2 style="margin-top: 0; line-height: 1">API tool for dynamic development</h2>

DotBond is a code-first tool for integrating frontend with backend that provides a powerful query layer on top of the API.<br/>
It provides code generations and server-side query processing capabilities to clients, and it allows server to easily control the query processing.
Queries are integrated, i.e. they are made using TypeScript and not a DSL language, and they are similar to LINQ in terms of syntax and functionality.
Until these new endpoints are compiled on the backend, queries are executed client-side.

It consists of these two fresh mediums:

- **Endpoint reference**: A TypeScript client generator that generates classes representing controllers, along with class definitions of action parameter and return types.<br/>
  It provides <ins>full translation</ins> of used classes and records which includes method declarations, along with validation and attributes.<br/><br/>
- **API IQ**: Integrated queries (IQ) is the API layer for executing client queries using existing endpoints in the API.
  Frontend queries are compiled into their LINQ counterparts, and are evaluated on the server
  or in the datastore (if return type is IQueryable).<br/>
  Query implementation can be overriden with custom implementations, or they can be completely disabled using `[NonAction]` attribute.

## Strategy and implementation

The tool is inspired by fullstack development and it aims to reduce friction between
frontend and backend team by developing simpler, but more powerful, APIs.
To avoid any mysterious allusions: it allows frontend to create queries that fit their needs without waiting for someone from backend team to do it,
and it allows backend to setup (and change) its API without too much ceremony and customization beforehand.

Theoretically, the frontend would be able to get all the data it needs for a view model by using a single query, but the issue is that the used query implementation could be poor
or even blocked on the backend, which would not provide the expected performance.
During development, this could lead to writing a lot of badly performing queries and backend playing catch-up with frontend to tweak those query implementations.

This tool will try and solve this with features such as: informing frontend about performance expectations when writing queries
(using TS type system),
by telling if a new query is similar to a disabled one, if a filter operation will be fast (has index configured in EF), if a join will be fast (is IQueryable used),
does the query take advantage of backend overrides, etc.
On the backend, new endpoints will be compiled and recompiled to utilize "base" endpoints and overriden implementations
as much as possible, derivatives of disabled endpoints will be auto-disabled, etc.
Improvement of the compiler's performance will be the main focus of the future development,
since it needs to reach a point where it feels like it does a good enough job and the layer does not need to be micro-managed.

After creating a well functioning, intuitive base for the API, frontend could then extend to match the view model exactly.
The API would be a result of the development process, instead of the process being the result of a predefined API schema.
This approach also allows for an easy integration logic of different backend systems, so the agility and independence of different systems is maintained.

Tool will be usable inside a single code repository or inside multiple using GitHub Actions.

<img src="https://i.imgur.com/d0DVlfy.png" />

## Instructions

After installation, create a `bond.json` file in the project root for the configuration ([example](https://raw.githubusercontent.com/xseine/dotbond-prototype/develop/BondPrototype/bond.json)). After
that is done, run the tool with the path to the .csproj file as argument.

**Currently only Angular is supported.**