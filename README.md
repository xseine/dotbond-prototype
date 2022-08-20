
**This is a demo project that consists of a Web API project, Angular project, and "Translator" console project.
Run the first two to see what the demo is about, and then run Translator and start adding new Controllers and new queries!**

# <img src="https://i.imgur.com/guC4xm6.png" width="150" /> API tool for dynamic development

DotBond is a code-first tool for integrating frontend with backend that provides a powerful query layer on top of the API.<br/>
It provides server-side query processing capabilities to clients, and it also provides a set of features on the server to control and override query processing.
Queries are integrated, i.e. they are made using TypeScript and not a DSL language, and they are similar to LINQ in terms of syntax and functionality.
Until these new endpoints are compiled on the backend, queries are executed client-side.

It consists of these two fresh mediums:
- **Endpoint reference**: A TypeScript client generator that generates classes representing controllers, along with class definition of action parameters and return types.<br/>
  It provides <ins>full translation</ins> of used classes and records which includes method declarations, along with validation and attributes.<br/><br/>
- **API IQ**: Integrated queries (IQ) is the API layer for executing client queries using existing endpoints in the API.
  Frontend queries are compiled into their LINQ counterparts, and are evaluated on the server
  or in the datastore (if return type is IQueryable).<br/>
  Query implementation can be overriden with custom implementations, or they can be completely disabled using `[NonAction]` attribute.<br/>
  All the new endpoints are enabled by default, but future work will be done to look for redundant queries, queries similar to disabled ones, etc.