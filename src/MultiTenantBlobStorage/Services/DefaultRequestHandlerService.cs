﻿using Microsoft.Owin;
using SInnovations.Azure.MultiTenantBlobStorage.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using SInnovations.Azure.MultiTenantBlobStorage.Extensions;
using SInnovations.Azure.MultiTenantBlobStorage.Configuration.Hosting;
using SInnovations.Azure.MultiTenantBlobStorage.Logging;
using System.Net;
using SInnovations.Azure.MultiTenantBlobStorage.Notifications;
using System.IO;
using Newtonsoft.Json;
using System.Xml.Linq;
using System.Threading;
using SInnovations.Azure.MultiTenantBlobStorage.Services.RequestHandlers;
using System.Globalization;
using System.Xml;
namespace SInnovations.Azure.MultiTenantBlobStorage.Services
{
    
    
   
   
   
   
    public class DefaultRequestHandlerService : IRequestHandlerService
    {
        static readonly ILog Logger = LogProvider.GetCurrentClassLogger();

        protected MultiTenantBlobStorageOptions Options { get; set; }

        public DefaultRequestHandlerService(MultiTenantBlobStorageOptions options)
        {
            Options = options;
        }

        //protected IListBlobsService 

       // public Func<Stream, Stream,ListBlobsRequestOptions, Task> ListBlobsStreamingTransform { get; set; }



        public virtual async Task<ClaimsPrincipal> AuthenticateRequestAsync(IOwinRequest request, MultiTenantBlobStorageOptions options)
        {
            var result = await request.Context.Authentication.AuthenticateAsync(options.AuthenticationType);
            if (result == null)
                return null;

            return new ClaimsPrincipal(result.Identity);
        }


        public virtual async Task<TenantRoute> ParseRouteDataAsync(IOwinRequest request, MultiTenantBlobStorageOptions options)
        {
            var route = await request.Context.ResolveDependency<IRequestTenantResolver>().GetRouteAsync(request);

            return route;
        }


        public Task HandleAsync(IOwinContext context, ResourceContext resourceContext)
        {
            var requestOption = new RequestOptions(context.Request);
            
            
            
            if( resourceContext.Action==Constants.Actions.ContainerDelete && Options.DeleteOptions.SetMetaDataOnDelete != null)
            {
                return SetMetaDataOnDeleteAsync(context, resourceContext);
              
            }
            //switch (resourceContext.Action)
            //{
            //    case :

                 //   break;


            //    case Constants.Actions.ListBlobs:
            //        IListBlobsHandler listBlobs = context.ResolveDependency<IListBlobsHandler>();

            //        if(listBlobs.ImplementsHandleRequest)
            //        {
            //            Logger.Info("Using BlobList Provider");
            //            return listBlobs.HandleRequest(context, resourceContext, requestOption);

            //        }else if (listBlobs.ImplementsRequestTransformer)
            //        {
            //            return ExecuteAsync(context, resourceContext, listBlobs.Transformer, requestOption);
            //        }
            //        break;
         //   }


            return ExecuteAsync(context, resourceContext);
            

           
        }
        private async Task SetMetaDataOnDeleteAsync(IOwinContext context, ResourceContext resourceContext)
        {
            var storage = context.ResolveDependency<IStorageAccountResolverService>().GetStorageAccount(resourceContext.Route);

            var metadata = Options.DeleteOptions.SetMetaDataOnDelete();
            var container = storage.CreateCloudBlobClient().GetContainerReference(resourceContext.Route.ContainerName);
            if(await container.ExistsAsync())
            {
                await container.FetchAttributesAsync();
                foreach (var key in metadata.Keys)
                    container.Metadata[key] = metadata[key];

                await container.SetMetadataAsync();
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;

            }
            else
            {
                context.Response.StatusCode =(int) HttpStatusCode.NotFound;
                context.Response.ReasonPhrase = string.Format("{0} does not exist", Options.ContainerResourceName ?? "container");
            }
        }
      

        /// <summary>
        /// Builds a httpwebrequset to carry out a mirrow to blob storage.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        protected virtual HttpWebRequest BuildDefaultBlobStorageRequest(IOwinContext context, ResourceContext resourceContext)
        {
            
            var request = CreateHttpWebRequest(context.Request,resourceContext);
            var req = context.Request;

            if ((req.Method == Constants.HttpMethods.Post || req.Method == Constants.HttpMethods.Put))
            {
                // request.ContentLength = req.Body.Length;
                req.Headers.CopyTo(Constants.HeaderConstants.ContentLenght, request);
            }

            req.Headers.CopyTo(Constants.HeaderConstants.UserAgent, request);
            req.Headers.CopyTo(Constants.HeaderConstants.ContentType, request, "application/xml");
            req.Headers.CopyTo(Constants.HeaderConstants.Date, request);

            foreach (var header in req.Headers.Keys.Where(k => k.StartsWith("x-ms")))
            {
                req.Headers.CopyTo(header, request);

            }

            if (!req.Headers.ContainsKey("x-ms-version"))
            {
                request.Headers.Add("x-ms-version: 2014-02-14");
            }
            if (!req.Headers.ContainsKey("x-ms-date"))
            {
                request.Headers.Add("x-ms-date: "+DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture));
            }

            return request;

        }

        /// <summary>
        /// Create the HttpWebRequest to mirrow from blob storage, parsing and setting up url.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="resourceContext"></param>
        /// <returns></returns>
        protected virtual HttpWebRequest CreateHttpWebRequest(IOwinRequest request, ResourceContext resourceContext)
        {

            var resourceId = string.Format("{0}/{1}", resourceContext.Route.TenantId, resourceContext.Route.Resource);
            var idx = request.Uri.AbsoluteUri.IndexOf(resourceId) + resourceId.Length;
            var pathAndQuery = request.Uri.AbsoluteUri.Substring(idx);
            var qIdx = pathAndQuery.IndexOf('?');
           
            if (qIdx >= 0)
            {
                // ResourceContext.Action
                
            }
            if (resourceContext.Route.Resource.IsMissing())
                pathAndQuery += (qIdx>=0?"&":"?")+ "prefix=" + resourceContext.Route.ContainerName;

            resourceContext.Route.Path = qIdx > -1 ? pathAndQuery.Substring(0, qIdx) : pathAndQuery;

            var uri = new Uri(resourceContext.Route.Host + ( resourceContext.Route.Resource.IsMissing() ?"": resourceContext.Route.ContainerName) + pathAndQuery);
            // var request = new HttpRequestMessage(msg.Method, uri);
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.Method = request.Method;
         
            return req;
        }
        private Task ExecuteAsync(IOwinContext context, ResourceContext resourceContext)
        {
            return ExecuteAsync<RequestOptions>(context, resourceContext);
        }
        private async Task ExecuteAsync<T>(IOwinContext context, ResourceContext resourceContext, Func<Stream, Stream, T, Task> provider = null, T options = null) where T : RequestOptions
        {
            var request = BuildDefaultBlobStorageRequest(context, resourceContext);
            Logger.LogRequest(request);
            var blobAuthenticationService = context.ResolveDependency<IStorageAccountResolverService>();
            await blobAuthenticationService.SignRequestAsync(request, resourceContext.Route);

            await ForwardIncomingRequestStreamAsync(context, request);

            using (HttpWebResponse response = await GetResponseAsync(request))
            {

                context.Response.ReasonPhrase = response.StatusDescription;
                context.Response.StatusCode = (int)response.StatusCode;

                foreach (var headerKey in response.Headers.AllKeys)
                {
                    response.Headers.CopyTo(headerKey, context.Response);
                    //context.Response.Headers.Add(headerKey, response.Headers.GetValues(headerKey));
                }



                if (response.ContentLength != 0)
                {
                    using (var stream = response.GetResponseStream())
                    {

                        if (provider == null)
                        {
                            await ForwardDefaultResponseStreamAsync(context, stream, resourceContext, Options.ListBlobOptions.BlobListFilter);
                        }
                        else
                        {
                            await provider(stream, context.Response.Body, options);

                        }

                    }

                }
                
            }


        }
        private void writeJson(JsonWriter writer, XmlReader reader, string root)
        {
            while (reader.Read())
            {
                WriteJsonElement(reader, writer, root);
            }
        }
        private async Task WriteJsonAsync(Stream incoming, Stream outgoing,Func<string,string> tenantNameTransform, Func<XElement,bool> listFilter, params string[] parseInfo)
        {
            XmlReaderSettings readerSettings = new XmlReaderSettings();
            readerSettings.IgnoreWhitespace = false;
            readerSettings.Async = true;
            var reader = XmlReader.Create(incoming, readerSettings);
            
            var jsonWriter = new JsonTextWriter(new StreamWriter(outgoing));
           // jsonWriter.WriteStartObject();
          //  var serializer = JsonSerializer.CreateDefault();
            while (await reader.ReadAsync())
            {
                if (parseInfo.Any(a=>a== reader.Name))
                {
                  //  jsonWriter.WriteStartArray();
                    while (parseInfo.Any(a => a == reader.Name))
                    {
                        var name = reader.Name;
                        var node = XNode.ReadFrom(reader);
                        var el = (XElement)node;

                        if (listFilter==null || listFilter(el))
                        {
                            var nameEl = el.Element("Name");

                            if (tenantNameTransform != null)
                            {

                                nameEl.SetValue(tenantNameTransform(nameEl.Value));
                            }

                            writeJson(jsonWriter, node.CreateReader(), name);
                        }
                        //serializer.Serialize(jsonWriter, node);
                    }
                 //   jsonWriter.WriteEndArray();

                }

                WriteJsonElement(reader, jsonWriter, "EnumerationResults");

            }
          //  jsonWriter.WriteEndObject();
            jsonWriter.Flush();
        }
      
        private void WriteJsonElement(XmlReader reader, JsonWriter writer, string root)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                bool started = false;
                if (reader.LocalName != root)
                {
                    if (writer.WriteState != Newtonsoft.Json.WriteState.Object)
                        writer.WriteStartObject();
                    writer.WritePropertyName(reader.LocalName.Substring(0,1).ToLower() + reader.LocalName.Substring(1).Replace("-",""));
                }
                else
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("$type");
                    writer.WriteValue(root);
                    started = true;
                }
               
                

                if (reader.LocalName == "Containers" || reader.LocalName == "Blobs")
                {
                    writer.WriteStartArray();
                }
                else if (reader.HasAttributes)
                {
                    if(!started)
                    {
                        writer.WriteStartObject();
                        started = true;
                    }
                    
                    while (reader.MoveToNextAttribute())
                    {
                        writer.WritePropertyName(reader.Name);
                        writer.WriteValue(reader.Value);
                        //  Console.WriteLine(" {0}={1}", reader.Name, reader.Value);
                    }
                    // Move the reader back to the element node.
                    reader.MoveToElement();
                }

                if (reader.IsEmptyElement)
                {
                    if (reader.LocalName == "Containers" || reader.LocalName == "Blobs")
                    {
                        writer.WriteEndArray();
                    }
                    else
                    {
                        if (!started)
                            writer.WriteStartObject();
                        writer.WriteEndObject();
                    }
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.LocalName == "Containers" || reader.LocalName == "Blobs")
                {
                    writer.WriteEndArray();
                }
                else
                {
                    writer.WriteEndObject();
                }
            }
            else if (reader.NodeType == XmlNodeType.Text)
            {
                writer.WriteValue(reader.Value);
                reader.Read();//skip end of a value
            }

           
        }
        private async Task ForwardDefaultResponseStreamAsync(IOwinContext context, Stream stream, ResourceContext resourceContext, Func<XElement,bool> listFilter)
        {
            if (context.Request.Accept.IsPresent() && context.Request.Accept.IndexOf(Constants.ContentTypes.Json,StringComparison.OrdinalIgnoreCase) > -1)
            {
                if (resourceContext.Action == Constants.Actions.ListBlobs)
                {
                    await WriteJsonAsync(stream, context.Response.Body,null,listFilter, "Blob","BlobPrefix");
                    return;
                }else if(resourceContext.Action == Constants.Actions.ListResources)
                {
                    await WriteJsonAsync(stream, context.Response.Body, (s)=> s.Substring(resourceContext.Route.ContainerName.Length+1),listFilter,"Container");
                    return;
                }

            }
           

            {
                byte[] buffer = new byte[81920];
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, context.Request.CallCancelled)) != 0)
                {
                    context.Request.CallCancelled.ThrowIfCancellationRequested();

                    await context.Response.Body.WriteAsync(buffer, 0, count, context.Request.CallCancelled);
                }
                await context.Response.Body.FlushAsync();
            }
        }

        protected virtual async Task ForwardIncomingRequestStreamAsync(IOwinContext context, HttpWebRequest request)
        {
            if (request.ContentLength > 0)
            {
                using (var stream = await request.GetRequestStreamAsync())
                {
                    byte[] buffer = new byte[81920];
                    int count;
                    while ((count = await context.Request.Body.ReadAsync(buffer, 0, buffer.Length, context.Request.CallCancelled)) != 0)
                    {
                        context.Request.CallCancelled.ThrowIfCancellationRequested();

                        await stream.WriteAsync(buffer, 0, count, context.Request.CallCancelled);
                    }
                }
            }
        }

        private async Task<HttpWebResponse> GetResponseAsync(HttpWebRequest request)
        {
            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)await request.GetResponseAsync();

            }
            catch (WebException e)
            {
                response = (HttpWebResponse)e.Response;
            }
            return response;
        }




    }
}