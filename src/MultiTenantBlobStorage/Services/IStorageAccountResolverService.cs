﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SInnovations.Azure.MultiTenantBlobStorage.Services
{
    public interface IStorageAccountResolverService
    {
       

        Task<string> GetBlobEndpointAsync(TenantRoute tenantRoute);
    }
}