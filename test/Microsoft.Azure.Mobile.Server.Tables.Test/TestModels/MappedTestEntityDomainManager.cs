﻿// ----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.OData;

namespace Microsoft.Azure.Mobile.Server.TestModels
{
    public class MappedTestEntityDomainManager : MappedEntityDomainManager<TestEntity, TestEntityModel>
    {
        private TestEntityModelContext context;

        public MappedTestEntityDomainManager(TestEntityModelContext context, HttpRequestMessage request)
            : base(context, request)
        {
            this.Request = request;
            this.context = context;
        }

        public override SingleResult<TestEntity> Lookup(string id)
        {
            string entityId = GetKey<string>(id);
            return this.LookupEntity(m => m.Id == entityId);
        }

        public override async Task<TestEntity> InsertAsync(TestEntity data)
        {
            return await base.InsertAsync(data);
        }

        public override Task<TestEntity> UpdateAsync(string id, Delta<TestEntity> patch)
        {
            string entityId = GetKey<string>(id);
            return this.UpdateEntityAsync(patch, entityId);
        }

        public override Task<TestEntity> UpdateAsync(string id, Delta<TestEntity> patch, bool includeDeleted)
        {
            string entityId = GetKey<string>(id);
            return this.UpdateEntityAsync(patch, includeDeleted, entityId);
        }

        public override async Task<TestEntity> ReplaceAsync(string id, TestEntity data)
        {
            return await base.ReplaceAsync(id, data);
        }

        public override Task<bool> DeleteAsync(string id)
        {
            string movieId = GetKey<string>(id);
            return this.DeleteItemAsync(movieId);
        }

        protected override void SetOriginalVersion(TestEntityModel model, byte[] version)
        {
            this.context.Entry(model).OriginalValues["Version"] = version;
        }

        public override Task<IQueryable<TestEntity>> InsertAsync(IEnumerable<TestEntity> data)
        {
            throw new NotImplementedException();
        }

        public override Task<IEnumerable<TestEntity>> UpdateAsync(IEnumerable<Delta<TestEntity>> patches)
        {
            throw new NotImplementedException();
        }
    }
}