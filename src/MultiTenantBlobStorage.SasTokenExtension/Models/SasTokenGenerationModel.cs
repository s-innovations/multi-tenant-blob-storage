﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace SInnovations.Azure.MultiTenantBlobStorage.SasTokenExtension.Models
{
    public class SasTokenGenerationModel
    {
        public SasTokenGenerationModel()
        {
            Claims = new List<Claim>();
        }
        public SasTokenGenerationModel(string tenant,string resource)
        {
            Claims = new List<Claim> {
            new Claim("tenant",tenant),
            new Claim("resource",resource),
            };
        }
        public DateTimeOffset? Expires { get; set; }
        public List<Claim> Claims { get; set; }
    }

    public static class SasTokenGenerationModelExtensions
    {
        public static SasTokenGenerationModel ExpireAt(this SasTokenGenerationModel model, DateTimeOffset expire)
        {
            model.Claims.Add(new Claim("exp", expire.ToString("R", CultureInfo.InvariantCulture)));

            return model;
        }
        public static SasTokenGenerationModel WithReadAccess(this SasTokenGenerationModel model)
        {
            model.Claims.Add(new Claim("role", "read"));

            return model;
        }
        public static SasTokenGenerationModel WithWriteAccess(this SasTokenGenerationModel model)
        {
            model.Claims.Add(new Claim("role", "write"));

            return model;
        }
    }
}
