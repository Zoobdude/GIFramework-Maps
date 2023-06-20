﻿using GIFrameworkMaps.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using GIFrameworkMaps.Data.Models;
using GIFrameworkMaps.Data.Models.ViewModels.Management;

namespace GIFrameworkMaps.Web.Controllers
{
    public class MapController : Controller
    {
        //dependancy injection
        private readonly ILogger<MapController> _logger;
        private readonly IAuthorizationService _authorization;
        private readonly ICommonRepository _repository;
        private readonly IManagementRepository _adminRepository;
        private readonly IConfiguration _configuration;

        public MapController(
            ILogger<MapController> logger, 
            IAuthorizationService authorization,
            ICommonRepository repository,
            IManagementRepository adminRepository,
            IConfiguration configuration)
        {
            _logger = logger;
            _authorization = authorization;
            _repository = repository;
            _adminRepository = adminRepository;
            _configuration = configuration;
        }
        /// <summary>
        /// This route forces calls to the general /Map endpoint to redirect to the default slug route. Not pretty but does the job
        /// </summary>
        /// <returns></returns>
        public IActionResult RedirectToGeneral()
        {
            return RedirectToRoute("Default_Slug");
        }

        public async Task<IActionResult> IndexAsync(string slug1, string slug2, string slug3)
        {
            _logger.LogInformation("User requested version {slug1}/{slug2}/{slug3}",slug1,slug2,slug3);
            var version = _repository.GetVersionBySlug(slug1,slug2,slug3);
            if (version != null)
            {
                
                _logger.LogInformation("Found version {versionName}",version.Name);

                if (!version.Enabled)
                {
                    return View("Disabled",version);
                }
                if(!string.IsNullOrEmpty(version.RedirectionURL) && Uri.IsWellFormedUriString(version.RedirectionURL, UriKind.Absolute))
                {
                    return Redirect(version.RedirectionURL);
                }

                var authResult = await _authorization.AuthorizeAsync(User,version, "CanAccessVersion");

                if (authResult.Succeeded)
                { 
                    //now we get the full details
                    var viewModel = _repository.GetVersionViewModel(version.Id);
                    ViewData["AnalyticsModel"] = _adminRepository.GetAnalyticsModel();

                    var host = Request.Host.ToUriComponent();
                    var pathBase = Request.PathBase.ToUriComponent();
                    viewModel.AppRoot = $"{host}{pathBase}/";
                    if (bool.TryParse(_configuration["GIFrameworkMaps:AuthenticateWithMapServices"], out bool authWithMapServices))
                    {
                        if (authWithMapServices && !string.IsNullOrEmpty(_configuration["GIFrameworkMaps:MapServicesAccessURL"]))
                        {
                            /*NOTE - This requires using 'SaveTokens = true' in the auth setup*/
                            var idToken = await HttpContext.GetTokenAsync("id_token");
                            ViewData["MapServicesAccessURL"] = _configuration["GIFrameworkMaps:MapServicesAccessURL"];
                            ViewData["MapServicesAccessToken"] = idToken;
                        }
                    }

                    return View(viewModel);
                }
                else
                {
                    //if user not logged in, prompt for login, otherwise redirect to unauthorized
                    if (!User.Identity.IsAuthenticated)
                    {
                        return new ChallengeResult();                       
                    }
                    else
                    {
                        return View("Forbidden", version);
                    }
                }  
            }
            else
            {
                return View("VersionNotFound");
            }

        }

        public async Task<IActionResult> UserShortLink(string id)
        {
            string redirectUrl = await _repository.GetFullUrlFromShortId(id);
            if(redirectUrl == null || !Uri.IsWellFormedUriString(redirectUrl,UriKind.Absolute))
            {
                return View("ShortLinkNotFound");
            }
            return Redirect(redirectUrl);
        }
    }
}
