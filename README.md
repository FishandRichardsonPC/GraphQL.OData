# GraphQL.OData
C# Library for nesting an OData API within a graphql schema, Intended to be used with the GraphQL 
package on nuget. A sample project is included which creates a graphql server with the MS Graph api
nested within

# Usage
Within `Startup.ConfigureServices` add
```c#
services.AddGraphQLOData();
```
Within your Query constructor classes add ODataResolver as an injected parameter, then add the following
replacing everything between <angle brackets>
```c#
this.AddField(
    new FieldType
    {
        Name = "<the root endpoint which you will be nesting under>",
        ResolvedType = oDataResolver.GetQueryType(
            "<the prefix which will be added to all types>",
            "<the base url which can be used to fetch the metadata and call the api>"
        ),
        Resolver = oDataResolver
    }
);
```
This will add all EntitySet and Singleton elements to your query type

Within your Mutation constructor classes add ODataResolver as an injected parameter, then add the following
replacing everything between <angle brackets>
```c#
this.AddField(
    new FieldType
    {
        Name = "<the root endpoint which you will be nesting under>",
        ResolvedType = oDataResolver.GetMutationType(
            "<the prefix which will be added to all types>",
            "<the base url which can be used to fetch the metadata and call the api>"
        ),
        Resolver = oDataResolver
    }
);
```
This currently only adds a placeholder
