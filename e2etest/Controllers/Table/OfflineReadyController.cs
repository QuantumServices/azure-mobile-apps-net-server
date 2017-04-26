// ----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ----------------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.OData;
using Microsoft.Azure.Mobile.Server;
using ZumoE2EServerApp.DataObjects;
using ZumoE2EServerApp.Models;
using System.Collections.Generic;

namespace ZumoE2EServerApp.Controllers
{
    public class OfflineReadyController : TableController<OfflineReady>
    {
        private SDKClientTestContext context;

        protected override void Initialize(HttpControllerContext controllerContext)
        {
            base.Initialize(controllerContext);
            context = new SDKClientTestContext();
            this.DomainManager = new EntityDomainManager<OfflineReady>(context, Request);
        }

        [EnableQuery(MaxTop = 1000)]
        public IQueryable<OfflineReady> GetAll()
        {
            return Query();
        }

        public SingleResult<OfflineReady> Get(string id)
        {
            return Lookup(id);
        }

        public Task<OfflineReady> Post(OfflineReady item)
        {
            return InsertAsync(item);
        }

        public Task<OfflineReady> Patch(string id, Delta<OfflineReady> item)
        {
            return UpdateAsync(id, item);
        }

        [Route("tables/bulk/offlineready")]
        public async Task<IEnumerable<OfflineReady>> PostAll(IEnumerable<OfflineReady> items)
        {
            return await InsertAsync(items);
        }

        [Route("tables/bulk/offlineready")]
        public async Task<IEnumerable<OfflineReady>> PatchAll(IEnumerable<Delta<OfflineReady>> patches)
        {
            return await UpdateAsync(patches);
        }

        [Route("tables/bulk/offlineready")]
        public Task DeleteAll(IEnumerable<string> ids)
        {
            return DeleteAsync(ids);
        }

        public Task Delete(string id)
        {
            return DeleteAsync(id);
        }
    }
}