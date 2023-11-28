/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

using Autodesk.Forge;
using Autodesk.Forge.Core;
using Autodesk.Forge.DesignAutomation;
using Autodesk.Forge.DesignAutomation.Model;
using Autodesk.Forge.Model;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Activity = Autodesk.Forge.DesignAutomation.Model.Activity;
using Alias = Autodesk.Forge.DesignAutomation.Model.Alias;
using AppBundle = Autodesk.Forge.DesignAutomation.Model.AppBundle;
using Parameter = Autodesk.Forge.DesignAutomation.Model.Parameter;
using WorkItem = Autodesk.Forge.DesignAutomation.Model.WorkItem;
using WorkItemStatus = Autodesk.Forge.DesignAutomation.Model.WorkItemStatus;



namespace DesignCheck.Controllers
{
    public class DesignAutomation4Revit
    {
        private const string APPNAME = "FindColumnsApp";
        private const string APPBUNBLENAME = "FindColumnsIO.zip";
        private const string ACTIVITY_NAME = "FindColumnsActivity";
        protected string Script { get; set; }
        private const string ENGINE_NAME = "Autodesk.Revit+2022";
        public const string NickName = "chuong";
        /// NickName.AppBundle+Alias
        private string AppBundleFullName => $"{NickName}.{APPNAME}+{Alias}";

        /// NickName.Activity+Alias
        // private string ActivityFullName => $"{Utils.NickName}.{ACTIVITY_NAME}+{Alias}";
        private string ActivityFullName => $"{NickName}.{ACTIVITY_NAME}+{Alias}";
        //
        // /// Prefix for AppBundles and Activities
        // public static string NickName { get { return Credentials.GetAppSetting("APS_CLIENT_ID"); } }
        /// Alias for the app (e.g. DEV, STG, PROD). This value may come from an environment variable
        public static string Alias { get { return "dev"; } }
        // Design Automation v3 API
        private DesignAutomationClient _designAutomation;

        public DesignAutomation4Revit()
        {
            // need to initialize manually as this class runs in background
            ForgeService service =
                new ForgeService(
                    new HttpClient(
                        new ForgeHandler(Microsoft.Extensions.Options.Options.Create(new ForgeConfiguration()
                        {
                            ClientId = Credentials.GetAppSetting("APS_CLIENT_ID"),
                            ClientSecret = Credentials.GetAppSetting("APS_CLIENT_SECRET")
                        }))
                        {
                            InnerHandler = new HttpClientHandler()
                        })
                );
            _designAutomation = new DesignAutomationClient(service);
        }

        public async Task EnsureAppBundle(string contentRootPath)
        {
            // get the list and check for the name
            Console.WriteLine("Retrieving app bundles");
            Page<string> appBundles = await _designAutomation.GetAppBundlesAsync();
            bool existAppBundle = false;
            foreach (string appName in appBundles.Data)
            {
                if (appName.Contains(AppBundleFullName))
                {
                    existAppBundle = true;
                    Console.WriteLine("Found existing app bundle: " + appName);
                    break;
                }
            }

            if (!existAppBundle)
            {
                // check if ZIP with bundle is here
                Console.WriteLine("Start Create new app bundle");
                string packageZipPath = Path.Combine(contentRootPath + "/bundles/", APPBUNBLENAME);
                if (!File.Exists(packageZipPath)) throw new Exception(APPBUNBLENAME +" not found at " + packageZipPath);

                AppBundle appBundleSpec = new AppBundle()
                {
                    Package = APPNAME,
                    Engine = ENGINE_NAME,
                    Id = APPNAME,
                    Description = $"Description for {APPBUNBLENAME}",

                };
                AppBundle newAppVersion = await _designAutomation.CreateAppBundleAsync(appBundleSpec);
                if (newAppVersion == null) throw new Exception("Cannot create new app");
                Console.WriteLine("Created new bundle: " + newAppVersion.Appbundles);
                // create alias pointing to v1
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateAppBundleAliasAsync(APPNAME, aliasSpec);
                Console.WriteLine("Created new alias version: " + newAppVersion.Version);
                // upload the zip with .bundle
                RestClient uploadClient = new RestClient(newAppVersion.UploadParameters.EndpointURL);
                RestRequest request = new RestRequest(string.Empty, Method.POST);
                request.AlwaysMultipartFormData = true;
                foreach (KeyValuePair<string, string> x in newAppVersion.UploadParameters.FormData) request.AddParameter(x.Key, x.Value);
                request.AddFile("file", packageZipPath);
                request.AddHeader("Cache-Control", "no-cache");
                await uploadClient.ExecuteAsync(request);
                Console.WriteLine("Uploaded app bundle: " + packageZipPath);
            }
        }

        public async Task EnsureActivity()
        {
            Console.WriteLine("Retrieving activity");
            Page<string> activities = await _designAutomation.GetActivitiesAsync();
            bool existActivity = false;
            foreach (string activity in activities.Data)
            {
                if (activity.Contains(ActivityFullName))
                {
                    existActivity = true;
                    Console.WriteLine("Found existing activity: " + activity);
                    continue;
                }
            }

            if (!existActivity)
            {
                string commandLine = string.Format(@"$(engine.path)\\revitcoreconsole.exe /i {0}$(args[inputFile].path){0} /al {0}$(appbundles[{1}].path){0}", "\"", APPNAME);
                Activity activitySpec = new Activity()
                {
                    Id = ACTIVITY_NAME,
                    Appbundles = new List<string>() { AppBundleFullName },
                    CommandLine = new List<string>() { commandLine },
                    Engine = ENGINE_NAME,
                    Parameters = new Dictionary<string, Parameter>()
                    {
                        { "inputFile", new Parameter() { Description = "Input Revit File", LocalName = "$(inputFile)", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
                        { "result", new Parameter() { Description = "Resulting JSON File", LocalName = "result.txt", Ondemand = false, Required = true, Verb = Verb.Put, Zip = false } }
                    },
                    Settings = new Dictionary<string, ISetting>()
                    {
                        { "script", new StringSetting(){ Value = Script } }
                    },
                    Description = "Description for activity FindColumnsActivity",
                };
                Activity newActivity = await _designAutomation.CreateActivityAsync(activitySpec);
                Console.WriteLine("Created new activity: " + newActivity.Id);
                // specify the alias for this Activity
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateActivityAliasAsync(ACTIVITY_NAME, aliasSpec);
                Console.WriteLine("Created new alias for activity Version:{0} ID{1}: " + newAlias.Version + newAlias.Id);
            }
        }

        private async Task<XrefTreeArgument> BuildDownloadURL(string userAccessToken, string projectId, string versionId)
        {
            VersionsApi versionApi = new VersionsApi();
            versionApi.Configuration.AccessToken = userAccessToken;
            dynamic version = await versionApi.GetVersionAsync(projectId, versionId);
            dynamic versionItem = await versionApi.GetVersionItemAsync(projectId, versionId);

            string[] versionItemParams = ((string)version.data.relationships.storage.data.id).Split('/');
            string[] bucketKeyParams = versionItemParams[versionItemParams.Length - 2].Split(':');
            string bucketKey = bucketKeyParams[bucketKeyParams.Length - 1];
            string objectName = versionItemParams[versionItemParams.Length - 1];
            string downloadUrl = $"https://developer.api.autodesk.com/oss/v2/buckets/{bucketKey}/objects/{objectName}";

            return new XrefTreeArgument()
            {
                Url = downloadUrl,
                Verb = Verb.Get,
                Headers = new Dictionary<string, string>()
                {
                    { "Authorization", "Bearer " + userAccessToken }
                }
            };
        }

        private async Task<XrefTreeArgument> BuildUploadURL(string resultFilename)
        {
            string bucketName = "revitdesigncheck" + NickName.ToLower();
            BucketsApi buckets = new BucketsApi();
            dynamic token = await Credentials.Get2LeggedTokenAsync(new Scope[] { Scope.BucketCreate, Scope.DataWrite });
            buckets.Configuration.AccessToken = token.access_token;
            PostBucketsPayload bucketPayload = new PostBucketsPayload(bucketName, null, PostBucketsPayload.PolicyKeyEnum.Transient);
            try
            {
                await buckets.CreateBucketAsync(bucketPayload, "US");
            }
            catch { }

            ObjectsApi objects = new ObjectsApi();
            dynamic signedUrl = await objects.CreateSignedResourceAsyncWithHttpInfo(bucketName, resultFilename, new PostBucketsSigned(5), "readwrite");

            return new XrefTreeArgument()
            {
                Url = (string)(signedUrl.Data.signedUrl),
                Verb = Verb.Put
            };
        }

        public async Task StartDesignCheck(string projectId, string versionId, string contentRootPath)
        {
            // uncomment these lines to clear all appbundles & activities under your account
            //await _designAutomation.DeleteForgeAppAsync("me");

            dynamic credentials = await Credentials.Get2LeggedTokenAsync(new Scope[] {Scope.DataRead, Scope.CodeAll, Scope.DataWrite});
            // Credentials credentials = await Credentials.FromDatabaseAsync(userId);

            await EnsureAppBundle(contentRootPath);
            await EnsureActivity();

            string resultFilename = versionId.Base64Encode() + ".txt";
            // string callbackUrl =
            //     $"{Credentials.GetAppSetting("APS_WEBHOOK_URL")}/api/aps/callback/designautomation/{userId}/{hubId}/{projectId}/{versionId.Base64Encode()}";
            string callbackUrl = "https://webhook.site/25ad18c1-2f09-4148-bfd1-2c9cd2618a9b";
            Console.WriteLine("ActivityId: " + ActivityFullName);
            dynamic downloadUrl = await BuildDownloadURL(credentials.access_token, projectId, versionId);
            Console.WriteLine("Download URL: " + downloadUrl.Url);
            var treeArgument = await BuildUploadURL(resultFilename);
            Console.WriteLine("TreeArgument: " + treeArgument.Url);
            Console.WriteLine("Start  Create a workItem");
            WorkItem workItemSpec = new WorkItem()
            {
                ActivityId = ActivityFullName,
                Arguments = new Dictionary<string, IArgument>()
                {
                    { "inputFile", downloadUrl },
                    { "result",  treeArgument },
                    { "onComplete", new XrefTreeArgument { Verb = Verb.Post, Url = callbackUrl } }
                }
            };
            Console.WriteLine("Start Create WorkItem");
            WorkItemStatus workItemStatus = await _designAutomation.CreateWorkItemAsync(workItemSpec);
            Console.WriteLine("WorkItemStatus: " + workItemStatus.Status);
            Console.WriteLine("Working Item Id: " + workItemStatus.Id);
        }
    }
}
