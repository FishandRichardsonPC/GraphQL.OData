# GraphQL.OData
[![Build Status](https://dev.azure.com/fishandrichardson-oss/GraphQL.OData/_apis/build/status/FishandRichardsonPC.GraphQL.OData?branchName=master)](https://dev.azure.com/fishandrichardson-oss/GraphQL.OData/_build/latest?definitionId=1&branchName=master)
[![SemVer](https://img.shields.io/nuget/v/GraphQL.OData.svg)](https://semver.org)
[![Nuget](https://img.shields.io/nuget/dt/GraphQL.OData.svg)](https://www.nuget.org/packages/GraphQL.OData)


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
This will add a new object type to your query with all EntitySet and Singleton elements on it

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
This will add a new object type to your mutation which currently only has a placeholder

# Releasing
All Pull Requests should be available as a prerelease on nuget.org. To create an official release create a release in github
with the new version number, after the build completes it will be uploaded to nuget.org