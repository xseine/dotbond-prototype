using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Translator.IntegratedQueryRuntime;

public static class IqMiddleware
{
    private static List<ControllerActionDescriptor> _bondActions;

    public static IApplicationBuilder UseIntegratedQueriesLayer(this IApplicationBuilder builder)
    {
        using var serviceScope = builder.ApplicationServices.CreateScope();
        var actionProvider = serviceScope.ServiceProvider.GetRequiredService<IActionDescriptorCollectionProvider>();
        _bondActions = actionProvider.ActionDescriptors.Items.Where(e => e is ControllerActionDescriptor { ControllerName: "Query" }).Cast<ControllerActionDescriptor>()
            .Where(action => action.MethodInfo.GetCustomAttributes(typeof(NonActionAttribute), true).Any() == false).ToList();
        
        builder.Use(async (context, next) =>
        {
            context.Request.Path = Regex.Replace(context.Request.Path, @"^/api(?=/.+)", "");
        
            var bondQueriesHeader = context.Request.Headers["bond-queries"].FirstOrDefault();
            if (bondQueriesHeader == null)
            {
                await next.Invoke();
                return;
            }

            var bondQueries = bondQueriesHeader.Split(",");
            var firstActiveQueryName = bondQueries.FirstOrDefault(queryName => _bondActions.Any(e => e.ActionName == queryName));
            
            context.Response.Headers.Add("first-active-query", firstActiveQueryName);
            if (firstActiveQueryName != null) {
                context.Request.Path = PathString.FromUriComponent($"/Query/{firstActiveQueryName}");
                var activeQueryParams = context.Request.Query.Where(pair => pair.Key.StartsWith(firstActiveQueryName + "-"))
                    .Select(pair => (Key: pair.Key[(firstActiveQueryName.Length + 1)..], pair.Value)).ToList();
                
                if (activeQueryParams.Any() && activeQueryParams.First().Key == "param0")
                {
                    var paramNames = _bondActions.First(e => e.ActionName == firstActiveQueryName).Parameters.Select(e => e.Name).ToList();
                    activeQueryParams = activeQueryParams.Select((pair, idx) => (paramNames[0], pair.Value)).ToList();
                }

                context.Request.Query = new QueryCollection(activeQueryParams.ToDictionary(e => e.Key, e => e.Value));
            }
            
            await next.Invoke();

        });
        return builder;
    }
}