// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Extension methods for <see cref="IApplicationBuilder"/> to add MVC to the request execution pipeline.
    /// </summary>
    public static class MvcApplicationBuilderExtensions
    {
        // Property key set in routing package by UseEndpointRouting to indicate middleware is registered
        private const string EndpointRoutingRegisteredKey = "__EndpointRoutingMiddlewareRegistered";

        /// <summary>
        /// Adds MVC to the <see cref="IApplicationBuilder"/> request execution pipeline.
        /// </summary>
        /// <param name="app">The <see cref="IApplicationBuilder"/>.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        /// <remarks>This method only supports attribute routing. To add conventional routes use
        /// <see cref="UseMvc(IApplicationBuilder, Action{IRouteBuilder})"/>.</remarks>
        public static IApplicationBuilder UseMvc(this IApplicationBuilder app)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app.UseMvc(routes =>
            {
            });
        }

        /// <summary>
        /// Adds MVC to the <see cref="IApplicationBuilder"/> request execution pipeline
        /// with a default route named 'default' and the following template:
        /// '{controller=Home}/{action=Index}/{id?}'.
        /// </summary>
        /// <param name="app">The <see cref="IApplicationBuilder"/>.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IApplicationBuilder UseMvcWithDefaultRoute(this IApplicationBuilder app)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }

        /// <summary>
        /// Adds MVC to the <see cref="IApplicationBuilder"/> request execution pipeline.
        /// </summary>
        /// <param name="app">The <see cref="IApplicationBuilder"/>.</param>
        /// <param name="configureRoutes">A callback to configure MVC routes.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IApplicationBuilder UseMvc(
            this IApplicationBuilder app,
            Action<IRouteBuilder> configureRoutes)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            if (configureRoutes == null)
            {
                throw new ArgumentNullException(nameof(configureRoutes));
            }

            VerifyMvcIsRegistered(app);

            var options = app.ApplicationServices.GetRequiredService<IOptions<MvcOptions>>();

            if (options.Value.EnableEndpointRouting)
            {
                var dataSource = app.ApplicationServices.GetRequiredService<ActionEndpointDataSource>();

                var endpointRouteBuilder = new EndpointRouteBuilder(app);

                configureRoutes(endpointRouteBuilder);

                foreach (var router in endpointRouteBuilder.Routes)
                {
                    // Only accept Microsoft.AspNetCore.Routing.Route when converting to endpoint
                    // Sub-types could have additional customization that we can't knowingly convert
                    if (router is Route route && router.GetType() == typeof(Route))
                    {
                        var entry = new ConventionalRouteEntry(
                            route.Name,
                            route.RouteTemplate,
                            route.Defaults,
                            route.Constraints.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
                            route.DataTokens);

                        dataSource.AddRoute(entry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Cannot use '{router.GetType().FullName}' with Endpoint Routing.");
                    }
                }

                if (!app.Properties.TryGetValue(EndpointRoutingRegisteredKey, out _))
                {
                    // Matching middleware has not been registered yet
                    // For back-compat register middleware so an endpoint is matched and then immediately used
                    app.UseRouting(routerBuilder =>
                    {
                        routerBuilder.DataSources.Add(dataSource);
                    });
                }

                return app.UseEndpoint();
            }
            else
            {
                var routes = new RouteBuilder(app)
                {
                    DefaultHandler = app.ApplicationServices.GetRequiredService<MvcRouteHandler>(),
                };

                configureRoutes(routes);

                routes.Routes.Insert(0, AttributeRouting.CreateAttributeMegaRoute(app.ApplicationServices));

                return app.UseRouter(routes.Build());
            }
        }

        private class EndpointRouteBuilder : IRouteBuilder
        {
            public EndpointRouteBuilder(IApplicationBuilder applicationBuilder)
            {
                ApplicationBuilder = applicationBuilder;
                Routes = new List<IRouter>();
                DefaultHandler = NullRouter.Instance;
            }

            public IApplicationBuilder ApplicationBuilder { get; }

            public IRouter DefaultHandler { get; set; }

            public IServiceProvider ServiceProvider
            {
                get { return ApplicationBuilder.ApplicationServices; }
            }

            public IList<IRouter> Routes { get; }

            public IRouter Build()
            {
                throw new NotSupportedException();
            }
        }

        private static void VerifyMvcIsRegistered(IApplicationBuilder app)
        {
            // Verify if AddMvc was done before calling UseMvc
            // We use the MvcMarkerService to make sure if all the services were added.
            if (app.ApplicationServices.GetService(typeof(MvcMarkerService)) == null)
            {
                throw new InvalidOperationException(Resources.FormatUnableToFindServices(
                    nameof(IServiceCollection),
                    "AddMvc",
                    "ConfigureServices(...)"));
            }
        }
    }
}
