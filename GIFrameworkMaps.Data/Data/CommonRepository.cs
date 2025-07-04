﻿using AutoMapper;
using GIFrameworkMaps.Data.Models;
using GIFrameworkMaps.Data.Models.Authorization;
using GIFrameworkMaps.Data.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using shortid;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace GIFrameworkMaps.Data
{
	public class CommonRepository(ILogger<CommonRepository> logger, IApplicationDbContext context, IMemoryCache memoryCache, IMapper mapper, IHttpContextAccessor httpContextAccessor) : ICommonRepository
    {
        //dependency injection
        private readonly ILogger<CommonRepository> _logger = logger;
        private readonly IApplicationDbContext _context = context;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly IMapper _mapper = mapper;
        private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

		/// <summary>
		/// Gets the basic, top-level version information by slug. This should NOT be used to get all linked entities
		/// </summary>
		/// <param name="slug1">First part of the URL slug</param>
		/// <param name="slug2">Second part of the URL slug</param>
		/// <param name="slug3">Third part of the URL slug</param>
		/// <returns>Version object containing basic information only, or null</returns>
		public async Task<Models.Version?> GetVersionBySlug(string slug1, string slug2, string slug3)
        {            
            string slug = CreateSlug(slug1, slug2, slug3);

            string cacheKey = "VersionBySlug/" + slug;

			// Check to see if the version for this slug has already been cached and, if so, return that.
			if (_memoryCache.TryGetValue(cacheKey, out Models.Version? cacheValue))
			{
				return cacheValue;
			}

			var version = await _context.Versions
				.AsNoTracking()
				.IgnoreAutoIncludes()
                .FirstOrDefaultAsync(v => v.Slug == slug);

            // Cache the results so they can be used next time we call this function.
            if (version is not null)
            {
                _memoryCache.Set(cacheKey, version, TimeSpan.FromMinutes(10));
            }
            return version;
        }

		/// <summary>
		/// Join all the parts of a slug, separated by a "/". Null or empty parts are ignored.
		/// </summary>
		/// <param name="slugParts">A list of the slug parts</param>
		/// <returns>A list of combined slugs, in lowercase, separated by forward slash</returns>
		private static string CreateSlug(params string[] slugParts)
		{
			return string.Join("/", slugParts
				.Where(part => !string.IsNullOrEmpty(part))
				.Select(part => part.ToLower())
			);
		}

		public async Task<Models.Version?> GetVersion(int versionId)
		{
			string cacheKey = "Version/" + versionId.ToString();

			// Check to see if the version has already been cached and, if so, return that.
			if (_memoryCache.TryGetValue(cacheKey, out Models.Version? cacheValue))
			{
				return cacheValue;
			}

			var version = await _context.Versions
				.AsNoTrackingWithIdentityResolution()
				.AsSplitQuery()
				.Where(o => o.Id == versionId)
				.FirstOrDefaultAsync();

			if (version is not null && string.IsNullOrEmpty(version.HelpURL))
			{
				var generalVersion = await GetVersionBySlug("general", "", "");
				version.HelpURL = generalVersion!.HelpURL;
			}

			// Cache the results so they can be used next time we call this function.
			_memoryCache.Set(cacheKey, version, TimeSpan.FromMinutes(10));

			return version;
		}

		public async Task<VersionViewModel> GetVersionViewModel(Models.Version version)
        {

            List<BasemapViewModel> basemaps = _mapper.Map<List<VersionBasemap>, List<BasemapViewModel>>(version.VersionBasemaps);
            List<CategoryViewModel> categories = _mapper.Map<List<VersionCategory>, List<CategoryViewModel>>(version.VersionCategories);
			List<ProjectionViewModel> projections = _mapper.Map<List<VersionProjection>, List<ProjectionViewModel>>(version.VersionProjections);

			//remove duplicates
			var allLayers = (from cat in version.VersionCategories from layers in cat.Category!.Layers select layers).ToList();
            var dupes = allLayers.GroupBy(l => l.LayerId).Where(l => l.Count() > 1).ToList();
            foreach(var duplicate in dupes)
            {
                foreach(var layer in duplicate.Skip(1))
                {
                    //remove layer
                    _logger.LogWarning("Unhandled duplicate layer detected. Removing layer ID {layerId} from category {categoryId}", layer.LayerId, layer.CategoryId);
                    var matchedCategory = categories.Where(c => c.Id == layer.CategoryId).FirstOrDefault();
                    matchedCategory?.Layers.RemoveAll(l => l.Id == layer.LayerId);

                }
            }

            //Set version layer overrides
            foreach (var customisation in version.VersionLayerCustomisations)
            {
                var matchedCategory = categories.Where(c => c.Id == customisation.CategoryId).FirstOrDefault();
                if(matchedCategory != null) {
                    var matchedLayer = matchedCategory.Layers.Where(c => c.Id == customisation.LayerId).FirstOrDefault();
                    if(matchedLayer != null)
                    {
                        matchedLayer.IsDefault = customisation.IsDefault;
                        matchedLayer.DefaultOpacity = (int)(customisation.DefaultOpacity == null ? matchedLayer.DefaultOpacity : customisation.DefaultOpacity);
                        matchedLayer.DefaultSaturation = (int)(customisation.DefaultSaturation == null ? matchedLayer.DefaultSaturation : customisation.DefaultSaturation);
                        matchedLayer.SortOrder = (int)(customisation.SortOrder == null ? matchedLayer.SortOrder : customisation.SortOrder);
                        matchedLayer.MinZoom = (customisation.MinZoom == null ? matchedLayer.MinZoom : customisation.MinZoom);
                        matchedLayer.MaxZoom = (customisation.MaxZoom == null ? matchedLayer.MaxZoom : customisation.MaxZoom);
                    }
                }
            }
			if (projections.Count == 0)
			{
				throw new ApplicationException($"No projections were defined for version {version.Name}. This is an invalid configuration.");
			}
			if(!projections.Any(p => p.IsDefaultMapProjection))
			{
				projections.First().IsDefaultMapProjection = true;
				_logger.LogWarning("Version {version} does not have a default map projection set. First projection has been automatically selected", version.Name);
			}

            var viewModel = _mapper.Map<VersionViewModel>(version);
            viewModel.Categories = categories;
            viewModel.Basemaps = basemaps;
			viewModel.AvailableProjections = projections;
			viewModel.URLAuthorizationRules = await GetURLAuthorizationRules();

            return viewModel;
        }

		public async Task<List<URLAuthorizationRule>> GetURLAuthorizationRules()
		{
			return await _context.URLAuthorizationRules
			.AsNoTracking()
			.ToListAsync();
		}

		public async Task<List<Models.Version>> GetVersions()
        {
            return await _context.Versions
				.AsNoTracking()
				.IgnoreAutoIncludes()
				.ToListAsync();
        }

		public async Task<bool> CanUserAccessVersion(string userId, int versionId)
        {
            var version = await GetVersion(versionId);
            if (version is not null && !version.RequireLogin)
            {
                return true;
            }

            var versionuser = await _context.VersionUsers
                .AsNoTracking()
                .AnyAsync(vu => vu.UserId == userId && vu.VersionId == versionId);
            
            return versionuser;
        }

		public async Task<List<Models.Version>> GetVersionsListForUser(string? userId)
		{
			// Get user-specific versions
			var users_versions = await _context.VersionUsers
				.AsNoTracking()
				.IgnoreAutoIncludes()
				.Where(vu => vu.UserId == userId && vu.Version != null && vu.Version.Enabled == true && vu.Version.Hidden == false && vu.Version.RequireLogin == true)
				.Select(vu => vu.Version)
				.ToListAsync();

			// Get public versions
			var public_versions = await _context.Versions
				.AsNoTracking()
				.IgnoreAutoIncludes()
				.Where(v => v.Enabled == true && v.RequireLogin == false && v.Hidden == false)
				.ToListAsync();

			return [..users_versions, ..public_versions];
		}

		public List<ApplicationUserRole> GetUserRoles(string userId)
        {
            string cacheKey = $"UserRole/{userId}";

            // Check to see if the version has already been cached and, if so, return that.
            if (_memoryCache.TryGetValue(cacheKey, out List<ApplicationUserRole>? cacheValue))
            {
                return cacheValue!;
            }
            var roles = _context.ApplicationUserRoles
                .Where(u => u.UserId == userId)
                .Include(a => a.Role)
                .AsNoTrackingWithIdentityResolution()
                .ToList();

            _memoryCache.Set(cacheKey, roles, new MemoryCacheEntryOptions{ 
                Priority = CacheItemPriority.Low,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
            });
            return roles;
        }

        public List<WebLayerServiceDefinition> GetWebLayerServiceDefinitions()
        {
            bool includeAdminDefinitions = _httpContextAccessor.HttpContext!.User.IsInRole("GIFWAdmin");
            string cacheKey = $"WebLayerServiceDefinitions/{includeAdminDefinitions}";
            if (_memoryCache.TryGetValue(cacheKey, out List<WebLayerServiceDefinition>? cacheValue))
            {
                return cacheValue!;
            }

            var services = _context.WebLayerServiceDefinitions.AsNoTracking().ToList();
            
            if(!includeAdminDefinitions)
            {
                services.RemoveAll(d => d.AdminOnly == true);
            }
            
            _memoryCache.Set(cacheKey, services, new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.Low,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });
            return services;
        }

        public List<ProxyAllowedHost> GetProxyAllowedHosts()
        {
            string cacheKey = $"ProxyAllowedHosts";
            if (_memoryCache.TryGetValue(cacheKey, out List<ProxyAllowedHost>? cacheValue))
            {
                if(cacheValue != null)
                {
                    return cacheValue;
                }
            }

            var allowedHosts = _context.ProxyAllowedHosts.AsNoTracking().ToList();

            _memoryCache.Set(cacheKey, allowedHosts, new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.Low,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12)
            });
            return allowedHosts;
        }

        public async Task<List<ProxyAllowedHost>> GetProxyAllowedHostsAsync()
        {
            string cacheKey = $"ProxyAllowedHosts";
            if (_memoryCache.TryGetValue(cacheKey, out List<ProxyAllowedHost>? cacheValue))
            {
                return cacheValue!;
            }

            var allowedHosts = await _context.ProxyAllowedHosts.AsNoTracking().ToListAsync();

            _memoryCache.Set(cacheKey, allowedHosts, new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.Low,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12)
            });
            return allowedHosts;
        }

        public async Task<List<Bookmark>> GetBookmarksForUserAsync(string userId)
        {
            var userBookmarks = await _context.Bookmarks.Where(b => b.UserId ==  userId).OrderBy(b => b.Name).AsNoTracking().ToListAsync();
            return userBookmarks;
        }

        public async Task<string> GenerateShortId(string url)
        {
            string shortId = ShortId.Generate();

            var existing = await _context.ShortLinks.AsNoTracking().FirstOrDefaultAsync(s => s.ShortId == shortId);
            var iterations = 0;
            var maxIterations = 100;
            while (existing != null && iterations < maxIterations) {
                shortId = ShortId.Generate();
                existing = await _context.ShortLinks.AsNoTracking().FirstOrDefaultAsync(s => s.ShortId == shortId);
                iterations++;
            }
            
            if(existing == null)
            {
                return shortId;
            }
            else
            {
                //we couldn't get a unique short id in 100 tries
                _logger.LogError("Could not generate a unique short id for url {url} after {maxIterations} tries",
                    //Sanitise user input to prevent log forging
                    url.Replace(Environment.NewLine, ""), maxIterations);
                return "";
            }
        }

        public async Task<string> GetFullUrlFromShortId(string shortId)
        {
			var shortLink = await _context.ShortLinks.FindAsync(shortId);
            if(shortLink is null || shortLink.FullUrl is null)
            {
                return "";
            }
            return shortLink.FullUrl;
        }

        public bool IsURLCurrentApplication(string url)
        {
            var uri = new Uri(url);
            var host = uri.Host;
            var port = uri.Port;
            var scheme = uri.Scheme;
            var currentHost = _httpContextAccessor.HttpContext!.Request.Host.Host;
            var currentPort = _httpContextAccessor.HttpContext.Request.Host.Port;
            var currentScheme = _httpContextAccessor.HttpContext.Request.Scheme;
            //port is ignored if not specified in the http context
            if (host == currentHost && (currentPort == null || port == currentPort) && scheme == currentScheme)
            {
                return true;
            }
            return false;
        }
    }
}
