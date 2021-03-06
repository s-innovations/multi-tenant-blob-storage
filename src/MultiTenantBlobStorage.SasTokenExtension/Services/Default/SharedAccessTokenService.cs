﻿using Microsoft.Owin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SInnovations.Azure.MultiTenantBlobStorage.Configuration.Hosting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SInnovations.Azure.MultiTenantBlobStorage.Extensions;
using SInnovations.Azure.MultiTenantBlobStorage.SasTokenExtension.Models;
using SInnovations.Azure.MultiTenantBlobStorage.Logging;
using Microsoft.WindowsAzure.Storage.Blob;

namespace SInnovations.Azure.MultiTenantBlobStorage.Services.Default
{
    public class KeyPair
    {
        public string Primary { get; set; }
        public string Secondary { get; set; }
    }

    public class SharedAccessTokenService : ISharedAccessTokenService
    {
        private readonly ILog Logger = LogProvider.GetCurrentClassLogger();

        private readonly IStorageAccountResolverService _storage;
        private readonly ITenantContainerNameService _containers;
        private readonly Lazy<Task<KeyPair>> _keyProvider;

        public SharedAccessTokenService(IStorageAccountResolverService storage, ITenantContainerNameService containers, Func<Task<KeyPair>> keysProvider)
        {
            _storage = storage;
            _containers = containers;
            _keyProvider = new Lazy<Task<KeyPair>>(keysProvider);
        }

        public SasTokenGenerationModel GetModel(IOwinRequest request)
        {


            string[] expires, noexpire;
            DateTimeOffset? expire =
                request.Headers.TryGetValue("x-ms-token-exp", out expires) && expires.Length == 1 ?
                    DateTimeOffset.Parse(expires[0], CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow.AddHours(4);
            if (request.Headers.TryGetValue("x-ms-token-noexp", out noexpire))
                expire = null;

            var claims = new List<Claim>();

            var tokenIds = request.Headers.GetValues("x-ms-token");
            if (tokenIds != null && tokenIds.Any())
            {
                claims.AddRange(tokenIds.Select(t => new Claim("token", t)));
            }

            //Expire

            if (expire.HasValue)
                claims.Add(new Claim("exp", expire.Value.ToString("R", CultureInfo.InvariantCulture)));

            return new SasTokenGenerationModel
            {
                Claims = claims,
            };
        }

        public virtual Task<SasTokenGenerationModel> GetTokenModelAsync(IOwinContext context, ResourceContext resourceContext)
        {
            var model = GetModel(context.Request);

            model.Claims.Add(new Claim("nbf", DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture)));
            model.Claims.Add(new Claim("role", "read"));
            model.Claims.Add(new Claim("role", "write"));
            model.Claims.Add(new Claim("tenant", resourceContext.Route.TenantId));
            model.Claims.Add(new Claim("resource", resourceContext.Route.Resource));
            if (!string.IsNullOrWhiteSpace(resourceContext.Route.Path))
            {
                model.Claims.Add(new Claim("prefix", resourceContext.Route.Path));

            }
            Logger.InfoFormat("Creating SasTokenGenerationModel: uri={0}, claims={1}", context.Request.Uri, string.Join(", ", model.Claims.Select(c => c.Type)));

            return Task.FromResult(model);
        }
        public virtual async Task<IEnumerable<Claim>> CheckSignatureAsync(string token)
        {

            var parts = token.Split('.');
            if (parts.Length != 3)
                return Enumerable.Empty<Claim>();

            var body = GetBase64Value(parts[1]);
            var signature = GetBase64Value(parts[2]);

            byte[] bytes = Convert.FromBase64String(body);
            string originalText = Encoding.UTF8.GetString(bytes);
            var claims = (JObject.Parse(originalText).Properties().SelectMany(GetClaims)).ToArray();

            var keys = await _keyProvider.Value;

            if ((string.Equals(parts[2], CalculateSignature(token, keys.Primary), StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[2], CalculateSignature(token, keys.Secondary), StringComparison.OrdinalIgnoreCase)))
                return claims;

            return Enumerable.Empty<Claim>();

        }

        private static string GetBase64Value(string part)
        {
            var value = part.Replace('_', '/').Replace('-', '+');
            switch (value.Length % 4)
            {
                case 2: value += "=="; break;
                case 3: value += "="; break;
            }
            return value;
        }

        private IEnumerable<Claim> GetClaims(JProperty arg)
        {
            if (arg.Value.Type == JTokenType.Array)
                return (arg.Value as JArray).Select(p => new Claim(arg.Name, p.ToString()));
            return new Claim[] { new Claim(arg.Name, arg.Value.ToString()) };
        }

        private string CalculateSignature(string token, string key)
        {
            using (HMACSHA256 hmacSha256 = new HMACSHA256(Convert.FromBase64String(key)))
            {

                return Base64UrlEncode(hmacSha256.ComputeHash(Encoding.UTF8.GetBytes(token.Substring(0, token.LastIndexOf('.')))));

            }
        }

        public virtual async Task<string> GetTokenAsync(SasTokenGenerationModel model)
        {

            if (model.Claims.Any(c => c.Type == "token"))
            {
                var tenant = model.Claims.FindFirstOrEmptyValue("tenant");
                var resource = model.Claims.FindFirstOrEmptyValue("resource");
                var path = model.Claims.FindFirstOrEmptyValue("prefix");
                var purpose = model.Claims.FindFirstOrEmptyValue("purpose");

                var tokens = model.Claims.Where(k => k.Type == "token").Select(t => t.Value).ToList();

                if (path.IsPresent())
                {
                    var blob = model.Blob;
                    if (blob == null)
                    {
                        var account = await _storage.GetStorageAccountAsync(tenant, purpose);
                        var container = account.CreateCloudBlobClient()
                            .GetContainerReference(await _containers.GetContainerNameAsync(tenant, purpose, resource));
                        blob = container.GetBlockBlobReference(path);
                    }

                    await blob.LoadTokensAsync(tokens);
                    await blob.SaveTokensAsync(tokens);
                  

                }
                else
                {
                    var account = await _storage.GetStorageAccountAsync(tenant, purpose);
                    var container = account.CreateCloudBlobClient()
                        .GetContainerReference(await _containers.GetContainerNameAsync(tenant, purpose, resource));

                    await container.FetchAttributesAsync();
                    if (container.Metadata.ContainsKey("token"))
                        tokens.AddRange(container.Metadata["token"].Split(','));

                    container.Metadata["token"] = string.Join(",", tokens);

                    await container.SetMetadataAsync();
                }

            }

            var body = string.Format("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.{0}",
                Base64UrlEncode(string.Format("{{{0}}}", string.Join(",", model.Claims.GroupBy(o => o.Type).OrderBy(o => o.Key).Select(GetStringProperty)))));

            var keys = await _keyProvider.Value;

            using (HMACSHA256 hmacSha256 = new HMACSHA256(Convert.FromBase64String(keys.Primary)))
            {

                var signature = Base64UrlEncode(hmacSha256.ComputeHash(Encoding.UTF8.GetBytes(body)));
                return string.Format("{0}.{1}", body, signature);
            }


        }

        private string GetStringProperty(IGrouping<string, Claim> arg)
        {
            return string.Format("\"{0}\":{1}", arg.Key, GetValue(arg));
        }

        private string GetValue(IGrouping<string, Claim> arg)
        {
            if (arg.Skip(1).Any())
                return string.Format("[{0}]", string.Join(",", arg.Select(a => string.Format("\"{0}\"", a.Value))));
            return string.Format("\"{0}\"", arg.First().Value);
        }
        static readonly char[] padding = { '=' };
        public string Base64UrlEncode(string input)
        {
            return Base64UrlEncode(Encoding.UTF8.GetBytes(input));

        }
        public string Base64UrlEncode(byte[] input)
        {
            return System.Convert.ToBase64String(input)
          .TrimEnd(padding).Replace('+', '-').Replace('/', '_');
        }

    }
}
