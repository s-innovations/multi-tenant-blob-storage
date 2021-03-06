﻿using Microsoft.Owin;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using SInnovations.Azure.MultiTenantBlobStorage.Logging;
using SInnovations.Azure.MultiTenantBlobStorage.Notifications;
using SInnovations.Azure.MultiTenantBlobStorage.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SInnovations.Azure.MultiTenantBlobStorage.Configuration
{
    
    public class Blob{
        public string Name {get;set;}
        public IDictionary<string, string> Properties { get; set; }
        public IDictionary<string, string> MetaData { get; set; }
    }
   
    public class AddResourceOptions
    {
        public Action<TenantRoute> OnAdded { get; set; }
    }
    
    public class ListOptions
    {    

        public Func<XElement, object, bool> BlobListFilter { get; set; }

        public Func<object, IEnumerable<XElement>> BlobListFilterFinalizer { get; set; }
        public Func<IOwinRequest,object> StateInitializer { get; set; }

    }
    public class ListBlobOptions : ListOptions
    {

      

    }
    public class StorageAccountOptions
    {
        public CloudStorageAccount DefaultStorageAccount { get; set; }
        
    }
    public class MultiTenantBlobStorageOptions
    {
        static readonly ILog Logger = LogProvider.GetCurrentClassLogger();

        public MultiTenantBlobStorageOptions()
        {
            //ContainerResourceName = "Container";
            LoggingOptions = new LoggingOptions();
            Notifications = new AzureMultiTenantStorageNotifications();
            ListBlobOptions = new ListBlobOptions();
            DeleteOptions = new DeleteOptions();
            AddOptions = new AddResourceOptions();
            StorageAccountOptions = new StorageAccountOptions();

          //  var blob = new CloudBlockBlob(new Uri(""));
           //var a = new BlobProperties() { BlobType = "aa"}; 
        }

        public MultiTenantBlobStorageServiceFactory Factory { get; set; }
        public AzureMultiTenantStorageNotifications Notifications { get; set; }

        /// <summary>
        /// In all endpoints the container resource can be replaced with a custom one.
        /// </summary>
        public string ContainerResourceName { get; set; }

        public bool RequireSsl { get; set; }

        /// <summary>
        /// Gets or sets the diagnostics options.
        /// </summary>
        /// <value>
        /// The diagnostics options.
        /// </value>
        public LoggingOptions LoggingOptions { get; set; }


        public string AuthenticationType { get; set; }

        public ListBlobOptions ListBlobOptions { get; set; }

        public DeleteOptions DeleteOptions { get; set; }

        public AddResourceOptions AddOptions { get; set; }

        public StorageAccountOptions StorageAccountOptions {get;set;}
       
    }
}
