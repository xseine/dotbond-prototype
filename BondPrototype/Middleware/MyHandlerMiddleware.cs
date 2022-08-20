using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace BondPrototype.Middleware;

public static class MyHandlerMiddleware
{
    private static List<ControllerActionDescriptor> _bondActions;

    public static IApplicationBuilder UseCustomEngine(this IApplicationBuilder builder)
    {
        using var serviceScope = builder.ApplicationServices.CreateScope();
        var actionProvider = serviceScope.ServiceProvider.GetRequiredService<IActionDescriptorCollectionProvider>();
        _bondActions = actionProvider.ActionDescriptors.Items.Where(e => e is ControllerActionDescriptor { ControllerName: "Query" }).Cast<ControllerActionDescriptor>()
            .Where(action => action.MethodInfo.GetCustomAttributes(typeof(NonActionAttribute), true).Any() == false).ToList();
        
        builder.Use(async (context, next) =>
        {
            var bondQueriesHeader = context.Request.Headers["bond-queries"].FirstOrDefault();
            if (bondQueriesHeader == null)
            {
                await next.Invoke();
                return;
            }

            var bondQueries = bondQueriesHeader.Split(",");
            var firstActiveQuery = bondQueries.FirstOrDefault(queryName => _bondActions.Any(e => e.ActionName == queryName));
            
            context.Response.Headers.Add("first-active-query", firstActiveQuery);
            if (firstActiveQuery != null) {
                context.Request.Path = PathString.FromUriComponent($"/Query/{firstActiveQuery}");
                context.Request.Query = new QueryCollection(context.Request.Query.Where(pair => pair.Key.StartsWith(firstActiveQuery + "-"))
                    .Select(pair => (Key: pair.Key[(firstActiveQuery.Length + 1)..], pair.Value)).ToDictionary(e => e.Key, e => e.Value));
            }
            
            await next.Invoke();

        });
        return builder;
    }
}