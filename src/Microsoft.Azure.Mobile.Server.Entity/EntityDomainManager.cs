// ----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.OData;
using System.Web.Http.OData.Query;
using Microsoft.Azure.Mobile.Server.Properties;
using Microsoft.Azure.Mobile.Server.Tables;

namespace Microsoft.Azure.Mobile.Server
{
    /// <summary>
    /// Provides a <see cref="DomainManager{T}"/> implementation targeting SQL as the backend store using
    /// Entity Framework. In this model, there is a 1:1 mapping between the data object (DTO) exposed through
    /// a <see cref="TableController{T}"/> and the domain model. The <see cref="MappedEntityDomainManager{TData,TModel}"/>
    /// is the recommended <see cref="DomainManager{T}"/> for situations where there is not a 1:1
    /// relationship between the Data Object (DTO) and the domain model managed by SQL.
    /// </summary>
    /// <typeparam name="TData">The data object (DTO) type.</typeparam>
    public class EntityDomainManager<TData> : DomainManager<TData>
        where TData : class, ITableData
    {
        private DbContext context;

        /// <summary>
        /// Creates a new instance of <see cref="EntityDomainManager{TData}"/>
        /// </summary>
        /// <param name="context">
        /// An instance of <see cref="DbContext"/>
        /// </param>
        /// <param name="request">
        /// An instance of <see cref="HttpRequestMessage"/>
        /// </param>
        public EntityDomainManager(DbContext context, HttpRequestMessage request)
            : this(context, request, enableSoftDelete: false)
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="EntityDomainManager{TData}"/>
        /// </summary>
        /// <param name="context">
        /// An instance of <see cref="DbContext"/>
        /// </param>
        /// <param name="request">
        /// An instance of <see cref="HttpRequestMessage"/>
        /// </param>
        /// <param name="enableSoftDelete">
        /// Determines whether rows are hard deleted or marked as deleted.
        /// </param>
        public EntityDomainManager(DbContext context, HttpRequestMessage request, bool enableSoftDelete)
            : base(request, enableSoftDelete)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            this.context = context;
        }

        public DbContext Context
        {
            get
            {
                return this.context;
            }

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                this.context = value;
            }
        }

        public override IQueryable<TData> Query()
        {
            IQueryable<TData> query = this.Context.Set<TData>();
            return TableUtils.ApplyDeletedFilter(query, this.IncludeDeleted);
        }

        public override SingleResult<TData> Lookup(string id)
        {
            return this.Lookup(id, this.IncludeDeleted);
        }

        public override Task<IEnumerable<TData>> QueryAsync(ODataQueryOptions query)
        {
            throw TableUtils.GetQueryableOnlyQueryException(this.GetType(), "Query");
        }

        public override Task<SingleResult<TData>> LookupAsync(string id)
        {
            throw TableUtils.GetQueryableOnlyLookupException(this.GetType(), "Lookup");
        }

        public async override Task<TData> InsertAsync(TData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            if (data.Id == null)
            {
                data.Id = Guid.NewGuid().ToString("N");
            }

            this.Context.Set<TData>().Add(data);

            await this.SubmitChangesAsync();

            return data;
        }

        public async override Task<IEnumerable<TData>> InsertAsync(IEnumerable<TData> data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            var dataItems = data.Select(dataItem =>
            {
                if (dataItem.Id == null)
                {
                    dataItem.Id = Guid.NewGuid().ToString("N");
                }

                return dataItem;
            }).ToList();
            foreach (var dataItem in dataItems)
            {
                this.Context.Set<TData>().Add(dataItem);
            }

            await this.SubmitChangesAsync();

            return dataItems.AsQueryable();
        }

        public async override Task<TData> UpdateAsync(string id, Delta<TData> patch)
        {
            return await this.UpdateAsync(id, patch, this.IncludeDeleted);
        }

        public async override Task<IEnumerable<TData>> UpdateAsync(IEnumerable<Delta<TData>> patches)
        {
            return await this.UpdateAsync(patches, this.IncludeDeleted);
        }

        public override Task<TData> UndeleteAsync(string id, Delta<TData> patch)
        {
            patch = patch ?? new Delta<TData>();
            patch.TrySetPropertyValue(TableUtils.DeletedPropertyName, false);
            return this.UpdateAsync(id, patch, true);
        }

        public async override Task<TData> ReplaceAsync(string id, TData data)
        {
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }

            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            // We don't have the original version here so let the check happen in the DB
            this.VerifyUpdatedKey(id, data);

            try
            {
                this.Context.Entry(data).State = EntityState.Modified;
            }
            catch (Exception ex)
            {
                string updateError = EFResources.DomainManager_InvalidOperation.FormatForUser(ex.Message);
                this.Request.GetConfiguration().Services.GetTraceWriter().Debug(updateError, this.Request, LogCategories.TableControllers);
                HttpResponseMessage invalid = this.Request.CreateErrorResponse(HttpStatusCode.BadRequest, updateError, ex);
                throw new HttpResponseException(invalid);
            }

            await this.SubmitChangesAsync();

            return data;
        }

        public async override Task<bool> DeleteAsync(string id)
        {
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }

            TData current = await this.Lookup(id).Queryable.FirstOrDefaultAsync();
            if (current == null)
            {
                return false;
            }

            if (this.EnableSoftDelete)
            {
                current.Deleted = true;
            }
            else
            {
                this.Context.Set<TData>().Remove(current);
            }

            // Set the original version based on etag (if present)
            byte[] version = this.Request.GetVersionFromIfMatch();
            if (version != null)
            {
                this.context.Entry(current).OriginalValues[TableUtils.VersionPropertyName] = version;
            }

            int result = await this.SubmitChangesAsync();
            return result > 0;
        }

        public override async Task<bool> DeleteAsync(IEnumerable<string> ids)
        {
            if (ids == null)
            {
                throw new ArgumentNullException("ids");
            }

            List<TData> current = await this.Lookup(ids, this.IncludeDeleted).ToListAsync();

            // Check if all the entities exist
            if (!current.All(x => ids.Contains(x.Id)))
            {
                return false;
            }

            if (this.EnableSoftDelete)
            {
                foreach (TData item in current)
                {
                    item.Deleted = true;
                }
            }
            else
            {
                this.Context.Set<TData>().RemoveRange(current);
            }

            int result = await this.SubmitChangesAsync();
            return result > 0;
        }

        /// <summary>
        /// Submits the change through Entity Framework while logging any exceptions
        /// and produce appropriate <see cref="HttpResponseMessage"/> instances.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the operation.</returns>
        protected virtual Task<int> SubmitChangesAsync()
        {
            return EntityUtils.SubmitChangesAsync(this.Context, this.Request, this.GetOriginalValue);
        }

        /// <summary>
        /// Gets the original value of an entity in case an update or replace operation
        /// resulted in an <see cref="DbUpdateConcurrencyException"/>. The original value extracted
        /// from the exception will get returned to the client so that it can merge the data and
        /// possibly try the operation again.
        /// </summary>
        /// <param name="conflict">The <see cref="DbUpdateConcurrencyException"/> thrown by
        /// the update or replace operation.</param>
        /// <returns>The original value or <c>null</c> if none are available.</returns>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters", Justification = "keeping the derived class is better from a usability point of view.")]
        protected object GetOriginalValue(DbUpdateConcurrencyException conflict)
        {
            DbEntityEntry entry = conflict != null && conflict.Entries != null ? conflict.Entries.FirstOrDefault() : null;
            if (entry == null)
            {
                return null;
            }

            DbPropertyValues values = entry.GetDatabaseValues();
            return values != null ? values.ToObject() : null;
        }

        /// <summary>
        /// Verify that the id specified in the request URI matches that of an
        /// updated entity. If not then throw an exception as ids can't be modified.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Caller owns response object.")]
        private void VerifyUpdatedKey(string id, TData data)
        {
            // Verify that keys match after patch
            if (data == null || data.Id != id)
            {
                string msg = EFResources.TableController_KeyMismatch.FormatForUser("Id", id, data.Id);
                HttpResponseMessage badKey = this.Request.CreateErrorResponse(HttpStatusCode.BadRequest, msg);
                throw new HttpResponseException(badKey);
            }
        }

        private async Task<TData> UpdateAsync(string id, Delta<TData> patch, bool includeDeleted)
        {
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }

            if (patch == null)
            {
                throw new ArgumentNullException("patch");
            }

            TData current = await this.Lookup(id, includeDeleted).Queryable.FirstOrDefaultAsync();
            if (current == null)
            {
                throw new HttpResponseException(this.Request.CreateNotFoundResponse());
            }

            // Set the original version based on etag (if present)
            byte[] patchVersion = patch.GetPropertyValueOrDefault<TData, byte[]>(TableUtils.VersionPropertyName);
            if (patchVersion != null)
            {
                this.context.Entry(current).OriginalValues[TableUtils.VersionPropertyName] = patchVersion;
            }

            // Apply the patch and verify that keys match (keys can't change as part of an update)
            patch.Patch(current);
            this.VerifyUpdatedKey(id, current);

            await this.SubmitChangesAsync();

            return current;
        }

        private async Task<IEnumerable<TData>> UpdateAsync(IEnumerable<Delta<TData>> patches, bool includeDeleted)
        {
            if (patches == null)
            {
                throw new ArgumentNullException("patches");
            }

            // check if Id is not a null or an empty string
            if (patches.Any(patch => string.IsNullOrWhiteSpace(patch.GetPropertyValueOrDefault<TData, string>(TableUtils.IdPropertyName))))
            {
                throw new ArgumentException("Id cannot be null or empty");
            }

            // make sure there are no duplicate items
            if (patches.GroupBy(x => x.GetPropertyValueOrDefault<TData, string>(TableUtils.IdPropertyName)).Where(group => group.Count() > 1).Any())
            {
                throw new ArgumentException("Cannot have multiple entities with same Id");
            }

            var ids = patches.Select(patch => patch.GetPropertyValueOrDefault<TData, string>(TableUtils.IdPropertyName));
            var current = await this.Lookup(ids, includeDeleted).ToListAsync();

            // Check if all the entities exist
            if (!current.All(x => ids.Contains(x.Id)))
            {
                throw new HttpResponseException(this.Request.CreateNotFoundResponse("One or more of the entities does not exist"));
            }

            foreach (TData item in current)
            {
                Delta<TData> patch = patches.Single(x => x.GetPropertyValueOrDefault<TData, string>(TableUtils.IdPropertyName) == item.Id);

                byte[] patchVersion = patch.GetPropertyValueOrDefault<TData, byte[]>(TableUtils.VersionPropertyName);
                if (patchVersion != null)
                {
                    this.context.Entry(item).OriginalValues[TableUtils.VersionPropertyName] = patchVersion;
                }

                string id = patch.GetPropertyValueOrDefault<TData, string>(TableUtils.IdPropertyName);
                patch.Patch(item);
                this.VerifyUpdatedKey(id, item);
            }

            await this.SubmitChangesAsync();

            return current;
        }

        private SingleResult<TData> Lookup(string id, bool includeDeleted)
        {
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }

            IQueryable<TData> query = this.Context.Set<TData>().Where(item => item.Id == id);
            query = TableUtils.ApplyDeletedFilter(query, includeDeleted);
            return SingleResult.Create(query);
        }

        private IQueryable<TData> Lookup(IEnumerable<string> ids, bool includeDeleted)
        {
            if (ids == null)
            {
                throw new ArgumentNullException("ids");
            }

            IQueryable<TData> query = this.Context.Set<TData>().Where(item => ids.Contains(item.Id));
            query = TableUtils.ApplyDeletedFilter(query, includeDeleted);
            return query;
        }
    }
}