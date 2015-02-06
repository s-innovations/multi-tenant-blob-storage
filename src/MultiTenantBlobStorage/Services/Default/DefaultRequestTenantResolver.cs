﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SInnovations.Azure.MultiTenantBlobStorage.Services.Default
{
    public class DefaultRequestTenantResolver : IRequestTenantResolver
    {
        protected  ITenantContainerNameService  ContainerNameService{get;set;}
        protected IStorageAccountResolverService StorageAccountResolverService{get;set;}

        public DefaultRequestTenantResolver(ITenantContainerNameService s1, IStorageAccountResolverService s2)
        {
            ContainerNameService = s1;
            StorageAccountResolverService = s2;
        }
        public async Task<TenantRoute> GetRouteAsync(Microsoft.Owin.IOwinRequest owinRequest)
        {
            var parts = owinRequest.Path.Value.Trim('/').Split('/');

            var route = new TenantRoute
            {
                TenantId = parts[0],
                Resource = parts[1],
            };
            route.ContainerName = await ContainerNameService.GetContainerNameAsync(route);
            route.Host = await StorageAccountResolverService.GetBlobEndpointAsync(route);

            return route;
        }
    }
}